#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 033 (T022/T026) — pure schema-JSON → tree-model parsing for the Format Styles
    /// editor. Extracted from the window because <c>DialogWindow</c> cannot be constructed
    /// outside a VS/SSMS process (DpiHelper needs IVsSettingsManager), so anything worth
    /// testing must not live in WPF construction code.
    ///
    /// <para>
    /// v2 schemas carry <c>parentId</c> on group rows → the 5-category hierarchy; v1 schemas
    /// (older engine, mixed-version window) have none → flat groups, and all v2 setting fields
    /// (<c>description</c>/<c>allowedEnumValues</c>/<c>min</c>/<c>max</c>) parse as absent.
    /// </para>
    /// </summary>
    internal static class FormatStylesSchemaModel
    {
        internal sealed class Group
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            /// <summary>Normalized category id, or null on a v1 (flat) schema.</summary>
            public string? CategoryId { get; set; }
            /// <summary>SQL Prompt model: the page's preview SQL (what its options act on).</summary>
            public string? Sample { get; set; }
            public List<FormatSettingNode> Settings { get; } = new List<FormatSettingNode>();
        }

        internal sealed class Category
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<Group> Groups { get; } = new List<Group>();
        }

        /// <summary>SQL Prompt model: a query the editor formats to show what an option does.</summary>
        internal sealed class ExampleQuery
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Sql { get; set; } = string.Empty;
        }

        /// <summary>
        /// SQL Prompt model: where an option shows — the query (<see cref="PageSampleQueryId"/> for
        /// its page's sample) and the other settings it needs, by setting id.
        /// </summary>
        internal sealed class ExampleTarget
        {
            public string QueryId { get; set; } = string.Empty;
            public List<KeyValuePair<string, object?>> With { get; } = new List<KeyValuePair<string, object?>>();
        }

        /// <summary>SQL Prompt model: an option's "what does each value do" example.</summary>
        internal sealed class OptionExample
        {
            public ExampleTarget Target { get; set; } = new ExampleTarget();
            /// <summary>The values to show a card for, the option's default first.</summary>
            public List<object?> Values { get; } = new List<object?>();
            /// <summary>Values that only show on another query, keyed by <see cref="ValueKey"/>.</summary>
            public Dictionary<string, ExampleTarget> ByValue { get; } = new Dictionary<string, ExampleTarget>(StringComparer.Ordinal);

            /// <summary>Where <paramref name="value"/> shows: its own target, or the option's.</summary>
            public ExampleTarget TargetFor(object? value) =>
                ByValue.TryGetValue(ValueKey(value), out var own) ? own : Target;
        }

        /// <summary>The query id that stands for "this option's page sample".</summary>
        internal const string PageSampleQueryId = "page";

        /// <summary>A value as the schema's <c>byValue</c> keys spell it: <c>true</c>, <c>60</c>, <c>always</c>.</summary>
        internal static string ValueKey(object? value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        internal sealed class Model
        {
            /// <summary>True when the schema carried category information (v2).</summary>
            public bool Categorized { get; set; }
            /// <summary>True for SQL Prompt's option model (<c>"model":"sqlPrompt"</c>): ids are <c>sqlPrompt.&lt;path&gt;</c>.</summary>
            public bool IsSqlPrompt { get; set; }
            /// <summary>Populated when <see cref="Categorized"/>; canonical order, used ones only.</summary>
            public List<Category> Categories { get; } = new List<Category>();
            /// <summary>Always populated (schema order) — the v1 flat rendering source.</summary>
            public List<Group> FlatGroups { get; } = new List<Group>();
            /// <summary>SQL Prompt model (schema 2002+): the queries option examples use, by id.</summary>
            public Dictionary<string, ExampleQuery> ExampleQueries { get; } = new Dictionary<string, ExampleQuery>(StringComparer.Ordinal);
            /// <summary>SQL Prompt model (schema 2002+): the SELECT statements the preview offers, in order.</summary>
            public List<ExampleQuery> SelectExamples { get; } = new List<ExampleQuery>();

            /// <summary>The page <paramref name="settingId"/> is on, or null.</summary>
            public Group? GroupOf(string settingId)
            {
                foreach (var g in FlatGroups)
                    foreach (var s in g.Settings)
                        if (string.Equals(s.Id, settingId, StringComparison.Ordinal)) return g;
                return null;
            }

            /// <summary>The SQL behind an example query id: a page's sample for <see cref="PageSampleQueryId"/>.</summary>
            public string? SqlFor(string queryId, Group? page) =>
                queryId == PageSampleQueryId ? page?.Sample
                : ExampleQueries.TryGetValue(queryId, out var q) ? q.Sql : null;
        }

        /// <summary>Category id → display name. Ids travel only as ParentId values on group rows.</summary>
        internal static readonly Dictionary<string, string> CategoryDisplayNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["global"] = "Global",
                ["statements"] = "Statements",
                ["clauses"] = "Clauses",
                ["expressions"] = "Expressions",
                ["other"] = "Other",
            };

        internal static readonly string[] CategoryOrder = ["global", "statements", "clauses", "expressions", "other"];

        /// <summary>Which control the editor renders for a setting — single source of truth.</summary>
        internal enum ControlKind
        {
            CheckBox,
            IntBox,
            EnumComboBox,
            EnumTextBox,
            ReadOnly,
        }

        internal static ControlKind ControlKindFor(FormatSettingNode setting) => setting.Type switch
        {
            "Bool" => ControlKind.CheckBox,
            "Int" => ControlKind.IntBox,
            "Enum" => setting.AllowedEnumValues is { Count: > 0 }
                ? ControlKind.EnumComboBox
                : ControlKind.EnumTextBox, // v1 degrade: free-text
            _ => ControlKind.ReadOnly,
        };

        /// <summary>Parses the engine's schema JSON defensively (every v2 field optional). Throws on malformed JSON.</summary>
        internal static Model Parse(string schemaJson)
        {
            var model = new Model();

            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;
            model.IsSqlPrompt = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                                && string.Equals(modelEl.GetString(), "sqlPrompt", StringComparison.Ordinal);
            if (!root.TryGetProperty("groups", out var groupsEl) || !root.TryGetProperty("settings", out var settingsEl))
                return model;

            var settingsByGroup = new Dictionary<string, List<FormatSettingNode>>(StringComparer.Ordinal);
            foreach (var s in settingsEl.EnumerateArray())
            {
                var groupId = s.TryGetProperty("groupId", out var g) ? g.GetString() ?? string.Empty : string.Empty;
                if (!settingsByGroup.TryGetValue(groupId, out var list))
                {
                    list = new List<FormatSettingNode>();
                    settingsByGroup[groupId] = list;
                }

                List<string>? allowedValues = null;
                if (s.TryGetProperty("allowedEnumValues", out var avEl) && avEl.ValueKind == JsonValueKind.Array)
                {
                    allowedValues = new List<string>();
                    foreach (var v in avEl.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.String) allowedValues.Add(v.GetString()!);
                    }
                    if (allowedValues.Count == 0) allowedValues = null;
                }

                list.Add(new FormatSettingNode
                {
                    Id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                    DisplayName = s.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                    Type = s.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Other" : "Other",
                    Status = s.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "Implemented" : "Implemented",
                    SqlPromptKey = s.TryGetProperty("sqlPromptKey", out var spEl) && spEl.ValueKind != JsonValueKind.Null ? spEl.GetString() : null,
                    DefaultJson = s.TryGetProperty("default", out var defEl) ? defEl.GetRawText() : "null",
                    Description = s.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null,
                    AllowedEnumValues = allowedValues,
                    Min = s.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.Number ? minEl.GetInt32() : null,
                    Max = s.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number ? maxEl.GetInt32() : null,
                    EnumLabels = ReadStrings(s, "enumLabels"),
                    Note = s.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String ? noteEl.GetString() : null,
                    Subgroup = s.TryGetProperty("subgroup", out var subEl) && subEl.ValueKind == JsonValueKind.String ? subEl.GetString() : null,
                    EnabledWhenId = s.TryGetProperty("enabledWhen", out var gateEl) && gateEl.ValueKind == JsonValueKind.Object
                                    && gateEl.TryGetProperty("id", out var gateIdEl) ? gateIdEl.GetString() : null,
                    EnabledWhenValue = s.TryGetProperty("enabledWhen", out var gateEl2) && gateEl2.ValueKind == JsonValueKind.Object
                                       && gateEl2.TryGetProperty("value", out var gateValueEl) ? ProfileJsonMerger.ReadScalar(gateValueEl) : null,
                    Example = s.TryGetProperty("example", out var exampleEl) ? ReadExample(exampleEl) : null,
                });
            }

            if (root.TryGetProperty("exampleQueries", out var queriesEl) && queriesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in queriesEl.EnumerateArray())
                {
                    var query = new ExampleQuery
                    {
                        Id = q.TryGetProperty("id", out var qid) && qid.ValueKind == JsonValueKind.String ? qid.GetString()! : string.Empty,
                        Name = q.TryGetProperty("title", out var qt) && qt.ValueKind == JsonValueKind.String ? qt.GetString()! : string.Empty,
                        Sql = q.TryGetProperty("sql", out var qs) && qs.ValueKind == JsonValueKind.String ? qs.GetString()! : string.Empty,
                    };
                    if (query.Id.Length > 0 && query.Sql.Length > 0) model.ExampleQueries[query.Id] = query;
                }
            }
            if (root.TryGetProperty("selectExamples", out var selectEl) && selectEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in selectEl.EnumerateArray())
                    if (id.ValueKind == JsonValueKind.String && model.ExampleQueries.TryGetValue(id.GetString()!, out var q))
                        model.SelectExamples.Add(q);
            }

            var byCategory = new Dictionary<string, Category>(StringComparer.Ordinal);
            foreach (var g in groupsEl.EnumerateArray())
            {
                var group = new Group
                {
                    Id = g.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                    DisplayName = g.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                    Sample = g.TryGetProperty("sample", out var sampleEl) && sampleEl.ValueKind == JsonValueKind.String ? sampleEl.GetString() : null,
                };
                if (string.IsNullOrEmpty(group.DisplayName)) group.DisplayName = group.Id;

                var parentId = g.TryGetProperty("parentId", out var pEl) && pEl.ValueKind == JsonValueKind.String ? pEl.GetString() : null;
                if (parentId != null)
                {
                    model.Categorized = true;
                    // Unknown category ids from a NEWER engine land under Other rather than vanishing.
                    group.CategoryId = CategoryDisplayNames.ContainsKey(parentId) ? parentId : "other";
                }

                if (settingsByGroup.TryGetValue(group.Id, out var groupSettings))
                    group.Settings.AddRange(groupSettings);

                model.FlatGroups.Add(group);

                if (group.CategoryId != null)
                {
                    if (!byCategory.TryGetValue(group.CategoryId, out var category))
                    {
                        category = new Category { Id = group.CategoryId, DisplayName = CategoryDisplayNames[group.CategoryId] };
                        byCategory[group.CategoryId] = category;
                    }
                    category.Groups.Add(group);
                }
            }

            if (model.Categorized)
            {
                foreach (var id in CategoryOrder)
                {
                    if (byCategory.TryGetValue(id, out var category) && category.Groups.Count > 0)
                        model.Categories.Add(category);
                }
            }

            return model;
        }

        /// <summary>A setting's <c>example</c>, or null when it has none or it is malformed.</summary>
        private static OptionExample? ReadExample(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            var target = ReadTarget(el);
            if (target == null) return null;
            var example = new OptionExample { Target = target };
            if (el.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                foreach (var v in values.EnumerateArray()) example.Values.Add(ProfileJsonMerger.ReadScalar(v));
            if (example.Values.Count < 2) return null;
            if (el.TryGetProperty("byValue", out var byValue) && byValue.ValueKind == JsonValueKind.Object)
                foreach (var entry in byValue.EnumerateObject())
                    if (ReadTarget(entry.Value) is { } own) example.ByValue[entry.Name] = own;
            return example;
        }

        private static ExampleTarget? ReadTarget(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String) return null;
            var target = new ExampleTarget { QueryId = q.GetString()! };
            if (el.TryGetProperty("with", out var with) && with.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in with.EnumerateArray())
                {
                    if (w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                    target.With.Add(new KeyValuePair<string, object?>(id.GetString()!, w.TryGetProperty("value", out var value) ? ProfileJsonMerger.ReadScalar(value) : null));
                }
            }
            return target;
        }

        private static List<string>? ReadStrings(JsonElement owner, string property)
        {
            if (!owner.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var v in arr.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String) list.Add(v.GetString()!);
            return list.Count == 0 ? null : list;
        }
    }
}
