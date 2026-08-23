using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Canonical descriptor of every setting in <see cref="FormattingProfile"/>, used by the
/// Format Styles editor UI (spec 020 US3) to build its tree, render type-appropriate controls,
/// and surface SQL Prompt mapping information.
///
/// <para>
/// Built by reflection over <see cref="FormattingProfile"/>'s 12 sub-category POCOs. Each
/// public class-typed property on <see cref="FormattingProfile"/> becomes a
/// <see cref="FormatSettingGroup"/>; each public scalar property on those sub-classes becomes
/// a <see cref="FormatSetting"/>. This means the schema stays in sync with the profile
/// shape automatically as the profile evolves.
/// </para>
///
/// <para>
/// SQL Prompt mapping (the <see cref="FormatSetting.SqlPromptKey"/> field) is resolved by
/// <see cref="LookupSqlPromptKey"/>: an explicit per-setting map (<see cref="ExplicitKeyMap"/>)
/// is consulted first, then — as a best-effort, never-guaranteed fallback — a heuristic over
/// <see cref="SqlPromptImporter"/>'s <c>OptionMap</c> keys. Settings without a SQL Prompt
/// equivalent have a null <c>SqlPromptKey</c>.
/// </para>
/// </summary>
public class FormatSettingSchema
{
    /// <summary>Monotonically-increasing schema revision. Clients cache by this number.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>Group definitions in display order.</summary>
    [JsonPropertyName("groups")]
    public List<FormatSettingGroup> Groups { get; set; } = [];

    /// <summary>Flat list of every setting across every group.</summary>
    [JsonPropertyName("settings")]
    public List<FormatSetting> Settings { get; set; } = [];

    // -------------------------------------------------------------------

    private static readonly Lazy<FormatSettingSchema> _default = new(BuildDefault);

    /// <summary>The default (process-wide) schema. Built once on first access.</summary>
    public static FormatSettingSchema Default => _default.Value;

