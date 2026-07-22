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

    [JsonPropertyName("insertStatements")]
    public InsertStatementsOptions InsertStatements { get; set; } = new();

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
    [SettingMeta(Description = "Controls whether indentation uses spaces, tabs, or tabs where possible.",
        AllowedValues = new[] { "spaces", "tabs", "tabsWhenPossible" })]
    public string TabStyle { get; set; } = "spaces";

    [JsonPropertyName("tabSize")]
    [SettingMeta(Description = "Number of spaces per indentation level (and tab stop width).", Min = 1, Max = 16)]
    public int TabSize { get; set; } = 4;

    [JsonPropertyName("indentStyle")]
    [SettingMeta(Description = "Chooses block or hanging indentation for wrapped continuation lines.",
        AllowedValues = new[] { "block", "hanging" })]
    public string IndentStyle { get; set; } = "block";

    [JsonPropertyName("maxLineWidth")]
    [SettingMeta(Description = "Maximum line width in characters before long lines are wrapped.", Min = 40, Max = 400)]
    public int MaxLineWidth { get; set; } = 120;

    /// <summary>Spec 031 FR-020 — master gate for long-line wrapping; when false, MaxLineWidth is not enforced. Redgate whitespace.wrapLongLines.</summary>
    [JsonPropertyName("wrapLongLines")]
    [SettingMeta(Description = "Master switch for long-line wrapping; when off, the maximum line width is not enforced.")]
    public bool WrapLongLines { get; set; } = true;

    [JsonPropertyName("lineBreakBeforeClause")]
    [SettingMeta(Description = "Places a line break before each major clause keyword.")]
    public bool LineBreakBeforeClause { get; set; } = true;

    [JsonPropertyName("lineBreakAfterClause")]
    [SettingMeta(Description = "Places a line break after each major clause keyword.")]
    public bool LineBreakAfterClause { get; set; }

    [JsonPropertyName("lineBreakBeforeComma")]
    [SettingMeta(Description = "Places a line break before each list comma.")]
    public bool LineBreakBeforeComma { get; set; }

    [JsonPropertyName("lineBreakAfterComma")]
    [SettingMeta(Description = "Places a line break after each list comma.")]
    public bool LineBreakAfterComma { get; set; } = true;

    [JsonPropertyName("emptyLineBetweenStatements")]
    [SettingMeta(Description = "Number of empty lines inserted between consecutive statements.", Min = 0, Max = 10)]
    public int EmptyLineBetweenStatements { get; set; } = 1;

    [JsonPropertyName("emptyLineBeforeGO")]
    [SettingMeta(Description = "Inserts an empty line before the GO batch separator.")]
    public bool EmptyLineBeforeGo { get; set; } = true;

    [JsonPropertyName("emptyLineAfterGO")]
    [SettingMeta(Description = "Inserts an empty line after the GO batch separator.")]
    public bool EmptyLineAfterGo { get; set; } = true;

    [JsonPropertyName("preserveEmptyLinesAfterBatch")]
    [SettingMeta(Description = "Preserves existing empty lines that follow a batch separator.")]
    public bool PreserveEmptyLinesAfterBatch { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Whitespace.BlankLinesBeforeGo</c> (0–5).
    /// Counts the blank lines inserted before the <c>GO</c> batch separator. Pairs with the
    /// pre-existing <see cref="EmptyLineBeforeGo"/> boolean (kept for back-compat — when
    /// <see cref="EmptyLineBeforeGo"/> is true and <see cref="BlankLinesBeforeGoCount"/> is 0,
    /// the boolean wins and a single blank line is inserted; non-zero count overrides).
    /// </summary>
    [JsonPropertyName("blankLinesBeforeGoCount")]
    [SettingMeta(Description = "Number of blank lines inserted before the GO batch separator; a non-zero count overrides the empty-line-before-GO toggle.", Min = 0, Max = 5)]
    public int BlankLinesBeforeGoCount { get; set; }

    [JsonPropertyName("preserveEmptyLines")]
    [SettingMeta(Description = "Preserves existing empty lines between statements instead of removing them.")]
    public bool PreserveEmptyLines { get; set; } = true;

    [JsonPropertyName("maxConsecutiveEmptyLines")]
    [SettingMeta(Description = "Maximum number of consecutive empty lines kept when preserving empty lines.", Min = 0, Max = 10)]
    public int MaxConsecutiveEmptyLines { get; set; } = 2;

    [JsonPropertyName("trailingWhitespace")]
    [SettingMeta(Description = "Controls whether trailing whitespace at the end of each line is removed or kept.",
        AllowedValues = new[] { "remove", "keep", "preserve" })]
    public string TrailingWhitespace { get; set; } = "remove";

    [JsonPropertyName("finalNewline")]
    [SettingMeta(Description = "Controls whether the formatted script ends with a final newline.",
        AllowedValues = new[] { "ensure", "remove", "none" })]
    public string FinalNewline { get; set; } = "ensure";

    [JsonPropertyName("spaceAfterComma")]
    [SettingMeta(Description = "Inserts a space after each comma.")]
    public bool SpaceAfterComma { get; set; } = true;

    [JsonPropertyName("spaceAroundOperators")]
    [SettingMeta(Description = "Inserts spaces around arithmetic and comparison operators.")]
    public bool SpaceAroundOperators { get; set; } = true;

    [JsonPropertyName("spaceAroundBooleanOperators")]
    [SettingMeta(Description = "Inserts spaces around boolean operators such as AND and OR.")]
    public bool SpaceAroundBooleanOperators { get; set; } = true;

    [JsonPropertyName("spaceInsideParentheses")]
    [SettingMeta(Description = "Inserts spaces just inside opening and closing parentheses.")]
    public bool SpaceInsideParentheses { get; set; }

    [JsonPropertyName("spaceBeforeParentheses")]
    [SettingMeta(Description = "Inserts a space before an opening parenthesis.")]
    public bool SpaceBeforeParentheses { get; set; }

    [JsonPropertyName("lineBreakAfterSemicolon")]
    [SettingMeta(Description = "Places a line break after each statement-terminating semicolon.")]
    public bool LineBreakAfterSemicolon { get; set; } = true;

    /// <summary>Spec 031 FR-033 — none | spaceBefore | newLineBefore. Gates NormalizeSemicolonSpacing in phase 3.</summary>
    [JsonPropertyName("semicolonPlacement")]
    [SettingMeta(Description = "Controls the whitespace placed before a statement-terminating semicolon.",
        AllowedValues = new[] { "none", "spaceBefore", "newLineBefore" })]
    public string SemicolonPlacement { get; set; } = "none";

    /// <summary>Spec 031 FR-034 — blank lines after a GO batch separator.</summary>
    [JsonPropertyName("emptyLinesAfterBatchSeparator")]
    [SettingMeta(Description = "Number of blank lines inserted after a GO batch separator.", Min = 0, Max = 10)]
    public int EmptyLinesAfterBatchSeparator { get; set; } = 1;
}

