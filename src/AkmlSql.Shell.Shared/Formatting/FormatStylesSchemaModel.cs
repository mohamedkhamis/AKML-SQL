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
            public List<FormatSettingNode> Settings { get; } = new List<FormatSettingNode>();
        }

        internal sealed class Category
        {
            public string Id { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<Group> Groups { get; } = new List<Group>();
        }

        internal sealed class Model
        {
            /// <summary>True when the schema carried category information (v2).</summary>
            public bool Categorized { get; set; }
            /// <summary>Populated when <see cref="Categorized"/>; canonical order, used ones only.</summary>
            public List<Category> Categories { get; } = new List<Category>();
            /// <summary>Always populated (schema order) — the v1 flat rendering source.</summary>
            public List<Group> FlatGroups { get; } = new List<Group>();
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
                });
            }

            var byCategory = new Dictionary<string, Category>(StringComparer.Ordinal);
            foreach (var g in groupsEl.EnumerateArray())
            {
                var group = new Group
                {
                    Id = g.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                    DisplayName = g.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
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
    }
}
