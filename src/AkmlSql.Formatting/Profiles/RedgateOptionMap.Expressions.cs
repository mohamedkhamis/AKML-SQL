namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    static partial void RegisterJoinInsertFunctionCaseOperators()
    {
        // ----- joinStatements -----
        Add("joinStatements.join.placeOnNewLine", "true", (p, v) => p.Join.OnNewLine = B(v));
        Add("joinStatements.join.keywordAlignment", "toFrom", (p, v) => p.Join.AlignJoinKeyword = v.Trim().ToLowerInvariant() switch
        {
            "rightalignedtofrom" => "right",
            "totable" => "toTable",
            "indented" => "indentedFromFrom",
            _ => "left", // toFrom
        });
        Add("joinStatements.join.indentJoinTable", "true", (p, v) => p.Join.IndentJoin = B(v));
        AddUnsupported("joinStatements.join.placeJoinTableOnNewLine", "false",
            "AKML keeps the joined table on the JOIN line; placing it on its own line is not modeled.");
        Add("joinStatements.join.insertEmptyLineBetweenJoinClauses", "false", (p, v) => p.Join.EmptyLineBeforeJoin = B(v));
        Add("joinStatements.on.placeOnNewLine", "true", (p, v) => p.Join.OnConditionNewLine = B(v));
        Add("joinStatements.on.keywordAlignment", "toJoin", (p, v) => p.Join.OnConditionIndent = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indent",
            _ => "toJoin", // toJoin/rightAlignedToJoin/rightAlignedToInner/toTable — phase 3 extends; only 'indent' renders today
        });
        AddUnsupported("joinStatements.on.placeConditionOnNewLine", "false",
            "AKML controls ON-keyword placement (on.placeOnNewLine) but has no independent break control for the ON condition expression.");
        AddUnsupported("joinStatements.on.conditionAlignment", "toOnKeyword",
            "ON-condition column alignment is not modeled; conditions follow the ON keyword's indent.");

        // ----- insertStatements -----
        Add("insertStatements.columns.parenthesisStyle", "expandedToStatement", (p, v) => p.InsertStatements.Columns.ParenthesisStyle = NormalizeParenStyle(v));
        Add("insertStatements.columns.indentContents", "true", (p, v) => p.InsertStatements.Columns.IndentContents = B(v));
        Add("insertStatements.columns.placeSubsequentColumnsOnNewLines", "always", (p, v) => p.InsertStatements.Columns.PlaceSubsequentItemsOnNewLines = NormalizePlacement(v));
        Add("insertStatements.values.parenthesisStyle", "compactToStatement", (p, v) => p.InsertStatements.Values.ParenthesisStyle = NormalizeParenStyle(v));
        Add("insertStatements.values.indentContents", "false", (p, v) => p.InsertStatements.Values.IndentContents = B(v));
        Add("insertStatements.values.placeSubsequentValuesOnNewLines", "never", (p, v) => p.InsertStatements.Values.PlaceSubsequentItemsOnNewLines = NormalizePlacement(v));

        // ----- functionCalls -----
        Add("functionCalls.placeArgumentsOnNewLines", "ifLongerThanMaxLineLength", (p, v) => p.FunctionCalls.PlaceParametersOnNewLine = NormalizePlacement(v));
        Add("functionCalls.addSpacesAroundParentheses", "false", (p, v) => p.FunctionCalls.SpaceAroundParentheses = B(v));
        Add("functionCalls.addSpacesAroundArgumentList", "false", (p, v) => p.FunctionCalls.SpaceAroundArgumentList = B(v));
        Add("functionCalls.addSpaceBetweenEmptyParentheses", "false", (p, v) => p.FunctionCalls.SpaceBetweenEmptyParentheses = B(v));

        // ----- caseExpressions -----
        Add("caseExpressions.placeFirstWhenOnNewLine", "always", (p, v) => p.Case.FirstWhenOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "ifinputexpression" => "auto",
            _ => "always",
        });
        Add("caseExpressions.placeExpressionOnNewLine", "true", (p, v) => p.Case.ExpressionOnNewLine = B(v));
        Add("caseExpressions.whenAlignment", "indentedFromCase", (p, v) => p.Case.WhenAlignment = v.Trim().ToLowerInvariant() switch
        {
            "tocase" => "toCase",
            "tofirstitem" => "toFirstItem",
            _ => "indentedFromCase",
        });
        Add("caseExpressions.placeThenOnNewLine", "false", (p, v) => p.Case.ThenOnNewLine = B(v));
        Add("caseExpressions.thenAlignment", "indentedFromWhen", (p, v) => p.Case.ThenAlignment = v.Trim().ToLowerInvariant() switch
        {
            "towhen" => "toWhen",
            "towhenexpression" => "toWhenExpression",
            "intentedfromwhen" => "indentedFromWhen", // Redgate's own historical typo build
            _ => "indentedFromWhen",
        });
        Add("caseExpressions.placeElseOnNewLine", "true", (p, v) => p.Case.ElseOnNewLine = B(v));
        AddUnsupported("caseExpressions.alignElseToWhen", "true",
            "ELSE alignment follows whenAlignment in AKML's CASE model; independent ELSE alignment is not modeled.");
        Add("caseExpressions.placeEndOnNewLine", "true", (p, v) => p.Case.EndOnNewLine = B(v));
        Add("caseExpressions.endAlignment", "toCase", (p, v) => p.Case.EndAlignment = v.Trim().ToLowerInvariant() switch
        {
            "towhen" or "rightalignedtowhen" => "indented",
            _ => "toCase",
        });
        Add("caseExpressions.collapseShortCaseExpressions", "false", (p, v) => p.Case.CollapseShortCase = B(v));
        Add("caseExpressions.collapseCaseExpressionsShorterThan", "80", (p, v) => p.Case.CollapseThreshold = I(v, 80));

        // ----- operators -----
        Add("operators.andOr.alignment", "leftAligned", (p, v) => p.Operators.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "rightaligned" => "rightAligned",
            "beforefirstlistitem" => "beforeFirstListItem",
            "tofirstlistitem" => "toFirstListItem",
            "indented" => "indentedFromStatement",
            _ => "inlineWithStatement", // leftAligned
        });
        AddUnsupported("operators.andOr.placeOnNewLine", "always",
            "AKML always places each AND/OR condition on its own line; 'never'/'ifLongerThanMaxLineLength' are not supported.");
        Add("operators.andOr.placeKeywordBeforeCondition", "true", (p, v) => p.Dml.AndOrNewLine = B(v) ? "before" : "after");
        Add("operators.between.placeOnNewLine", "true", (p, v) => p.Operators.BetweenOnNewLine = B(v));
        Add("operators.between.placeAndKeywordOnNewLine", "false", (p, v) => p.Operators.AndBetweenOnNewLine = B(v));
        Add("operators.between.andAlignment", "toBetween", (p, v) => p.Operators.BetweenAndAlignment = v.Trim().ToLowerInvariant() switch
        {
            "rightalignedtobetween" => "rightAlignedToBetween",
            "tobeginningofexpression" => "toBeginningOfExpression",
            _ => "toBetween",
        });
        Add("operators.in.placeFirstValueOnNewLine", "ifLongerThanMaxLineLength", (p, v) => p.InStatements.PlaceItemsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "always" or "ifsubsequentvalues" => "always",
            _ => "ifLongerThanWrap",
        });
        AddUnsupported("operators.in.placeSubsequentValuesOnNewLines", "ifLongerThanMaxLineLength",
            "AKML stacks IN-list items all-or-none via in.placeFirstValueOnNewLine; independent subsequent-values control is not modeled.");
        AddUnsupported("operators.in.placeOpeningParenthesisOnNewLine", "false",
            "Breaking before the IN list's opening parenthesis is not modeled.");
        Add("operators.in.alignment", "leftAligned", (p, v) => p.InStatements.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "rightaligned" => "rightAligned",
            "indented" => "stacked",
            _ => "stacked",
        });
        Add("operators.in.addSpaceAroundInContents", "false", (p, v) => p.InStatements.SpaceAroundContents = B(v));
    }

    private static string NormalizePlacement(string v) => v.Trim().ToLowerInvariant() switch
    {
        "always" => "always",
        "never" => "never",
        _ => "ifLongerThanWrap", // ifLongerThanMaxLineLength
    };
}