    /// <summary>
    /// Discovers the schema from <see cref="FormattingProfile"/>'s public structure via reflection.
    /// </summary>
    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "Reflection over POCO type properties; clarity over micro-optimization.")]
    public static FormatSettingSchema BuildDefault()
    {
        // Spec 033 (T021) — v2: category hierarchy (ParentId), SettingMeta-sourced
        // descriptions/enum values/ranges, and flattened nested option objects. Bumping this
        // literal invalidates the engine's Cached=true short-circuit and the shell's static
        // schema cache automatically.
        var schema = new FormatSettingSchema { SchemaVersion = 2 };

        var profileType = typeof(FormattingProfile);
        var defaultInstance = new FormattingProfile();
        var order = 0;

        foreach (var categoryProp in profileType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip extension data + non-class properties
            if (categoryProp.Name == nameof(FormattingProfile.ExtensionData)) continue;
            if (categoryProp.Name == nameof(FormattingProfile.Metadata)) continue;
            if (!categoryProp.PropertyType.IsClass) continue;
            if (categoryProp.PropertyType == typeof(string)) continue;

            var categoryAttr = categoryProp.GetCustomAttribute<JsonPropertyNameAttribute>();
            var groupId = categoryAttr?.Name ?? LowercaseFirst(categoryProp.Name);
            var groupDisplay = PrettifyName(categoryProp.Name);

            schema.Groups.Add(new FormatSettingGroup
            {
                Id = groupId,
                DisplayName = groupDisplay,
                // v2 — five-category hierarchy; unmapped future groups land under "other"
                // (the schema completeness test flags them so the map stays exhaustive).
                ParentId = CategoryMap.TryGetValue(groupId, out var category) ? category : CategoryOther,
                Order = order++,
            });

            // Resolve the category instance from the default profile (so we can read default values)
            var categoryInstance = categoryProp.GetValue(defaultInstance);
            if (categoryInstance == null) continue;

            AddSettingsFrom(schema, groupId, groupId, categoryInstance, depth: 0);
        }

        return schema;
    }

    /// <summary>
    /// v2 — emits one <see cref="FormatSetting"/> per scalar property under
    /// <paramref name="idPrefix"/>. Class-typed sub-objects (InsertStatementsOptions'
    /// Columns/Values) recurse into multi-segment ids (<c>insertStatements.columns.*</c>)
    /// instead of surfacing as opaque "Other" blobs.
    /// </summary>
    private static void AddSettingsFrom(FormatSettingSchema schema, string groupId, string idPrefix, object instance, int depth)
    {
        if (depth > 2) return; // structural safety net — the profile nests at most one extra level

        foreach (var settingProp in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!settingProp.CanRead || !settingProp.CanWrite) continue;

            var settingAttr = settingProp.GetCustomAttribute<JsonPropertyNameAttribute>();
            var settingShortName = settingAttr?.Name ?? LowercaseFirst(settingProp.Name);
            var settingId = $"{idPrefix}.{settingShortName}";

            if (settingProp.PropertyType.IsClass && settingProp.PropertyType != typeof(string))
            {
                var nested = TryGetDefault(instance, settingProp);
                if (nested != null) AddSettingsFrom(schema, groupId, settingId, nested, depth + 1);
                continue;
            }

            var type = ClassifyType(settingProp.PropertyType);
            var defaultValue = TryGetDefault(instance, settingProp);
            var sqlPromptKey = LookupSqlPromptKey(settingId, settingProp.Name);
            var meta = settingProp.GetCustomAttribute<SettingMetaAttribute>();

            schema.Settings.Add(new FormatSetting
            {
                Id = settingId,
                GroupId = groupId,
                DisplayName = PrettifyName(settingProp.Name),
                Type = type,
                Default = defaultValue,
                AllowedEnumValues = meta?.AllowedValues is { Length: > 0 } allowed ? [.. allowed] : null,
                Min = meta != null && meta.Min != SettingMetaAttribute.Unset ? meta.Min : null,
                Max = meta != null && meta.Max != SettingMetaAttribute.Unset ? meta.Max : null,
                Description = string.IsNullOrWhiteSpace(meta?.Description) ? null : meta!.Description,
                SqlPromptKey = sqlPromptKey,
                Status = sqlPromptKey != null ? "Implemented" : "AkmlOnly",
            });
        }
    }

    // -------------------------------------------------------------------
    // v2 category hierarchy (spec 033) — mirrors SQL Prompt's Edit Formatting Styles tree.
    // Category ids travel ONLY as ParentId values on group rows (never as group rows
    // themselves — a v1 shell rendering rows flatly must not see empty category nodes);
    // the shell maps id → display name.
    // -------------------------------------------------------------------

    internal const string CategoryGlobal = "global";
    internal const string CategoryStatements = "statements";
    internal const string CategoryClauses = "clauses";
    internal const string CategoryExpressions = "expressions";
    internal const string CategoryOther = "other";

    /// <summary>Group id → category id. Every current group MUST appear here (test-enforced).</summary>
    internal static readonly Dictionary<string, string> CategoryMap = new(StringComparer.Ordinal)
    {
        ["whitespace"] = CategoryGlobal,
        ["list"] = CategoryGlobal,
        ["parenthesis"] = CategoryGlobal,
        ["casing"] = CategoryGlobal,
        ["dml"] = CategoryStatements,
        ["ddl"] = CategoryStatements,
        ["cte"] = CategoryStatements,
        ["controlFlow"] = CategoryStatements,
        ["declare"] = CategoryStatements,
        ["join"] = CategoryClauses,
        ["insertStatements"] = CategoryClauses,
        ["case"] = CategoryExpressions,
        ["operators"] = CategoryExpressions,
        ["inStatements"] = CategoryExpressions,
        ["functionCalls"] = CategoryExpressions,
        ["expression"] = CategoryExpressions,
        ["comments"] = CategoryOther,
        ["formatActions"] = CategoryOther,
    };

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Explicit settingId -> SQL Prompt importer key, for settings whose importer key
    /// deliberately differs from the AKML property name (notably the "collapseThreshold"
    /// name shared across six categories, which a property-name heuristic cannot resolve).
    /// Checked first so the Format Styles editor reports an accurate Implemented status.
    /// Spec 020 — PR #239 review follow-up; the full authoritative table is the deferred
    /// SqlPromptKeyMap (T085).
    /// </summary>
    private static readonly Dictionary<string, string> ExplicitKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parenthesis.collapseShort"]       = "CollapseShortParenthesisContents",
        ["parenthesis.collapseThreshold"]   = "CollapseParenthesesShorterThan",
        ["dml.collapseShortStatements"]     = "DmlCollapseShortStatements",
        ["dml.collapseThreshold"]           = "DmlCollapseStatementsShorterThan",
        ["dml.collapseShortSubqueries"]     = "DmlCollapseShortSubqueries",
        ["dml.subqueryCollapseThreshold"]   = "DmlCollapseSubqueriesShorterThan",
        ["ddl.firstParameterOnNewLine"]     = "PlaceFirstProcedureParameterOnNewLine",
        ["ddl.collapseShortDDL"]            = "DdlCollapseShortStatements",
        ["ddl.collapseThreshold"]           = "DdlCollapseStatementsShorterThan",
        ["controlFlow.collapseShortIfElse"] = "ControlFlowCollapseShortIfElse",
        ["controlFlow.collapseThreshold"]   = "ControlFlowCollapseStatementsShorterThan",
        // T080 — CTE additions
        ["cte.placeColumnsOnNewLine"]       = "PlaceCteColumnsOnNewLine",
        // T082 — CASE additions
        ["case.firstWhenOnNewLine"]         = "PlaceFirstWhenOnNewLine",
        ["case.whenAlignment"]              = "WhenAlignment",
        ["case.expressionOnNewLine"]        = "PlaceCaseExpressionOnNewLine",
        // T083 — Operators
        ["operators.alignment"]             = "OperatorsAlignment",
        ["operators.betweenOnNewLine"]      = "PlaceBetweenKeywordOnNewLine",
        // T084 — IN Statements
        ["inStatements.alignment"]          = "InStatementsAlignment",
        // Phase B closure — full SQL Prompt feature parity
        ["whitespace.blankLinesBeforeGoCount"] = "BlankLinesBeforeGo",
        ["whitespace.tabStyle"]             = "TabBehavior",
        ["list.placeSubsequentItemsOnNewLines"] = "PlaceSubsequentItemsOnNewLines",
        ["dml.rightAlignClauses"]           = "RightAlignClauses",
        ["dml.clauseIndentation"]           = "ClauseIndentation",
        ["dml.insertColumnListFormat"]      = "InsertColumnListFormat",
        ["dml.valuesFormat"]                = "ValuesFormat",
        ["ddl.constraintColumnsOnNewLine"]  = "ConstraintColumnsOnNewLine",
        ["join.onConditionIndentMode"]      = "OnConditionIndentMode",
        ["case.endAlignment"]               = "CaseEndAlignment",
        ["cte.asOnNewLine"]                 = "CtePlaceAsOnNewLine",
        ["operators.andBetweenOnNewLine"]   = "PlaceAndBetweenBetweenOnNewLine",
        ["inStatements.placeItemsOnNewLine"] = "InStatementsPlaceItemsOnNewLine",
        ["functionCalls.placeParametersOnNewLine"] = "FunctionCallsPlaceParametersOnNewLine",
        ["functionCalls.indentParameters"]  = "IndentFunctionParameters",
        ["comments.multilineFormatting"]    = "MultilineCommentFormatting",
        ["comments.recognizeCommonPatterns"] = "RecognizeCommonCommentPatterns",
        // Spec 033 (T021) — flattened insertStatements sub-objects (Redgate modern-JSON
        // insertStatements.columns/values section; imported by the spec-031 JSON importer).
        ["insertStatements.columns.parenthesisStyle"] = "InsertColumnsParenthesisStyle",
        ["insertStatements.columns.indentContents"] = "InsertColumnsIndentContents",
        ["insertStatements.columns.placeSubsequentItemsOnNewLines"] = "InsertColumnsPlaceSubsequentItemsOnNewLines",
        ["insertStatements.values.parenthesisStyle"] = "InsertValuesParenthesisStyle",
        ["insertStatements.values.indentContents"] = "InsertValuesIndentContents",
        ["insertStatements.values.placeSubsequentItemsOnNewLines"] = "InsertValuesPlaceSubsequentItemsOnNewLines",
    };

    /// <summary>
    /// Returns the SQL Prompt option name (from <see cref="SqlPromptImporter"/>) that maps to
    /// the given AKML setting, or null if none is known. Resolves via the explicit
    /// <see cref="ExplicitKeyMap"/> first, then a best-effort heuristic on the property name.
    /// </summary>
    private static string? LookupSqlPromptKey(string settingId, string akmlPropertyName)
    {
        // 0. Explicit per-setting map — importer keys that differ from the property name.
        if (ExplicitKeyMap.TryGetValue(settingId, out var explicitKey))
            return explicitKey;

        // The importer's map keys are stylised SQL Prompt option names (e.g. "KeywordCasing");
        // AKML property names are PascalCase (e.g. "ReservedKeywords"). Try a few heuristics.
        var keys = SqlPromptImporterReflectionHelper.GetOptionMapKeys();

        // 1. Exact match
        if (keys.Contains(akmlPropertyName, StringComparer.OrdinalIgnoreCase))
            return keys.First(k => k.Equals(akmlPropertyName, StringComparison.OrdinalIgnoreCase));

        // 2. Known sibling pairs (manual table for the documented mappings)
        // Kept small and explicit; new aliases can be added here as the AKML profile grows.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReservedKeywords"] = "KeywordCasing",
            ["BuiltInFunctions"] = "FunctionCasing",
            ["BuiltInDataTypes"] = "DataTypeCasing",
            ["Identifiers"] = "IdentifierCasing",
            ["CommaPosition"] = "CommaPosition",
            ["TabSize"] = "TabSize",
            ["MaxLineWidth"] = "MaxLineWidth",
            ["TabStyle"] = "InsertTabs",
            ["SpaceAfterComma"] = "SpaceAfterComma",
            ["SpaceAroundOperators"] = "SpaceAroundOperators",
            ["SpaceInsideParentheses"] = "SpaceInsideParentheses",
        };
        return aliases.TryGetValue(akmlPropertyName, out var sqlPromptName) ? sqlPromptName : null;
    }

    private static string ClassifyType(Type t)
    {
        if (t == typeof(bool)) return "Bool";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "Int";
        if (t == typeof(string)) return "Enum"; // String fields are typically string enums (e.g. "trailing"/"leading")
        return "Other";
    }

    private static object? TryGetDefault(object instance, PropertyInfo prop)
    {
        try { return prop.GetValue(instance); }
        catch { return null; }
    }

    private static string LowercaseFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>
    /// Converts a PascalCase identifier to a human-friendly display string with spaces.
    /// <c>"LineBreakBeforeClause"</c> → <c>"Line break before clause"</c>.
    /// </summary>
    private static string PrettifyName(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
            {
                sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (i == 0)
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>Display-ordered grouping shown as a parent node in the Format Styles editor tree.</summary>
public class FormatSettingGroup
{
    [JsonPropertyName("id")]              public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]     public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("parentId")]        public string? ParentId { get; set; }
    [JsonPropertyName("order")]           public int Order { get; set; }
}