public class CasingOptions
{
    [JsonPropertyName("reservedKeywords")]
    [SettingMeta(Description = "Casing applied to reserved T-SQL keywords such as SELECT and FROM.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string ReservedKeywords { get; set; } = "UPPERCASE";

    [JsonPropertyName("builtInFunctions")]
    [SettingMeta(Description = "Casing applied to built-in function names such as GETDATE and COUNT.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string BuiltInFunctions { get; set; } = "UPPERCASE";

    [JsonPropertyName("builtInDataTypes")]
    [SettingMeta(Description = "Casing applied to built-in data type names such as int and varchar.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string BuiltInDataTypes { get; set; } = "lowercase";

    [JsonPropertyName("systemObjects")]
    [SettingMeta(Description = "Casing applied to system object names such as sys.objects catalog views.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string SystemObjects { get; set; } = "lowercase";

    [JsonPropertyName("globalVariables")]
    [SettingMeta(Description = "Casing applied to global @@-prefixed variables such as @@ROWCOUNT.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string GlobalVariables { get; set; } = "lowercase";

    [JsonPropertyName("localVariables")]
    [SettingMeta(Description = "Casing applied to local @-prefixed variable names.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string LocalVariables { get; set; } = "AsIs";

    [JsonPropertyName("identifiers")]
    [SettingMeta(Description = "Casing applied to user identifiers such as table and column names.",
        AllowedValues = new[] { "UPPERCASE", "lowercase", "PascalCase", "camelCase", "AsIs" })]
    public string Identifiers { get; set; } = "AsIs";

    [JsonPropertyName("syncWithDatabase")]
    [SettingMeta(Description = "Matches identifier casing to the definition stored in the connected database.")]
    public bool SyncWithDatabase { get; set; }

    [JsonPropertyName("camelCaseDictionary")]
    [SettingMeta(Description = "Uses the camel-case dictionary when recasing compound identifiers.")]
    public bool CamelCaseDictionary { get; set; } = true;

    [JsonPropertyName("applyOnTyping")]
    [SettingMeta(Description = "Applies keyword casing automatically while typing.")]
    public bool ApplyOnTyping { get; set; } = true;
}

public class ListOptions
{
    [JsonPropertyName("commaPosition")]
    [SettingMeta(Description = "Places list commas at the end of an item (trailing) or the start of the next item (leading).",
        AllowedValues = new[] { "trailing", "leading" })]
    public string CommaPosition { get; set; } = "trailing";

    [JsonPropertyName("alignItemsAcrossClauses")]
    [SettingMeta(Description = "Aligns list items to a common column across clauses.")]
    public bool AlignItemsAcrossClauses { get; set; } = true;

    [JsonPropertyName("alignAliases")]
    [SettingMeta(Description = "Aligns column aliases to a common column.")]
    public bool AlignAliases { get; set; } = true;

    [JsonPropertyName("oneItemPerLine")]
    [SettingMeta(Description = "Places each list item on its own line.")]
    public bool OneItemPerLine { get; set; } = true;

    [JsonPropertyName("collapseShortLists")]
    [SettingMeta(Description = "Collapses short lists onto a single line.")]
    public bool CollapseShortLists { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for a list to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 60;

    [JsonPropertyName("indentListItems")]
    [SettingMeta(Description = "Indents list items one level from the clause keyword.")]
    public bool IndentListItems { get; set; } = true;

    [JsonPropertyName("alignDataTypesInDDL")]
    [SettingMeta(Description = "Aligns column data types to a common column in DDL column lists.")]
    public bool AlignDataTypesInDdl { get; set; } = true;

    [JsonPropertyName("alignValuesInInsert")]
    [SettingMeta(Description = "Aligns values to a common column in INSERT statements.")]
    public bool AlignValuesInInsert { get; set; } = true;

    [JsonPropertyName("spaceAfterListComma")]
    [SettingMeta(Description = "Inserts a space after each list comma.")]
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
    [SettingMeta(Description = "Controls when list items after the first are placed on new lines.",
        AllowedValues = new[] { "always", "never", "ifLongerThanWrap" })]
    public string PlaceSubsequentItemsOnNewLines { get; set; } = "always";

    /// <summary>Spec 031 FR-021 — space between an item and its following comma.</summary>
    [JsonPropertyName("spaceBeforeComma")]
    [SettingMeta(Description = "Inserts a space between a list item and its following comma.")]
    public bool SpaceBeforeComma { get; set; }

    /// <summary>Spec 031 FR-021 — leading-comma column: beforeItem | toList | toStatement.</summary>
    [JsonPropertyName("commaAlignment")]
    [SettingMeta(Description = "Chooses the column that leading commas align to.",
        AllowedValues = new[] { "beforeItem", "toList", "toStatement" })]
    public string CommaAlignment { get; set; } = "beforeItem";

    /// <summary>Spec 031 FR-020 — round alignment columns up to the next tab stop.</summary>
    [JsonPropertyName("alignItemsToTabStops")]
    [SettingMeta(Description = "Rounds list alignment columns up to the next tab stop.")]
    public bool AlignItemsToTabStops { get; set; }
}

public class ParenthesisOptions
{
    [JsonPropertyName("openOnSameLine")]
    [SettingMeta(Description = "Keeps the opening parenthesis on the same line as the preceding token.")]
    public bool OpenOnSameLine { get; set; } = true;

    [JsonPropertyName("closeOnNewLine")]
    [SettingMeta(Description = "Controls whether the closing parenthesis is placed on its own line.",
        AllowedValues = new[] { "false", "true", "matchOpen" })]
    public string CloseOnNewLine { get; set; } = "false";

    [JsonPropertyName("collapseShort")]
    [SettingMeta(Description = "Collapses short parenthesized content onto a single line.")]
    public bool CollapseShort { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for parenthesized content to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 40;

    [JsonPropertyName("indentContents")]
    [SettingMeta(Description = "Indents the contents of parentheses one level deeper than the opening parenthesis.")]
    public bool IndentContents { get; set; } = true;

    [JsonPropertyName("spaceInside")]
    [SettingMeta(Description = "Inserts spaces just inside the parentheses, around their contents.")]
    public bool SpaceInside { get; set; }

    [JsonPropertyName("removeRedundant")]
    [SettingMeta(Description = "Removes redundant parentheses that do not affect evaluation.")]
    public bool RemoveRedundant { get; set; }

    [JsonPropertyName("createTableColumns")]
    [SettingMeta(Description = "Places the CREATE TABLE column list on a new line or the same line.",
        AllowedValues = new[] { "newLine", "sameLine" })]
    public string CreateTableColumns { get; set; } = "newLine";

    [JsonPropertyName("procedureParameters")]
    [SettingMeta(Description = "Places the procedure parameter list on a new line or the same line.",
        AllowedValues = new[] { "newLine", "sameLine" })]
    public string ProcedureParameters { get; set; } = "newLine";

    [JsonPropertyName("subqueryStyle")]
    [SettingMeta(Description = "Controls how a parenthesized subquery body is indented.",
        AllowedValues = new[] { "indent", "alignWithParen", "sameLine" })]
    public string SubqueryStyle { get; set; } = "indent";

    /// <summary>
    /// Spec 031 FR-022 — Redgate 9-value style; empty = legacy OpenOnSameLine/CloseOnNewLine govern.
    /// compactSimple | compactToStatement | compactIndented | compactRightAligned | expandedSimple |
    /// expandedSplit | expandedToStatement | expandedIndented | expandedRightAligned; empty = inherit/legacy.
    /// </summary>
    [JsonPropertyName("style")]
    [SettingMeta(Description = "Redgate nine-value parenthesis style; empty inherits the legacy open/close settings.",
        AllowedValues = new[] { "", "compactSimple", "compactToStatement", "compactIndented", "compactRightAligned",
            "expandedSimple", "expandedSplit", "expandedToStatement", "expandedIndented", "expandedRightAligned" })]
    public string Style { get; set; } = "";
}

public class DmlOptions
{
    [JsonPropertyName("selectItemsOnNewLine")]
    [SettingMeta(Description = "Places each SELECT list item on its own line.")]
    public bool SelectItemsOnNewLine { get; set; } = true;

    [JsonPropertyName("selectStarOnSameLine")]
    [SettingMeta(Description = "Keeps SELECT * on the same line as the SELECT keyword.")]
    public bool SelectStarOnSameLine { get; set; } = true;

    [JsonPropertyName("fromOnNewLine")]
    [SettingMeta(Description = "Places the FROM clause on a new line.")]
    public bool FromOnNewLine { get; set; } = true;

    [JsonPropertyName("whereOnNewLine")]
    [SettingMeta(Description = "Places the WHERE clause on a new line.")]
    public bool WhereOnNewLine { get; set; } = true;

    [JsonPropertyName("andOrNewLine")]
    [SettingMeta(Description = "Controls whether AND/OR conditions break before the keyword, after it, or stay inline.",
        AllowedValues = new[] { "before", "after", "none" })]
    public string AndOrNewLine { get; set; } = "before";

    [JsonPropertyName("andOrIndent")]
    [SettingMeta(Description = "Controls the indentation of AND/OR keywords relative to the WHERE clause.",
        AllowedValues = new[] { "alignWithWhere", "indent", "doubleIndent" })]
    public string AndOrIndent { get; set; } = "alignWithWhere";

    [JsonPropertyName("groupByOnNewLine")]
    [SettingMeta(Description = "Places the GROUP BY clause on a new line.")]
    public bool GroupByOnNewLine { get; set; } = true;

    [JsonPropertyName("havingOnNewLine")]
    [SettingMeta(Description = "Places the HAVING clause on a new line.")]
    public bool HavingOnNewLine { get; set; } = true;

    [JsonPropertyName("orderByOnNewLine")]
    [SettingMeta(Description = "Places the ORDER BY clause on a new line.")]
    public bool OrderByOnNewLine { get; set; } = true;

    [JsonPropertyName("topOnSameLine")]
    [SettingMeta(Description = "Keeps TOP (n) on the same line as SELECT.")]
    public bool TopOnSameLine { get; set; } = true;

    [JsonPropertyName("distinctOnSameLine")]
    [SettingMeta(Description = "Keeps DISTINCT on the same line as SELECT.")]
    public bool DistinctOnSameLine { get; set; } = true;

    [JsonPropertyName("intoOnNewLine")]
    [SettingMeta(Description = "Places the INTO clause on a new line.")]
    public bool IntoOnNewLine { get; set; } = true;

    [JsonPropertyName("valuesOnNewLine")]
    [SettingMeta(Description = "Places the VALUES keyword on a new line.")]
    public bool ValuesOnNewLine { get; set; } = true;

    [JsonPropertyName("setOnNewLine")]
    [SettingMeta(Description = "Places the UPDATE SET clause on a new line.")]
    public bool SetOnNewLine { get; set; } = true;

    [JsonPropertyName("deleteFromOnSameLine")]
    [SettingMeta(Description = "Keeps DELETE FROM on a single line.")]
    public bool DeleteFromOnSameLine { get; set; } = true;

    [JsonPropertyName("mergeWhenOnNewLine")]
    [SettingMeta(Description = "Places each MERGE WHEN clause on a new line.")]
    public bool MergeWhenOnNewLine { get; set; } = true;

    [JsonPropertyName("collapseShortStatements")]
    [SettingMeta(Description = "Collapses short DML statements onto a single line.")]
    public bool CollapseShortStatements { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for a DML statement to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 80;

    [JsonPropertyName("collapseShortSubqueries")]
    [SettingMeta(Description = "Collapses short subqueries onto a single line.")]
    public bool CollapseShortSubqueries { get; set; } = true;

    [JsonPropertyName("subqueryCollapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for a subquery to be collapsed onto one line.", Min = 0, Max = 500)]
    public int SubqueryCollapseThreshold { get; set; } = 60;

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.RightAlignClauses</c>. When true, the
    /// clause keywords (SELECT / FROM / WHERE / GROUP BY / HAVING / ORDER BY) are
    /// right-justified to a common column rather than left-aligned. Pairs with
    /// <see cref="ClauseIndentation"/>.
    /// </summary>
    [JsonPropertyName("rightAlignClauses")]
    [SettingMeta(Description = "Right-aligns clause keywords such as SELECT and FROM to a common column.")]
    public bool RightAlignClauses { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Dml.ClauseIndentation</c>:
    /// "none" (default — clauses at column 0), "indented" (clauses indented one level
    /// from the surrounding statement), "rightAligned" (clauses right-aligned to the
    /// widest clause keyword). Broader than the existing <see cref="AndOrIndent"/>
    /// which only controls boolean-operator indent inside WHERE.
    /// </summary>
    [JsonPropertyName("clauseIndentation")]
    [SettingMeta(Description = "Controls how clause keywords are indented relative to the statement.",
        AllowedValues = new[] { "none", "indented", "rightAligned" })]
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
    [SettingMeta(Description = "Controls how the INSERT column list is broken across lines.",
        AllowedValues = new[] { "onePerLine", "compact", "ifLongerThanWrap" })]
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
    [SettingMeta(Description = "Controls how INSERT VALUES tuples are broken across lines.",
        AllowedValues = new[] { "onePerLine", "compact", "ifLongerThanWrap" })]
    public string ValuesFormat { get; set; } = "onePerLine";

    /// <summary>Spec 031 FR-023 — break AFTER DISTINCT/TOP so the select list starts on the next line.</summary>
    [JsonPropertyName("newLineAfterDistinctTop")]
    [SettingMeta(Description = "Breaks after DISTINCT/TOP so the select list starts on the next line.")]
    public bool NewLineAfterDistinctTop { get; set; }
}

public class JoinOptions
{
    [JsonPropertyName("onNewLine")]
    [SettingMeta(Description = "Places each JOIN on a new line.")]
    public bool OnNewLine { get; set; } = true;

    [JsonPropertyName("indentJoin")]
    [SettingMeta(Description = "Indents JOIN keywords one level from the FROM clause.")]
    public bool IndentJoin { get; set; }

    [JsonPropertyName("onConditionNewLine")]
    [SettingMeta(Description = "Places the ON condition on a new line.")]
    public bool OnConditionNewLine { get; set; } = true;

    [JsonPropertyName("onConditionIndent")]
    [SettingMeta(Description = "Controls the indentation of the ON condition relative to the JOIN.",
        AllowedValues = new[] { "indent", "toJoin", "alignWithJoin" })]
    public string OnConditionIndent { get; set; } = "indent";

    [JsonPropertyName("multipleOnConditions")]
    [SettingMeta(Description = "Places multiple AND conditions in an ON clause on new lines or the same line.",
        AllowedValues = new[] { "newLine", "sameLine" })]
    public string MultipleOnConditions { get; set; } = "newLine";

    [JsonPropertyName("emptyLineBeforeJoin")]
    [SettingMeta(Description = "Inserts an empty line before each JOIN clause.")]
    public bool EmptyLineBeforeJoin { get; set; }

    /// <summary>
    /// Accepted values: "right" (default), "none", "left", "indentedFromFrom".
    /// Spec 031 FR-028 adds "toTable" as an accepted alias — the importer normalises it to
    /// "left" (see <c>SqlPromptImporter.OptionMap["AlignJoinKeyword"]</c>) since AKML's layout
    /// engine does not (yet) distinguish "align to table" from generic left-alignment.
    /// </summary>
    [JsonPropertyName("alignJoinKeyword")]
    [SettingMeta(Description = "Controls how JOIN keywords are aligned relative to FROM.",
        AllowedValues = new[] { "right", "none", "left", "indentedFromFrom", "toTable" })]
    public string AlignJoinKeyword { get; set; } = "right";

    [JsonPropertyName("joinTypeStyle")]
    [SettingMeta(Description = "Controls whether join types are written explicitly (INNER JOIN), implicitly (JOIN), or left as-is.",
        AllowedValues = new[] { "explicit", "implicit", "asIs" })]
    public string JoinTypeStyle { get; set; } = "explicit";

    [JsonPropertyName("crossApplyNewLine")]
    [SettingMeta(Description = "Places CROSS/OUTER APPLY on a new line.")]
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
    [SettingMeta(Description = "Canonical SQL Prompt ON-condition indent mode; takes precedence over the legacy indent setting when non-default.",
        AllowedValues = new[] { "indentedFromJoin", "toTable", "indentedFromTable" })]
    public string OnConditionIndentMode { get; set; } = "indentedFromJoin";
}

public class DdlOptions
{
    [JsonPropertyName("createTableColumnsOnNewLine")]
    [SettingMeta(Description = "Places each CREATE TABLE column definition on its own line.")]
    public bool CreateTableColumnsOnNewLine { get; set; } = true;

    [JsonPropertyName("alignDataTypes")]
    [SettingMeta(Description = "Aligns column data types to a common column in CREATE TABLE.")]
    public bool AlignDataTypes { get; set; } = true;

    [JsonPropertyName("alignConstraints")]
    [SettingMeta(Description = "Aligns column constraints to a common column in CREATE TABLE.")]
    public bool AlignConstraints { get; set; } = true;

    [JsonPropertyName("constraintsOnNewLine")]
    [SettingMeta(Description = "Places inline column constraints on their own lines.")]
    public bool ConstraintsOnNewLine { get; set; }

    [JsonPropertyName("inlineConstraintStyle")]
    [SettingMeta(Description = "Places inline column constraints on the same line as the column or on the next line.",
        AllowedValues = new[] { "sameLine", "nextLine" })]
    public string InlineConstraintStyle { get; set; } = "sameLine";

    [JsonPropertyName("tableConstraintsSeparate")]
    [SettingMeta(Description = "Keeps table-level constraints on separate lines from column definitions.")]
    public bool TableConstraintsSeparate { get; set; } = true;

    [JsonPropertyName("firstParameterOnNewLine")]
    [SettingMeta(Description = "Controls whether the first procedure parameter is placed on a new line.",
        AllowedValues = new[] { "auto", "always", "never" })]
    public string FirstParameterOnNewLine { get; set; } = "auto";

    [JsonPropertyName("parameterAlignment")]
    [SettingMeta(Description = "Controls how procedure parameters are aligned when placed on multiple lines.",
        AllowedValues = new[] { "aligned", "hanging" })]
    public string ParameterAlignment { get; set; } = "aligned";

    [JsonPropertyName("alignParameterDataTypes")]
    [SettingMeta(Description = "Aligns parameter data types to a common column in procedure signatures.")]
    public bool AlignParameterDataTypes { get; set; } = true;

    [JsonPropertyName("alignParameterDefaults")]
    [SettingMeta(Description = "Aligns parameter default values to a common column in procedure signatures.")]
    public bool AlignParameterDefaults { get; set; } = true;

    [JsonPropertyName("asOnNewLine")]
    [SettingMeta(Description = "Places the AS keyword of a module definition on its own line.")]
    public bool AsOnNewLine { get; set; } = true;

    [JsonPropertyName("beginOnNewLine")]
    [SettingMeta(Description = "Places the BEGIN keyword of a module body on its own line.")]
    public bool BeginOnNewLine { get; set; } = true;

    [JsonPropertyName("collapseShortDDL")]
    [SettingMeta(Description = "Collapses short DDL statements onto a single line.")]
    public bool CollapseShortDdl { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for a DDL statement to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 60;

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>Ddl.ConstraintColumnsOnNewLine</c>:
    /// "always", "never", "ifLongerOrMultipleColumns" (default — break only when there
    /// are multiple columns or the list would exceed wrap width). Controls how PRIMARY KEY
    /// / FOREIGN KEY / UNIQUE constraint column lists are placed.
    /// </summary>
    [JsonPropertyName("constraintColumnsOnNewLine")]
    [SettingMeta(Description = "Controls when constraint column lists (PRIMARY KEY, FOREIGN KEY, UNIQUE) break onto new lines.",
        AllowedValues = new[] { "always", "never", "ifLongerOrMultipleColumns" })]
    public string ConstraintColumnsOnNewLine { get; set; } = "ifLongerOrMultipleColumns";

    /// <summary>
    /// Spec 031 FR-022 — construct-scoped paren style; empty = inherit Parenthesis.Style.
    /// compactSimple | compactToStatement | compactIndented | compactRightAligned | expandedSimple |
    /// expandedSplit | expandedToStatement | expandedIndented | expandedRightAligned; empty = inherit/legacy.
    /// </summary>
    [JsonPropertyName("parenthesisStyle")]
    [SettingMeta(Description = "DDL-scoped Redgate parenthesis style; empty inherits the global parenthesis style.",
        AllowedValues = new[] { "", "compactSimple", "compactToStatement", "compactIndented", "compactRightAligned",
            "expandedSimple", "expandedSplit", "expandedToStatement", "expandedIndented", "expandedRightAligned" })]
    public string ParenthesisStyle { get; set; } = "";

    /// <summary>
    /// Spec 031 FR-022 — indent contents of DDL-scoped parentheses (e.g. CREATE TABLE column
    /// lists / procedure signatures) when <see cref="ParenthesisStyle"/> is construct-scoped.
    /// Distinct from the global <see cref="ParenthesisOptions.IndentContents"/> — Redgate's
    /// <c>ddl.indentParenthesesContents</c> only governs DDL constructs, not every parenthesis.
    /// </summary>
    [JsonPropertyName("indentParenContents")]
    [SettingMeta(Description = "Indents the contents of DDL-scoped parentheses such as CREATE TABLE column lists.")]
    public bool IndentParenContents { get; set; }
}

public class ControlFlowOptions
{
    [JsonPropertyName("beginOnNewLine")]
    [SettingMeta(Description = "Places BEGIN on its own line.")]
    public bool BeginOnNewLine { get; set; } = true;

    [JsonPropertyName("endOnNewLine")]
    [SettingMeta(Description = "Places END on its own line.")]
    public bool EndOnNewLine { get; set; } = true;

    [JsonPropertyName("indentBetweenBeginEnd")]
    [SettingMeta(Description = "Indents the statements between BEGIN and END.")]
    public bool IndentBetweenBeginEnd { get; set; } = true;

    [JsonPropertyName("collapseShortIfElse")]
    [SettingMeta(Description = "Collapses short IF/ELSE statements onto a single line.")]
    public bool CollapseShortIfElse { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for an IF/ELSE statement to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 60;

    [JsonPropertyName("elseOnNewLine")]
    [SettingMeta(Description = "Places ELSE on its own line.")]
    public bool ElseOnNewLine { get; set; } = true;

    [JsonPropertyName("elseAlignWithIf")]
    [SettingMeta(Description = "Aligns ELSE with its matching IF keyword.")]
    public bool ElseAlignWithIf { get; set; } = true;

    [JsonPropertyName("tryCatchOnNewLine")]
    [SettingMeta(Description = "Places BEGIN TRY and BEGIN CATCH keywords on their own lines.")]
    public bool TryCatchOnNewLine { get; set; } = true;

    /// <summary>Spec 031 FR-025 — indent the BEGIN/END keywords themselves one level from IF/WHILE/ELSE.</summary>
    [JsonPropertyName("indentBeginEndKeywords")]
    [SettingMeta(Description = "Indents the BEGIN/END keywords themselves one level from IF/WHILE/ELSE.")]
    public bool IndentBeginEndKeywords { get; set; }
}

public class CaseOptions
{
    [JsonPropertyName("whenOnNewLine")]
    [SettingMeta(Description = "Places each WHEN branch of a CASE expression on its own line.")]
    public bool WhenOnNewLine { get; set; } = true;

    [JsonPropertyName("thenOnNewLine")]
    [SettingMeta(Description = "Places THEN on its own line below its WHEN condition.")]
    public bool ThenOnNewLine { get; set; }

    [JsonPropertyName("elseOnNewLine")]
    [SettingMeta(Description = "Places the CASE ELSE branch on its own line.")]
    public bool ElseOnNewLine { get; set; } = true;

    [JsonPropertyName("endOnNewLine")]
    [SettingMeta(Description = "Places the CASE END keyword on its own line.")]
    public bool EndOnNewLine { get; set; } = true;

    [JsonPropertyName("indentWhen")]
    [SettingMeta(Description = "Indents WHEN branches one level from the CASE keyword.")]
    public bool IndentWhen { get; set; } = true;

    [JsonPropertyName("alignThen")]
    [SettingMeta(Description = "Aligns THEN keywords to a common column across WHEN branches.")]
    public bool AlignThen { get; set; } = true;

    [JsonPropertyName("collapseShortCase")]
    [SettingMeta(Description = "Collapses short CASE expressions onto a single line.")]
    public bool CollapseShortCase { get; set; } = true;

    [JsonPropertyName("collapseThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for a CASE expression to be collapsed onto one line.", Min = 0, Max = 500)]
    public int CollapseThreshold { get; set; } = 60;

    /// <summary>
    /// Spec 020 T082 — SQL Prompt <c>caseExpressions.placeFirstWhenOnNewLine</c>:
    /// "auto" (default — honour <see cref="WhenOnNewLine"/> for the first WHEN),
    /// "always" (force first WHEN on a new line regardless),
    /// "never" (keep first WHEN inline with the CASE expression).
    /// </summary>
    [JsonPropertyName("firstWhenOnNewLine")]
    [SettingMeta(Description = "Controls whether the first WHEN goes on a new line, stays inline, or follows the when-on-new-line toggle.",
        AllowedValues = new[] { "auto", "always", "never" })]
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
    [SettingMeta(Description = "Chooses the column WHEN branches align to; empty honors the legacy indent-when toggle.",
        AllowedValues = new[] { "", "toCase", "toFirstItem", "indentedFromCase" })]
    public string WhenAlignment { get; set; } = "";

    /// <summary>
    /// Spec 020 T082 — SQL Prompt <c>caseExpressions.placeExpressionOnNewLine</c>:
    /// when true, the simple-CASE expression goes on its own line below CASE.
    /// </summary>
    [JsonPropertyName("expressionOnNewLine")]
    [SettingMeta(Description = "Places the simple-CASE input expression on its own line below CASE.")]
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
    [SettingMeta(Description = "Chooses the column END aligns to; empty keeps the legacy one-level indent from CASE.",
        AllowedValues = new[] { "", "toCase", "indented" })]
    public string EndAlignment { get; set; } = "";

    /// <summary>Spec 031 FR-031 — line-start THEN column: indentedFromWhen | toWhen | toWhenExpression.</summary>
    [JsonPropertyName("thenAlignment")]
    [SettingMeta(Description = "Chooses the column a line-starting THEN aligns to.",
        AllowedValues = new[] { "indentedFromWhen", "toWhen", "toWhenExpression" })]
    public string ThenAlignment { get; set; } = "indentedFromWhen";
}

public class CteOptions
{
    [JsonPropertyName("withOnNewLine")]
    [SettingMeta(Description = "Places the WITH keyword of a CTE on its own line.")]
    public bool WithOnNewLine { get; set; } = true;

    [JsonPropertyName("cteBodyIndent")]
    [SettingMeta(Description = "Indents the CTE body one level from its definition.")]
    public bool CteBodyIndent { get; set; } = true;

    [JsonPropertyName("commaBeforeCte")]
    [SettingMeta(Description = "Places the comma separating CTE definitions before the next CTE name.")]
    public bool CommaBeforeCte { get; set; }

    [JsonPropertyName("emptyLineBetweenCtes")]
    [SettingMeta(Description = "Inserts an empty line between consecutive CTE definitions.")]
    public bool EmptyLineBetweenCtes { get; set; } = true;

    /// <summary>
    /// Spec 020 T080 — SQL Prompt <c>cte.placeColumnsOnNewLine</c> (enum):
    /// "ifLongerThanWrap" (default — wrap onto a new line only when the column list
    /// exceeds the max line width), "always" (always place the column list on its own
    /// line), "never" (always keep inline with the CTE name).
    /// </summary>
    [JsonPropertyName("placeColumnsOnNewLine")]
    [SettingMeta(Description = "Controls when the CTE column list is placed on its own line.",
        AllowedValues = new[] { "ifLongerThanWrap", "always", "never" })]
    public string PlaceColumnsOnNewLine { get; set; } = "ifLongerThanWrap";

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>cte.placeAsOnNewLine</c>:
    /// when true, the <c>AS</c> keyword that introduces a CTE body goes on its own line.
    /// </summary>
    [JsonPropertyName("asOnNewLine")]
    [SettingMeta(Description = "Places the AS keyword that introduces a CTE body on its own line.")]
    public bool AsOnNewLine { get; set; }

    /// <summary>
    /// Spec 031 FR-022 — construct-scoped paren style; empty = inherit Parenthesis.Style.
    /// compactSimple | compactToStatement | compactIndented | compactRightAligned | expandedSimple |
    /// expandedSplit | expandedToStatement | expandedIndented | expandedRightAligned; empty = inherit/legacy.
    /// </summary>
    [JsonPropertyName("parenthesisStyle")]
    [SettingMeta(Description = "CTE-scoped Redgate parenthesis style; empty inherits the global parenthesis style.",
        AllowedValues = new[] { "", "compactSimple", "compactToStatement", "compactIndented", "compactRightAligned",
            "expandedSimple", "expandedSplit", "expandedToStatement", "expandedIndented", "expandedRightAligned" })]
    public string ParenthesisStyle { get; set; } = "";

    /// <summary>Spec 031 FR-026 — CTE name on the line after WITH.</summary>
    [JsonPropertyName("placeNameOnNewLine")]
    [SettingMeta(Description = "Places the CTE name on the line after WITH.")]
    public bool PlaceNameOnNewLine { get; set; }

    /// <summary>Spec 031 FR-026 — indent the CTE name one level from WITH (with PlaceNameOnNewLine).</summary>
    [JsonPropertyName("indentName")]
    [SettingMeta(Description = "Indents the CTE name one level from WITH when it is on its own line.")]
    public bool IndentName { get; set; }

    /// <summary>Spec 031 FR-026 — indented | leftAligned | rightAligned.</summary>
    [JsonPropertyName("columnAlignment")]
    [SettingMeta(Description = "Controls how CTE column list items are aligned.",
        AllowedValues = new[] { "leftAligned", "indented", "rightAligned" })]
    public string ColumnAlignment { get; set; } = "leftAligned";
}

public class ExpressionOptions
{
    [JsonPropertyName("booleanOperatorNewLine")]
    [SettingMeta(Description = "Controls whether boolean operators outside WHERE break before the keyword, after it, or stay inline.",
        AllowedValues = new[] { "before", "after", "none" })]
    public string BooleanOperatorNewLine { get; set; } = "before";

    [JsonPropertyName("betweenOnOneLine")]
    [SettingMeta(Description = "Keeps BETWEEN x AND y on a single line.")]
    public bool BetweenOnOneLine { get; set; } = true;

    [JsonPropertyName("inListStyle")]
    [SettingMeta(Description = "Controls whether IN (...) lists stay on one line, expand to one item per line, or decide by threshold.",
        AllowedValues = new[] { "multiLine", "singleLine", "auto" })]
    public string InListStyle { get; set; } = "multiLine";

    [JsonPropertyName("inListThreshold")]
    [SettingMeta(Description = "Maximum rendered length in characters for an IN list to stay on one line in auto mode.", Min = 0, Max = 500)]
    public int InListThreshold { get; set; } = 60;

    [JsonPropertyName("existsSubqueryIndent")]
    [SettingMeta(Description = "Controls how the subquery inside EXISTS (...) is indented.",
        AllowedValues = new[] { "indent", "alignWithExists" })]
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
    /// Spec 031 FR-032 adds two Redgate list-alignment variants: "toFirstListItem" (align to
    /// the column of the first list item) and "beforeFirstListItem" (sit one column left of it).
    /// </summary>
    [JsonPropertyName("alignment")]
    [SettingMeta(Description = "Controls how AND/OR operators are aligned within a clause.",
        AllowedValues = new[] { "inlineWithStatement", "indentedFromStatement", "rightAligned", "toFirstListItem", "beforeFirstListItem" })]
    public string Alignment { get; set; } = "inlineWithStatement";

    /// <summary>
    /// SQL Prompt <c>operators.placeBetweenKeywordOnNewLine</c>:
    /// when true, the <c>BETWEEN</c> keyword goes on its own line.
    /// </summary>
    [JsonPropertyName("betweenOnNewLine")]
    [SettingMeta(Description = "Places the BETWEEN keyword on its own line.")]
    public bool BetweenOnNewLine { get; set; }

    /// <summary>
    /// Spec 020 Phase B closure — SQL Prompt <c>operators.placeAndBetweenBetweenOnNewLine</c>.
    /// When true, the <c>AND</c> that pairs with a <c>BETWEEN</c> (i.e. the AND in
    /// <c>BETWEEN x AND y</c>) gets its own line. Pairs with the existing
    /// <see cref="ExpressionOptions.BetweenOnOneLine"/> — when <c>BetweenOnOneLine</c> is
    /// true it wins (no break), regardless of this flag.
    /// </summary>
    [JsonPropertyName("andBetweenOnNewLine")]
    [SettingMeta(Description = "Places the AND that pairs with a BETWEEN on its own line.")]
    public bool AndBetweenOnNewLine { get; set; }

    /// <summary>Spec 031 FR-032 — wrapped BETWEEN's AND: toBetween | rightAlignedToBetween | toBeginningOfExpression.</summary>
    [JsonPropertyName("betweenAndAlignment")]
    [SettingMeta(Description = "Chooses the column a wrapped BETWEEN's AND aligns to.",
        AllowedValues = new[] { "toBetween", "rightAlignedToBetween", "toBeginningOfExpression" })]
    public string BetweenAndAlignment { get; set; } = "toBetween";
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
    [SettingMeta(Description = "Controls how the items of an expanded IN (...) list line up.",
        AllowedValues = new[] { "stacked", "wrapped", "rightAligned" })]
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
    [SettingMeta(Description = "Controls when IN list items are placed on new lines.",
        AllowedValues = new[] { "always", "never", "ifLongerThanWrap" })]
    public string PlaceItemsOnNewLine { get; set; } = "ifLongerThanWrap";

    /// <summary>Spec 031 FR-032 — spaces just inside IN-list parens.</summary>
    [JsonPropertyName("spaceAroundContents")]
    [SettingMeta(Description = "Inserts spaces just inside the IN list parentheses.")]
    public bool SpaceAroundContents { get; set; }
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
    [SettingMeta(Description = "Controls when function call arguments are placed on new lines.",
        AllowedValues = new[] { "always", "never", "ifLongerThanWrap" })]
    public string PlaceParametersOnNewLine { get; set; } = "ifLongerThanWrap";

    /// <summary>
    /// SQL Prompt <c>functionCalls.indentParameters</c>: when the parameter list is broken
    /// onto multiple lines, indent the parameters one level past the opening paren.
    /// </summary>
    [JsonPropertyName("indentParameters")]
    [SettingMeta(Description = "Indents wrapped function call arguments one level past the opening parenthesis.")]
    public bool IndentParameters { get; set; } = true;

    /// <summary>Spec 031 FR-030 — space between function name and '('.</summary>
    [JsonPropertyName("spaceAroundParentheses")]
    [SettingMeta(Description = "Inserts a space between the function name and its opening parenthesis.")]
    public bool SpaceAroundParentheses { get; set; }

    /// <summary>Spec 031 FR-030 — spaces just inside call parens, around the arguments.</summary>
    [JsonPropertyName("spaceAroundArgumentList")]
    [SettingMeta(Description = "Inserts spaces just inside the call parentheses, around the arguments.")]
    public bool SpaceAroundArgumentList { get; set; }

    /// <summary>Spec 031 FR-030 — '( )' for zero-argument calls.</summary>
    [JsonPropertyName("spaceBetweenEmptyParentheses")]
    [SettingMeta(Description = "Writes ( ) with a space between the parentheses for zero-argument calls.")]
    public bool SpaceBetweenEmptyParentheses { get; set; }
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
    [SettingMeta(Description = "Controls how multi-line comments are reformatted.",
        AllowedValues = new[] { "preserve", "normaliseIndent", "joinShortLines" })]
    public string MultilineFormatting { get; set; } = "preserve";

    /// <summary>
    /// SQL Prompt <c>comments.recognizeCommonPatterns</c>: when true, the formatter
    /// detects header / banner / TODO-style comments and leaves their internal layout intact
    /// even when other formatting passes would otherwise reflow them.
    /// </summary>
    [JsonPropertyName("recognizeCommonPatterns")]
    [SettingMeta(Description = "Leaves the internal layout of recognized header, banner, and TODO-style comments intact.")]
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
    [SettingMeta(Description = "Expands a multi-variable DECLARE so each variable occupies its own line.")]
    public bool OneDeclarationPerLine { get; set; }

    /// <summary>
    /// When true (and <see cref="OneDeclarationPerLine"/> is also true), aligns the data-type
    /// tokens of each variable in a DECLARE block to a common column by padding with spaces.
    /// Mirrors SQL Prompt's <c>variables.alignDataTypes</c>.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("alignDataTypes")]
    [SettingMeta(Description = "Aligns the data types of variables in a DECLARE block to a common column.")]
    public bool AlignDataTypes { get; set; }

    /// <summary>
    /// When true (and <see cref="OneDeclarationPerLine"/> is also true), aligns the <c>=</c>
    /// assignment operators (default values) in a DECLARE block to a common column.
    /// Mirrors SQL Prompt's <c>variables.alignDefaultValues</c>.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("alignDefaultValues")]
    [SettingMeta(Description = "Aligns the = default-value assignments in a DECLARE block to a common column.")]
    public bool AlignDefaultValues { get; set; }

    /// <summary>Spec 031 FR-027 — '=' leads the continuation line in DECLARE/SET breaks.</summary>
    [JsonPropertyName("equalsOnNewLine")]
    [SettingMeta(Description = "Starts the continuation line with = when a DECLARE/SET assignment breaks.")]
    public bool EqualsOnNewLine { get; set; }
}

/// <summary>
/// Spec 031 FR-029 — Redgate insertStatements section: per-construct parenthesis style,
/// content indent, and per-item line placement for the INSERT column list and VALUES tuples.
/// Supersedes the dead <c>DmlOptions.InsertColumnListFormat</c>/<c>ValuesFormat</c> fields.
/// </summary>
public class InsertStatementsOptions
{
    [JsonPropertyName("columns")]
    public InsertParenOptions Columns { get; set; } = new()
    {
        IndentContents = true,
        PlaceSubsequentItemsOnNewLines = "always",
    };

    [JsonPropertyName("values")]
    public InsertParenOptions Values { get; set; } = new()
    {
        IndentContents = false,
        PlaceSubsequentItemsOnNewLines = "never",
    };
}

public class InsertParenOptions
{
    /// <summary>
    /// Redgate 9-value parenthesis style; empty string = inherit <c>Parenthesis.Style</c>.
    /// compactSimple | compactToStatement | compactIndented | compactRightAligned | expandedSimple |
    /// expandedSplit | expandedToStatement | expandedIndented | expandedRightAligned; empty = inherit/legacy.
    /// </summary>
    [JsonPropertyName("parenthesisStyle")]
    [SettingMeta(Description = "Redgate parenthesis style for this INSERT construct; empty inherits the global parenthesis style.",
        AllowedValues = new[] { "", "compactSimple", "compactToStatement", "compactIndented", "compactRightAligned",
            "expandedSimple", "expandedSplit", "expandedToStatement", "expandedIndented", "expandedRightAligned" })]
    public string ParenthesisStyle { get; set; } = "";

    [JsonPropertyName("indentContents")]
    [SettingMeta(Description = "Indents the contents of this INSERT construct's parentheses.")]
    public bool IndentContents { get; set; }

    /// <summary>always | never | ifLongerThanWrap</summary>
    [JsonPropertyName("placeSubsequentItemsOnNewLines")]
    [SettingMeta(Description = "Controls when items after the first are placed on new lines within this INSERT construct.",
        AllowedValues = new[] { "always", "never", "ifLongerThanWrap" })]
    public string PlaceSubsequentItemsOnNewLines { get; set; } = "never";
}

public class FormatActionConfig
{
    [JsonPropertyName("applyLayout")]
    [SettingMeta(Description = "Applies layout rules (line breaks and indentation) when formatting.")]
    public bool ApplyLayout { get; set; } = true;

    [JsonPropertyName("applyCasing")]
    [SettingMeta(Description = "Applies casing rules when formatting.")]
    public bool ApplyCasing { get; set; } = true;

    [JsonPropertyName("insertSemicolons")]
    [SettingMeta(Description = "Adds missing statement-terminating semicolons when formatting.")]
    public bool InsertSemicolons { get; set; }

    [JsonPropertyName("removeSemicolons")]
    [SettingMeta(Description = "Removes statement-terminating semicolons when formatting.")]
    public bool RemoveSemicolons { get; set; }

    [JsonPropertyName("expandWildcards")]
    [SettingMeta(Description = "Expands SELECT * into the explicit column list when formatting.")]
    public bool ExpandWildcards { get; set; }

    [JsonPropertyName("qualifyObjectNames")]
    [SettingMeta(Description = "Adds schema qualifiers to object names when formatting.")]
    public bool QualifyObjectNames { get; set; }

    [JsonPropertyName("addAsKeyword")]
    [SettingMeta(Description = "Inserts the AS keyword before column aliases when formatting.")]
    public bool AddAsKeyword { get; set; } = true;

    [JsonPropertyName("addSquareBrackets")]
    [SettingMeta(Description = "Wraps identifiers in square brackets when formatting.")]
    public bool AddSquareBrackets { get; set; }
}
