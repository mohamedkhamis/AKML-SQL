namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// Spec 042 (tasks T149, T153, FR-145) - which options of a SQL Prompt style mean something when the script is PostgreSQL.
/// Most do, as they are: lists, parentheses, casing, clauses, joins, CASE, operators, CTEs, INSERT, function calls, and the layout of
/// definitions. Those that have no PostgreSQL meaning are listed here with the reason; the PostgreSQL formatter never reads them, and the
/// Format Styles page marks them "no effect for PostgreSQL" while a PostgreSQL connection is active. The table is next to the
/// catalog (not in the PostgreSQL library) because the browser's style editor reads it too.
/// </summary>
public static class SqlPromptApplicability
{
    public const string NoEffectLabel = "No effect for PostgreSQL";

    private const string NoBatchSeparator = "PostgreSQL has no batch separator.";
    private const string NoProceduralCode = "PostgreSQL procedural code (PL/pgSQL) is kept exactly as written.";
    private const string Subquery = "A subquery that fits on the line is always kept on it.";

    private static readonly Dictionary<string, string> NoEffect = new(StringComparer.Ordinal)
    {
        // whitespace
        ["whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator"] = NoBatchSeparator,
        ["whitespace.newLines.emptyLinesAfterBatchSeparator"] = NoBatchSeparator,
        ["whitespace.newLines.alignMultilineCommentsMatchingPatterns"] = "Comments are kept exactly as written.",

        // lists
        ["lists.alignAliases"] = "Aliases are not aligned for PostgreSQL.",
        ["lists.alignComments"] = "End-of-line comments are not aligned for PostgreSQL.",

        // parentheses
        ["parentheses.collapseShortParenthesisContents"] = "Parenthesis contents that fit on the line are always kept on it.",
        ["parentheses.collapseParenthesesShorterThan"] = "Parenthesis contents that fit on the line are always kept on it.",

        // casing
        ["casing.globalVariables"] = "PostgreSQL has no global variables.",
        ["casing.useObjectDefinitionCase"] = "PostgreSQL folds unquoted names to lower case; the formatter never changes a name.",

        // DML
        ["dml.collapseShortSubqueries"] = Subquery,
        ["dml.collapseSubqueriesShorterThan"] = Subquery,

        // DDL
        ["ddl.placeConstraintColumnsOnNewLines"] = "Constraint column lists are kept on one line.",
        ["ddl.placeFirstProcedureParameterOnNewLine"] = "Function and procedure parameters are kept as written.",
        ["ddl.collapseShortStatements"] = "Definitions are laid out as the style's other options say.",
        ["ddl.collapseStatementsShorterThan"] = "Definitions are laid out as the style's other options say.",

        // control flow and variables: PL/pgSQL is verbatim, and there is no DECLARE / SET @variable
        ["controlFlow.placeBeginAndEndOnNewLine"] = NoProceduralCode,
        ["controlFlow.indentBeginAndEndKeywords"] = NoProceduralCode,
        ["controlFlow.indentContentsOfStatements"] = NoProceduralCode,
        ["controlFlow.collapseShortStatements"] = NoProceduralCode,
        ["controlFlow.collapseStatementsShorterThan"] = NoProceduralCode,
        ["variables.alignDataTypesAndValues"] = "PostgreSQL has no DECLARE outside PL/pgSQL, which is kept as written.",
        ["variables.addSpaceBetweenDataTypeAndPrecision"] = "PostgreSQL has no DECLARE outside PL/pgSQL, which is kept as written.",
        ["variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength"] = "PostgreSQL has no variable assignment outside PL/pgSQL.",
        ["variables.placeEqualsSignOnNewLine"] = "PostgreSQL has no variable assignment outside PL/pgSQL.",

        // operators
        ["operators.comparison.align"] = "Comparison operators are not aligned for PostgreSQL.",
        ["operators.in.placeOpeningParenthesisOnNewLine"] = "The opening parenthesis of IN stays on the line of IN.",
        ["operators.in.alignment"] = "IN values align after the opening parenthesis.",
    };

    /// <summary>True when the PostgreSQL formatter never reads the option.</summary>
    public static bool IsNoEffectForPostgreSql(string path) => NoEffect.ContainsKey(path);

    /// <summary>Why the option has no effect for PostgreSQL, or null when it has one.</summary>
    public static string? ReasonNoEffect(string path) => NoEffect.GetValueOrDefault(path);

    /// <summary>Every option with no PostgreSQL effect, with its reason.</summary>
    public static IReadOnlyDictionary<string, string> NoEffectOptions => NoEffect;
}
