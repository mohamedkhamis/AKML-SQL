using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Root object for an .akmlstyle formatting profile.
/// Contains metadata, 12 option categories, format actions, and extension data.
/// </summary>
public class FormattingProfile
{
    [JsonPropertyName("metadata")]
    public ProfileMetadata Metadata { get; set; } = new();

    [JsonPropertyName("whitespace")]
    public WhitespaceOptions Whitespace { get; set; } = new();

    [JsonPropertyName("casing")]
    public CasingOptions Casing { get; set; } = new();

    [JsonPropertyName("list")]
    public ListOptions List { get; set; } = new();

    [JsonPropertyName("parenthesis")]
    public ParenthesisOptions Parenthesis { get; set; } = new();

    [JsonPropertyName("dml")]
    public DmlOptions Dml { get; set; } = new();

    [JsonPropertyName("join")]
    public JoinOptions Join { get; set; } = new();

    [JsonPropertyName("ddl")]
    public DdlOptions Ddl { get; set; } = new();

    [JsonPropertyName("controlFlow")]
    public ControlFlowOptions ControlFlow { get; set; } = new();

    [JsonPropertyName("case")]
    public CaseOptions Case { get; set; } = new();

    [JsonPropertyName("cte")]
    public CteOptions Cte { get; set; } = new();

    [JsonPropertyName("expression")]
    public ExpressionOptions Expression { get; set; } = new();

