namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    static partial void RegisterJoinInsertFunctionCaseOperators()
    {
        // ----- joinStatements -----
        Add("joinStatements.join.placeOnNewLine", "true", (_, _) => { }); // AKML always breaks before JOIN; matches default true. Value false is honored by phase-3 join work if a golden demands it.
        Add("joinStatements.join.keywordAlignment", "toFrom", (p, v) => p.Join.AlignJoinKeyword = v.Trim().ToLowerInvariant() switch
        {
            "rightalignedtofrom" => "right",
            "totable" => "toTable",
            "indented" => "indentedFromFrom",
            _ => "left", // toFrom
        });
        Add("joinStatements.join.indentJoinTable", "true", (p, v) => p.Join.IndentJoin = B(v));
        Add("joinStatements.join.placeJoinTableOnNewLine", "false", (_, _) => { }); // no AKML model; false (default) is AKML behavior
        Add("joinStatements.join.insertEmptyLineBetweenJoinClauses", "false", (p, v) => p.Join.EmptyLineBeforeJoin = B(v));
        Add("joinStatements.on.placeOnNewLine", "true", (p, v) => p.Join.OnConditionNewLine = B(v));
        Add("joinStatements.on.keywordAlignment", "toJoin", (p, v) => p.Join.OnConditionIndent = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indent",
            _ => "toJoin", // toJoin/rightAlignedToJoin/rightAlignedToInner/toTable — phase 3 extends; only 'indent' renders today
        });
        Add("joinStatements.on.placeConditionOnNewLine", "false", (_, _) => { });
        Add("joinStatements.on.conditionAlignment", "toOnKeyword", (_, _) => { });

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
        Add("functionCalls.indentContents", "false", (p, v) => p.FunctionCalls.IndentParameters = B(v));

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
        Add("caseExpressions.alignElseToWhen", "true", (_, _) => { }); // follows WhenAlignment in AKML's model
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
        Add("operators.andOr.placeOnNewLine", "always", (_, _) => { }); // AKML breaks each condition; matches default
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
        Add("operators.in.placeSubsequentValuesOnNewLines", "ifLongerThanMaxLineLength", (_, _) => { }); // AKML stacks all-or-none via PlaceItemsOnNewLine
        Add("operators.in.placeOpeningParenthesisOnNewLine", "false", (_, _) => { });
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
