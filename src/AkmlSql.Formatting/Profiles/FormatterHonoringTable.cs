namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Spec 031 — which Redgate JSON options the layout engine renders end-to-end TODAY.
/// Phase 1 seeds this with the wired set from the spec's Option Fidelity Contract;
/// each Phase 3 feature adds its option paths as its corpus files go green.
/// Paths not present here (but mapped) classify as "mapped-pending-render".
/// </summary>
public static class FormatterHonoringTable
{
    private static readonly HashSet<string> Rendered = new(StringComparer.OrdinalIgnoreCase)
    {
        // Contract rows with Today = wired (spec.md Option Fidelity Contract)
        "whitespace.numberOfSpacesInTabs",
        "whitespace.wrapLinesLongerThan",
        "lists.placeCommasBeforeItems",
        "parentheses.indentParenthesesContents",
        "parentheses.collapseShortParenthesisContents",
        "parentheses.collapseParenthesesShorterThan",
        "parentheses.addSpacesInsideParentheses",
        "casing.reservedKeywords",
        "casing.builtInFunctions",
        "casing.builtInDataTypes",
        "dml.collapseStatementsShorterThan",
        "dml.collapseSubqueriesShorterThan",
        "ddl.indentParenthesesContents",
        "ddl.placeConstraintsOnNewLines",
        "ddl.collapseShortStatements",
        "ddl.collapseStatementsShorterThan",
        "controlFlow.collapseStatementsShorterThan",
        "cte.indentContents",
        "cte.placeAsOnNewLine",
        "variables.alignDataTypesAndValues",
        "joinStatements.join.indentJoinTable",
        "joinStatements.on.placeOnNewLine",
        "joinStatements.on.keywordAlignment",
        "functionCalls.placeArgumentsOnNewLines",
        "caseExpressions.placeFirstWhenOnNewLine",
        "caseExpressions.placeThenOnNewLine",
        "caseExpressions.collapseShortCaseExpressions",
        "caseExpressions.collapseCaseExpressionsShorterThan",
        "operators.between.placeOnNewLine",
        "operators.in.placeFirstValueOnNewLine",
        // n/a rows (hold by construction with the user's values):
        "whitespace.newLines.preserveExistingEmptyLinesBetweenStatements",
        "whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator",
    };

    public static bool IsRendered(string redgatePath) => Rendered.Contains(redgatePath);
}
