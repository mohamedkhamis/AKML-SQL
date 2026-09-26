namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>The value type of one SQL Prompt style option, as the Redgate schema declares it.</summary>
public enum SqlPromptOptionKind
{
    Boolean,
    Integer,
    Choice,
}

/// <summary>One allowed value of a <see cref="SqlPromptOptionKind.Choice"/> option: the exact JSON
/// value SQL Prompt writes, plus the label the style editors show for it.</summary>
public sealed record SqlPromptChoice(string Value, string Label);

/// <summary>
/// One option of a SQL Prompt formatting style: its JSON path exactly as SQL Prompt writes it
/// (<c>lists.placeCommasBeforeItems</c>), where it sits in the style editor, its type, its
/// default, and what it does.
/// </summary>
public sealed record SqlPromptOption
{
    /// <summary>Dotted JSON path, exactly as it appears in a SQL Prompt <c>.json</c> style.</summary>
    public required string Path { get; init; }

    /// <summary>Top-level section id (<c>whitespace</c>, <c>joinStatements</c>, …).</summary>
    public string Section => Path[..Path.IndexOf('.')];

    /// <summary>Sub-heading inside the section page (e.g. "New lines", "ON"), or null.</summary>
    public string? Group { get; init; }

    public required string Label { get; init; }
    public required string Description { get; init; }
    public required SqlPromptOptionKind Kind { get; init; }

    /// <summary>Default as its JSON literal text: <c>true</c>, <c>4</c>, <c>"spaces"</c> without quotes.</summary>
    public required string Default { get; init; }

    public IReadOnlyList<SqlPromptChoice> Choices { get; init; } = [];
    public int? Min { get; init; }
    public int? Max { get; init; }

    /// <summary>
    /// The option this one only matters under, with the value that turns it on — the editors
    /// grey it out otherwise, the way SQL Prompt's editor does (a collapse threshold under its
    /// collapse switch, say). Null when the option always applies.
    /// </summary>
    public (string Path, string Value)? EnabledWhen { get; init; }

    /// <summary>
    /// True for options Redgate added after the published schema (documented in SQL Prompt
    /// release notes rather than the schema file). Kept so they survive a round trip.
    /// </summary>
    public bool IsPostSchemaAddition { get; init; }

    /// <summary>When the option's effect depends on other settings or on the code, a short
    /// "shows when…" note the editors display beside it.</summary>
    public string? Note { get; init; }
}

/// <summary>One page of the style editor (Whitespace, Lists, …) and the options on it.</summary>
public sealed record SqlPromptSection(string Id, string Label, string Category, IReadOnlyList<SqlPromptOption> Options);

/// <summary>
/// Every SQL Prompt formatting option, in the order and grouping of SQL Prompt's own style editor:
/// four categories (Global, Statements, Clauses, Expressions), fourteen pages, 114 options from
/// Redgate's published schema plus the documented later addition
/// <c>whitespace.newLines.alignMultilineCommentsMatchingPatterns</c>.
/// <para>
/// Paths, types, allowed values and defaults are Redgate's own — pinned by a test against the
/// vendored schema (specs/031-redgate-style-import/reference/formattingstyle-schema.json), so a
/// style saved here opens unchanged in SQL Prompt and the other way round.
/// </para>
/// </summary>
public static class SqlPromptOptionCatalog
{
    public const string CategoryGlobal = "Global";
    public const string CategoryStatements = "Statements";
    public const string CategoryClauses = "Clauses";
    public const string CategoryExpressions = "Expressions";

    // Sub-heading of the Data (DML) page's statement-keyword options.
    private const string DmlKeywords = "INSERT, DISTINCT and TOP";

    public static readonly IReadOnlyList<string> Categories =
        [CategoryGlobal, CategoryStatements, CategoryClauses, CategoryExpressions];

    public static SqlPromptOption? Find(string path) => ByPath.GetValueOrDefault(path);

    /// <summary>
    /// Version of the catalog as served to style editors that cannot load this library (the
    /// SSMS shell). Bump when an option, label or allowed value changes.
    /// Kept apart from the AKML settings schema's versions so a cached copy of one is never
    /// mistaken for the other.
    /// </summary>
    public const int SchemaVersion = 2001;

    /// <summary>Prefix of the setting ids in <see cref="ToEditorSchemaJson"/>: working values keyed
    /// <c>sqlPrompt.&lt;path&gt;</c> nest straight into the style's <c>sqlPrompt</c> document.</summary>
    public const string EditorIdPrefix = "sqlPrompt.";

