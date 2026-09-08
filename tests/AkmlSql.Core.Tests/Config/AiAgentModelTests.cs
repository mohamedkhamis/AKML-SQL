using System;
using System.Collections.Generic;
using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 037 Phase 2 (T011) — the agent model (data-model E1–E4), the edit-time validation
    /// rules V1–V12 exposed for the Options page, the S1 usability predicate across all eight
    /// providers, and the 20-agent ceiling (V12).
    /// </summary>
    public class AiAgentModelTests
    {
        // ── Defaults ─────────────────────────────────────────────────────────

        [Fact]
        public void AiAgent_Defaults()
        {
            var a = new AiAgent();
            Assert.Equal("", a.Id);
            Assert.Equal("", a.Name);
            Assert.Equal("", a.Provider);
            Assert.Equal("", a.Model);
            Assert.Equal("", a.ApiKey);
            Assert.Equal("", a.Endpoint);
            Assert.Equal(4096, a.MaxTokens);
            Assert.Equal(0.2, a.Temperature);
            Assert.Equal(30, a.Timeout);
            Assert.Equal(2, a.Retries);
            Assert.True(a.Enabled);
            Assert.Equal("", a.CreatedUtc);
            Assert.Null(a.Health);
        }

        [Fact]
        public void AgentHealth_Defaults()
        {
            var h = new AgentHealth();
            Assert.Equal(AgentHealthStatus.Unknown, h.Status);
            Assert.Null(h.CheckedUtc);
            Assert.Equal(0, h.LatencyMs);
            Assert.Equal("", h.Message);
        }

        [Fact]
        public void AgentHealthStatus_AreStringConstants_NotAnEnum()
        {
            Assert.Equal("unknown", AgentHealthStatus.Unknown);
            Assert.Equal("ready", AgentHealthStatus.Ready);
            Assert.Equal("needsKey", AgentHealthStatus.NeedsKey);
            Assert.Equal("failed", AgentHealthStatus.Failed);
        }

        [Fact]
        public void FeatureAgentAssignments_DefaultsAllEmpty()
        {
            var f = new FeatureAgentAssignments();
            Assert.Equal("", f.Chat);
            Assert.Equal("", f.TextToSql);
            Assert.Equal("", f.Explain);
            Assert.Equal("", f.Fix);
            Assert.Equal("", f.Optimize);
            Assert.Equal("", f.IndexSuggestions);
            Assert.Equal("", f.GhostText);
        }

        [Fact]
        public void AiSettings_AgentDefaults()
        {
            var s = new AiSettings();
            Assert.NotNull(s.Agents);
            Assert.Empty(s.Agents);
            Assert.Equal("", s.ActiveAgentId);
            Assert.NotNull(s.FeatureAgents);
            Assert.NotNull(s.FallbackOrder);
            Assert.Empty(s.FallbackOrder);
        }

        [Fact]
        public void AiFeature_HasExactlyTheSevenAssignableFeatures()
        {
            Assert.Equal(
                new[]
                {
                    "Chat", "TextToSql", "Explain", "Fix", "Optimize", "IndexSuggestions", "GhostText"
                },
                Enum.GetNames(typeof(AiFeature)));
        }

        [Fact]
        public void AgentModel_JsonPropertyNames_AreCamelCase()
        {
            var s = new AiSettings();
            s.Agents.Add(new AiAgent { Id = "abc", Health = new AgentHealth() });

            var json = JsonSerializer.Serialize(s);

            Assert.Contains("\"agents\"", json);
            Assert.Contains("\"activeAgentId\"", json);
            Assert.Contains("\"featureAgents\"", json);
            Assert.Contains("\"fallbackOrder\"", json);
            Assert.Contains("\"id\"", json);
            Assert.Contains("\"apiKey\"", json);
            Assert.Contains("\"maxTokens\"", json);
            Assert.Contains("\"createdUtc\"", json);
            Assert.Contains("\"health\"", json);
            Assert.Contains("\"status\"", json);
            Assert.Contains("\"checkedUtc\"", json);
            Assert.Contains("\"latencyMs\"", json);
        }

        // ── V1–V12 via AiAgentResolver.Validate ──────────────────────────────

        private static AiAgent ValidAgentFor(string provider)
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Test " + provider,
                Provider = provider,
                Model = "some-model",   // unknown family → V7 passes for every provider
                ApiKey = AiAgentResolver.RequiresApiKey(provider) ? "sk-test" : "",
                Endpoint = AiAgentResolver.RequiresEndpoint(provider) ? "https://example.test/v1" : "",
            };
        }

        private static List<AiAgent> ListOf(AiAgent agent) => new() { agent };

        [Fact]
        public void Validate_ValidAgent_ReturnsNull()
        {
            foreach (var provider in AiProviderIds.CanonicalIds)
            {
                var agent = ValidAgentFor(provider);
                Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void V1_NameEmptyAfterTrim_IsRejected(string name)
        {
            var agent = ValidAgentFor("ollama");
            agent.Name = name;
            Assert.Equal("Name is required.", AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Fact]
        public void V2_NameLongerThan40AfterTrim_IsRejected()
        {
            var agent = ValidAgentFor("ollama");
            agent.Name = new string('x', 41);
            Assert.NotNull(AiAgentResolver.Validate(agent, ListOf(agent)));

            agent.Name = new string('x', 40);
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Fact]
        public void V3_DuplicateName_CaseAndTrimInsensitive_IsRejected()
        {
            var agent = ValidAgentFor("ollama");
            agent.Name = "Kimi";
            var other = ValidAgentFor("openai");
            other.Name = "kimi ";
            var all = new List<AiAgent> { agent, other };

            var error = AiAgentResolver.Validate(agent, all);
            Assert.NotNull(error);
            Assert.Contains("Kimi", error);
        }

        [Fact]
        public void V3_AgentDoesNotCollideWithItself()
        {
            var agent = ValidAgentFor("ollama");
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Fact]
        public void V4_ProviderMustBeCanonical()
        {
            var agent = ValidAgentFor("ollama");
            agent.Provider = "bogus";
            Assert.NotNull(AiAgentResolver.Validate(agent, ListOf(agent)));

            agent.Provider = "";
            Assert.NotNull(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("0123456789abcdef0123456789abcdeg")]   // 32 chars but 'g' is not hex
        [InlineData("0123456789abcdef0123456789abcde")]    // 31 hex chars
        public void V5_IdMustBe32HexCharacters(string id)
        {
            var agent = ValidAgentFor("ollama");
            agent.Id = id;
            Assert.Equal("Id must be 32 hexadecimal characters.",
                AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Fact]
        public void V5_DuplicateId_IsRejected()
        {
            var agent = ValidAgentFor("ollama");
            var other = ValidAgentFor("openai");
            other.Id = agent.Id;
            var all = new List<AiAgent> { agent, other };

            Assert.NotNull(AiAgentResolver.Validate(agent, all));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void V6_ModelEmpty_IsRejected(string model)
        {
            var agent = ValidAgentFor("ollama");
            agent.Model = model;
            Assert.Equal("Model is required.", AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("anthropic", "gpt-4o")]
        [InlineData("openai", "claude-sonnet-4-6")]
        [InlineData("gemini", "kimi-latest")]
        [InlineData("kimi", "gemini-flash-latest")]
        public void V7_ForeignModelFamily_IsRejected(string provider, string model)
        {
            var agent = ValidAgentFor(provider);
            agent.Model = model;
            var error = AiAgentResolver.Validate(agent, ListOf(agent));
            Assert.NotNull(error);
            Assert.Contains(model, error);
        }

        [Theory]
        [InlineData("anthropic", "claude-sonnet-4-6")]
        [InlineData("openai", "gpt-4o")]
        [InlineData("gemini", "gemini-flash-latest")]
        [InlineData("kimi", "kimi-latest")]
        [InlineData("azure", "gpt-4o")]          // azure deployment names are free-form — exempt
        [InlineData("ollama", "gpt-4o")]         // local providers are exempt
        [InlineData("lmstudio", "claude-x")]     // local providers are exempt
        [InlineData("custom", "claude-x")]       // custom endpoints are exempt
        public void V7_MatchingOrExemptFamily_Passes(string provider, string model)
        {
            var agent = ValidAgentFor(provider);
            agent.Model = model;
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("anthropic")]
        [InlineData("openai")]
        [InlineData("azure")]
        [InlineData("gemini")]
        [InlineData("kimi")]
        public void V8_KeyRequiredProviders_RejectEmptyKey(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.ApiKey = "";
            Assert.NotNull(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Fact]
        public void V8_DecryptFailure_KeepsStoredKeyStanding()
        {
            // V23: an undecryptable stored key is not an edit-time error — the agent is
            // reported needsKey instead.
            var agent = ValidAgentFor("anthropic");
            agent.ApiKey = "";
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent), keyDecryptFailed: true));
        }

        [Theory]
        [InlineData("ollama")]
        [InlineData("lmstudio")]
        [InlineData("custom")]
        public void V8_NoKeyProviders_AcceptEmptyKey(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.ApiKey = "";
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("azure")]
        [InlineData("custom")]
        public void V9_EndpointRequiredProviders_RejectEmptyEndpoint(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.Endpoint = "";
            Assert.NotNull(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("not a url")]
        [InlineData("endpoint.example.com/path")]   // no scheme → relative
        [InlineData("path/to/endpoint")]            // no scheme → relative
        public void V10_NonAbsoluteEndpoint_IsRejected(string endpoint)
        {
            var agent = ValidAgentFor("ollama");
            agent.Endpoint = endpoint;
            Assert.Equal("Endpoint must be an absolute URL.",
                AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData("http://localhost:11434")]
        [InlineData("https://example.test/v1")]
        public void V10_AbsoluteEndpoint_Passes(string endpoint)
        {
            var agent = ValidAgentFor("ollama");
            agent.Endpoint = endpoint;
            Assert.Null(AiAgentResolver.Validate(agent, ListOf(agent)));
        }

        [Theory]
        [InlineData(255, false)]
        [InlineData(256, true)]
        [InlineData(32768, true)]
        [InlineData(32769, false)]
        public void V11_MaxTokensRange(int maxTokens, bool valid)
        {
            var agent = ValidAgentFor("ollama");
            agent.MaxTokens = maxTokens;
            Assert.Equal(valid, AiAgentResolver.Validate(agent, ListOf(agent)) == null);
        }

        [Theory]
        [InlineData(-0.1, false)]
        [InlineData(0.0, true)]
        [InlineData(2.0, true)]
        [InlineData(2.1, false)]
        public void V11_TemperatureRange(double temperature, bool valid)
        {
            var agent = ValidAgentFor("ollama");
            agent.Temperature = temperature;
            Assert.Equal(valid, AiAgentResolver.Validate(agent, ListOf(agent)) == null);
        }

        [Theory]
        [InlineData(4, false)]
        [InlineData(5, true)]
        [InlineData(300, true)]
        [InlineData(301, false)]
        public void V11_TimeoutRange(int timeout, bool valid)
        {
            var agent = ValidAgentFor("ollama");
            agent.Timeout = timeout;
            Assert.Equal(valid, AiAgentResolver.Validate(agent, ListOf(agent)) == null);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        public void V11_RetriesRange(int retries, bool valid)
        {
            var agent = ValidAgentFor("ollama");
            agent.Retries = retries;
            Assert.Equal(valid, AiAgentResolver.Validate(agent, ListOf(agent)) == null);
        }

        [Fact]
        public void V12_TwentyAgentCeiling()
        {
            Assert.Equal(20, AiAgentResolver.MaxAgents);
            Assert.True(AiAgentResolver.CanAddAgent(0));
            Assert.True(AiAgentResolver.CanAddAgent(19));
            Assert.False(AiAgentResolver.CanAddAgent(20));
            Assert.False(AiAgentResolver.CanAddAgent(21));
        }

        // ── S1: usability across all eight providers ─────────────────────────

        [Theory]
        [InlineData("anthropic", true, false)]
        [InlineData("openai", true, false)]
        [InlineData("azure", true, true)]
        [InlineData("gemini", true, false)]
        [InlineData("kimi", true, false)]
        [InlineData("ollama", false, false)]
        [InlineData("lmstudio", false, false)]
        [InlineData("custom", false, true)]
        public void RequiresApiKey_And_Endpoint_PerProvider(string provider, bool needsKey, bool needsEndpoint)
        {
            Assert.Equal(needsKey, AiAgentResolver.RequiresApiKey(provider));
            Assert.Equal(needsEndpoint, AiAgentResolver.RequiresEndpoint(provider));
        }

        [Theory]
        [InlineData("anthropic")]
        [InlineData("openai")]
        [InlineData("azure")]
        [InlineData("gemini")]
        [InlineData("kimi")]
        [InlineData("ollama")]
        [InlineData("lmstudio")]
        [InlineData("custom")]
        public void S1_FullyConfiguredAgent_IsUsable(string provider)
        {
            Assert.True(AiAgentResolver.IsUsable(ValidAgentFor(provider)));
        }

        [Theory]
        [InlineData("anthropic")]
        [InlineData("openai")]
        [InlineData("azure")]
        [InlineData("gemini")]
        [InlineData("kimi")]
        public void S1_KeyProviderWithoutKey_IsNotUsable(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.ApiKey = "";
            Assert.False(AiAgentResolver.IsUsable(agent));
        }

        [Theory]
        [InlineData("ollama")]
        [InlineData("lmstudio")]
        public void S1_LocalProviderNeedsNoKeyNorEndpoint(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.ApiKey = "";
            agent.Endpoint = "";
            Assert.True(AiAgentResolver.IsUsable(agent));
        }

        [Theory]
        [InlineData("azure")]
        [InlineData("custom")]
        public void S1_EndpointProviderWithoutEndpoint_IsNotUsable(string provider)
        {
            var agent = ValidAgentFor(provider);
            agent.Endpoint = "";
            Assert.False(AiAgentResolver.IsUsable(agent));
        }

        [Fact]
        public void S1_DisabledAgent_IsNotUsable()
        {
            var agent = ValidAgentFor("ollama");
            agent.Enabled = false;
            Assert.False(AiAgentResolver.IsUsable(agent));
        }

        [Fact]
        public void S1_UnknownProvider_IsNotUsable()
        {
            var agent = ValidAgentFor("ollama");
            agent.Provider = "bogus";
            Assert.False(AiAgentResolver.IsUsable(agent));
        }

        [Fact]
        public void S1_EmptyModel_IsNotUsable()
        {
            var agent = ValidAgentFor("ollama");
            agent.Model = "";
            Assert.False(AiAgentResolver.IsUsable(agent));
        }
    }
}
