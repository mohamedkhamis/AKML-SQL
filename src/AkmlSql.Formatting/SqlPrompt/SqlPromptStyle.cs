namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// Read-only, typed view of a <see cref="SqlPromptStyleDocument"/> for the layout engine: every
/// option resolved once (stored value or SQL Prompt default). Choice values keep Redgate's
/// spelling, so the engine compares against the same strings the style file holds.
/// </summary>
public sealed class SqlPromptStyle
{
    public SqlPromptStyle(SqlPromptStyleDocument doc)
    {
        Whitespace = new WhitespaceStyle(doc);
        Lists = new ListStyle(doc);
        Parentheses = new ParenthesisStyle(doc);
        Casing = new CasingStyle(doc);
        Dml = new DmlStyle(doc);
        Ddl = new DdlStyle(doc);
        ControlFlow = new ControlFlowStyle(doc);
        Cte = new CteStyle(doc);
        Variables = new VariableStyle(doc);
        Join = new JoinStyle(doc);
        Insert = new InsertStyle(doc);
        FunctionCalls = new FunctionCallStyle(doc);
        Case = new CaseStyle(doc);
        Operators = new OperatorStyle(doc);
    }

    public static SqlPromptStyle Default { get; } = new(SqlPromptStyleDocument.CreateDefault("Default", "default"));

    public WhitespaceStyle Whitespace { get; }
    public ListStyle Lists { get; }
    public ParenthesisStyle Parentheses { get; }
    public CasingStyle Casing { get; }
    public DmlStyle Dml { get; }
    public DdlStyle Ddl { get; }
    public ControlFlowStyle ControlFlow { get; }
    public CteStyle Cte { get; }
    public VariableStyle Variables { get; }
    public JoinStyle Join { get; }
    public InsertStyle Insert { get; }
    public FunctionCallStyle FunctionCalls { get; }
    public CaseStyle Case { get; }
    public OperatorStyle Operators { get; }

    public sealed class WhitespaceStyle(SqlPromptStyleDocument d)
    {
        public string SpacesOrTabs { get; } = d.Get("whitespace.spacesOrTabs");
        public int TabSize { get; } = d.GetInt("whitespace.numberOfSpacesInTabs");
        public bool WrapLongLines { get; } = d.GetBool("whitespace.wrapLongLines");
        public int WrapLinesLongerThan { get; } = d.GetInt("whitespace.wrapLinesLongerThan");
        public string BeforeSemicolon { get; } = d.Get("whitespace.whiteSpaceBeforeSemiColon");
        public bool PreserveEmptyLinesBetweenStatements { get; } = d.GetBool("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements");
        public int EmptyLinesBetweenStatements { get; } = d.GetInt("whitespace.newLines.emptyLinesBetweenStatements");
        public bool PreserveEmptyLinesAfterBatchSeparator { get; } = d.GetBool("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator");
        public int EmptyLinesAfterBatchSeparator { get; } = d.GetInt("whitespace.newLines.emptyLinesAfterBatchSeparator");
        public bool AlignMultilineComments { get; } = d.GetBool("whitespace.newLines.alignMultilineCommentsMatchingPatterns");

        /// <summary>The width lines are kept within: the wrap width, or effectively unlimited when wrapping is off.</summary>
        public int MaxLineLength => WrapLongLines ? WrapLinesLongerThan : int.MaxValue / 4;
    }