    /// <summary>
    /// The catalog in the shape the SSMS Format Styles window renders (the
    /// spec-033 settings schema: <c>groups</c> with <c>parentId</c> categories, <c>settings</c>
    /// with <c>type</c>/<c>default</c>/<c>allowedEnumValues</c>), plus <c>"model":"sqlPrompt"</c>
    /// and the extras SQL Prompt's editor shows: value labels, notes, sub-headings, which option
    /// turns another on, and each page's preview sample. Setting ids are
    /// <see cref="EditorIdPrefix"/> + the SQL Prompt path.
    /// </summary>
    public static string ToEditorSchemaJson()
    {
        static System.Text.Json.Nodes.JsonNode? Literal(SqlPromptOption o, string value) => o.Kind switch
        {
            SqlPromptOptionKind.Boolean => System.Text.Json.Nodes.JsonValue.Create(value == "true"),
            SqlPromptOptionKind.Integer => System.Text.Json.Nodes.JsonValue.Create(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => System.Text.Json.Nodes.JsonValue.Create(value),
        };

        var groups = new System.Text.Json.Nodes.JsonArray();
        var settings = new System.Text.Json.Nodes.JsonArray();
        foreach (var section in Sections)
        {
            groups.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = section.Id,
                ["displayName"] = section.Label,
                ["parentId"] = section.Category.ToLowerInvariant(),
                ["sample"] = SqlPromptPreviewSamples.For(section.Id),
            });
            foreach (var o in section.Options)
            {
                var node = new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = EditorIdPrefix + o.Path,
                    ["groupId"] = section.Id,
                    ["displayName"] = o.Label,
                    ["type"] = o.Kind switch { SqlPromptOptionKind.Boolean => "Bool", SqlPromptOptionKind.Integer => "Int", _ => "Enum" },
                    ["status"] = "Implemented",
                    ["sqlPromptKey"] = o.Path,
                    ["default"] = Literal(o, o.Default),
                    ["description"] = o.Description,
                };
                if (o.Group is not null) node["subgroup"] = o.Group;
                if (o.Note is not null) node["note"] = o.Note;
                if (o.Kind == SqlPromptOptionKind.Choice)
                {
                    node["allowedEnumValues"] = new System.Text.Json.Nodes.JsonArray(o.Choices.Select(c => (System.Text.Json.Nodes.JsonNode?)c.Value).ToArray());
                    node["enumLabels"] = new System.Text.Json.Nodes.JsonArray(o.Choices.Select(c => (System.Text.Json.Nodes.JsonNode?)c.Label).ToArray());
                }
                if (o.Min is int min) node["min"] = min;
                if (o.Max is int max) node["max"] = max;
                if (o.EnabledWhen is { } gate && Find(gate.Path) is { } gateOption)
                    node["enabledWhen"] = new System.Text.Json.Nodes.JsonObject { ["id"] = EditorIdPrefix + gate.Path, ["value"] = Literal(gateOption, gate.Value) };
                settings.Add(node);
            }
        }
        return new System.Text.Json.Nodes.JsonObject
        {
            ["model"] = "sqlPrompt",
            ["schemaVersion"] = SchemaVersion,
            ["groups"] = groups,
            ["settings"] = settings,
        }.ToJsonString();
    }

    // ── shared value sets ────────────────────────────────────────────────────

    private static readonly SqlPromptChoice[] Casing =
    [
        new("leaveAsIs", "Leave as is"),
        new("lowercase", "lowercase"),
        new("uppercase", "UPPERCASE"),
        new("lowerCamelCase", "lowerCamelCase"),
        new("upperCamelCase", "UpperCamelCase"),
    ];

    private static readonly SqlPromptChoice[] ParenthesisStyles =
    [
        new("compactSimple", "Compact: simple"),
        new("compactToStatement", "Compact: closing parenthesis to statement"),
        new("compactIndented", "Compact: closing parenthesis indented"),
        new("compactRightAligned", "Compact: closing parenthesis right-aligned"),
        new("expandedSimple", "Expanded: simple"),
        new("expandedSplit", "Expanded: split"),
        new("expandedToStatement", "Expanded: to statement"),
        new("expandedIndented", "Expanded: indented"),
        new("expandedRightAligned", "Expanded: right-aligned"),
    ];

    private static readonly SqlPromptChoice[] AlwaysNeverIfLonger =
    [
        new("always", "Always"),
        new("never", "Never"),
        new("ifLongerThanMaxLineLength", "If longer than the wrap width"),
    ];

    private static readonly SqlPromptChoice[] AlwaysNeverIfMultiple =
    [
        new("always", "Always"),
        new("never", "Never"),
        new("ifMultiple", "If there is more than one"),
    ];

    private static readonly SqlPromptChoice[] IndentedLeftRight =
    [
        new("indented", "Indented"),
        new("leftAligned", "Left-aligned"),
        new("rightAligned", "Right-aligned"),
    ];

    // Static initializers run in declaration order: these must follow the value sets above.
    public static IReadOnlyList<SqlPromptSection> Sections { get; } = Build();

    public static IReadOnlyList<SqlPromptOption> Options { get; } =
        Sections.SelectMany(s => s.Options).ToList();

    private static readonly Dictionary<string, SqlPromptOption> ByPath =
        Options.ToDictionary(o => o.Path, StringComparer.OrdinalIgnoreCase);

    // ── builders ─────────────────────────────────────────────────────────────

    private static SqlPromptOption Bool(string path, bool @default, string label, string description,
        string? group = null, (string, string)? enabledWhen = null) => new()
    {
        Path = path, Kind = SqlPromptOptionKind.Boolean, Default = @default ? "true" : "false",
        Label = label, Description = description, Group = group, EnabledWhen = enabledWhen,
    };

    private static SqlPromptOption Int(string path, int @default, int min, int max, string label, string description,
        string? group = null, (string, string)? enabledWhen = null) => new()
    {
        Path = path, Kind = SqlPromptOptionKind.Integer, Default = @default.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Min = min, Max = max, Label = label, Description = description, Group = group, EnabledWhen = enabledWhen,
    };

    private static SqlPromptOption Choice(string path, string @default, SqlPromptChoice[] choices, string label, string description,
        string? group = null, (string, string)? enabledWhen = null) => new()
    {
        Path = path, Kind = SqlPromptOptionKind.Choice, Default = @default, Choices = choices,
        Label = label, Description = description, Group = group, EnabledWhen = enabledWhen,
    };

    private static IReadOnlyList<SqlPromptSection> Build() =>
    [
        // ═══ Global ═══════════════════════════════════════════════════════════
        new("whitespace", "Whitespace", CategoryGlobal,
        [
            Choice("whitespace.spacesOrTabs", "spaces",
                [new("spaces", "Use spaces"), new("tabs", "Use tabs"), new("tabsIfPossible", "Use tabs where possible")],
                "Indent with", "Whether spaces or tabs are used while formatting. \"Where possible\" indents with tabs and pads alignment with spaces.",
                "Indentation"),
            Int("whitespace.numberOfSpacesInTabs", 4, 1, 16,
                "Spaces per tab", "How many spaces are inserted when tab is pressed.", "Indentation"),
            Bool("whitespace.wrapLongLines", true,
                "Wrap long lines", "Whether long lines will be wrapped onto a new line.", "Line length"),
            Int("whitespace.wrapLinesLongerThan", 120, 20, 1000,
                "Wrap lines longer than", "Lines with more than this number of characters will be wrapped.", "Line length",
                ("whitespace.wrapLongLines", "true")),
            Choice("whitespace.whiteSpaceBeforeSemiColon", "none",
                [new("none", "None"), new("spaceBefore", "Space before"), new("newLineBefore", "New line before")],
                "Before semicolons", "Whether spaces will be inserted before semicolons.", "Semicolons"),
            Bool("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements", true,
                "Preserve existing empty lines between statements", "Keep empty lines that already separate statements.", "New lines"),
            Int("whitespace.newLines.emptyLinesBetweenStatements", 1, 0, 10,
                "Empty lines between statements", "How many empty lines to insert between separate statements.", "New lines"),
            Bool("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator", true,
                "Preserve existing empty lines after batch separator", "Keep empty lines that already follow GO.", "New lines"),
            Int("whitespace.newLines.emptyLinesAfterBatchSeparator", 1, 0, 10,
                "Empty lines after batch separator", "How many empty lines to insert after a batch separator.", "New lines"),
            Bool("whitespace.newLines.alignMultilineCommentsMatchingPatterns", false,
                "Align multi-line comments", "Re-indent the lines of a /* */ comment with the code around it, leaving banner comments (lines of *, =, - or #) as they are.",
                "New lines") with { IsPostSchemaAddition = true },
        ]),
        new("lists", "Lists", CategoryGlobal,
        [
            Choice("lists.placeFirstItemOnNewLine", "never",
                [new("always", "Always"), new("never", "Never"), new("ifSubsequentItems", "If there are subsequent items")],
                "Place first item on new line", "Whether the first item of a list starts on the line after its keyword."),
            Choice("lists.placeSubsequentItemsOnNewLines", "always", AlwaysNeverIfLonger,
                "Place subsequent items on new lines", "Place subsequent list items on new lines."),
            Bool("lists.alignSubsequentItemsWithFirstItem", true,
                "Align subsequent items with first item", "Align subsequent list items with first item."),
            Bool("lists.alignItemsAcrossClauses", true,
                "Align items across clauses", "Align list items across clauses: the lists of SELECT, FROM, WHERE, GROUP BY… start in one column."),
            Bool("lists.indentListItems", true,
                "Indent list items", "Indent list items that are not aligned with the first item.") with { Note = "Shows when the first item is on its own line, or items are not aligned with the first one." },
            Bool("lists.alignItemsToTabStops", false,
                "Align items to tab stops", "Round aligned list columns up to the next tab stop."),
            Bool("lists.alignAliases", false,
                "Align aliases", "Align aliases to each other."),
            Bool("lists.alignComments", false,
                "Align comments", "Align end-of-line comments to each other."),
            Bool("lists.placeCommasBeforeItems", false,
                "Place commas before items", "Place commas before list items.", "Commas"),
            Bool("lists.addSpaceBeforeComma", false,
                "Add space before comma", "Add space before commas.", "Commas"),
            Bool("lists.addSpaceAfterComma", true,
                "Add space after comma", "Add space after commas.", "Commas"),
            Choice("lists.commaAlignment", "toList",
                [new("beforeItem", "Before item"), new("toList", "To list"), new("toStatement", "To statement")],
                "Comma alignment", "How commas separating list items are aligned.", "Commas",
                ("lists.placeCommasBeforeItems", "true")),
        ]),
        new("parentheses", "Parentheses", CategoryGlobal,
        [
            Choice("parentheses.parenthesisStyle", "compactSimple", ParenthesisStyles,
                "Parenthesis style", "The format to use for parenthesized code."),
            Bool("parentheses.indentParenthesesContents", false,
                "Indent contents", "Indent the contents of parentheses."),
            Bool("parentheses.collapseShortParenthesisContents", false,
                "Collapse short contents", "Collapse short contents of parentheses onto a single line."),
            Int("parentheses.collapseParenthesesShorterThan", 80, 1, 1000,
                "Collapse contents shorter than", "Contents with fewer than this number of characters will be collapsed onto a single line if enabled.",
                enabledWhen: ("parentheses.collapseShortParenthesisContents", "true")),
            Bool("parentheses.addSpacesAroundParentheses", true,
                "Add spaces around parentheses", "Whether spaces will be added around parentheses.", "Spaces"),
            Bool("parentheses.addSpacesInsideParentheses", false,
                "Add spaces inside parentheses", "Whether spaces are added around parentheses' contents.", "Spaces"),
        ]),
        new("casing", "Casing", CategoryGlobal,
        [
            Choice("casing.reservedKeywords", "leaveAsIs", Casing, "Reserved keywords", "How reserved keywords are cased."),
            Choice("casing.builtInFunctions", "leaveAsIs", Casing, "Built-in functions", "How built-in functions are cased."),
            Choice("casing.builtInDataTypes", "leaveAsIs", Casing, "Built-in data types", "How built-in data types are cased."),
            Choice("casing.globalVariables", "leaveAsIs", Casing, "Global variables", "How global variables (@@ROWCOUNT…) are cased."),
            Bool("casing.useObjectDefinitionCase", false,
                "Use object definition case", "Use the case from the definition when casing objects (needs a connection).") with { Note = "Needs a database connection, so it does not change the preview." },
        ]),

        // ═══ Statements ═══════════════════════════════════════════════════════
        new("dml", "Data (DML)", CategoryStatements,
        [
            Choice("dml.clauses.clauseAlignment", "leftAligned",
                [new("leftAligned", "Left-aligned"), new("rightAligned", "Right-aligned"), new("toFirstListItem", "To first list item")],
                "Clause alignment", "How clauses within DML statements are aligned.", "Clauses"),
            Int("dml.clauses.clauseIndentation", 0, 0, 16,
                "Clause indentation", "The number of spaces to indent clauses within DML statements.", "Clauses"),
            Choice("dml.listItems.placeFromTableOnNewLine", "never", AlwaysNeverIfMultiple,
                "Place FROM table on new line", "When to place the table in a DML statement on a new line.", "List items"),
            Choice("dml.listItems.placeWhereConditionOnNewLine", "never", AlwaysNeverIfMultiple,
                "Place WHERE condition on new line", "When to place a WHERE condition in a DML statement on a new line.", "List items"),
            Choice("dml.listItems.placeGroupByAndOrderByOnNewLine", "never", AlwaysNeverIfMultiple,
                "Place GROUP BY and ORDER BY items on new line", "When to place GROUP BY and ORDER BY clauses in a DML statement on a new line.", "List items"),
            // Their own heading: after "List items", an ungrouped option is drawn under that heading
            // by both editors (they only start a heading when the group changes to a named one).
            Bool("dml.placeInsertTableOnNewLine", false,
                "Place INSERT table on new line", "In INSERT statements, place the table name on a new line.", DmlKeywords),
            Bool("dml.placeDistinctAndTopClausesOnNewLine", false,
                "Place DISTINCT and TOP on new line", "Whether to place DISTINCT and TOP clauses on a new line.", DmlKeywords),
            Bool("dml.addNewLineAfterDistinctAndTopClauses", false,
                "Add new line after DISTINCT and TOP", "Whether to add a new line after DISTINCT and TOP clauses.", DmlKeywords),
            Bool("dml.collapseShortStatements", false,
                "Collapse short statements", "Collapse short statements onto a single line.", "Collapsing"),
            Int("dml.collapseStatementsShorterThan", 80, 1, 1000,
                "Collapse statements shorter than", "Statements that are shorter than this will be collapsed onto a single line if enabled.", "Collapsing",
                ("dml.collapseShortStatements", "true")),
            Bool("dml.collapseShortSubqueries", false,
                "Collapse short subqueries", "Collapse short subqueries onto a single line.", "Collapsing"),
            Int("dml.collapseSubqueriesShorterThan", 80, 1, 1000,
                "Collapse subqueries shorter than", "Subqueries that are shorter than this will be collapsed onto a single line if enabled.", "Collapsing",
                ("dml.collapseShortSubqueries", "true")),
        ]),
        new("ddl", "Schema (DDL)", CategoryStatements,
        [
            Choice("ddl.parenthesisStyle", "compactSimple", ParenthesisStyles,
                "Parenthesis style", "The format to use for parenthesized DDL code."),
            Bool("ddl.indentParenthesesContents", false,
                "Indent parentheses contents", "Whether the contents of parentheses in DDL statements are indented."),
            Bool("ddl.alignDataTypesAndConstraints", true,
                "Align data types and constraints", "Whether to align data types and constraints."),
            Bool("ddl.placeConstraintsOnNewLines", false,
                "Place constraints on new lines", "Whether to place constraints on a new line."),
            Choice("ddl.placeConstraintColumnsOnNewLines", "ifLongerThanMaxLineLength",
                [new("always", "Always"), new("ifLongerThanMaxLineLength", "If longer than the wrap width"), new("ifLongerOrMultipleColumns", "If longer, or more than one column")],
                "Place constraint columns on new lines", "When to place constraint columns on a new line."),
            Bool("ddl.indentClauses", false,
                "Indent clauses", "Whether to indent clauses in DDL statements."),
            Choice("ddl.placeFirstProcedureParameterOnNewLine", "ifMultipleItems",
                [new("always", "Always"), new("never", "Never"), new("ifMultipleItems", "If there is more than one")],
                "Place first procedure parameter on new line", "Whether to place the first parameter of a stored procedure on a new line.") with { Note = "Applies to stored procedures and functions." },
            Bool("ddl.collapseShortStatements", false,
                "Collapse short statements", "Collapse short DDL statements onto a single line.", "Collapsing"),
            Int("ddl.collapseStatementsShorterThan", 80, 1, 1000,
                "Collapse statements shorter than", "DDL statements that are shorter than this will be collapsed onto a single line if enabled.", "Collapsing",
                ("ddl.collapseShortStatements", "true")),
        ]),
        new("controlFlow", "Control flow", CategoryStatements,
        [
            Bool("controlFlow.placeBeginAndEndOnNewLine", true,
                "Place BEGIN and END on new lines", "Whether BEGIN and END keywords are placed on new lines."),
            Bool("controlFlow.indentBeginAndEndKeywords", false,
                "Indent BEGIN and END", "Whether to indent BEGIN and END keywords."),
            Bool("controlFlow.indentContentsOfStatements", true,
                "Indent contents", "Whether the contents of control flow statements are indented."),
            Bool("controlFlow.collapseShortStatements", false,
                "Collapse short statements", "Collapse short control flow statements onto a single line.", "Collapsing"),
            Int("controlFlow.collapseStatementsShorterThan", 80, 1, 1000,
                "Collapse statements shorter than", "Control flow statements that are shorter than this will be collapsed onto a single line if enabled.", "Collapsing",
                ("controlFlow.collapseShortStatements", "true")),
        ]),
        new("cte", "CTE", CategoryStatements,
        [
            Choice("cte.parenthesisStyle", "compactSimple", ParenthesisStyles,
                "Parenthesis style", "The format to use for parenthesized CTE code."),
            Bool("cte.indentContents", false,
                "Indent contents", "Whether to indent clauses in CTE statements."),
            Bool("cte.placeNameOnNewLine", false,
                "Place name on new line", "Whether the CTE name is placed on a new line."),
            Bool("cte.indentName", false,
                "Indent name", "Whether the CTE name is indented.", enabledWhen: ("cte.placeNameOnNewLine", "true")),
            Bool("cte.placeColumnsOnNewLine", false,
                "Place columns on new line", "Whether the CTE columns are placed on a new line."),
            Choice("cte.columnAlignment", "leftAligned", IndentedLeftRight,
                "Column alignment", "How the columns in a CTE are aligned.", enabledWhen: ("cte.placeColumnsOnNewLine", "true")),
            Bool("cte.placeAsOnNewLine", true,
                "Place AS on new line", "Whether the AS keyword is placed on a new line."),
            Choice("cte.asAlignment", "leftAligned", IndentedLeftRight,
                "AS alignment", "How the AS keyword is aligned.", enabledWhen: ("cte.placeAsOnNewLine", "true")),
        ]),
        new("variables", "Variables", CategoryStatements,
        [
            Bool("variables.alignDataTypesAndValues", true,
                "Align data types and values", "Whether to align the data types and values in DECLARE statements."),
            Bool("variables.addSpaceBetweenDataTypeAndPrecision", false,
                "Add space between data type and precision", "Whether a space is added between a data type and its precision: nvarchar (100)."),
            Bool("variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength", true,
                "Place assigned value on new line if too long", "Whether the assigned value is placed on a new line in long SET statements."),
            Bool("variables.placeEqualsSignOnNewLine", false,
                "Place = on new line", "Whether the = sign is placed on a new line with the assigned value.") with { Note = "Shows when an assigned value is too long for its line." },
        ]),

        // ═══ Clauses ══════════════════════════════════════════════════════════
        new("joinStatements", "Join", CategoryClauses,
        [
            Bool("joinStatements.join.placeOnNewLine", true,
                "Place JOIN on new line", "Whether the JOIN keyword is placed on a new line.", "JOIN"),
            Choice("joinStatements.join.keywordAlignment", "toFrom",
                [new("toFrom", "To FROM"), new("rightAlignedToFrom", "Right-aligned to FROM"), new("toTable", "To table"), new("indented", "Indented")],
                "JOIN alignment", "How the JOIN keyword is aligned.", "JOIN", ("joinStatements.join.placeOnNewLine", "true")) with { Note = "Right-aligned to FROM needs room left of FROM, as with right-aligned clauses." },
            Bool("joinStatements.join.insertEmptyLineBetweenJoinClauses", false,
                "Insert empty line between JOIN clauses", "Whether empty lines are inserted between JOIN clauses.", "JOIN"),
            Bool("joinStatements.join.placeJoinTableOnNewLine", false,
                "Place join table on new line", "Whether the join table is placed on a new line.", "JOIN"),
            Bool("joinStatements.join.indentJoinTable", true,
                "Indent join table", "Whether the join table is indented (when it is on its own line).", "JOIN",
                ("joinStatements.join.placeJoinTableOnNewLine", "true")),
            Bool("joinStatements.on.placeOnNewLine", true,
                "Place ON on new line", "Whether the ON keyword is placed on a new line.", "ON"),
            Choice("joinStatements.on.keywordAlignment", "toJoin",
                [new("toJoin", "To JOIN"), new("rightAlignedToJoin", "Right-aligned to JOIN"), new("rightAlignedToInner", "Right-aligned to INNER"), new("toTable", "To table"), new("indented", "Indented")],
                "ON alignment", "How the ON keyword is aligned.", "ON", ("joinStatements.on.placeOnNewLine", "true")),
            Bool("joinStatements.on.placeConditionOnNewLine", false,
                "Place condition on new line", "Whether a join condition is placed on a new line.", "ON"),
            Choice("joinStatements.on.conditionAlignment", "toOnKeyword",
                [new("toOnKeyword", "To ON"), new("toInner", "To INNER"), new("toTable", "To table"), new("indented", "Indented")],
                "Condition alignment", "How join conditions are aligned.", "ON", ("joinStatements.on.placeConditionOnNewLine", "true")) with { Note = "To INNER and to ON differ only when ON is not aligned to JOIN." },
        ]),
        new("insertStatements", "Insert", CategoryClauses,
        [
            Choice("insertStatements.columns.parenthesisStyle", "expandedToStatement", ParenthesisStyles,
                "Parenthesis style", "The format to use for parenthesized insert statements.", "Columns"),
            Bool("insertStatements.columns.indentContents", true,
                "Indent contents", "Whether to indent content.", "Columns") with { Note = "Shows when the column list is split over several lines." },
            Choice("insertStatements.columns.placeSubsequentColumnsOnNewLines", "always", AlwaysNeverIfLonger,
                "Place subsequent columns on new lines", "When to place subsequent columns on new lines.", "Columns"),
            Choice("insertStatements.values.parenthesisStyle", "compactToStatement", ParenthesisStyles,
                "Parenthesis style", "The format to use for values in INSERT statements.", "Values"),
            Bool("insertStatements.values.indentContents", false,
                "Indent contents", "Whether to indent content.", "Values") with { Note = "Shows when a row is split over several lines." },
            Choice("insertStatements.values.placeSubsequentValuesOnNewLines", "never", AlwaysNeverIfLonger,
                "Place subsequent values on new lines", "When to place subsequent values on new lines.", "Values"),
        ]),

        // ═══ Expressions ══════════════════════════════════════════════════════
        new("functionCalls", "Function calls", CategoryExpressions,
        [
            Choice("functionCalls.placeArgumentsOnNewLines", "ifLongerThanMaxLineLength", AlwaysNeverIfLonger,
                "Place arguments on new lines", "When to place arguments on new lines."),
            Bool("functionCalls.addSpacesAroundParentheses", false,
                "Add spaces around parentheses", "Whether to add spaces around parentheses: COUNT (*)."),
            Bool("functionCalls.addSpacesAroundArgumentList", false,
                "Add spaces around argument list", "Whether to add spaces around argument list: COALESCE( a, b )."),
            Bool("functionCalls.addSpaceBetweenEmptyParentheses", false,
                "Add space between empty parentheses", "Whether to add space between empty parentheses: GETDATE( )."),
        ]),
        new("caseExpressions", "CASE", CategoryExpressions,
        [
            Bool("caseExpressions.placeExpressionOnNewLine", true,
                "Place result expression on new line", "Whether to place the THEN / ELSE result expression on a new line."),
            Choice("caseExpressions.placeFirstWhenOnNewLine", "always",
                [new("always", "Always"), new("never", "Never"), new("ifInputExpression", "If there is an input expression")],
                "Place first WHEN on new line", "When to place the first WHEN keyword on a new line."),
            Choice("caseExpressions.whenAlignment", "indentedFromCase",
                [new("indentedFromCase", "Indented from CASE"), new("toCase", "To CASE"), new("toFirstItem", "To first item")],
                "WHEN alignment", "How the WHEN keywords are aligned."),
            Bool("caseExpressions.placeThenOnNewLine", false,
                "Place THEN on new line", "Whether the THEN keyword is placed on a new line."),
            Choice("caseExpressions.thenAlignment", "indentedFromWhen",
                [new("indentedFromWhen", "Indented from WHEN"), new("toWhen", "To WHEN"), new("toWhenExpression", "To WHEN expression")],
                "THEN alignment", "How the THEN keywords are aligned.", enabledWhen: ("caseExpressions.placeThenOnNewLine", "true")),
            Bool("caseExpressions.placeElseOnNewLine", true,
                "Place ELSE on new line", "Whether the ELSE keyword is placed on new line."),
            Bool("caseExpressions.alignElseToWhen", true,
                "Align ELSE to WHEN", "Whether to align the ELSE keyword to the WHEN keyword.", enabledWhen: ("caseExpressions.placeElseOnNewLine", "true")),
            Bool("caseExpressions.placeEndOnNewLine", true,
                "Place END on new line", "Whether to place the END keyword on new line."),
            Choice("caseExpressions.endAlignment", "toCase",
                [new("toCase", "To CASE"), new("toWhen", "To WHEN"), new("rightAlignedToWhen", "Right-aligned to WHEN")],
                "END alignment", "How the END keyword is aligned.", enabledWhen: ("caseExpressions.placeEndOnNewLine", "true")),
            Bool("caseExpressions.collapseShortCaseExpressions", false,
                "Collapse short CASE expressions", "Collapse short CASE expressions onto a single line.", "Collapsing"),
            Int("caseExpressions.collapseCaseExpressionsShorterThan", 80, 1, 1000,
                "Collapse CASE expressions shorter than", "CASE expressions shorter than this will be collapsed onto a single line if enabled.", "Collapsing",
                ("caseExpressions.collapseShortCaseExpressions", "true")),
        ]),
        new("operators", "Operators", CategoryExpressions,
        [
            Bool("operators.comparison.align", false,
                "Align comparison operators", "Whether to align comparison operators.", "Comparison"),
            Bool("operators.comparison.addSpacesAround", true,
                "Add spaces around comparison operators", "Whether to add spaces around comparison operators.", "Comparison"),
            Bool("operators.arithmetic.addSpacesAround", true,
                "Add spaces around arithmetic operators", "Whether to add spaces around arithmetic operators.", "Arithmetic"),
            Choice("operators.andOr.placeOnNewLine", "always", AlwaysNeverIfLonger,
                "Place AND / OR on new line", "When to place AND / OR operators on a new line.", "AND / OR"),
            Choice("operators.andOr.alignment", "leftAligned",
                [new("leftAligned", "Left-aligned"), new("rightAligned", "Right-aligned"), new("beforeFirstListItem", "Before first list item"), new("toFirstListItem", "To first list item"), new("indented", "Indented")],
                "AND / OR alignment", "How to align AND / OR operators.", "AND / OR"),
            Bool("operators.andOr.placeKeywordBeforeCondition", true,
                "Place keyword before condition", "Whether to place the AND / OR keyword before the condition (otherwise at the end of the previous line).", "AND / OR"),
            Bool("operators.between.placeOnNewLine", true,
                "Place BETWEEN on new line", "Whether to place BETWEEN operator on a new line.", "BETWEEN"),
            Bool("operators.between.placeAndKeywordOnNewLine", false,
                "Place AND on new line", "Whether to place AND keyword on a new line.", "BETWEEN"),
            Choice("operators.between.andAlignment", "toBetween",
                [new("toBetween", "To BETWEEN"), new("rightAlignedToBetween", "Right-aligned to BETWEEN"), new("toBeginningOfExpression", "To beginning of expression")],
                "AND alignment", "How to align the AND keyword if placing AND keyword on a new line is enabled.", "BETWEEN",
                ("operators.between.placeAndKeywordOnNewLine", "true")),
            Bool("operators.in.placeOpeningParenthesisOnNewLine", false,
                "Place opening parenthesis on new line", "Whether to place opening parenthesis on new line.", "IN"),
            Choice("operators.in.alignment", "leftAligned", [new("leftAligned", "Left-aligned"), new("rightAligned", "Right-aligned"), new("indented", "Indented")],
                "IN alignment", "How to align the IN list.", "IN"),
            Choice("operators.in.placeFirstValueOnNewLine", "ifLongerThanMaxLineLength",
                [new("always", "Always"), new("never", "Never"), new("ifLongerThanMaxLineLength", "If longer than the wrap width"), new("ifSubsequentValues", "If there are subsequent values")],
                "Place first value on new line", "When to place the first value on a new line.", "IN"),
            Choice("operators.in.placeSubsequentValuesOnNewLines", "ifLongerThanMaxLineLength", AlwaysNeverIfLonger,
                "Place subsequent values on new lines", "When to place subsequent values on new lines.", "IN"),
            Bool("operators.in.addSpaceAroundInContents", false,
                "Add space around IN contents", "Whether to add a space around IN contents: IN ( 1, 2 ).", "IN"),
        ]),
    ];
}
