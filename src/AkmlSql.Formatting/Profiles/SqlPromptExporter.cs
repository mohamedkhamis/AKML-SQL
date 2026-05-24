using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Exports an AKML <see cref="FormattingProfile"/> back to the SQL Prompt
/// <c>.sqlpromptstylev2</c> XML format. The output uses the
/// <c>&lt;SqlPromptStyle&gt;&lt;Options&gt;&lt;Option Name= Value= /&gt;&lt;/Options&gt;&lt;/SqlPromptStyle&gt;</c>
/// shape — the canonical form <see cref="SqlPromptImporter"/> consumes — so a
/// file exported by AKML can be re-imported by another team member's SQL Prompt
/// install (round-trip).
///
/// <para>
/// <b>Round-trip semantics.</b> Every key in <see cref="SqlPromptImporter"/>'s
/// <c>OptionMap</c> that has a known inverse mapping is emitted. Settings that
/// AKML supports but SQL Prompt does not have an equivalent for are skipped —
/// they remain available inside the AKML <c>.akmlstyle</c> file but are not
/// representable in SQL Prompt's schema. Settings imported from a SQL Prompt
/// file that AKML doesn't yet map are NOT preserved here — round-trip
/// preservation of unknown XML option names would require capturing them at
/// import time (a future enhancement; see
/// <c>specs/020-sqlprompt-visual-parity/contracts/ipc-profile-import-sqlprompt.md</c>
/// for the schema).
/// </para>
///
/// <para>
/// The exporter never throws on individual mapping failures — it best-effort emits
/// every option it can and surfaces the count for telemetry / UI. Only catastrophic
/// failures (e.g. profile is null) throw.
/// </para>
/// </summary>
public class SqlPromptExportResult
{
    /// <summary>The serialised XML, ready to write to a <c>.sqlpromptstylev2</c> file.</summary>
    public string Xml { get; set; } = string.Empty;

