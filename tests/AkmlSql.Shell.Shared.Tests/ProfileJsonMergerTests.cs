#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 (T007) — pure-function coverage for the merge-save. The merger must overlay
    /// only genuinely changed working values onto the raw loaded profile JSON, preserving
    /// metadata, unknown keys, and untouched groups byte-for-byte semantically.
    /// </summary>
    public class ProfileJsonMergerTests
    {
        private const string BaseJson = @"{
  ""metadata"": { ""name"": ""My Style"", ""id"": ""abc-123"", ""isBuiltIn"": false },
  ""casing"": { ""reservedKeywords"": ""UPPERCASE"" },
  ""whitespace"": { ""tabSize"": 4, ""someFutureKey"": ""keepMe"" },
  ""rootUnknown"": { ""nested"": true }
}";

        private static readonly Dictionary<string, object?> NoDefaults = new Dictionary<string, object?>();

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        [Fact]
        public void Changed_value_is_overlaid()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["casing.reservedKeywords"] = "lowercase" },
                NoDefaults);

            var root = Parse(merged);
            Assert.Equal("lowercase", root.GetProperty("casing").GetProperty("reservedKeywords").GetString());
        }

        [Fact]
        public void Unchanged_value_leaves_base_untouched()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?>
                {
                    ["casing.reservedKeywords"] = "UPPERCASE", // equals base
                    ["whitespace.tabSize"] = 4,                // equals base
                },
                NoDefaults);

            var root = Parse(merged);
            Assert.Equal("UPPERCASE", root.GetProperty("casing").GetProperty("reservedKeywords").GetString());
            Assert.Equal(4, root.GetProperty("whitespace").GetProperty("tabSize").GetInt32());
        }

        [Fact]
        public void Metadata_and_unknown_keys_survive_merge()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["whitespace.tabSize"] = 2 },
                NoDefaults);

            var root = Parse(merged);
            Assert.Equal("My Style", root.GetProperty("metadata").GetProperty("name").GetString());
            Assert.Equal("abc-123", root.GetProperty("metadata").GetProperty("id").GetString());
            Assert.Equal("keepMe", root.GetProperty("whitespace").GetProperty("someFutureKey").GetString());
            Assert.True(root.GetProperty("rootUnknown").GetProperty("nested").GetBoolean());
            Assert.Equal(2, root.GetProperty("whitespace").GetProperty("tabSize").GetInt32());
        }

        [Fact]
        public void Absent_path_matching_schema_default_stays_implicit()
        {
            // "list.commaPlacement" isn't in the base; the working value equals the schema
            // default, so the stored file must stay minimal (no new key added).
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["list.commaPlacement"] = "trailing" },
                new Dictionary<string, object?> { ["list.commaPlacement"] = "trailing" });

            var root = Parse(merged);
            Assert.False(root.TryGetProperty("list", out _));
        }

        [Fact]
        public void Absent_path_departing_from_default_is_written()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["list.commaPlacement"] = "leading" },
                new Dictionary<string, object?> { ["list.commaPlacement"] = "trailing" });

            var root = Parse(merged);
            Assert.Equal("leading", root.GetProperty("list").GetProperty("commaPlacement").GetString());
        }

        [Fact]
        public void Multi_segment_path_nests_all_dot_segments()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["insertStatements.columns.parenthesisStyle"] = "expandedSimple" },
                NoDefaults);

            var root = Parse(merged);
            Assert.Equal("expandedSimple",
                root.GetProperty("insertStatements").GetProperty("columns").GetProperty("parenthesisStyle").GetString());
        }

        [Fact]
        public void Metadata_working_key_is_never_applied()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["metadata.name"] = "Hijacked" },
                NoDefaults);

            Assert.Equal("My Style", Parse(merged).GetProperty("metadata").GetProperty("name").GetString());
        }

        [Fact]
        public void Bool_and_int_edits_round_trip_typed()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?>
                {
                    ["whitespace.tabSize"] = 8,
                    ["dml.starOnNewLine"] = true,
                },
                NoDefaults);

            var root = Parse(merged);
            Assert.Equal(8, root.GetProperty("whitespace").GetProperty("tabSize").GetInt32());
            Assert.True(root.GetProperty("dml").GetProperty("starOnNewLine").GetBoolean());
        }

        [Fact]
        public void Output_reparses_as_valid_json()
        {
            var merged = ProfileJsonMerger.Merge(BaseJson,
                new Dictionary<string, object?> { ["casing.reservedKeywords"] = "PascalCase" },
                NoDefaults);

            var doc = JsonDocument.Parse(merged); // throws if invalid
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }
}