    public sealed class ListStyle(SqlPromptStyleDocument d)
    {
        public string PlaceFirstItemOnNewLine { get; } = d.Get("lists.placeFirstItemOnNewLine");
        public string PlaceSubsequentItemsOnNewLines { get; } = d.Get("lists.placeSubsequentItemsOnNewLines");
        public bool AlignSubsequentItemsWithFirstItem { get; } = d.GetBool("lists.alignSubsequentItemsWithFirstItem");
        public bool AlignItemsAcrossClauses { get; } = d.GetBool("lists.alignItemsAcrossClauses");
        public bool IndentListItems { get; } = d.GetBool("lists.indentListItems");
        public bool AlignItemsToTabStops { get; } = d.GetBool("lists.alignItemsToTabStops");
        public bool AlignAliases { get; } = d.GetBool("lists.alignAliases");
        public bool AlignComments { get; } = d.GetBool("lists.alignComments");
        public bool CommasBeforeItems { get; } = d.GetBool("lists.placeCommasBeforeItems");
        public bool SpaceBeforeComma { get; } = d.GetBool("lists.addSpaceBeforeComma");
        public bool SpaceAfterComma { get; } = d.GetBool("lists.addSpaceAfterComma");
        public string CommaAlignment { get; } = d.Get("lists.commaAlignment");
    }

    public sealed class ParenthesisStyle(SqlPromptStyleDocument d)
    {
        public string Style { get; } = d.Get("parentheses.parenthesisStyle");
        public bool IndentContents { get; } = d.GetBool("parentheses.indentParenthesesContents");
        public bool CollapseShort { get; } = d.GetBool("parentheses.collapseShortParenthesisContents");
        public int CollapseShorterThan { get; } = d.GetInt("parentheses.collapseParenthesesShorterThan");
        public bool SpacesAround { get; } = d.GetBool("parentheses.addSpacesAroundParentheses");
        public bool SpacesInside { get; } = d.GetBool("parentheses.addSpacesInsideParentheses");
    }

    public sealed class CasingStyle(SqlPromptStyleDocument d)
    {
        public string ReservedKeywords { get; } = d.Get("casing.reservedKeywords");
        public string BuiltInFunctions { get; } = d.Get("casing.builtInFunctions");
        public string BuiltInDataTypes { get; } = d.Get("casing.builtInDataTypes");
        public string GlobalVariables { get; } = d.Get("casing.globalVariables");
        public bool UseObjectDefinitionCase { get; } = d.GetBool("casing.useObjectDefinitionCase");
    }

    public sealed class DmlStyle(SqlPromptStyleDocument d)
    {
        public string ClauseAlignment { get; } = d.Get("dml.clauses.clauseAlignment");
        public int ClauseIndentation { get; } = d.GetInt("dml.clauses.clauseIndentation");
        public string PlaceFromTableOnNewLine { get; } = d.Get("dml.listItems.placeFromTableOnNewLine");
        public string PlaceWhereConditionOnNewLine { get; } = d.Get("dml.listItems.placeWhereConditionOnNewLine");
        public string PlaceGroupByAndOrderByOnNewLine { get; } = d.Get("dml.listItems.placeGroupByAndOrderByOnNewLine");
        public bool PlaceInsertTableOnNewLine { get; } = d.GetBool("dml.placeInsertTableOnNewLine");
        public bool PlaceDistinctAndTopOnNewLine { get; } = d.GetBool("dml.placeDistinctAndTopClausesOnNewLine");
        public bool NewLineAfterDistinctAndTop { get; } = d.GetBool("dml.addNewLineAfterDistinctAndTopClauses");
        public bool CollapseShortStatements { get; } = d.GetBool("dml.collapseShortStatements");
        public int CollapseStatementsShorterThan { get; } = d.GetInt("dml.collapseStatementsShorterThan");
        public bool CollapseShortSubqueries { get; } = d.GetBool("dml.collapseShortSubqueries");
        public int CollapseSubqueriesShorterThan { get; } = d.GetInt("dml.collapseSubqueriesShorterThan");
    }

    public sealed class DdlStyle(SqlPromptStyleDocument d)
    {
        public string ParenthesisStyle { get; } = d.Get("ddl.parenthesisStyle");
        public bool IndentParenthesesContents { get; } = d.GetBool("ddl.indentParenthesesContents");
        public bool AlignDataTypesAndConstraints { get; } = d.GetBool("ddl.alignDataTypesAndConstraints");
        public bool PlaceConstraintsOnNewLines { get; } = d.GetBool("ddl.placeConstraintsOnNewLines");
        public string PlaceConstraintColumnsOnNewLines { get; } = d.Get("ddl.placeConstraintColumnsOnNewLines");
        public bool IndentClauses { get; } = d.GetBool("ddl.indentClauses");
        public string PlaceFirstProcedureParameterOnNewLine { get; } = d.Get("ddl.placeFirstProcedureParameterOnNewLine");
        public bool CollapseShortStatements { get; } = d.GetBool("ddl.collapseShortStatements");
        public int CollapseStatementsShorterThan { get; } = d.GetInt("ddl.collapseStatementsShorterThan");
    }