/// <summary>One configurable option. Type-driven control rendering happens at the editor.</summary>
public class FormatSetting
{
    [JsonPropertyName("id")]                public string Id { get; set; } = string.Empty;
    [JsonPropertyName("groupId")]           public string GroupId { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]       public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("type")]              public string Type { get; set; } = "Bool";  // Bool | Int | Enum | Other
    [JsonPropertyName("default")]           public object? Default { get; set; }
    [JsonPropertyName("allowedEnumValues")] public List<string>? AllowedEnumValues { get; set; }
    [JsonPropertyName("min")]               public int? Min { get; set; }
    [JsonPropertyName("max")]               public int? Max { get; set; }
    [JsonPropertyName("sqlPromptKey")]      public string? SqlPromptKey { get; set; }
    [JsonPropertyName("status")]            public string Status { get; set; } = "Implemented";  // Implemented | AkmlOnly | Unsupported
    [JsonPropertyName("description")]       public string? Description { get; set; }
}

/// <summary>
/// Internal: surfaces the importer's <c>OptionMap</c> keys without exposing the dictionary itself.
/// </summary>
internal static class SqlPromptImporterReflectionHelper
{
    private static readonly Lazy<string[]> _keys = new(() =>
    {
        // Reflect over SqlPromptImporter to grab the OptionMap keys without exposing the field publicly.
        var importerType = typeof(SqlPromptImporter);
        var field = importerType.GetField("OptionMap", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is System.Collections.IDictionary map)
        {
            var keys = new List<string>();
            foreach (var key in map.Keys)
            {
                if (key is string s) keys.Add(s);
            }
            return [.. keys];
        }
        return [];
    });

    public static string[] GetOptionMapKeys() => _keys.Value;
}
