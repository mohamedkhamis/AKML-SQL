using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 037 Phase 2 (T013) — V14 migration from the pre-agents flat-field shape, pinned
    /// against the T003 fixture (<c>Config/Fixtures/legacy-single-provider-config.json</c>):
    /// one agent named for its provider, active, key byte-identical and never unwrapped;
    /// idempotence; legacy provider spellings; the empty config; and both V18 mirroring
    /// branches through <c>ConfigManager.Load</c>.
    /// </summary>
    public class AiAgentMigrationTests : IDisposable
    {
        private static readonly string FixturePath = Path.Combine(
            AppContext.BaseDirectory, "Config", "Fixtures", "legacy-single-provider-config.json");

        // The exact values of the T003 fixture — assertions are pinned against them.
        private const string FixtureProvider = "anthropic";
        private const string FixtureModel = "claude-sonnet-4-20250514";
        private const string FixtureApiKey =
            "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA7Zx1m0bZQkG2n0h0placeholderbase64blob==";

        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { /* leave it for OS %TEMP% cleanup */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        private string WriteTempConfig(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), "akmlsql-migration-test-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, json);
            _tempFiles.Add(path);
            return path;
        }

        // ── V14: the fixture migrates ────────────────────────────────────────

        [Fact]
        public void LegacyConfig_MigratesToOneActiveAgent()
        {
            Assert.True(File.Exists(FixturePath), "T003 fixture was not copied to the output directory");

            var settings = ConfigManager.Load(FixturePath);

            var agent = Assert.Single(settings.Ai.Agents);
            Assert.Equal("Anthropic", agent.Name);                 // the provider's display name
            Assert.Equal(FixtureProvider, agent.Provider);
            Assert.Equal(FixtureModel, agent.Model);
            Assert.Equal(FixtureApiKey, agent.ApiKey);
            Assert.Equal("", agent.Endpoint);
            Assert.Equal(4096, agent.MaxTokens);
            Assert.Equal(0.2, agent.Temperature);
            Assert.Equal(30, agent.Timeout);
            Assert.Equal(2, agent.Retries);
            Assert.True(agent.Enabled);
            Assert.Null(agent.Health);
            Assert.False(string.IsNullOrEmpty(agent.Id));
            Assert.False(string.IsNullOrEmpty(agent.CreatedUtc));

            Assert.Equal(agent.Id, settings.Ai.ActiveAgentId);
            Assert.Same(agent, AiAgentResolver.Active(settings.Ai));
            Assert.True(settings.Ai.Enabled);                      // V19

            // Mirroring holds in memory straight after the load (V18, active branch).
            Assert.Equal(FixtureProvider, settings.Ai.Provider);
            Assert.Equal(FixtureModel, settings.Ai.Model);
            Assert.Equal(FixtureApiKey, settings.Ai.ApiKey);
        }

        [Fact]
        public void Migration_CopiesApiKeyVerbatim_NeverPassedToUnprotect()
        {
            var settings = ConfigManager.Load(FixturePath);

            var agent = Assert.Single(settings.Ai.Agents);
            // The fixture blob is a placeholder, not a real DPAPI value: any Unprotect call
            // would have thrown and Load would have returned defaults. Byte-identical survival —
            // prefix included — is the proof the key was never unwrapped or re-wrapped.
            Assert.Equal(FixtureApiKey, agent.ApiKey);
            Assert.True(ApiKeyProtector.IsProtected(agent.ApiKey));
            Assert.Equal(FixtureApiKey, settings.Ai.ApiKey);       // mirrored verbatim
        }

        [Fact]
        public void Migration_IsIdempotent_AcrossTwoLoads()
        {
            var first = ConfigManager.Load(FixturePath);
            var second = ConfigManager.Load(FixturePath);

            // Each load migrates the same un-migrated file once — one agent, never two.
            Assert.Single(first.Ai.Agents);
            Assert.Single(second.Ai.Agents);

            // Running normalisation again on already-normalised settings is a no-op.
            var id = first.Ai.ActiveAgentId;
            AiAgentResolver.Normalize(first.Ai);
            Assert.Single(first.Ai.Agents);
            Assert.Equal(id, first.Ai.ActiveAgentId);
        }

        [Fact]
        public void LegacyAzureSpelling_MigratesToCanonicalAzure()
        {
            var json = File.ReadAllText(FixturePath)
                .Replace("\"provider\": \"anthropic\"", "\"provider\": \"AzureOpenAI\"")
                // azure requires an endpoint (S1/V9) — without one the migrated agent is not
                // usable and the mirror blanks the flat fields.
                .Replace("\"endpoint\": \"\"", "\"endpoint\": \"https://my-resource.openai.azure.com/\"");
            var path = WriteTempConfig(json);

            var settings = ConfigManager.Load(path);

            var agent = Assert.Single(settings.Ai.Agents);
            Assert.Equal("azure", agent.Provider);
            Assert.Equal("Azure OpenAI", agent.Name);
            Assert.Equal(FixtureModel, agent.Model);               // carried, not validated
            Assert.Equal("azure", settings.Ai.Provider);           // mirrored canonical id
        }

        // ── Empty config ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("{}")]
        [InlineData("{ \"ai\": {} }")]
        public void EmptyConfig_StaysEmpty_WithEnabledFalse(string json)
        {
            var path = WriteTempConfig(json);

            var settings = ConfigManager.Load(path);

            Assert.Empty(settings.Ai.Agents);
            Assert.Equal("", settings.Ai.ActiveAgentId);
            Assert.False(settings.Ai.Enabled);                     // V19
            Assert.Null(AiAgentResolver.Active(settings.Ai));
        }

        // ── V18 mirroring through Load, both branches ────────────────────────

        [Fact]
        public void Mirroring_ActiveBranch_FlatFieldsOverwrittenFromActiveAgent()
        {
            var agent = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Primary",
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "dpapi:agent-key",
                Endpoint = "",
                MaxTokens = 8192,
                Temperature = 0.9,
                Timeout = 60,
                Retries = 4,
                Enabled = true,
            };
            var stored = new AppSettings();
            stored.Ai.Provider = "stale";
            stored.Ai.Model = "stale";
            stored.Ai.ApiKey = "stale";
            stored.Ai.Endpoint = "stale";
            stored.Ai.Agents.Add(agent);
            stored.Ai.ActiveAgentId = agent.Id;
            var path = WriteTempConfig(JsonSerializer.Serialize(stored));

            var settings = ConfigManager.Load(path);

            Assert.Equal("anthropic", settings.Ai.Provider);
            Assert.Equal("claude-sonnet-4-6", settings.Ai.Model);
            Assert.Equal("dpapi:agent-key", settings.Ai.ApiKey);
            Assert.Equal("", settings.Ai.Endpoint);
            Assert.Equal(8192, settings.Ai.MaxTokens);
            Assert.Equal(0.9, settings.Ai.Temperature);
            Assert.Equal(60, settings.Ai.Timeout);
            Assert.Equal(4, settings.Ai.Retries);
            Assert.True(settings.Ai.Enabled);
        }

        [Fact]
        public void Mirroring_BlankBranch_NonEmptyListNothingActive_BlanksStringsKeepsParams()
        {
            var agent = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Off",
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "dpapi:agent-key",
                Enabled = false,                                     // unusable → nothing active
            };
            var stored = new AppSettings();
            stored.Ai.Provider = "anthropic";
            stored.Ai.Model = "flat-model";
            stored.Ai.ApiKey = "flat-key";
            stored.Ai.Endpoint = "flat-endpoint";
            stored.Ai.MaxTokens = 1234;
            stored.Ai.Temperature = 0.7;
            stored.Ai.Timeout = 45;
            stored.Ai.Retries = 1;
            stored.Ai.Enabled = true;                                // stale — re-derived on load
            stored.Ai.Agents.Add(agent);
            stored.Ai.ActiveAgentId = agent.Id;
            var path = WriteTempConfig(JsonSerializer.Serialize(stored));

            var settings = ConfigManager.Load(path);

            Assert.Equal("", settings.Ai.Provider);
            Assert.Equal("", settings.Ai.Model);
            Assert.Equal("", settings.Ai.ApiKey);
            Assert.Equal("", settings.Ai.Endpoint);
            // Request parameters keep the values the file carried — harmless without a provider.
            Assert.Equal(1234, settings.Ai.MaxTokens);
            Assert.Equal(0.7, settings.Ai.Temperature);
            Assert.Equal(45, settings.Ai.Timeout);
            Assert.Equal(1, settings.Ai.Retries);
            Assert.False(settings.Ai.Enabled);                       // V19: no usable agent
        }

        // ── T092: Normalize never throws, on anything ──────────────────────

        [Fact]
        public void Normalize_NeverThrows_OnHandAssembledGarbage_AndEstablishesItsInvariants()
        {
            var cases = new List<KeyValuePair<string, AiSettings>>();
            void Case(string name, Action<AiSettings> mutate)
            {
                var ai = new AiSettings();
                mutate(ai);
                cases.Add(new KeyValuePair<string, AiSettings>(name, ai));
            }

            Case("null agents list", ai => ai.Agents = null!);
            Case("null feature assignments", ai => ai.FeatureAgents = null!);
            Case("null fallback order", ai => ai.FallbackOrder = null!);
            Case("null active id", ai => ai.ActiveAgentId = null!);
            Case("garbage active id", ai => ai.ActiveAgentId = "does-not-exist");
            Case("null agent entries", ai => { ai.Agents.Add(null!); ai.Agents.Add(null!); });
            Case("agent with null id", ai => ai.Agents.Add(new AiAgent { Id = null! }));
            Case("agent with empty id", ai => ai.Agents.Add(new AiAgent { Id = "" }));
            Case("agent with garbage id", ai => ai.Agents.Add(new AiAgent { Id = "not-an-id" }));
            Case("duplicate ids", ai =>
            {
                ai.Agents.Add(new AiAgent { Id = "aaaa1111aaaa1111aaaa1111aaaa1111", Name = "One" });
                ai.Agents.Add(new AiAgent { Id = "aaaa1111aaaa1111aaaa1111aaaa1111", Name = "Two" });
            });
            Case("agent with null fields", ai => ai.Agents.Add(new AiAgent
            {
                Id = "aaaa1111aaaa1111aaaa1111aaaa1111",
                Name = null!, Provider = null!, Model = null!, ApiKey = null!, Endpoint = null!,
            }));
            Case("agent with absurd numbers", ai => ai.Agents.Add(new AiAgent
            {
                Id = "aaaa1111aaaa1111aaaa1111aaaa1111",
                MaxTokens = int.MinValue, Temperature = -999.5, Timeout = -1, Retries = int.MaxValue,
            }));
            Case("garbage health", ai => ai.Agents.Add(new AiAgent
            {
                Id = "aaaa1111aaaa1111aaaa1111aaaa1111",
                Health = new AgentHealth { Status = "banana", CheckedUtc = "not-a-date", LatencyMs = -5, Message = null! },
            }));
            Case("null health status", ai => ai.Agents.Add(new AiAgent
            {
                Id = "aaaa1111aaaa1111aaaa1111aaaa1111",
                Health = new AgentHealth { Status = null! },
            }));
            Case("garbage feature assignments", ai =>
            {
                ai.FeatureAgents.Chat = "does-not-exist";
                ai.FeatureAgents.Explain = null!;
            });
            Case("garbage fallback order", ai =>
            {
                ai.FallbackOrder.Add(null!);
                ai.FallbackOrder.Add("");
                ai.FallbackOrder.Add("does-not-exist");
                ai.FallbackOrder.Add("does-not-exist");
            });
            Case("beyond the agent ceiling", ai =>
            {
                for (var i = 0; i < 25; i++)
                    ai.Agents.Add(new AiAgent { Id = i.ToString("D32").Replace('0', 'a'), Name = "Agent " + i });
            });
            Case("everything at once", ai =>
            {
                ai.Agents = null!;
                ai.FeatureAgents = null!;
                ai.FallbackOrder = null!;
                ai.ActiveAgentId = null!;
                ai.Provider = null!;
                ai.Model = null!;
                ai.ApiKey = null!;
            });

            foreach (var (name, ai) in cases)
            {
                var ex = Record.Exception(() => AiAgentResolver.Normalize(ai));
                Assert.True(ex == null, $"case \"{name}\" threw: {ex}");
                AssertNormalised(ai);

                // Idempotent: a second pass changes nothing, even bytewise.
                var once = JsonSerializer.Serialize(ai);
                AiAgentResolver.Normalize(ai);
                Assert.Equal(once, JsonSerializer.Serialize(ai));
            }
        }

        [Fact]
        public void Normalize_NeverThrows_UnderRandomisedFuzzing()
        {
            var rng = new Random(20260907);   // fixed seed — a failure must reproduce
            for (var i = 0; i < 500; i++)
            {
                var ai = FuzzSettings(rng);
                Exception? ex = null;
                try
                {
                    AiAgentResolver.Normalize(ai);
                    AiAgentResolver.Normalize(ai);   // the idempotence pass must be safe too
                }
                catch (Exception caught)
                {
                    ex = caught;
                }
                Assert.True(ex == null, $"fuzz iteration {i} threw: {ex}");
                AssertNormalised(ai);
            }
        }

        [Fact]
        public void Load_KitchenSinkMalformedConfig_ReturnsUsableSettings()
        {
            var path = WriteTempConfig("""
                {
                  "ai": {
                    "enabled": true,
                    "provider": "anthropic",
                    "model": "claude-sonnet-4-6",
                    "apiKey": "dpapi:flat-key",
                    "agents": [
                      null,
                      { "id": "", "name": "No id" },
                      { "id": "aaaa1111aaaa1111aaaa1111aaaa1111", "name": "Real",
                        "provider": "anthropic", "model": "claude-sonnet-4-6", "apiKey": "dpapi:k",
                        "enabled": true,
                        "health": { "status": "banana", "latencyMs": -5 } },
                      { "id": "aaaa1111aaaa1111aaaa1111aaaa1111", "name": "Dupe id" },
                      { "id": null, "name": "Null id" }
                    ],
                    "activeAgentId": "does-not-exist",
                    "featureAgents": { "chat": "does-not-exist", "explain": null },
                    "fallbackOrder": [ "does-not-exist", "aaaa1111aaaa1111aaaa1111aaaa1111",
                                       "aaaa1111aaaa1111aaaa1111aaaa1111", null, "" ]
                  }
                }
                """);

            AppSettings settings = null!;
            var ex = Record.Exception(() => settings = ConfigManager.Load(path));

            Assert.Null(ex);
            Assert.NotNull(settings);
            var ai = settings.Ai;
            var agent = Assert.Single(ai.Agents);              // V15: only "Real" survives
            Assert.Equal("Real", agent.Name);
            Assert.Equal(AgentHealthStatus.Unknown, agent.Health!.Status);   // V20
            Assert.Equal(agent.Id, ai.ActiveAgentId);          // V13: the only usable agent
            Assert.Equal("", ai.FeatureAgents.Chat);           // V16
            Assert.Equal("", ai.FeatureAgents.Explain);
            Assert.Empty(ai.FallbackOrder);                    // V17: dangling, duplicates, nulls and the self-reference all go
            Assert.True(ai.Enabled);                           // V19
            Assert.Equal("anthropic", ai.Provider);            // V18 mirror
            AssertNormalised(ai);
        }

        [Fact]
        public void Load_AllAgentEntriesMalformed_FlatProviderMigrates()
        {
            // Ordering: V15's drops run before V14, so a list that lost every entry to V15 is the
            // pre-migration shape again — the flat fields are rescued into an agent rather than lost.
            var path = WriteTempConfig("""
                {
                  "ai": {
                    "provider": "anthropic",
                    "model": "claude-sonnet-4-6",
                    "apiKey": "dpapi:flat-key",
                    "agents": [ { "id": "", "name": "Broken" } ]
                  }
                }
                """);

            var settings = ConfigManager.Load(path);

            var agent = Assert.Single(settings.Ai.Agents);
            Assert.Equal("Anthropic", agent.Name);
            Assert.Equal("anthropic", agent.Provider);
            Assert.Equal("dpapi:flat-key", agent.ApiKey);      // verbatim, as every migration
            Assert.Equal(agent.Id, settings.Ai.ActiveAgentId);
            Assert.True(settings.Ai.Enabled);
        }

        // ── T092 helpers ─────────────────────────────────────────────────────

        /// <summary>The contract <c>Normalize</c> must establish from ANY starting state.</summary>
        private static void AssertNormalised(AiSettings ai)
        {
            Assert.NotNull(ai.Agents);
            Assert.NotNull(ai.FeatureAgents);
            Assert.NotNull(ai.FallbackOrder);
            Assert.True(ai.Agents.Count <= AiAgentResolver.MaxAgents);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agent in ai.Agents)
            {
                Assert.NotNull(agent);
                Assert.False(string.IsNullOrEmpty(agent.Id), "an empty id survived normalisation");
                Assert.True(ids.Add(agent.Id), "a duplicate id survived normalisation");
                if (agent.Health != null)
                {
                    Assert.Contains(agent.Health.Status,
                        new[] { "unknown", "ready", "needsKey", "failed" }, StringComparer.OrdinalIgnoreCase);
                }
            }

            Assert.NotNull(ai.ActiveAgentId);
            Assert.True(ai.ActiveAgentId.Length == 0 || ids.Contains(ai.ActiveAgentId),
                "the active id names no agent after normalisation");

            foreach (var assignment in new[]
                {
                    ai.FeatureAgents.Chat, ai.FeatureAgents.TextToSql, ai.FeatureAgents.Explain,
                    ai.FeatureAgents.Fix, ai.FeatureAgents.Optimize, ai.FeatureAgents.IndexSuggestions,
                    ai.FeatureAgents.GhostText,
                })
            {
                Assert.NotNull(assignment);
                if (assignment.Length == 0) continue;
                var assigned = ai.Agents.Find(a => a.Id == assignment);
                Assert.NotNull(assigned);
                Assert.True(AiAgentResolver.IsUsable(assigned), "an unusable assignment survived normalisation");
            }

            var seenFallback = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ai.FallbackOrder)
            {
                Assert.False(string.IsNullOrEmpty(id), "an empty fallback entry survived normalisation");
                Assert.NotEqual(ai.ActiveAgentId, id);
                Assert.True(seenFallback.Add(id), "a duplicate fallback entry survived normalisation");
                var fallback = ai.Agents.Find(a => a.Id == id);
                Assert.NotNull(fallback);
                Assert.True(AiAgentResolver.IsUsable(fallback), "an unusable fallback entry survived normalisation");
            }
        }

        private static AiSettings FuzzSettings(Random rng)
        {
            var ai = new AiSettings
            {
                Provider = JunkString(rng),
                Model = JunkString(rng),
                ApiKey = JunkString(rng),
                Endpoint = JunkString(rng),
                ActiveAgentId = JunkString(rng),
                MaxTokens = JunkInt(rng),
                Temperature = JunkDouble(rng),
                Timeout = JunkInt(rng),
                Retries = JunkInt(rng),
            };
            var agentCount = rng.Next(0, 26);
            for (var i = 0; i < agentCount; i++)
            {
                if (rng.Next(10) == 0)
                {
                    ai.Agents.Add(null!);
                    continue;
                }
                ai.Agents.Add(new AiAgent
                {
                    Id = JunkString(rng),
                    Name = JunkString(rng),
                    Provider = JunkString(rng),
                    Model = JunkString(rng),
                    ApiKey = JunkString(rng),
                    Endpoint = JunkString(rng),
                    MaxTokens = JunkInt(rng),
                    Temperature = JunkDouble(rng),
                    Timeout = JunkInt(rng),
                    Retries = JunkInt(rng),
                    Enabled = rng.Next(2) == 0,
                    CreatedUtc = JunkString(rng),
                    Health = rng.Next(3) == 0
                        ? null
                        : new AgentHealth
                        {
                            Status = JunkString(rng),
                            CheckedUtc = JunkString(rng),
                            LatencyMs = JunkInt(rng),
                            Message = JunkString(rng),
                        },
                });
            }
            ai.FeatureAgents = new FeatureAgentAssignments
            {
                Chat = JunkString(rng),
                TextToSql = JunkString(rng),
                Explain = JunkString(rng),
                Fix = JunkString(rng),
                Optimize = JunkString(rng),
                IndexSuggestions = JunkString(rng),
                GhostText = JunkString(rng),
            };
            var fallbackCount = rng.Next(0, 8);
            for (var i = 0; i < fallbackCount; i++)
                ai.FallbackOrder.Add(JunkString(rng));
            return ai;
        }

        private static string JunkString(Random rng)
        {
            switch (rng.Next(8))
            {
                case 0: return null!;   // the helper's job is hostile values; callers expect them
                case 1: return "";
                case 2: return "   ";
                case 3: return "not-an-id";
                case 4: return Guid.NewGuid().ToString("N");
                case 5:
                    var chars = new char[rng.Next(1, 50)];
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = (char)rng.Next(32, 0x2FFF);   // printable BMP, no surrogates
                    return new string(chars);
                default: return rng.Next(2) == 0 ? "anthropic" : "claude-sonnet-4-6";
            }
        }

        private static int JunkInt(Random rng)
        {
            switch (rng.Next(5))
            {
                case 0: return int.MinValue;
                case 1: return -1;
                case 2: return 0;
                case 3: return int.MaxValue;
                default: return rng.Next(-1000, 1000);
            }
        }

        private static double JunkDouble(Random rng)
        {
            switch (rng.Next(5))
            {
                case 0: return -1;
                case 1: return 1e308;
                case 2: return double.NaN;
                case 3: return double.PositiveInfinity;
                default: return rng.NextDouble() * 1000 - 500;
            }
        }
    }
}
