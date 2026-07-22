#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 033 (T013) — merges the Format Styles editor's flat working values into a profile's
    /// RAW stored JSON (the ProfileGet merge base). Pure function: no IPC, no file I/O.
    ///
    /// <para>
    /// Why merge instead of synthesizing from working values: the stored JSON carries
    /// <c>metadata</c>, root extension data, and keys the editor never touched — synthesizing
    /// (the preview path's <c>BuildProfileJson</c>) would clobber all of it on save.
    /// </para>
    ///
    /// <para>
    /// Write policy keeps stored files minimal: a working value is written only when it differs
    /// from the base's effective value — the value at that path when present, else the schema
    /// default. Keys are full dotted paths; every segment nests (multi-segment ids like
    /// <c>insertStatements.columns.parenthesisStyle</c> produce nested objects).
    /// </para>
    /// </summary>
    internal static class ProfileJsonMerger
    {
        private static readonly JsonSerializerOptions IndentedJson = new JsonSerializerOptions { WriteIndented = true };

        /// <param name="baseJson">The profile's raw stored JSON (ProfileGet verbatim text).</param>
        /// <param name="workingValues">Editor values keyed by dotted setting id.</param>
        /// <param name="schemaDefaults">Schema default per setting id (the effective value for paths absent from the base).</param>
        /// <returns>The merged profile JSON (indented), metadata and untouched keys intact.</returns>
        internal static string Merge(
            string baseJson,
            IReadOnlyDictionary<string, object?> workingValues,
            IReadOnlyDictionary<string, object?> schemaDefaults)
        {
            if (baseJson == null) throw new ArgumentNullException(nameof(baseJson));
            if (workingValues == null) throw new ArgumentNullException(nameof(workingValues));
            if (schemaDefaults == null) throw new ArgumentNullException(nameof(schemaDefaults));

            if (JsonNode.Parse(baseJson) is not JsonObject root)
                throw new JsonException("Profile JSON root is not an object.");

            foreach (var kvp in workingValues)
            {
                var path = kvp.Key;
                if (string.IsNullOrEmpty(path)) continue;

                var segments = path.Split('.');
                if (segments.Length < 2) continue;                       // never touch root scalars
                if (string.Equals(segments[0], "metadata", StringComparison.OrdinalIgnoreCase))
                    continue;                                            // metadata is never editor-owned

                var existing = TryGetValueAt(root, segments, out var baseValue);
                if (existing)
                {
                    if (ValuesEqual(baseValue, kvp.Value)) continue;     // unchanged — leave base text alone
                }
                else
                {
                    // Absent from the base: stays implicit unless the edit departs from the default.
                    if (schemaDefaults.TryGetValue(path, out var def) && ValuesEqual(def, kvp.Value))
                        continue;
                }

                SetValueAt(root, segments, ToJsonValue(kvp.Value));
            }

            return root.ToJsonString(IndentedJson);
        }

        /// <summary>
        /// JsonElement → CLR scalar, the ONE coercion used by seeding, overlay, and merge
        /// comparison (int-vs-double policy must never diverge between them — it is the oracle
        /// for dirty tracking and default elision). Null for non-primitive kinds.
        /// </summary>
        internal static object? ReadScalar(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? (object)i : element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            _ => null,
        };

        private static bool TryGetValueAt(JsonObject root, string[] segments, out object? value)
        {
            value = null;
            JsonNode? current = root;
            foreach (var segment in segments)
            {
                if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                    return false;
            }

            if (current is not JsonValue jv) return false;
            var element = jv.GetValue<JsonElement>();
            value = ReadScalar(element);
            return element.ValueKind is JsonValueKind.True or JsonValueKind.False
                or JsonValueKind.Number or JsonValueKind.String;
        }

        /// <summary>Writes a value at a dotted path, creating intermediate objects. Shared with
        /// the preview path's <c>BuildProfileJson</c> so save and preview nest identically.</summary>
        internal static void SetValueAt(JsonObject root, string[] segments, JsonNode? value)
        {
            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (current[segments[i]] is JsonObject next)
                {
                    current = next;
                }
                else
                {
                    var created = new JsonObject();
                    current[segments[i]] = created;
                    current = created;
                }
            }
            current[segments[segments.Length - 1]] = value;
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a is bool ab && b is bool bb) return ab == bb;
            if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);

            if (IsNumeric(a) && IsNumeric(b))
                return Math.Abs(Convert.ToDouble(a) - Convert.ToDouble(b)) < double.Epsilon;

            return false; // type mismatch — treat as changed
        }

        private static bool IsNumeric(object o) => o is int || o is long || o is double || o is float || o is short;

        /// <summary>CLR scalar → JsonNode, shared with the preview path (see <see cref="SetValueAt"/>).</summary>
        internal static JsonNode? ToJsonValue(object? value) => value switch
        {
            null => null,
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(value.ToString()),
        };
    }
}
