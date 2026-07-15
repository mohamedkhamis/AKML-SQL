namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    static partial void RegisterDmlDdlControlFlowCteVariables()
    {
        // ----- dml -----
        Add("dml.addNewLineAfterDistinctAndTopClauses", "false", (p, v) => p.Dml.NewLineAfterDistinctTop = B(v));
        Add("dml.placeDistinctAndTopClausesOnNewLine", "false", (p, v) =>
        {
            p.Dml.TopOnSameLine = !B(v);
            p.Dml.DistinctOnSameLine = !B(v);
        });
        Add("dml.collapseShortStatements", "false", (p, v) => p.Dml.CollapseShortStatements = B(v));
        Add("dml.collapseStatementsShorterThan", "80", (p, v) => p.Dml.CollapseThreshold = I(v, 80));
        Add("dml.collapseShortSubqueries", "false", (p, v) => p.Dml.CollapseShortSubqueries = B(v));
        Add("dml.collapseSubqueriesShorterThan", "80", (p, v) => p.Dml.SubqueryCollapseThreshold = I(v, 80));
        Add("dml.placeInsertTableOnNewLine", "false", (_, _) => { }); // consumed by INSERT layout in phase 3; stored implicitly false

        // ----- ddl -----
        Add("ddl.parenthesisStyle", "compactSimple", (p, v) => p.Ddl.ParenthesisStyle = NormalizeParenStyle(v));
        Add("ddl.indentParenthesesContents", "false", (p, v) => p.Ddl.IndentParenContents = B(v));
        Add("ddl.alignDataTypesAndConstraints", "true", (p, v) => p.Ddl.AlignDataTypes = B(v));
        Add("ddl.placeConstraintsOnNewLines", "false", (p, v) => p.Ddl.ConstraintsOnNewLine = B(v));
        Add("ddl.placeConstraintColumnsOnNewLines", "ifLongerThanMaxLineLength", (p, v) => p.Ddl.ConstraintColumnsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" => "always",
            "iflongerormultiplecolumns" => "ifLongerOrMultipleColumns",
            _ => "ifLongerThanWrap",
        });
        Add("ddl.collapseShortStatements", "false", (p, v) => p.Ddl.CollapseShortDdl = B(v));
        Add("ddl.collapseStatementsShorterThan", "80", (p, v) => p.Ddl.CollapseThreshold = I(v, 80));

        // ----- controlFlow -----
        Add("controlFlow.indentBeginAndEndKeywords", "false", (p, v) => p.ControlFlow.IndentBeginEndKeywords = B(v));
        Add("controlFlow.placeBeginAndEndOnNewLine", "true", (p, v) => p.ControlFlow.BeginOnNewLine = B(v));
        Add("controlFlow.indentContentsOfStatements", "true", (p, v) => p.ControlFlow.IndentBetweenBeginEnd = B(v));
        Add("controlFlow.collapseShortStatements", "false", (p, v) => p.ControlFlow.CollapseShortIfElse = B(v));
        Add("controlFlow.collapseStatementsShorterThan", "80", (p, v) => p.ControlFlow.CollapseThreshold = I(v, 80));

        // ----- cte -----
        Add("cte.parenthesisStyle", "compactSimple", (p, v) => p.Cte.ParenthesisStyle = NormalizeParenStyle(v));
        Add("cte.indentContents", "false", (p, v) => p.Cte.CteBodyIndent = B(v));
        Add("cte.placeNameOnNewLine", "false", (p, v) => p.Cte.PlaceNameOnNewLine = B(v));
        Add("cte.indentName", "false", (p, v) => p.Cte.IndentName = B(v));
        Add("cte.columnAlignment", "leftAligned", (p, v) => p.Cte.ColumnAlignment = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indented",
            "rightaligned" => "rightAligned",
            _ => "leftAligned",
        });
        Add("cte.placeColumnsOnNewLine", "false", (p, v) => p.Cte.PlaceColumnsOnNewLine = B(v) ? "always" : "never");
        Add("cte.placeAsOnNewLine", "true", (p, v) => p.Cte.AsOnNewLine = B(v));
        AddUnsupported("cte.asAlignment", "leftAligned",
            "AS-keyword alignment applies only when AS is on its own line; AKML models AS placement but not its alignment column. Revisit with phase-3 CTE work if goldens require it.");

        // ----- variables -----
        Add("variables.alignDataTypesAndValues", "true", (p, v) =>
        {
            p.Declare.AlignDataTypes = B(v);
            p.Declare.AlignDefaultValues = B(v);
        });
        Add("variables.placeEqualsSignOnNewLine", "false", (p, v) => p.Declare.EqualsOnNewLine = B(v));
        Add("variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength", "true", (_, _) => { }); // phase-3 DECLARE/SET wrap behavior; no distinct field needed — wrap pass consults MaxLineWidth
    }
}
