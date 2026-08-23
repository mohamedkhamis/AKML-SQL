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
        AddUnsupported("dml.placeInsertTableOnNewLine", "false",
            "AKML keeps the INSERT target table on the INSERT INTO line; placing it on its own line is not modeled.");
        AddUnsupported("dml.clauses.clauseAlignment", "leftAligned",
            "AKML's Dml.RightAlignClauses is a boolean (left/right) with no representation for the third Redgate arm 'toFirstListItem'; mapping would silently collapse that arm to true or false. Needs a real 3-state field before this can be wired (dead field today — not consulted by any layout rule).");
        AddUnsupported("dml.clauses.clauseIndentation", "0",
            "Type mismatch: Redgate's clauseIndentation is an integer spaces-count (0-8); AKML's Dml.ClauseIndentation is a 3-value string enum (none/indented/rightAligned) designed against an older SQL Prompt XML shape and is not consulted by any layout rule (dead field). Needs a new integer field before this can be wired.");
        AddUnsupported("dml.listItems.placeFromTableOnNewLine", "never",
            "AKML's Dml.FromOnNewLine is a plain boolean (always/never); Redgate's tri-state adds 'ifMultiple' (break only when there are multiple tables), which has no distinct representation and would silently collapse to always or never.");
        AddUnsupported("dml.listItems.placeWhereConditionOnNewLine", "never",
            "AKML's Dml.WhereOnNewLine is a plain boolean (always/never); Redgate's tri-state adds 'ifMultiple' (break only when there are multiple conditions), which has no distinct representation.");
        AddUnsupported("dml.listItems.placeGroupByAndOrderByOnNewLine", "never",
            "AKML models GROUP BY and ORDER BY as two independent booleans (Dml.GroupByOnNewLine / Dml.OrderByOnNewLine), while Redgate controls both with one tri-state key (always/never/ifMultiple); neither the joint control nor the 'ifMultiple' arm can be represented faithfully.");

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
        AddUnsupported("ddl.indentClauses", "false",
            "AKML has no DDL-clause indentation control; the equivalent DML field (Dml.ClauseIndentation) is itself a dead, type-mismatched field (see dml.clauses.clauseIndentation) so there is nothing safe to reuse.");
        // ddl.placeFirstProcedureParameterOnNewLine maps to the LIVE Ddl.FirstParameterOnNewLine
        // field consulted unconditionally by DdlRules.cs:344-345 (always | auto-if->1-param | never) —
        // exact match to Redgate's always/never/ifMultipleItems tri-state.
        Add("ddl.placeFirstProcedureParameterOnNewLine", "ifMultipleItems", (p, v) => p.Ddl.FirstParameterOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" => "always",
            "never" => "never",
            _ => "auto", // ifMultipleItems
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
        AddUnsupported("variables.addSpaceBetweenDataTypeAndPrecision", "false",
            "AKML's Whitespace.SpaceBeforeParentheses is a single global 'space before any parenthesis' toggle already claimed by parentheses.addSpacesAroundParentheses; reusing it here for the DECLARE/SET-scoped data-type-precision case would collide whenever the two settings differ. No scoped field exists.");
        AddUnsupported("variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength", "true",
            "AKML's DECLARE/SET layout (DeclareRules) only implements one-declaration-per-line expansion and data-type/default-value column alignment; there is no line-length-triggered wrap for the assigned value. Revisit if phase-3 adds wrap-aware DECLARE/SET layout.");
        Add("variables.placeEqualsSignOnNewLine", "false", (p, v) => p.Declare.EqualsOnNewLine = B(v));
    }
}
