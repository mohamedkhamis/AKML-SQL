using System.Text.Json;
using Xunit;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Tests
{
    /// <summary>
    /// PR #247 fix: CodeAnalysisSettings.RuleOverrides must use OrdinalIgnoreCase so that
    /// hand-edited config.json entries with lowercase rule ids (e.g. "pe001") resolve the same
    /// as the engine's canonical uppercase ids ("PE001").
    /// </summary>
    public class Pr247_AppSettingsFix
    {
        // ── New-instance path ─────────────────────────────────────────────────

        [Fact]
        public void RuleOverrides_NewInstance_IsCaseInsensitive()
        {
            var s = new CodeAnalysisSettings();
            s.RuleOverrides["PE001"] = new RuleOverride { Enabled = false };

            // Lookup with lowercase must resolve — proves OrdinalIgnoreCase comparer.
            Assert.True(s.RuleOverrides.ContainsKey("pe001"),
                "RuleOverrides must be case-insensitive: 'pe001' should match 'PE001'.");
        }

        [Fact]
        public void RuleOverrides_LowercaseSet_ResolvesByUppercase()
        {
            var s = new CodeAnalysisSettings();
            s.RuleOverrides["pe001"] = new RuleOverride { Enabled = false, Severity = "warning" };

            Assert.True(s.RuleOverrides.TryGetValue("PE001", out var ov),
                "TryGetValue with 'PE001' must find a key that was inserted as 'pe001'.");
            Assert.NotNull(ov);
            Assert.False(ov!.Enabled);
            Assert.Equal("warning", ov.Severity);
        }

        // ── JSON deserialization path ──────────────────────────────────────────
        // System.Text.Json creates a brand-new Dictionary<string,…> with ordinal comparer
        // when deserialising into a property; the property setter must re-wrap it with
        // OrdinalIgnoreCase so the comparer survives the round-trip.

        [Fact]
        public void RuleOverrides_DeserializedLowercaseKey_ResolvesByUppercase()
        {
            const string json = """
                {
                  "codeAnalysis": {
                    "ruleOverrides": {
                      "pe001": { "enabled": false, "severity": "warning" }
                    }
                  }
                }
                """;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

            Assert.NotNull(settings);
            var overrides = settings!.CodeAnalysis.RuleOverrides;

            Assert.True(overrides.ContainsKey("PE001"),
                "After deserialising a lowercase key 'pe001', 'PE001' lookup must succeed.");
            Assert.True(overrides.TryGetValue("PE001", out var ov));
            Assert.NotNull(ov);
            Assert.False(ov!.Enabled);
        }

        [Fact]
        public void RuleOverrides_DeserializedUppercaseKey_ResolvesByLowercase()
        {
            const string json = """
                {
                  "codeAnalysis": {
                    "ruleOverrides": {
                      "PE001": { "enabled": false }
                    }
                  }
                }
                """;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

            Assert.NotNull(settings);
            Assert.True(settings!.CodeAnalysis.RuleOverrides.ContainsKey("pe001"),
                "After deserialising 'PE001', a lookup with 'pe001' must also succeed.");
        }

        [Fact]
        public void RuleOverrides_SetterWithNullValue_ReturnsCaseInsensitiveEmptyDict()
        {
            var s = new CodeAnalysisSettings();
            // Simulate JsonSerializer calling the setter with null (shouldn't normally happen,
            // but defensive coding in the setter must not throw or lose the comparer).
            s.RuleOverrides = null!;

            Assert.NotNull(s.RuleOverrides);
            Assert.Empty(s.RuleOverrides);
            // Verify it is still case-insensitive after the null assignment.
            s.RuleOverrides["pe001"] = new RuleOverride { Enabled = true };
            Assert.True(s.RuleOverrides.ContainsKey("PE001"));
        }
    }
}
