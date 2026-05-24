using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Imports Redgate SQL Prompt .sqlpromptstylev2 (XML) profiles and maps
/// their options to AKML SQL FormattingProfile settings.
/// </summary>
public class SqlPromptImportResult
{
    public FormattingProfile Profile { get; set; } = new();
    public int MappedCount { get; set; }
    public int UnmappedCount { get; set; }
    public List<string> UnmappedOptions { get; set; } = [];
}

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class SqlPromptImporter
{
    /// <summary>
    /// Static mapping table: SQL Prompt XML element name -> Action to apply value to a FormattingProfile.
    /// </summary>
    private static readonly Dictionary<string, Action<FormattingProfile, string>> OptionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ----- Whitespace / Indentation -----
        ["InsertTabs"] = (p, v) => p.Whitespace.TabStyle = ToBool(v) ? "tabs" : "spaces",
        ["TabSize"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.TabSize = n; },
        ["IndentationSize"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.TabSize = n; },
        ["MaxLineWidth"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.MaxLineWidth = n; },
        ["ColumnLimit"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.MaxLineWidth = n; },
        ["SpaceAfterComma"] = (p, v) => p.Whitespace.SpaceAfterComma = ToBool(v),
        ["SpaceAroundOperators"] = (p, v) => p.Whitespace.SpaceAroundOperators = ToBool(v),
        ["SpaceInsideParentheses"] = (p, v) => p.Whitespace.SpaceInsideParentheses = ToBool(v),
        ["TrailingWhitespace"] = (p, v) => p.Whitespace.TrailingWhitespace = ToBool(v) ? "remove" : "keep",
        ["FinalNewline"] = (p, v) => p.Whitespace.FinalNewline = ToBool(v) ? "ensure" : "none",
        ["LineBreakBeforeClause"] = (p, v) => p.Whitespace.LineBreakBeforeClause = ToBool(v),
        ["LineBreakAfterClause"] = (p, v) => p.Whitespace.LineBreakAfterClause = ToBool(v),
        ["LineBreakBeforeComma"] = (p, v) => p.Whitespace.LineBreakBeforeComma = ToBool(v),
        ["LineBreakAfterComma"] = (p, v) => p.Whitespace.LineBreakAfterComma = ToBool(v),
        ["EmptyLinesBetweenStatements"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.EmptyLineBetweenStatements = n; },
        ["LineBreakAfterSemicolon"] = (p, v) => p.Whitespace.LineBreakAfterSemicolon = ToBool(v),
        ["PreserveEmptyLinesAfterBatch"] = (p, v) => p.Whitespace.PreserveEmptyLinesAfterBatch = ToBool(v),

        // ----- Casing -----
        ["KeywordCasing"] = (p, v) => p.Casing.ReservedKeywords = MapCasing(v),
        ["FunctionCasing"] = (p, v) => p.Casing.BuiltInFunctions = MapCasing(v),
        ["DataTypeCasing"] = (p, v) => p.Casing.BuiltInDataTypes = MapCasing(v),
        ["IdentifierCasing"] = (p, v) => p.Casing.Identifiers = MapCasing(v),

        // ----- Comma / List -----
        ["CommaPosition"] = (p, v) => p.List.CommaPosition = v.Contains("before", StringComparison.OrdinalIgnoreCase) ? "leading" : "trailing",
        ["AlignAliases"] = (p, v) => p.List.AlignAliases = ToBool(v),
        ["OneItemPerLine"] = (p, v) => p.List.OneItemPerLine = ToBool(v),
        ["IndentListItems"] = (p, v) => p.List.IndentListItems = ToBool(v),
        ["AlignItemsAcrossClauses"] = (p, v) => p.List.AlignItemsAcrossClauses = ToBool(v),

        // ----- DML -----
        ["SelectOnNewLine"] = (p, v) => p.Dml.SelectItemsOnNewLine = ToBool(v),
        ["FromOnNewLine"] = (p, v) => p.Dml.FromOnNewLine = ToBool(v),
        ["WhereOnNewLine"] = (p, v) => p.Dml.WhereOnNewLine = ToBool(v),
        ["GroupByOnNewLine"] = (p, v) => p.Dml.GroupByOnNewLine = ToBool(v),
        ["HavingOnNewLine"] = (p, v) => p.Dml.HavingOnNewLine = ToBool(v),
        ["OrderByOnNewLine"] = (p, v) => p.Dml.OrderByOnNewLine = ToBool(v),
        ["ANDORNewLine"] = (p, v) => p.Dml.AndOrNewLine = v.Contains("before", StringComparison.OrdinalIgnoreCase) ? "before" : "after",
        ["SetOnNewLine"] = (p, v) => p.Dml.SetOnNewLine = ToBool(v),
        ["ValuesOnNewLine"] = (p, v) => p.Dml.ValuesOnNewLine = ToBool(v),
        ["DmlCollapseShortStatements"] = (p, v) => p.Dml.CollapseShortStatements = ToBool(v),
        ["DmlCollapseStatementsShorterThan"] = (p, v) => { if (int.TryParse(v, out var n)) p.Dml.CollapseThreshold = n; },
        ["DmlCollapseShortSubqueries"] = (p, v) => p.Dml.CollapseShortSubqueries = ToBool(v),
        ["DmlCollapseSubqueriesShorterThan"] = (p, v) => { if (int.TryParse(v, out var n)) p.Dml.SubqueryCollapseThreshold = n; },

        // ----- JOIN -----
        ["JoinOnNewLine"] = (p, v) => p.Join.OnNewLine = ToBool(v),
        ["IndentJoin"] = (p, v) => p.Join.IndentJoin = ToBool(v),
        ["OnConditionNewLine"] = (p, v) => p.Join.OnConditionNewLine = ToBool(v),
        ["EmptyLineBeforeJoin"] = (p, v) => p.Join.EmptyLineBeforeJoin = ToBool(v),
        ["AlignJoinKeyword"] = (p, v) => p.Join.AlignJoinKeyword = v.Trim().ToLowerInvariant() switch
        {
            "right" or "rightaligned" => "right",
            "none" => "none",
            // Phase B closure — keep "IndentedFromFrom" as its own AKML enum value rather than
            // collapsing into "left", so round-trip is lossless. Layout falls back to "left"
            // semantics until the layout sub-engine learns the variant (documented in
            // ControlFlowRules.cs ApplyOperatorRules `<remarks>`-style note).
            "indentedfromfrom" => "indentedFromFrom",
            "left" or "totable" or "tofrom" => "left",
            _ => "right",   // unrecognised — fall back to the AKML default
        },

        // ----- DDL -----
        ["AlignDataTypes"] = (p, v) => p.Ddl.AlignDataTypes = ToBool(v),
        ["AlignConstraints"] = (p, v) => p.Ddl.AlignConstraints = ToBool(v),
        ["AsOnNewLine"] = (p, v) => p.Ddl.AsOnNewLine = ToBool(v),
        ["BeginOnNewLine"] = (p, v) => p.Ddl.BeginOnNewLine = ToBool(v),
        ["PlaceFirstProcedureParameterOnNewLine"] = (p, v) => p.Ddl.FirstParameterOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            "auto" or "iflongerthanwrap" => "auto",   // SQL Prompt "IfLongerThanWrap" ~= AKML "auto"
            _ => "auto",                              // unrecognised — fall back to the AKML default
        },
        ["DdlCollapseShortStatements"] = (p, v) => p.Ddl.CollapseShortDdl = ToBool(v),
        ["DdlCollapseStatementsShorterThan"] = (p, v) => { if (int.TryParse(v, out var n)) p.Ddl.CollapseThreshold = n; },

        // ----- Control Flow -----
        ["IfBeginOnNewLine"] = (p, v) => p.ControlFlow.BeginOnNewLine = ToBool(v),
        ["ElseOnNewLine"] = (p, v) => p.ControlFlow.ElseOnNewLine = ToBool(v),
        ["IndentBetweenBeginEnd"] = (p, v) => p.ControlFlow.IndentBetweenBeginEnd = ToBool(v),
        ["ControlFlowCollapseShortIfElse"] = (p, v) => p.ControlFlow.CollapseShortIfElse = ToBool(v),
        ["ControlFlowCollapseStatementsShorterThan"] = (p, v) => { if (int.TryParse(v, out var n)) p.ControlFlow.CollapseThreshold = n; },

        // ----- CASE -----
        ["WhenOnNewLine"] = (p, v) => p.Case.WhenOnNewLine = ToBool(v),
        ["ThenOnNewLine"] = (p, v) => p.Case.ThenOnNewLine = ToBool(v),
        ["CaseElseOnNewLine"] = (p, v) => p.Case.ElseOnNewLine = ToBool(v),
        ["EndOnNewLine"] = (p, v) => p.Case.EndOnNewLine = ToBool(v),
        // T082 — SQL Prompt CASE additions
        ["PlaceFirstWhenOnNewLine"] = (p, v) => p.Case.FirstWhenOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            _ => "auto",                              // "auto" / "iflongerthanwrap" / anything else
        },
        ["WhenAlignment"] = (p, v) => p.Case.WhenAlignment = v.Trim().ToLowerInvariant() switch
        {
            "tofirstitem" => "toFirstItem",
            "indentedfromcase" or "indented" => "indentedFromCase",
            _ => "toCase",                            // "tocase" / default / unrecognised
        },
        ["PlaceCaseExpressionOnNewLine"] = (p, v) => p.Case.ExpressionOnNewLine = ToBool(v),

        // ----- CTE additions (T080) -----
        ["PlaceCteColumnsOnNewLine"] = (p, v) => p.Cte.PlaceColumnsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            _ => "ifLongerThanWrap",
        },

        // ----- Operators (T083) -----
        ["OperatorsAlignment"] = (p, v) => p.Operators.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "indentedfromstatement" or "indented" => "indentedFromStatement",
            "rightaligned" or "right" => "rightAligned",
            _ => "inlineWithStatement",
        },
        ["PlaceBetweenKeywordOnNewLine"] = (p, v) => p.Operators.BetweenOnNewLine = ToBool(v),

        // ----- IN Statements (T084) -----
        ["InStatementsAlignment"] = (p, v) => p.InStatements.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "wrapped" => "wrapped",
            "rightaligned" or "right" => "rightAligned",
            _ => "stacked",
        },

        // ===== Phase B closure — full SQL Prompt feature parity =====

        // ----- Whitespace additions -----
        ["TabBehavior"] = (p, v) => p.Whitespace.TabStyle = v.Trim().ToLowerInvariant() switch
        {
            "tabsonly" or "tabs" => "tabs",
            "tabswherepossible" => "tabsWhenPossible",
            _ => "spaces",                          // "spacesonly" / "spaces" / default
        },
        ["BlankLinesBeforeGo"] = (p, v) => { if (int.TryParse(v, out var n)) p.Whitespace.BlankLinesBeforeGoCount = n; },

        // ----- Lists addition -----
        ["PlaceSubsequentItemsOnNewLines"] = (p, v) => p.List.PlaceSubsequentItemsOnNewLines = v.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "iflongerthanwrap" or "iflonger" => "ifLongerThanWrap",
            _ => "always",                          // "always" / default
        },

        // ----- DML additions -----
        ["RightAlignClauses"] = (p, v) => p.Dml.RightAlignClauses = ToBool(v),
        ["ClauseIndentation"] = (p, v) => p.Dml.ClauseIndentation = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indented",
            "rightaligned" or "rightalignedtostatement" => "rightAligned",
            _ => "none",
        },
        ["InsertColumnListFormat"] = (p, v) => p.Dml.InsertColumnListFormat = v.Trim().ToLowerInvariant() switch
        {
            "compact" => "compact",
            "iflongerthanwrap" or "iflonger" => "ifLongerThanWrap",
            _ => "onePerLine",                      // "oneperline" / default
        },
        ["ValuesFormat"] = (p, v) => p.Dml.ValuesFormat = v.Trim().ToLowerInvariant() switch
        {
            "compact" => "compact",
            "iflongerthanwrap" or "iflonger" => "ifLongerThanWrap",
            _ => "onePerLine",
        },

        // ----- DDL addition -----
        ["ConstraintColumnsOnNewLine"] = (p, v) => p.Ddl.ConstraintColumnsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            _ => "ifLongerOrMultipleColumns",
        },

        // ----- JOIN additions -----
        // Note: AlignJoinKeyword is already mapped above; add the 4th SQL Prompt variant "IndentedFromFrom".
        // Override the earlier binding by re-keying with a broader switch.
        ["OnConditionIndentMode"] = (p, v) => p.Join.OnConditionIndentMode = v.Trim().ToLowerInvariant() switch
        {
            "totable" => "toTable",
            "indentedfromtable" => "indentedFromTable",
            _ => "indentedFromJoin",                // "indentedfromjoin" / default
        },

        // ----- CASE additions -----
        ["CaseEndAlignment"] = (p, v) => p.Case.EndAlignment = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indented",
            _ => "toCase",
        },

        // ----- CTE additions -----
        ["CtePlaceAsOnNewLine"] = (p, v) => p.Cte.AsOnNewLine = ToBool(v),

        // ----- Operators additions -----
        ["PlaceAndBetweenBetweenOnNewLine"] = (p, v) => p.Operators.AndBetweenOnNewLine = ToBool(v),

        // ----- IN Statements addition -----
        ["InStatementsPlaceItemsOnNewLine"] = (p, v) => p.InStatements.PlaceItemsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            _ => "ifLongerThanWrap",
        },

        // ----- Function Calls (new) -----
        ["FunctionCallsPlaceParametersOnNewLine"] = (p, v) => p.FunctionCalls.PlaceParametersOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" or "true" => "always",
            "never" or "false" => "never",
            _ => "ifLongerThanWrap",
        },
        ["IndentFunctionParameters"] = (p, v) => p.FunctionCalls.IndentParameters = ToBool(v),

        // ----- Comments (new) -----
        ["MultilineCommentFormatting"] = (p, v) => p.Comments.MultilineFormatting = v.Trim().ToLowerInvariant() switch
        {
            "normaliseindent" or "normalizeindent" => "normaliseIndent",
            "joinshortlines" => "joinShortLines",
            _ => "preserve",
        },
        ["RecognizeCommonCommentPatterns"] = (p, v) => p.Comments.RecognizeCommonPatterns = ToBool(v),

        // ----- Parenthesis -----
        ["OpenParenOnSameLine"] = (p, v) => p.Parenthesis.OpenOnSameLine = ToBool(v),
        ["CloseParenOnNewLine"] = (p, v) => p.Parenthesis.CloseOnNewLine = ToBool(v) ? "true" : "false",
        ["IndentParenContents"] = (p, v) => p.Parenthesis.IndentContents = ToBool(v),
        ["CollapseShortParenthesisContents"] = (p, v) => p.Parenthesis.CollapseShort = ToBool(v),
        ["CollapseParenthesesShorterThan"] = (p, v) => { if (int.TryParse(v, out var n)) p.Parenthesis.CollapseThreshold = n; },

        // ----- Format Actions -----
        ["InsertSemicolons"] = (p, v) => p.FormatActions.InsertSemicolons = ToBool(v),
        ["QualifyObjectNames"] = (p, v) => p.FormatActions.QualifyObjectNames = ToBool(v),
        ["AddSquareBrackets"] = (p, v) => p.FormatActions.AddSquareBrackets = ToBool(v),
    };

    /// <summary>
    /// Imports a SQL Prompt style file (.sqlpromptstylev2 XML) and converts it to a FormattingProfile.
    /// </summary>
    /// <param name="xmlContent">The raw XML content of the .sqlpromptstylev2 file.</param>
    /// <param name="profileName">Optional name for the resulting profile.</param>
    public static SqlPromptImportResult Import(string xmlContent, string? profileName = null)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);

        var result = new SqlPromptImportResult();
        var profile = new FormattingProfile();

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null)
            {
                result.UnmappedOptions.Add("(empty document)");
                result.UnmappedCount = 1;
                result.Profile = profile;
                return result;
            }

            // SQL Prompt style files have elements like <Options><Option Name="KeywordCasing" Value="..." />...</Options>
            // or flat elements like <KeywordCasing>UPPERCASE</KeywordCasing> depending on version.
            // We handle both patterns.

            var elements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Pattern 1: <Options><Option Name="..." Value="..." /></Options>
            foreach (var opt in root.Descendants("Option"))
            {
                var name = opt.Attribute("Name")?.Value ?? opt.Attribute("name")?.Value;
                var value = opt.Attribute("Value")?.Value ?? opt.Attribute("value")?.Value ?? opt.Value;
                if (!string.IsNullOrEmpty(name))
                    elements[name] = value;
            }

            // Pattern 2: Flat child elements <KeywordCasing>UPPERCASE</KeywordCasing>
            foreach (var el in root.Elements())
            {
                if (!el.HasElements && !elements.ContainsKey(el.Name.LocalName))
                {
                    elements[el.Name.LocalName] = el.Value;
                }
            }

            // Apply each discovered option
            foreach (var (name, value) in elements)
            {
                if (OptionMap.TryGetValue(name, out var action))
                {
                    try
                    {
                        action(profile, value);
                        result.MappedCount++;
                    }
                    catch (Exception ex)
                    {
                        // Mapping failed — record the error detail so users know what went wrong
                        result.UnmappedOptions.Add($"{name} (error: {ex.Message})");
                        result.UnmappedCount++;
                    }
                }
                else
                {
                    result.UnmappedOptions.Add(name);
                    result.UnmappedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            result.UnmappedOptions.Add($"Parse error: {ex.Message}");
        }

        // Set metadata
        profile.Metadata.Name = profileName ?? "Imported from SQL Prompt";
        profile.Metadata.Description = $"Imported from SQL Prompt style file ({result.MappedCount} options mapped, {result.UnmappedCount} unmapped)";
        profile.Metadata.BasedOn = "SQL Prompt Import";
        profile.Metadata.Created = DateTime.UtcNow;
        profile.Metadata.Modified = DateTime.UtcNow;

        result.Profile = profile;
        return result;
    }

    /// <summary>
    /// Imports from a file path.
    /// </summary>
    public static SqlPromptImportResult ImportFromFile(string filePath, string? profileName = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"SQL Prompt style file not found: '{filePath}'", filePath);

        var xmlContent = File.ReadAllText(filePath);
        return Import(xmlContent, profileName);
    }

    private static bool ToBool(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.Ordinal)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapCasing(string value)
    {
        if (value.Contains("upper", StringComparison.OrdinalIgnoreCase)) return "UPPERCASE";
        if (value.Contains("lower", StringComparison.OrdinalIgnoreCase)) return "lowercase";
        if (value.Contains("pascal", StringComparison.OrdinalIgnoreCase)) return "PascalCase";
        if (value.Contains("camel", StringComparison.OrdinalIgnoreCase)) return "camelCase";
        return "AsIs";
    }
}