    public sealed class ControlFlowStyle(SqlPromptStyleDocument d)
    {
        public bool PlaceBeginAndEndOnNewLine { get; } = d.GetBool("controlFlow.placeBeginAndEndOnNewLine");
        public bool IndentBeginAndEnd { get; } = d.GetBool("controlFlow.indentBeginAndEndKeywords");
        public bool IndentContents { get; } = d.GetBool("controlFlow.indentContentsOfStatements");
        public bool CollapseShortStatements { get; } = d.GetBool("controlFlow.collapseShortStatements");
        public int CollapseStatementsShorterThan { get; } = d.GetInt("controlFlow.collapseStatementsShorterThan");
    }

    public sealed class CteStyle(SqlPromptStyleDocument d)
    {
        public string ParenthesisStyle { get; } = d.Get("cte.parenthesisStyle");
        public bool IndentContents { get; } = d.GetBool("cte.indentContents");
        public bool PlaceNameOnNewLine { get; } = d.GetBool("cte.placeNameOnNewLine");
        public bool IndentName { get; } = d.GetBool("cte.indentName");
        public bool PlaceColumnsOnNewLine { get; } = d.GetBool("cte.placeColumnsOnNewLine");
        public string ColumnAlignment { get; } = d.Get("cte.columnAlignment");
        public bool PlaceAsOnNewLine { get; } = d.GetBool("cte.placeAsOnNewLine");
        public string AsAlignment { get; } = d.Get("cte.asAlignment");
    }

    public sealed class VariableStyle(SqlPromptStyleDocument d)
    {
        public bool AlignDataTypesAndValues { get; } = d.GetBool("variables.alignDataTypesAndValues");
        public bool SpaceBetweenDataTypeAndPrecision { get; } = d.GetBool("variables.addSpaceBetweenDataTypeAndPrecision");
        public bool PlaceAssignedValueOnNewLineIfTooLong { get; } = d.GetBool("variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength");
        public bool PlaceEqualsSignOnNewLine { get; } = d.GetBool("variables.placeEqualsSignOnNewLine");
    }

    public sealed class JoinStyle(SqlPromptStyleDocument d)
    {
        public bool PlaceJoinOnNewLine { get; } = d.GetBool("joinStatements.join.placeOnNewLine");
        public string JoinAlignment { get; } = d.Get("joinStatements.join.keywordAlignment");
        public bool EmptyLineBetweenJoins { get; } = d.GetBool("joinStatements.join.insertEmptyLineBetweenJoinClauses");
        public bool PlaceJoinTableOnNewLine { get; } = d.GetBool("joinStatements.join.placeJoinTableOnNewLine");
        public bool IndentJoinTable { get; } = d.GetBool("joinStatements.join.indentJoinTable");
        public bool PlaceOnOnNewLine { get; } = d.GetBool("joinStatements.on.placeOnNewLine");
        public string OnAlignment { get; } = d.Get("joinStatements.on.keywordAlignment");
        public bool PlaceConditionOnNewLine { get; } = d.GetBool("joinStatements.on.placeConditionOnNewLine");
        public string ConditionAlignment { get; } = d.Get("joinStatements.on.conditionAlignment");
    }