    [JsonPropertyName("formatActions")]
    public FormatActionConfig FormatActions { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// ---------------------------------------------------------------------------
// Option category classes
// ---------------------------------------------------------------------------

public class WhitespaceOptions
{
    [JsonPropertyName("tabStyle")]
    public string TabStyle { get; set; } = "spaces";

    [JsonPropertyName("tabSize")]
    public int TabSize { get; set; } = 4;

    [JsonPropertyName("indentStyle")]
    public string IndentStyle { get; set; } = "block";

    [JsonPropertyName("maxLineWidth")]
    public int MaxLineWidth { get; set; } = 120;

    [JsonPropertyName("lineBreakBeforeClause")]
    public bool LineBreakBeforeClause { get; set; } = true;

    [JsonPropertyName("lineBreakAfterClause")]
    public bool LineBreakAfterClause { get; set; }

    [JsonPropertyName("lineBreakBeforeComma")]
    public bool LineBreakBeforeComma { get; set; }

    [JsonPropertyName("lineBreakAfterComma")]
    public bool LineBreakAfterComma { get; set; } = true;

    [JsonPropertyName("emptyLineBetweenStatements")]
    public int EmptyLineBetweenStatements { get; set; } = 1;

    [JsonPropertyName("emptyLineBeforeGO")]
    public bool EmptyLineBeforeGo { get; set; } = true;

    [JsonPropertyName("emptyLineAfterGO")]
    public bool EmptyLineAfterGo { get; set; } = true;

    [JsonPropertyName("preserveEmptyLinesAfterBatch")]
    public bool PreserveEmptyLinesAfterBatch { get; set; }

    [JsonPropertyName("preserveEmptyLines")]
    public bool PreserveEmptyLines { get; set; } = true;

    [JsonPropertyName("maxConsecutiveEmptyLines")]
    public int MaxConsecutiveEmptyLines { get; set; } = 2;

    [JsonPropertyName("trailingWhitespace")]
    public string TrailingWhitespace { get; set; } = "remove";

    [JsonPropertyName("finalNewline")]
    public string FinalNewline { get; set; } = "ensure";

    [JsonPropertyName("spaceAfterComma")]
    public bool SpaceAfterComma { get; set; } = true;

    [JsonPropertyName("spaceAroundOperators")]
    public bool SpaceAroundOperators { get; set; } = true;

    [JsonPropertyName("spaceAroundBooleanOperators")]
    public bool SpaceAroundBooleanOperators { get; set; } = true;

    [JsonPropertyName("spaceInsideParentheses")]
    public bool SpaceInsideParentheses { get; set; }

    [JsonPropertyName("spaceBeforeParentheses")]
    public bool SpaceBeforeParentheses { get; set; }

    [JsonPropertyName("lineBreakAfterSemicolon")]
    public bool LineBreakAfterSemicolon { get; set; } = true;
}

public class CasingOptions
{
    [JsonPropertyName("reservedKeywords")]
    public string ReservedKeywords { get; set; } = "UPPERCASE";

    [JsonPropertyName("builtInFunctions")]
    public string BuiltInFunctions { get; set; } = "UPPERCASE";

    [JsonPropertyName("builtInDataTypes")]
    public string BuiltInDataTypes { get; set; } = "lowercase";

    [JsonPropertyName("systemObjects")]
    public string SystemObjects { get; set; } = "lowercase";

    [JsonPropertyName("globalVariables")]
    public string GlobalVariables { get; set; } = "lowercase";

    [JsonPropertyName("localVariables")]
    public string LocalVariables { get; set; } = "AsIs";

    [JsonPropertyName("identifiers")]
    public string Identifiers { get; set; } = "AsIs";

    [JsonPropertyName("syncWithDatabase")]
    public bool SyncWithDatabase { get; set; }

    [JsonPropertyName("camelCaseDictionary")]
    public bool CamelCaseDictionary { get; set; } = true;

    [JsonPropertyName("applyOnTyping")]
    public bool ApplyOnTyping { get; set; } = true;
}

public class ListOptions
{
    [JsonPropertyName("commaPosition")]
    public string CommaPosition { get; set; } = "trailing";

    [JsonPropertyName("alignItemsAcrossClauses")]
    public bool AlignItemsAcrossClauses { get; set; } = true;

    [JsonPropertyName("alignAliases")]
    public bool AlignAliases { get; set; } = true;

    [JsonPropertyName("oneItemPerLine")]
    public bool OneItemPerLine { get; set; } = true;

    [JsonPropertyName("collapseShortLists")]
    public bool CollapseShortLists { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 60;

    [JsonPropertyName("indentListItems")]
    public bool IndentListItems { get; set; } = true;

    [JsonPropertyName("alignDataTypesInDDL")]
    public bool AlignDataTypesInDdl { get; set; } = true;

    [JsonPropertyName("alignValuesInInsert")]
    public bool AlignValuesInInsert { get; set; } = true;

    [JsonPropertyName("spaceAfterListComma")]
    public bool SpaceAfterListComma { get; set; } = true;
}

public class ParenthesisOptions
{
    [JsonPropertyName("openOnSameLine")]
    public bool OpenOnSameLine { get; set; } = true;

    [JsonPropertyName("closeOnNewLine")]
    public string CloseOnNewLine { get; set; } = "false";

    [JsonPropertyName("collapseShort")]
    public bool CollapseShort { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 40;

    [JsonPropertyName("indentContents")]
    public bool IndentContents { get; set; } = true;

    [JsonPropertyName("spaceInside")]
    public bool SpaceInside { get; set; }

    [JsonPropertyName("removeRedundant")]
    public bool RemoveRedundant { get; set; }

    [JsonPropertyName("createTableColumns")]
    public string CreateTableColumns { get; set; } = "newLine";

    [JsonPropertyName("procedureParameters")]
    public string ProcedureParameters { get; set; } = "newLine";

    [JsonPropertyName("subqueryStyle")]
    public string SubqueryStyle { get; set; } = "indent";
}

public class DmlOptions
{
    [JsonPropertyName("selectItemsOnNewLine")]
    public bool SelectItemsOnNewLine { get; set; } = true;

    [JsonPropertyName("selectStarOnSameLine")]
    public bool SelectStarOnSameLine { get; set; } = true;

    [JsonPropertyName("fromOnNewLine")]
    public bool FromOnNewLine { get; set; } = true;

    [JsonPropertyName("whereOnNewLine")]
    public bool WhereOnNewLine { get; set; } = true;

    [JsonPropertyName("andOrNewLine")]
    public string AndOrNewLine { get; set; } = "before";

    [JsonPropertyName("andOrIndent")]
    public string AndOrIndent { get; set; } = "alignWithWhere";

    [JsonPropertyName("groupByOnNewLine")]
    public bool GroupByOnNewLine { get; set; } = true;

    [JsonPropertyName("havingOnNewLine")]
    public bool HavingOnNewLine { get; set; } = true;

    [JsonPropertyName("orderByOnNewLine")]
    public bool OrderByOnNewLine { get; set; } = true;

    [JsonPropertyName("topOnSameLine")]
    public bool TopOnSameLine { get; set; } = true;

    [JsonPropertyName("distinctOnSameLine")]
    public bool DistinctOnSameLine { get; set; } = true;

    [JsonPropertyName("intoOnNewLine")]
    public bool IntoOnNewLine { get; set; } = true;

    [JsonPropertyName("valuesOnNewLine")]
    public bool ValuesOnNewLine { get; set; } = true;

    [JsonPropertyName("setOnNewLine")]
    public bool SetOnNewLine { get; set; } = true;

    [JsonPropertyName("deleteFromOnSameLine")]
    public bool DeleteFromOnSameLine { get; set; } = true;

    [JsonPropertyName("mergeWhenOnNewLine")]
    public bool MergeWhenOnNewLine { get; set; } = true;

    [JsonPropertyName("collapseShortStatements")]
    public bool CollapseShortStatements { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 80;

    [JsonPropertyName("collapseShortSubqueries")]
    public bool CollapseShortSubqueries { get; set; } = true;

    [JsonPropertyName("subqueryCollapseThreshold")]
    public int SubqueryCollapseThreshold { get; set; } = 60;
}

public class JoinOptions
{
    [JsonPropertyName("onNewLine")]
    public bool OnNewLine { get; set; } = true;

    [JsonPropertyName("indentJoin")]
    public bool IndentJoin { get; set; }

    [JsonPropertyName("onConditionNewLine")]
    public bool OnConditionNewLine { get; set; } = true;

    [JsonPropertyName("onConditionIndent")]
    public string OnConditionIndent { get; set; } = "indent";

    [JsonPropertyName("multipleOnConditions")]
    public string MultipleOnConditions { get; set; } = "newLine";

    [JsonPropertyName("emptyLineBeforeJoin")]
    public bool EmptyLineBeforeJoin { get; set; }

    [JsonPropertyName("alignJoinKeyword")]
    public string AlignJoinKeyword { get; set; } = "right";

    [JsonPropertyName("joinTypeStyle")]
    public string JoinTypeStyle { get; set; } = "explicit";

    [JsonPropertyName("crossApplyNewLine")]
    public bool CrossApplyNewLine { get; set; } = true;
}

public class DdlOptions
{
    [JsonPropertyName("createTableColumnsOnNewLine")]
    public bool CreateTableColumnsOnNewLine { get; set; } = true;

    [JsonPropertyName("alignDataTypes")]
    public bool AlignDataTypes { get; set; } = true;

    [JsonPropertyName("alignConstraints")]
    public bool AlignConstraints { get; set; } = true;

    [JsonPropertyName("constraintsOnNewLine")]
    public bool ConstraintsOnNewLine { get; set; }

    [JsonPropertyName("inlineConstraintStyle")]
    public string InlineConstraintStyle { get; set; } = "sameLine";

    [JsonPropertyName("tableConstraintsSeparate")]
    public bool TableConstraintsSeparate { get; set; } = true;

    [JsonPropertyName("firstParameterOnNewLine")]
    public string FirstParameterOnNewLine { get; set; } = "auto";

    [JsonPropertyName("parameterAlignment")]
    public string ParameterAlignment { get; set; } = "aligned";

    [JsonPropertyName("alignParameterDataTypes")]
    public bool AlignParameterDataTypes { get; set; } = true;

    [JsonPropertyName("alignParameterDefaults")]
    public bool AlignParameterDefaults { get; set; } = true;

    [JsonPropertyName("asOnNewLine")]
    public bool AsOnNewLine { get; set; } = true;

    [JsonPropertyName("beginOnNewLine")]
    public bool BeginOnNewLine { get; set; } = true;

    [JsonPropertyName("collapseShortDDL")]
    public bool CollapseShortDdl { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 60;
}

public class ControlFlowOptions
{
    [JsonPropertyName("beginOnNewLine")]
    public bool BeginOnNewLine { get; set; } = true;

    [JsonPropertyName("endOnNewLine")]
    public bool EndOnNewLine { get; set; } = true;

    [JsonPropertyName("indentBetweenBeginEnd")]
    public bool IndentBetweenBeginEnd { get; set; } = true;

    [JsonPropertyName("collapseShortIfElse")]
    public bool CollapseShortIfElse { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 60;

    [JsonPropertyName("elseOnNewLine")]
    public bool ElseOnNewLine { get; set; } = true;

    [JsonPropertyName("elseAlignWithIf")]
    public bool ElseAlignWithIf { get; set; } = true;

    [JsonPropertyName("tryCatchOnNewLine")]
    public bool TryCatchOnNewLine { get; set; } = true;
}

public class CaseOptions
{
    [JsonPropertyName("whenOnNewLine")]
    public bool WhenOnNewLine { get; set; } = true;

    [JsonPropertyName("thenOnNewLine")]
    public bool ThenOnNewLine { get; set; }

    [JsonPropertyName("elseOnNewLine")]
    public bool ElseOnNewLine { get; set; } = true;

    [JsonPropertyName("endOnNewLine")]
    public bool EndOnNewLine { get; set; } = true;

    [JsonPropertyName("indentWhen")]
    public bool IndentWhen { get; set; } = true;

    [JsonPropertyName("alignThen")]
    public bool AlignThen { get; set; } = true;

    [JsonPropertyName("collapseShortCase")]
    public bool CollapseShortCase { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    public int CollapseThreshold { get; set; } = 60;
}

public class CteOptions
{
    [JsonPropertyName("withOnNewLine")]
    public bool WithOnNewLine { get; set; } = true;

    [JsonPropertyName("cteBodyIndent")]
    public bool CteBodyIndent { get; set; } = true;

    [JsonPropertyName("commaBeforeCte")]
    public bool CommaBeforeCte { get; set; }

    [JsonPropertyName("emptyLineBetweenCtes")]
    public bool EmptyLineBetweenCtes { get; set; } = true;
}

public class ExpressionOptions
{
    [JsonPropertyName("booleanOperatorNewLine")]
    public string BooleanOperatorNewLine { get; set; } = "before";

    [JsonPropertyName("betweenOnOneLine")]
    public bool BetweenOnOneLine { get; set; } = true;

    [JsonPropertyName("inListStyle")]
    public string InListStyle { get; set; } = "multiLine";

    [JsonPropertyName("inListThreshold")]
    public int InListThreshold { get; set; } = 60;

    [JsonPropertyName("existsSubqueryIndent")]
    public string ExistsSubqueryIndent { get; set; } = "indent";
}

public class FormatActionConfig
{
    [JsonPropertyName("applyLayout")]
    public bool ApplyLayout { get; set; } = true;

    [JsonPropertyName("applyCasing")]
    public bool ApplyCasing { get; set; } = true;

    [JsonPropertyName("insertSemicolons")]
    public bool InsertSemicolons { get; set; }

    [JsonPropertyName("removeSemicolons")]
    public bool RemoveSemicolons { get; set; }

    [JsonPropertyName("expandWildcards")]
    public bool ExpandWildcards { get; set; }

    [JsonPropertyName("qualifyObjectNames")]
    public bool QualifyObjectNames { get; set; }

    [JsonPropertyName("addAsKeyword")]
    public bool AddAsKeyword { get; set; } = true;

    [JsonPropertyName("addSquareBrackets")]
    public bool AddSquareBrackets { get; set; }
}
