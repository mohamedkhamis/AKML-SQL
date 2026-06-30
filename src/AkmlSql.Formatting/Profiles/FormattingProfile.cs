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

    [JsonPropertyName("operators")]
    public OperatorsOptions Operators { get; set; } = new();

    [JsonPropertyName("inStatements")]
    public InStatementsOptions InStatements { get; set; } = new();

    [JsonPropertyName("functionCalls")]
    public FunctionCallsOptions FunctionCalls { get; set; } = new();

    [JsonPropertyName("comments")]
    public CommentsOptions Comments { get; set; } = new();

    [JsonPropertyName("declare")]
    public DeclareOptions Declare { get; set; } = new();

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

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Whitespace.BlankLinesBeforeGo</c> (0–5).
    /// Counts the blank lines inserted before the <c>GO</c> batch separator. Pairs with the
    /// pre-existing <see cref="EmptyLineBeforeGo"/> boolean (kept for back-compat — when
    /// <see cref="EmptyLineBeforeGo"/> is true and <see cref="BlankLinesBeforeGoCount"/> is 0,
    /// the boolean wins and a single blank line is inserted; non-zero count overrides).
    /// </summary>
    [JsonPropertyName("blankLinesBeforeGoCount")]
    public int BlankLinesBeforeGoCount { get; set; }

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

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Lists.PlaceSubsequentItemsOnNewLines</c>:
    /// "always" (default — match the legacy <see cref="OneItemPerLine"/> = true behaviour),
    /// "never" (force every list inline regardless of width),
    /// "ifLongerThanWrap" (break only when the rendered list would exceed
    /// <see cref="WhitespaceOptions.MaxLineWidth"/>). Distinct from
    /// <see cref="CollapseShortLists"/> + <see cref="CollapseThreshold"/> which control
    /// the inverse decision for *short* lists; this controls the global wrap rule.
    /// </summary>
    [JsonPropertyName("placeSubsequentItemsOnNewLines")]
    public string PlaceSubsequentItemsOnNewLines { get; set; } = "always";
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

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.RightAlignClauses</c>. When true, the
    /// clause keywords (SELECT / FROM / WHERE / GROUP BY / HAVING / ORDER BY) are
    /// right-justified to a common column rather than left-aligned. Pairs with
    /// <see cref="ClauseIndentation"/>.
    /// </summary>
    [JsonPropertyName("rightAlignClauses")]
    public bool RightAlignClauses { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.ClauseIndentation</c>:
    /// "none" (default — clauses at column 0), "indented" (clauses indented one level
    /// from the surrounding statement), "rightAligned" (clauses right-aligned to the
    /// widest clause keyword). Broader than the existing <see cref="AndOrIndent"/>
    /// which only controls boolean-operator indent inside WHERE.
    /// </summary>
    [JsonPropertyName("clauseIndentation")]
    public string ClauseIndentation { get; set; } = "none";

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.InsertColumnListFormat</c>:
    /// "onePerLine" (default — one column per line),
    /// "compact" (multiple columns per line, packed),
    /// "ifLongerThanWrap" (break only when the list would exceed
    /// <see cref="WhitespaceOptions.MaxLineWidth"/>). Distinct from
    /// <see cref="ListOptions.PlaceSubsequentItemsOnNewLines"/> which applies globally;
    /// this overrides specifically for <c>INSERT INTO t (...)</c> column lists.
    /// </summary>
    [JsonPropertyName("insertColumnListFormat")]
    public string InsertColumnListFormat { get; set; } = "onePerLine";

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.ValuesFormat</c>:
    /// "onePerLine" (default — one VALUES tuple per line),
    /// "compact" (tuples inline up to wrap),
    /// "ifLongerThanWrap" (break only when needed). Replaces the boolean
    /// <see cref="ValuesOnNewLine"/> as the more expressive control; the boolean is
    /// kept for back-compat (true ≡ "onePerLine", false ≡ "compact").
    /// </summary>
    [JsonPropertyName("valuesFormat")]
    public string ValuesFormat { get; set; } = "onePerLine";
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

    /// <summary>
    /// Spec 020 Phase B closure — extends <see cref="OnConditionIndent"/> from a free-form
    /// string into the canonical SQL Prompt enum: "indentedFromJoin" (default — current
    /// "indent" behaviour), "toTable" (align ON to the joined table column),
    /// "indentedFromTable" (indent one level from the joined table column).
    /// The legacy <see cref="OnConditionIndent"/> string is kept and continues to drive
    /// layout when set to "indent"; this new field takes precedence when non-default.
    /// </summary>
    [JsonPropertyName("onConditionIndentMode")]
    public string OnConditionIndentMode { get; set; } = "indentedFromJoin";
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

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Ddl.ConstraintColumnsOnNewLine</c>:
    /// "always", "never", "ifLongerOrMultipleColumns" (default — break only when there
    /// are multiple columns or the list would exceed wrap width). Controls how PRIMARY KEY
    /// / FOREIGN KEY / UNIQUE constraint column lists are placed.
    /// </summary>
    [JsonPropertyName("constraintColumnsOnNewLine")]
    public string ConstraintColumnsOnNewLine { get; set; } = "ifLongerOrMultipleColumns";
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

    /// <summary>
    /// Spec 020 T082 — SQL Prompt <c>caseExpressions.placeFirstWhenOnNewLine</c>:
    /// "auto" (default — honour <see cref="WhenOnNewLine"/> for the first WHEN),
    /// "always" (force first WHEN on a new line regardless),
    /// "never" (keep first WHEN inline with the CASE expression).
    /// </summary>
    [JsonPropertyName("firstWhenOnNewLine")]
    public string FirstWhenOnNewLine { get; set; } = "auto";

    /// <summary>
    /// Spec 020 T082 — SQL Prompt <c>caseExpressions.whenAlignment</c>:
    /// "toCase" (default — align WHEN with the CASE keyword),
    /// "toFirstItem" (align with the first WHEN's expression text),
    /// "indentedFromCase" (indent one level from CASE).
    /// </summary>
    [JsonPropertyName("whenAlignment")]
    // T008: "" = unset → honor the legacy IndentWhen flag in ResolveWhenIndent; an explicit
    // "toCase"/"toFirstItem"/"indentedFromCase" still wins.
    public string WhenAlignment { get; set; } = "";

    /// <summary>
    /// Spec 020 T082 — SQL Prompt <c>caseExpressions.placeExpressionOnNewLine</c>:
    /// when true, the simple-CASE expression goes on its own line below CASE.
    /// </summary>
    [JsonPropertyName("expressionOnNewLine")]
    public bool ExpressionOnNewLine { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>caseExpressions.endAlignment</c>:
    /// "toCase" (default — align END under the CASE keyword),
    /// "indented" (indent END one level from CASE).
    /// More expressive than the legacy bool <see cref="EndOnNewLine"/> which only
    /// decides whether END gets its own line — this decides where it aligns.
    /// </summary>
    [JsonPropertyName("endAlignment")]
    // T008: "" = unset → indent END one level from CASE (legacy "indented" intent), matching the
    // parity golden; an explicit "toCase"/"indented" still wins.
    public string EndAlignment { get; set; } = "";
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

    /// <summary>
    /// Spec 020 T080 — SQL Prompt <c>cte.placeColumnsOnNewLine</c> (enum):
    /// "ifLongerThanWrap" (default — wrap onto a new line only when the column list
    /// exceeds the max line width), "always" (always place the column list on its own
    /// line), "never" (always keep inline with the CTE name).
    /// </summary>
    [JsonPropertyName("placeColumnsOnNewLine")]
    public string PlaceColumnsOnNewLine { get; set; } = "ifLongerThanWrap";

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>cte.placeAsOnNewLine</c>:
    /// when true, the <c>AS</c> keyword that introduces a CTE body goes on its own line.
    /// </summary>
    [JsonPropertyName("asOnNewLine")]
    public bool AsOnNewLine { get; set; }
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

/// <summary>
/// Spec 020 T083 — SQL Prompt operator placement settings. Distinct from
/// <see cref="ExpressionOptions"/>: this group controls visual alignment of boolean
/// operators (AND / OR / BETWEEN) within a WHERE / ON / HAVING clause, while
/// <c>ExpressionOptions.BooleanOperatorNewLine</c> controls the line-break direction.
/// </summary>
public class OperatorsOptions
{
    /// <summary>
    /// SQL Prompt <c>operators.alignment</c> (enum):
    /// "inlineWithStatement" (default — keep operators inline with the clause keyword),
    /// "indentedFromStatement" (indent one level past the clause keyword),
    /// "rightAligned" (right-align operators to a common column).
    /// </summary>
    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "inlineWithStatement";

    /// <summary>
    /// SQL Prompt <c>operators.placeBetweenKeywordOnNewLine</c>:
    /// when true, the <c>BETWEEN</c> keyword goes on its own line.
    /// </summary>
    [JsonPropertyName("betweenOnNewLine")]
    public bool BetweenOnNewLine { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>operators.placeAndBetweenBetweenOnNewLine</c>.
    /// When true, the <c>AND</c> that pairs with a <c>BETWEEN</c> (i.e. the AND in
    /// <c>BETWEEN x AND y</c>) gets its own line. Pairs with the existing
    /// <see cref="ExpressionOptions.BetweenOnOneLine"/> — when <c>BetweenOnOneLine</c> is
    /// true it wins (no break), regardless of this flag.
    /// </summary>
    [JsonPropertyName("andBetweenOnNewLine")]
    public bool AndBetweenOnNewLine { get; set; }
}

/// <summary>
/// Spec 020 T084 — SQL Prompt <c>inStatements</c>-group settings. Controls visual
/// alignment of items inside an <c>IN (…)</c> list when expanded to multiple lines.
/// Pairs with <see cref="ExpressionOptions.InListStyle"/> + <c>InListThreshold</c>
/// (which decide WHEN to expand) — this group controls HOW the expanded form lines up.
/// </summary>
public class InStatementsOptions
{
    /// <summary>
    /// SQL Prompt <c>inStatements.alignment</c> (enum):
    /// "stacked" (default — each item on its own line, indented from the opening paren),
    /// "wrapped" (multiple items per line up to <c>maxLineWidth</c>),
    /// "rightAligned" (right-align each item to a common column).
    /// </summary>
    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "stacked";

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>inStatements.placeItemsOnNewLine</c>:
    /// "always" (force every IN list multi-line),
    /// "never" (force inline),
    /// "ifLongerThanWrap" (default — break only when the rendered list would exceed
    /// <see cref="WhitespaceOptions.MaxLineWidth"/>). Companion to the existing
    /// <see cref="ExpressionOptions.InListStyle"/>; this is the canonical SQL Prompt name.
    /// </summary>
    [JsonPropertyName("placeItemsOnNewLine")]
    public string PlaceItemsOnNewLine { get; set; } = "ifLongerThanWrap";
}

/// <summary>
/// Spec 020 Phase B closure — SQL Prompt <c>FunctionCalls</c> group.
/// Controls how a function invocation's parameter list is wrapped and indented.
/// Distinct from <see cref="DdlOptions.FirstParameterOnNewLine"/> (which controls
/// procedure DDL signature parameters) — this controls call-site formatting.
/// </summary>
public class FunctionCallsOptions
{
    /// <summary>
    /// SQL Prompt <c>functionCalls.placeParametersOnNewLine</c>:
    /// "always", "never", "ifLongerThanWrap" (default).
    /// </summary>
    [JsonPropertyName("placeParametersOnNewLine")]
    public string PlaceParametersOnNewLine { get; set; } = "ifLongerThanWrap";

    /// <summary>
    /// SQL Prompt <c>functionCalls.indentParameters</c>: when the parameter list is broken
    /// onto multiple lines, indent the parameters one level past the opening paren.
    /// </summary>
    [JsonPropertyName("indentParameters")]
    public bool IndentParameters { get; set; } = true;
}

/// <summary>
/// Spec 020 Phase B closure — SQL Prompt <c>Comments</c> group.
/// Controls how the formatter touches comments — line / block / recognised-pattern.
/// </summary>
public class CommentsOptions
{
    /// <summary>
    /// SQL Prompt <c>comments.multilineFormatting</c>:
    /// "preserve" (default — leave block comments exactly as the user wrote them),
    /// "normaliseIndent" (re-indent the comment body to the surrounding context),
    /// "joinShortLines" (collapse short multi-line comments to one line where possible).
    /// </summary>
    [JsonPropertyName("multilineFormatting")]
    public string MultilineFormatting { get; set; } = "preserve";

    /// <summary>
    /// SQL Prompt <c>comments.recognizeCommonPatterns</c>: when true, the formatter
    /// detects header / banner / TODO-style comments and leaves their internal layout intact
    /// even when other formatting passes would otherwise reflow them.
    /// </summary>
    [JsonPropertyName("recognizeCommonPatterns")]
    public bool RecognizeCommonPatterns { get; set; } = true;
}

/// <summary>
/// Spec 030 — SQL Prompt DECLARE / SET variable layout options.
/// Controls one-declaration-per-line expansion and optional column alignment for DECLARE blocks.
/// All flags default to <c>false</c> so that the default profile produces byte-identical output
/// to the pre-030 formatter (the rule early-exits when <see cref="OneDeclarationPerLine"/> is off).
/// </summary>
public class DeclareOptions
{
    /// <summary>
    /// When true, a multi-variable <c>DECLARE @a INT, @b INT</c> is expanded so that each
    /// variable occupies its own line. Individual <c>DECLARE @x TYPE</c> statements are
    /// already one-per-line; this only splits comma-separated declarations.
    /// Default: <c>false</c> (current behaviour — leave as-is).
    /// </summary>
    [JsonPropertyName("oneDeclarationPerLine")]
    public bool OneDeclarationPerLine { get; set; }

    /// <summary>
    /// When true (and <see cref="OneDeclarationPerLine"/> is also true), aligns the data-type
    /// tokens of each variable in a DECLARE block to a common column by padding with spaces.
    /// Mirrors SQL Prompt's <c>variables.alignDataTypes</c>.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("alignDataTypes")]
    public bool AlignDataTypes { get; set; }

    /// <summary>
    /// When true (and <see cref="OneDeclarationPerLine"/> is also true), aligns the <c>=</c>
    /// assignment operators (default values) in a DECLARE block to a common column.
    /// Mirrors SQL Prompt's <c>variables.alignDefaultValues</c>.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("alignDefaultValues")]
    public bool AlignDefaultValues { get; set; }
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