    public sealed class InsertStyle(SqlPromptStyleDocument d)
    {
        public string ColumnsParenthesisStyle { get; } = d.Get("insertStatements.columns.parenthesisStyle");
        public bool ColumnsIndentContents { get; } = d.GetBool("insertStatements.columns.indentContents");
        public string PlaceSubsequentColumnsOnNewLines { get; } = d.Get("insertStatements.columns.placeSubsequentColumnsOnNewLines");
        public string ValuesParenthesisStyle { get; } = d.Get("insertStatements.values.parenthesisStyle");
        public bool ValuesIndentContents { get; } = d.GetBool("insertStatements.values.indentContents");
        public string PlaceSubsequentValuesOnNewLines { get; } = d.Get("insertStatements.values.placeSubsequentValuesOnNewLines");
    }

    public sealed class FunctionCallStyle(SqlPromptStyleDocument d)
    {
        public string PlaceArgumentsOnNewLines { get; } = d.Get("functionCalls.placeArgumentsOnNewLines");
        public bool SpacesAroundParentheses { get; } = d.GetBool("functionCalls.addSpacesAroundParentheses");
        public bool SpacesAroundArgumentList { get; } = d.GetBool("functionCalls.addSpacesAroundArgumentList");
        public bool SpaceBetweenEmptyParentheses { get; } = d.GetBool("functionCalls.addSpaceBetweenEmptyParentheses");
    }

    public sealed class CaseStyle(SqlPromptStyleDocument d)
    {
        public bool PlaceExpressionOnNewLine { get; } = d.GetBool("caseExpressions.placeExpressionOnNewLine");
        public string PlaceFirstWhenOnNewLine { get; } = d.Get("caseExpressions.placeFirstWhenOnNewLine");
        public string WhenAlignment { get; } = d.Get("caseExpressions.whenAlignment");
        public bool PlaceThenOnNewLine { get; } = d.GetBool("caseExpressions.placeThenOnNewLine");
        public string ThenAlignment { get; } = d.Get("caseExpressions.thenAlignment");
        public bool PlaceElseOnNewLine { get; } = d.GetBool("caseExpressions.placeElseOnNewLine");
        public bool AlignElseToWhen { get; } = d.GetBool("caseExpressions.alignElseToWhen");
        public bool PlaceEndOnNewLine { get; } = d.GetBool("caseExpressions.placeEndOnNewLine");
        public string EndAlignment { get; } = d.Get("caseExpressions.endAlignment");
        public bool CollapseShort { get; } = d.GetBool("caseExpressions.collapseShortCaseExpressions");
        public int CollapseShorterThan { get; } = d.GetInt("caseExpressions.collapseCaseExpressionsShorterThan");
    }

    public sealed class OperatorStyle(SqlPromptStyleDocument d)
    {
        public bool AlignComparison { get; } = d.GetBool("operators.comparison.align");
        public bool SpacesAroundComparison { get; } = d.GetBool("operators.comparison.addSpacesAround");
        public bool SpacesAroundArithmetic { get; } = d.GetBool("operators.arithmetic.addSpacesAround");
        public string AndOrPlaceOnNewLine { get; } = d.Get("operators.andOr.placeOnNewLine");
        public string AndOrAlignment { get; } = d.Get("operators.andOr.alignment");
        public bool AndOrBeforeCondition { get; } = d.GetBool("operators.andOr.placeKeywordBeforeCondition");
        public bool BetweenOnNewLine { get; } = d.GetBool("operators.between.placeOnNewLine");
        public bool BetweenAndOnNewLine { get; } = d.GetBool("operators.between.placeAndKeywordOnNewLine");
        public string BetweenAndAlignment { get; } = d.Get("operators.between.andAlignment");
        public bool InOpenParenOnNewLine { get; } = d.GetBool("operators.in.placeOpeningParenthesisOnNewLine");
        public string InAlignment { get; } = d.Get("operators.in.alignment");
        public string InFirstValueOnNewLine { get; } = d.Get("operators.in.placeFirstValueOnNewLine");
        public string InSubsequentValuesOnNewLines { get; } = d.Get("operators.in.placeSubsequentValuesOnNewLines");
        public bool InSpaceAroundContents { get; } = d.GetBool("operators.in.addSpaceAroundInContents");
    }
}
