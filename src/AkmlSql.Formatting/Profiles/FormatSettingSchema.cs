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
/// looking up whether <see cref="SqlPromptImporter"/>'s <c>OptionMap</c> mentions the AKML
/// property's name — best-effort, never guaranteed. Settings without a SQL Prompt equivalent
/// have a null <c>SqlPromptKey</c>.
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
        var schema = new FormatSettingSchema { SchemaVersion = 1 };

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
                Order = order++,
            });

            // Resolve the category instance from the default profile (so we can read default values)
            var categoryInstance = categoryProp.GetValue(defaultInstance);
            if (categoryInstance == null) continue;

            foreach (var settingProp in categoryProp.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!settingProp.CanRead || !settingProp.CanWrite) continue;

                var settingAttr = settingProp.GetCustomAttribute<JsonPropertyNameAttribute>();
                var settingShortName = settingAttr?.Name ?? LowercaseFirst(settingProp.Name);
                var settingId = $"{groupId}.{settingShortName}";

                var type = ClassifyType(settingProp.PropertyType);
                var defaultValue = TryGetDefault(categoryInstance, settingProp);
                var sqlPromptKey = LookupSqlPromptKey(settingId, settingProp.Name);

                schema.Settings.Add(new FormatSetting
                {
                    Id = settingId,
                    GroupId = groupId,
                    DisplayName = PrettifyName(settingProp.Name),
                    Type = type,
                    Default = defaultValue,
                    SqlPromptKey = sqlPromptKey,
                    Status = sqlPromptKey != null ? "Implemented" : "AkmlOnly",
                });
            }
        }

        return schema;
    }

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
