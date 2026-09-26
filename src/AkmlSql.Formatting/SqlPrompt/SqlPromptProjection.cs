using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// The SQL Prompt reading of a style written in AKML's own model: the inverse of the spec-031
/// import map (<c>RedgateOptionMap</c>), option by option. Options AKML's model has no field for
/// keep SQL Prompt's default. Used to open an AKML-model style in the SQL Prompt style editor.
/// </summary>
internal static class SqlPromptProjection
{
    public static SqlPromptStyleDocument Project(FormattingProfile p)
    {
        var d = SqlPromptStyleDocument.CreateDefault(p.Metadata.Name, p.Metadata.Id);
        void Set(string path, string? value)
        {
            if (value is null) return;
            try { d.Set(path, value); }
            catch (ArgumentException) { /* a value SQL Prompt has no spelling for: keep its default */ }
        }
        static string B(bool v) => v ? "true" : "false";
        static string I(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string? Placement(string? v) => v switch
        {
            "always" => "always",
            "never" => "never",
            "ifLongerThanWrap" => "ifLongerThanMaxLineLength",
            _ => null,
        };
        static string? Casing(string? v) => v switch
        {
            "UPPERCASE" => "uppercase",
            "lowercase" => "lowercase",
            "PascalCase" => "upperCamelCase",
            "camelCase" => "lowerCamelCase",
            "AsIs" => "leaveAsIs",
            _ => null,
        };

        var ws = p.Whitespace;
        Set("whitespace.spacesOrTabs", ws.TabStyle switch { "tabs" => "tabs", "tabsWhenPossible" => "tabsIfPossible", _ => "spaces" });
        Set("whitespace.numberOfSpacesInTabs", I(ws.TabSize));
        Set("whitespace.wrapLongLines", B(ws.WrapLongLines));
        Set("whitespace.wrapLinesLongerThan", I(ws.MaxLineWidth));
        Set("whitespace.whiteSpaceBeforeSemiColon", ws.SemicolonPlacement);
        Set("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements", B(ws.PreserveEmptyLines));
        Set("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator", B(ws.PreserveEmptyLinesAfterBatch));
        Set("whitespace.newLines.emptyLinesBetweenStatements", I(ws.EmptyLineBetweenStatements));
        Set("whitespace.newLines.emptyLinesAfterBatchSeparator", I(ws.EmptyLinesAfterBatchSeparator));
        Set("whitespace.newLines.alignMultilineCommentsMatchingPatterns",
            B(p.Comments.MultilineFormatting == "normaliseIndent" && p.Comments.RecognizeCommonPatterns));

        var list = p.List;
        Set("lists.placeSubsequentItemsOnNewLines", Placement(list.PlaceSubsequentItemsOnNewLines));
        Set("lists.alignItemsAcrossClauses", B(list.AlignItemsAcrossClauses));
        Set("lists.indentListItems", B(list.IndentListItems));
        Set("lists.alignItemsToTabStops", B(list.AlignItemsToTabStops));
        Set("lists.alignAliases", B(list.AlignAliases));
        Set("lists.placeCommasBeforeItems", B(list.CommaPosition == "leading"));
        Set("lists.addSpaceBeforeComma", B(list.SpaceBeforeComma));
        Set("lists.addSpaceAfterComma", B(ws.SpaceAfterComma));
        Set("lists.commaAlignment", list.CommaAlignment);

        var paren = p.Parenthesis;
        Set("parentheses.parenthesisStyle", paren.Style);
        Set("parentheses.indentParenthesesContents", B(paren.IndentContents));
        Set("parentheses.collapseParenthesesShorterThan", I(paren.CollapseThreshold));
        Set("parentheses.collapseShortParenthesisContents", B(paren.CollapseShort));
        Set("parentheses.addSpacesInsideParentheses", B(paren.SpaceInside));
        Set("parentheses.addSpacesAroundParentheses", B(ws.SpaceBeforeParentheses));

        var casing = p.Casing;
        Set("casing.reservedKeywords", Casing(casing.ReservedKeywords));
        Set("casing.builtInFunctions", Casing(casing.BuiltInFunctions));
        Set("casing.builtInDataTypes", Casing(casing.BuiltInDataTypes));
        Set("casing.globalVariables", Casing(casing.GlobalVariables));
        Set("casing.useObjectDefinitionCase", B(casing.SyncWithDatabase));

        var dml = p.Dml;
        Set("dml.addNewLineAfterDistinctAndTopClauses", B(dml.NewLineAfterDistinctTop));
        Set("dml.placeDistinctAndTopClausesOnNewLine", B(!dml.TopOnSameLine));
        Set("dml.collapseStatementsShorterThan", I(dml.CollapseThreshold));
        Set("dml.collapseShortStatements", B(dml.CollapseShortStatements));
        Set("dml.collapseSubqueriesShorterThan", I(dml.SubqueryCollapseThreshold));
        Set("dml.collapseShortSubqueries", B(dml.CollapseShortSubqueries));

        var ddl = p.Ddl;
        Set("ddl.parenthesisStyle", ddl.ParenthesisStyle);
        Set("ddl.indentParenthesesContents", B(ddl.IndentParenContents));
        Set("ddl.alignDataTypesAndConstraints", B(ddl.AlignDataTypes));
        Set("ddl.placeConstraintsOnNewLines", B(ddl.ConstraintsOnNewLine));
        Set("ddl.placeConstraintColumnsOnNewLines", ddl.ConstraintColumnsOnNewLine switch
        {
            "always" => "always",
            "ifLongerOrMultipleColumns" => "ifLongerOrMultipleColumns",
            "ifLongerThanWrap" => "ifLongerThanMaxLineLength",
            _ => null,
        });
        Set("ddl.placeFirstProcedureParameterOnNewLine", ddl.FirstParameterOnNewLine switch
        {
            "always" => "always",
            "never" => "never",
            "auto" => "ifMultipleItems",
            _ => null,
        });
        Set("ddl.collapseStatementsShorterThan", I(ddl.CollapseThreshold));
        Set("ddl.collapseShortStatements", B(ddl.CollapseShortDdl));

        var cf = p.ControlFlow;
        Set("controlFlow.indentBeginAndEndKeywords", B(cf.IndentBeginEndKeywords));
        Set("controlFlow.placeBeginAndEndOnNewLine", B(cf.BeginOnNewLine));
        Set("controlFlow.indentContentsOfStatements", B(cf.IndentBetweenBeginEnd));
        Set("controlFlow.collapseStatementsShorterThan", I(cf.CollapseThreshold));
        Set("controlFlow.collapseShortStatements", B(cf.CollapseShortIfElse));

        var cte = p.Cte;
        Set("cte.parenthesisStyle", cte.ParenthesisStyle);
        Set("cte.indentContents", B(cte.CteBodyIndent));
        Set("cte.placeNameOnNewLine", B(cte.PlaceNameOnNewLine));
        Set("cte.indentName", B(cte.IndentName));
        Set("cte.columnAlignment", cte.ColumnAlignment);
        Set("cte.placeColumnsOnNewLine", B(cte.PlaceColumnsOnNewLine == "always"));
        Set("cte.placeAsOnNewLine", B(cte.AsOnNewLine));

        Set("variables.alignDataTypesAndValues", B(p.Declare.AlignDataTypes));
        Set("variables.placeEqualsSignOnNewLine", B(p.Declare.EqualsOnNewLine));

        var join = p.Join;
        Set("joinStatements.join.placeOnNewLine", B(join.OnNewLine));
        Set("joinStatements.join.keywordAlignment", join.AlignJoinKeyword switch
        {
            "right" => "rightAlignedToFrom",
            "toTable" => "toTable",
            "indentedFromFrom" => "indented",
            "left" => "toFrom",
            _ => null,
        });
        Set("joinStatements.join.indentJoinTable", B(join.IndentJoin));
        Set("joinStatements.join.insertEmptyLineBetweenJoinClauses", B(join.EmptyLineBeforeJoin));
        Set("joinStatements.on.placeOnNewLine", B(join.OnConditionNewLine));
        Set("joinStatements.on.keywordAlignment", join.OnConditionIndent == "indent" ? "indented" : "toJoin");

        var columns = p.InsertStatements.Columns;
        Set("insertStatements.columns.parenthesisStyle", columns.ParenthesisStyle);
        Set("insertStatements.columns.indentContents", B(columns.IndentContents));
        Set("insertStatements.columns.placeSubsequentColumnsOnNewLines", Placement(columns.PlaceSubsequentItemsOnNewLines));
        var values = p.InsertStatements.Values;
        Set("insertStatements.values.parenthesisStyle", values.ParenthesisStyle);
        Set("insertStatements.values.indentContents", B(values.IndentContents));
        Set("insertStatements.values.placeSubsequentValuesOnNewLines", Placement(values.PlaceSubsequentItemsOnNewLines));

        var fc = p.FunctionCalls;
        Set("functionCalls.placeArgumentsOnNewLines", Placement(fc.PlaceParametersOnNewLine));
        Set("functionCalls.addSpacesAroundParentheses", B(fc.SpaceAroundParentheses));
        Set("functionCalls.addSpacesAroundArgumentList", B(fc.SpaceAroundArgumentList));
        Set("functionCalls.addSpaceBetweenEmptyParentheses", B(fc.SpaceBetweenEmptyParentheses));

        var c = p.Case;
        Set("caseExpressions.placeFirstWhenOnNewLine", c.FirstWhenOnNewLine switch
        {
            "never" => "never",
            "auto" => "ifInputExpression",
            "always" => "always",
            _ => null,
        });
        Set("caseExpressions.placeExpressionOnNewLine", B(c.ExpressionOnNewLine));
        Set("caseExpressions.whenAlignment", c.WhenAlignment);
        Set("caseExpressions.placeThenOnNewLine", B(c.ThenOnNewLine));
        Set("caseExpressions.thenAlignment", c.ThenAlignment);
        Set("caseExpressions.placeElseOnNewLine", B(c.ElseOnNewLine));
        Set("caseExpressions.placeEndOnNewLine", B(c.EndOnNewLine));
        Set("caseExpressions.endAlignment", c.EndAlignment == "indented" ? "toWhen" : "toCase");
        Set("caseExpressions.collapseCaseExpressionsShorterThan", I(c.CollapseThreshold));
        Set("caseExpressions.collapseShortCaseExpressions", B(c.CollapseShortCase));

        var o = p.Operators;
        Set("operators.andOr.alignment", o.Alignment switch
        {
            "rightAligned" => "rightAligned",
            "beforeFirstListItem" => "beforeFirstListItem",
            "toFirstListItem" => "toFirstListItem",
            "indentedFromStatement" => "indented",
            "inlineWithStatement" => "leftAligned",
            _ => null,
        });
        Set("operators.andOr.placeKeywordBeforeCondition", B(dml.AndOrNewLine != "after"));
        Set("operators.between.placeOnNewLine", B(o.BetweenOnNewLine));
        Set("operators.between.placeAndKeywordOnNewLine", B(o.AndBetweenOnNewLine));
        Set("operators.between.andAlignment", o.BetweenAndAlignment);

        var inList = p.InStatements;
        Set("operators.in.placeFirstValueOnNewLine", Placement(inList.PlaceItemsOnNewLine));
        Set("operators.in.alignment", inList.Alignment == "rightAligned" ? "rightAligned" : "leftAligned");
        Set("operators.in.addSpaceAroundInContents", B(inList.SpaceAroundContents));

        return d;
    }
}