    /// <summary>How many AKML profile settings were successfully written as <c>&lt;Option&gt;</c> elements.</summary>
    public int WrittenCount { get; set; }
}

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class SqlPromptExporter
{
    /// <summary>
    /// Inverse of <see cref="SqlPromptImporter"/>'s <c>OptionMap</c> — for each SQL Prompt option
    /// name, a function that reads the current value from a <see cref="FormattingProfile"/> and
    /// returns the string representation expected in SQL Prompt's XML (or <c>null</c> to skip).
    /// </summary>
    private static readonly Dictionary<string, Func<FormattingProfile, string?>> ReverseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ----- Whitespace / Indentation -----
        ["InsertTabs"]                  = p => Bool(p.Whitespace.TabStyle.Equals("tabs", StringComparison.OrdinalIgnoreCase)),
        ["TabSize"]                     = p => p.Whitespace.TabSize.ToString(),
        ["IndentationSize"]             = p => p.Whitespace.TabSize.ToString(),
        ["MaxLineWidth"]                = p => p.Whitespace.MaxLineWidth.ToString(),
        ["ColumnLimit"]                 = p => p.Whitespace.MaxLineWidth.ToString(),
        ["SpaceAfterComma"]             = p => Bool(p.Whitespace.SpaceAfterComma),
        ["SpaceAroundOperators"]        = p => Bool(p.Whitespace.SpaceAroundOperators),
        ["SpaceInsideParentheses"]      = p => Bool(p.Whitespace.SpaceInsideParentheses),
        ["TrailingWhitespace"]          = p => Bool(p.Whitespace.TrailingWhitespace.Equals("remove", StringComparison.OrdinalIgnoreCase)),
        ["FinalNewline"]                = p => Bool(p.Whitespace.FinalNewline.Equals("ensure", StringComparison.OrdinalIgnoreCase)),
        ["LineBreakBeforeClause"]       = p => Bool(p.Whitespace.LineBreakBeforeClause),
        ["LineBreakAfterClause"]        = p => Bool(p.Whitespace.LineBreakAfterClause),
        ["LineBreakBeforeComma"]        = p => Bool(p.Whitespace.LineBreakBeforeComma),
        ["LineBreakAfterComma"]         = p => Bool(p.Whitespace.LineBreakAfterComma),
        ["EmptyLinesBetweenStatements"] = p => p.Whitespace.EmptyLineBetweenStatements.ToString(),
        ["LineBreakAfterSemicolon"]     = p => Bool(p.Whitespace.LineBreakAfterSemicolon),
        ["PreserveEmptyLinesAfterBatch"] = p => Bool(p.Whitespace.PreserveEmptyLinesAfterBatch),

        // ----- Casing -----
        ["KeywordCasing"]    = p => p.Casing.ReservedKeywords,
        ["FunctionCasing"]   = p => p.Casing.BuiltInFunctions,
        ["DataTypeCasing"]   = p => p.Casing.BuiltInDataTypes,
        ["IdentifierCasing"] = p => p.Casing.Identifiers,

        // ----- Comma / List -----
        ["CommaPosition"]   = p => p.List.CommaPosition.Equals("leading", StringComparison.OrdinalIgnoreCase) ? "before" : "after",
        ["AlignAliases"]    = p => Bool(p.List.AlignAliases),
        ["OneItemPerLine"]  = p => Bool(p.List.OneItemPerLine),
        ["IndentListItems"] = p => Bool(p.List.IndentListItems),
        ["AlignItemsAcrossClauses"] = p => Bool(p.List.AlignItemsAcrossClauses),

        // ----- DML -----
        ["SelectOnNewLine"]  = p => Bool(p.Dml.SelectItemsOnNewLine),
        ["FromOnNewLine"]    = p => Bool(p.Dml.FromOnNewLine),
        ["WhereOnNewLine"]   = p => Bool(p.Dml.WhereOnNewLine),
        ["GroupByOnNewLine"] = p => Bool(p.Dml.GroupByOnNewLine),
        ["HavingOnNewLine"]  = p => Bool(p.Dml.HavingOnNewLine),
        ["OrderByOnNewLine"] = p => Bool(p.Dml.OrderByOnNewLine),
        ["ANDORNewLine"]     = p => p.Dml.AndOrNewLine.Equals("before", StringComparison.OrdinalIgnoreCase) ? "before" : "after",
        ["SetOnNewLine"]     = p => Bool(p.Dml.SetOnNewLine),
        ["ValuesOnNewLine"]  = p => Bool(p.Dml.ValuesOnNewLine),
        ["DmlCollapseShortStatements"] = p => Bool(p.Dml.CollapseShortStatements),
        ["DmlCollapseStatementsShorterThan"] = p => p.Dml.CollapseThreshold.ToString(),
        ["DmlCollapseShortSubqueries"] = p => Bool(p.Dml.CollapseShortSubqueries),
        ["DmlCollapseSubqueriesShorterThan"] = p => p.Dml.SubqueryCollapseThreshold.ToString(),

        // ----- JOIN -----
        ["JoinOnNewLine"]      = p => Bool(p.Join.OnNewLine),
        ["IndentJoin"]         = p => Bool(p.Join.IndentJoin),
        ["OnConditionNewLine"] = p => Bool(p.Join.OnConditionNewLine),
        ["EmptyLineBeforeJoin"] = p => Bool(p.Join.EmptyLineBeforeJoin),
        ["AlignJoinKeyword"] = p => p.Join.AlignJoinKeyword.Trim().ToLowerInvariant() switch
        {
            "left" => "ToTable",
            "none" => "None",
            "indentedfromfrom" => "IndentedFromFrom",   // Phase B closure — 4th SQL Prompt variant
            _ => "RightAligned",   // "right" + AKML default — emit SQL Prompt's enum token
        },

        // ----- DDL -----
        ["AlignDataTypes"]   = p => Bool(p.Ddl.AlignDataTypes),
        ["AlignConstraints"] = p => Bool(p.Ddl.AlignConstraints),
        ["AsOnNewLine"]      = p => Bool(p.Ddl.AsOnNewLine),
        ["BeginOnNewLine"]   = p => Bool(p.Ddl.BeginOnNewLine),
        ["PlaceFirstProcedureParameterOnNewLine"] = p => p.Ddl.FirstParameterOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerThanWrap",   // "auto" + AKML default — emit SQL Prompt's enum token, not the AKML value
        },
        ["DdlCollapseShortStatements"] = p => Bool(p.Ddl.CollapseShortDdl),
        ["DdlCollapseStatementsShorterThan"] = p => p.Ddl.CollapseThreshold.ToString(),

        // ----- Control Flow -----
        ["IfBeginOnNewLine"]      = p => Bool(p.ControlFlow.BeginOnNewLine),
        ["ElseOnNewLine"]         = p => Bool(p.ControlFlow.ElseOnNewLine),
        ["IndentBetweenBeginEnd"] = p => Bool(p.ControlFlow.IndentBetweenBeginEnd),
        ["ControlFlowCollapseShortIfElse"] = p => Bool(p.ControlFlow.CollapseShortIfElse),
        ["ControlFlowCollapseStatementsShorterThan"] = p => p.ControlFlow.CollapseThreshold.ToString(),

        // ----- CASE -----
        ["WhenOnNewLine"]    = p => Bool(p.Case.WhenOnNewLine),
        ["ThenOnNewLine"]    = p => Bool(p.Case.ThenOnNewLine),
        ["CaseElseOnNewLine"] = p => Bool(p.Case.ElseOnNewLine),
        ["EndOnNewLine"]     = p => Bool(p.Case.EndOnNewLine),
        // T082 — SQL Prompt CASE additions
        ["PlaceFirstWhenOnNewLine"] = p => p.Case.FirstWhenOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerThanWrap",                  // "auto" + AKML default
        },
        ["WhenAlignment"] = p => p.Case.WhenAlignment.Trim().ToLowerInvariant() switch
        {
            "tofirstitem" => "ToFirstItem",
            "indentedfromcase" => "IndentedFromCase",
            _ => "ToCase",                            // default + unrecognised
        },
        ["PlaceCaseExpressionOnNewLine"] = p => Bool(p.Case.ExpressionOnNewLine),

        // ----- CTE additions (T080) -----
        ["PlaceCteColumnsOnNewLine"] = p => p.Cte.PlaceColumnsOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerThanWrap",                  // default + unrecognised
        },

        // ----- Operators (T083) -----
        ["OperatorsAlignment"] = p => p.Operators.Alignment.Trim().ToLowerInvariant() switch
        {
            "indentedfromstatement" => "IndentedFromStatement",
            "rightaligned" => "RightAligned",
            _ => "InlineWithStatement",               // default + unrecognised
        },
        ["PlaceBetweenKeywordOnNewLine"] = p => Bool(p.Operators.BetweenOnNewLine),

        // ----- IN Statements (T084) -----
        ["InStatementsAlignment"] = p => p.InStatements.Alignment.Trim().ToLowerInvariant() switch
        {
            "wrapped" => "Wrapped",
            "rightaligned" => "RightAligned",
            _ => "Stacked",                           // default + unrecognised
        },

        // ===== Phase B closure — full SQL Prompt feature parity =====

        // ----- Whitespace additions -----
        ["TabBehavior"] = p => p.Whitespace.TabStyle.Trim().ToLowerInvariant() switch
        {
            "tabs" => "TabsOnly",
            "tabswhenpossible" => "TabsWherePossible",
            _ => "SpacesOnly",
        },
        ["BlankLinesBeforeGo"] = p => p.Whitespace.BlankLinesBeforeGoCount.ToString(),

        // ----- Lists addition -----
        ["PlaceSubsequentItemsOnNewLines"] = p => p.List.PlaceSubsequentItemsOnNewLines.Trim().ToLowerInvariant() switch
        {
            "never" => "Never",
            "iflongerthanwrap" => "IfLongerThanWrap",
            _ => "Always",
        },

        // ----- DML additions -----
        ["RightAlignClauses"] = p => Bool(p.Dml.RightAlignClauses),
        ["ClauseIndentation"] = p => p.Dml.ClauseIndentation.Trim().ToLowerInvariant() switch
        {
            "indented" => "Indented",
            "rightaligned" => "RightAlignedToStatement",
            _ => "None",
        },
        ["InsertColumnListFormat"] = p => p.Dml.InsertColumnListFormat.Trim().ToLowerInvariant() switch
        {
            "compact" => "Compact",
            "iflongerthanwrap" => "IfLongerThanWrap",
            _ => "OnePerLine",
        },
        ["ValuesFormat"] = p => p.Dml.ValuesFormat.Trim().ToLowerInvariant() switch
        {
            "compact" => "Compact",
            "iflongerthanwrap" => "IfLongerThanWrap",
            _ => "OnePerLine",
        },

        // ----- DDL addition -----
        ["ConstraintColumnsOnNewLine"] = p => p.Ddl.ConstraintColumnsOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerOrMultipleColumns",
        },

        // ----- JOIN additions -----
        ["OnConditionIndentMode"] = p => p.Join.OnConditionIndentMode.Trim().ToLowerInvariant() switch
        {
            "totable" => "ToTable",
            "indentedfromtable" => "IndentedFromTable",
            _ => "IndentedFromJoin",
        },

        // ----- CASE addition -----
        ["CaseEndAlignment"] = p => p.Case.EndAlignment.Trim().ToLowerInvariant() switch
        {
            "indented" => "Indented",
            _ => "ToCase",
        },

        // ----- CTE addition -----
        ["CtePlaceAsOnNewLine"] = p => Bool(p.Cte.AsOnNewLine),

        // ----- Operators addition -----
        ["PlaceAndBetweenBetweenOnNewLine"] = p => Bool(p.Operators.AndBetweenOnNewLine),

        // ----- IN Statements addition -----
        ["InStatementsPlaceItemsOnNewLine"] = p => p.InStatements.PlaceItemsOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerThanWrap",
        },

        // ----- Function Calls (new) -----
        ["FunctionCallsPlaceParametersOnNewLine"] = p => p.FunctionCalls.PlaceParametersOnNewLine.Trim().ToLowerInvariant() switch
        {
            "always" => "Always",
            "never" => "Never",
            _ => "IfLongerThanWrap",
        },
        ["IndentFunctionParameters"] = p => Bool(p.FunctionCalls.IndentParameters),

        // ----- Comments (new) -----
        ["MultilineCommentFormatting"] = p => p.Comments.MultilineFormatting.Trim().ToLowerInvariant() switch
        {
            "normaliseindent" => "NormaliseIndent",
            "joinshortlines" => "JoinShortLines",
            _ => "Preserve",
        },
        ["RecognizeCommonCommentPatterns"] = p => Bool(p.Comments.RecognizeCommonPatterns),

        // ----- Parenthesis -----
        ["OpenParenOnSameLine"] = p => Bool(p.Parenthesis.OpenOnSameLine),
        ["CloseParenOnNewLine"] = p => Bool(p.Parenthesis.CloseOnNewLine.Equals("true", StringComparison.OrdinalIgnoreCase)),
        ["IndentParenContents"] = p => Bool(p.Parenthesis.IndentContents),
        ["CollapseShortParenthesisContents"] = p => Bool(p.Parenthesis.CollapseShort),
        ["CollapseParenthesesShorterThan"] = p => p.Parenthesis.CollapseThreshold.ToString(),

        // ----- Format Actions -----
        ["InsertSemicolons"]   = p => Bool(p.FormatActions.InsertSemicolons),
        ["QualifyObjectNames"] = p => Bool(p.FormatActions.QualifyObjectNames),
        ["AddSquareBrackets"]  = p => Bool(p.FormatActions.AddSquareBrackets),
    };

    /// <summary>Total number of distinct SQL Prompt option names the exporter knows about.</summary>
    public static int KnownOptionCount => ReverseMap.Count;

    /// <summary>
    /// Exports the given profile to a SQL Prompt-compatible XML string. Never throws on
    /// individual option-write failures; the count of successfully-written options is on
    /// the returned <see cref="SqlPromptExportResult"/>.
    /// </summary>
    public static SqlPromptExportResult Export(FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var options = new XElement("Options");
        var written = 0;

        // Preserve insertion order — Redgate's XML is human-edited so stable ordering helps diffs.
        foreach (var (name, getter) in ReverseMap)
        {
            string? value;
            try { value = getter(profile); }
            catch { continue; } // Best-effort — skip getters that throw

            if (value is null) continue;

            options.Add(new XElement("Option",
                new XAttribute("Name", name),
                new XAttribute("Value", value)));
            written++;
        }

        var root = new XElement("SqlPromptStyle", options);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        return new SqlPromptExportResult
        {
            Xml = doc.ToString(SaveOptions.None),
            WrittenCount = written,
        };
    }

    /// <summary>
    /// Convenience overload — exports and writes atomically (temp + rename) to a file path.
    /// </summary>
    public static SqlPromptExportResult ExportToFile(FormattingProfile profile, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(destinationPath);

        var result = Export(profile);

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = destinationPath + ".tmp";
        File.WriteAllText(tempPath, result.Xml);
        File.Move(tempPath, destinationPath, overwrite: true);

        return result;
    }

    private static string Bool(bool b) => b ? "true" : "false";
}
