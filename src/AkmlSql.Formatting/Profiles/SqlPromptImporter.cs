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

        // ----- JOIN -----
        ["JoinOnNewLine"] = (p, v) => p.Join.OnNewLine = ToBool(v),
        ["IndentJoin"] = (p, v) => p.Join.IndentJoin = ToBool(v),
        ["OnConditionNewLine"] = (p, v) => p.Join.OnConditionNewLine = ToBool(v),
        ["EmptyLineBeforeJoin"] = (p, v) => p.Join.EmptyLineBeforeJoin = ToBool(v),

        // ----- DDL -----
        ["AlignDataTypes"] = (p, v) => p.Ddl.AlignDataTypes = ToBool(v),
        ["AlignConstraints"] = (p, v) => p.Ddl.AlignConstraints = ToBool(v),
        ["AsOnNewLine"] = (p, v) => p.Ddl.AsOnNewLine = ToBool(v),
        ["BeginOnNewLine"] = (p, v) => p.Ddl.BeginOnNewLine = ToBool(v),

        // ----- Control Flow -----
        ["IfBeginOnNewLine"] = (p, v) => p.ControlFlow.BeginOnNewLine = ToBool(v),
        ["ElseOnNewLine"] = (p, v) => p.ControlFlow.ElseOnNewLine = ToBool(v),
        ["IndentBetweenBeginEnd"] = (p, v) => p.ControlFlow.IndentBetweenBeginEnd = ToBool(v),

        // ----- CASE -----
        ["WhenOnNewLine"] = (p, v) => p.Case.WhenOnNewLine = ToBool(v),
        ["ThenOnNewLine"] = (p, v) => p.Case.ThenOnNewLine = ToBool(v),
        ["CaseElseOnNewLine"] = (p, v) => p.Case.ElseOnNewLine = ToBool(v),
        ["EndOnNewLine"] = (p, v) => p.Case.EndOnNewLine = ToBool(v),

        // ----- Parenthesis -----
        ["OpenParenOnSameLine"] = (p, v) => p.Parenthesis.OpenOnSameLine = ToBool(v),
        ["CloseParenOnNewLine"] = (p, v) => p.Parenthesis.CloseOnNewLine = ToBool(v) ? "true" : "false",
        ["IndentParenContents"] = (p, v) => p.Parenthesis.IndentContents = ToBool(v),

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
