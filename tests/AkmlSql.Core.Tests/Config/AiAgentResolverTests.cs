using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Providers;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 037 Phase 2 (T012, T015) — <see cref="AiAgentResolver"/>: projection (FR-048),
    /// resolution (S3), naming, mirroring (V18) including the on-disk invariant, the derived
    /// <c>Enabled</c> flag (V19), and the regression pins that a migrated config still feeds
    /// <c>AiProviderFactory.Create</c> and <c>AiCommandVisibility</c> unchanged.
    /// Phase 8 (T090, T091) adds the load-time repairs: V13 (dangling active id), V15 (malformed
    /// entries dropped with a warning naming the index), V16 (dangling assignments cleared),
    /// V17 (fallback-order pruning), V20 (unknown health status) and V21 (the 20-agent ceiling).
    /// Like <c>ConfigManagerTests</c>, each test runs against an isolated throwaway AppData root
    /// (via the <c>AKML_APP_DATA_ROOT</c> override) so the round-trip tests never touch the real
    /// <c>%AppData%\AKML SQL</c> directory; the <c>[Collection]</c> serialises the process-global
    /// override against the other classes using it.
    /// </summary>
    [Collection("AkmlSql real AppData")]
    public class AiAgentResolverTests : IDisposable
    {
        private const string AppDataRootEnvVar = "AKML_APP_DATA_ROOT";
        private readonly string? _priorRoot;
        private readonly string _tempRoot;
        private readonly string _configPath;

        public AiAgentResolverTests()
        {
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            _tempRoot = Path.Combine(Path.GetTempPath(), "akmlsql-agent-test-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _tempRoot);
            _configPath = Constants.ConfigFilePath;   // now resolves under _tempRoot\AKML SQL
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException) { /* leave the temp tree for OS %TEMP% cleanup */ }
            catch (UnauthorizedAccessException) { }
        }

        private static AiAgent UsableAgent(string name, string provider = "anthropic", string model = "claude-sonnet-4-6")
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = AiAgentResolver.RequiresApiKey(provider) ? "sk-test-" + name : "",
                Endpoint = AiAgentResolver.RequiresEndpoint(provider) ? "https://example.test" : "",
            };
        }

        private static void SetAssignment(FeatureAgentAssignments f, AiFeature feature, string id)
        {
            switch (feature)
            {
                case AiFeature.Chat: f.Chat = id; break;
                case AiFeature.TextToSql: f.TextToSql = id; break;
                case AiFeature.Explain: f.Explain = id; break;
                case AiFeature.Fix: f.Fix = id; break;
                case AiFeature.Optimize: f.Optimize = id; break;
                case AiFeature.IndexSuggestions: f.IndexSuggestions = id; break;
                case AiFeature.GhostText: f.GhostText = id; break;
                default: throw new ArgumentOutOfRangeException(nameof(feature));
            }
        }

        // ── Project (FR-048) ─────────────────────────────────────────────────

        [Fact]
        public void Project_SubstitutesConnectionFieldsAndRequestParams()
        {
            var global = new AiSettings { Provider = "ollama", Model = "llama3.1" };
            var agent = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Cloud",
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "dpapi:wrapped",
                Endpoint = "https://api.anthropic.com",
                MaxTokens = 8192,
                Temperature = 0.9,
                Timeout = 60,
                Retries = 4,
            };

            var projected = AiAgentResolver.Project(global, agent);

            Assert.Equal("anthropic", projected.Provider);
            Assert.Equal("claude-sonnet-4-6", projected.Model);
            Assert.Equal("dpapi:wrapped", projected.ApiKey);
            Assert.Equal("https://api.anthropic.com", projected.Endpoint);
            Assert.Equal(8192, projected.MaxTokens);
            Assert.Equal(0.9, projected.Temperature);
            Assert.Equal(60, projected.Timeout);
            Assert.Equal(4, projected.Retries);
        }

        [Fact]
        public void Project_PreservesGlobalConcerns()
        {
            var global = new AiSettings
            {
                Enabled = true,
                PrivacyMode = "full",
                PrivacyConsentRequired = false,
                SchemaContextMaxObjects = 42,
                OfflineProvider = "ollama",
                OfflineModel = "qwen2.5-coder",
                OfflineEndpoint = "http://localhost:11434",
                TextToSql = false,
                Explain = false,
                Fix = false,
                AutoFixOnError = true,
                Optimize = false,
                IndexSuggestions = false,
                InlineCompletion = true,
                ChatPanel = false,
                OpenChatShortcut = "Alt+Q",
                FixShortcut = "Ctrl+F1",
                OptimizeShortcut = "Ctrl+F2",
                GhostTextShortcut = "Ctrl+F3",
                ShowEditorIcon = false,
                ShowFollowupSuggestions = false,
                CommentTriggerPrefix = "-- ai:",
                GhostTextDelayMs = 123,
            };
            var agent = UsableAgent("Cloud");

            var projected = AiAgentResolver.Project(global, agent);

            Assert.True(projected.Enabled);
            Assert.Equal("full", projected.PrivacyMode);
            Assert.False(projected.PrivacyConsentRequired);
            Assert.Equal(42, projected.SchemaContextMaxObjects);
            Assert.Equal("ollama", projected.OfflineProvider);
            Assert.Equal("qwen2.5-coder", projected.OfflineModel);
            Assert.Equal("http://localhost:11434", projected.OfflineEndpoint);
            Assert.False(projected.TextToSql);
            Assert.False(projected.Explain);
            Assert.False(projected.Fix);
            Assert.True(projected.AutoFixOnError);
            Assert.False(projected.Optimize);
            Assert.False(projected.IndexSuggestions);
            Assert.True(projected.InlineCompletion);
            Assert.False(projected.ChatPanel);
            Assert.Equal("Alt+Q", projected.OpenChatShortcut);
            Assert.Equal("Ctrl+F1", projected.FixShortcut);
            Assert.Equal("Ctrl+F2", projected.OptimizeShortcut);
            Assert.Equal("Ctrl+F3", projected.GhostTextShortcut);
            Assert.False(projected.ShowEditorIcon);
            Assert.False(projected.ShowFollowupSuggestions);
            Assert.Equal("-- ai:", projected.CommentTriggerPrefix);
            Assert.Equal(123, projected.GhostTextDelayMs);
        }

        [Fact]
        public void Project_DoesNotAliasMutableMembers()
        {
            var global = new AiSettings();
            global.Agents.Add(UsableAgent("One"));
            global.FallbackOrder.Add("fallback-id");
            global.FeatureAgents.Chat = "chat-id";
            var agent = UsableAgent("Cloud");

            var projected = AiAgentResolver.Project(global, agent);

            Assert.NotSame(global.Agents, projected.Agents);
            Assert.NotSame(global.FallbackOrder, projected.FallbackOrder);
            Assert.NotSame(global.FeatureAgents, projected.FeatureAgents);

            // Mutating the projection must never reach the cached global settings.
            projected.Agents.Add(UsableAgent("Two"));
            projected.FallbackOrder.Add("extra");
            projected.FeatureAgents.Chat = "changed";
            projected.Provider = "mutated";

            Assert.Single(global.Agents);
            Assert.Single(global.FallbackOrder);
            Assert.Equal("chat-id", global.FeatureAgents.Chat);
            Assert.Equal("", global.Provider);

            // The global's values are carried across, not shared.
            Assert.Equal(new[] { "fallback-id", "extra" }, projected.FallbackOrder);
            Assert.Equal(global.ActiveAgentId, projected.ActiveAgentId);
        }

        // ── Active / ResolveFor (S3) ─────────────────────────────────────────

        [Fact]
        public void Active_NoActiveId_ReturnsNull()
        {
            var ai = new AiSettings();
            ai.Agents.Add(UsableAgent("One"));
            Assert.Null(AiAgentResolver.Active(ai));
        }

        [Fact]
        public void Active_DanglingId_ReturnsNull()
        {
            var ai = new AiSettings { ActiveAgentId = "does-not-exist" };
            ai.Agents.Add(UsableAgent("One"));
            Assert.Null(AiAgentResolver.Active(ai));
        }

        [Fact]
        public void Active_UnusableAgent_ReturnsNull()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            agent.Enabled = false;
            ai.Agents.Add(agent);
            ai.ActiveAgentId = agent.Id;
            Assert.Null(AiAgentResolver.Active(ai));
        }

        [Fact]
        public void Active_UsableAgent_ReturnsIt()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            ai.Agents.Add(agent);
            ai.ActiveAgentId = agent.Id;
            Assert.Same(agent, AiAgentResolver.Active(ai));
        }

        [Fact]
        public void ResolveFor_AssignedUsableAgentWins()
        {
            var ai = new AiSettings();
            var active = UsableAgent("Active");
            var assigned = UsableAgent("Assigned", "ollama", "llama3.1");
            ai.Agents.Add(active);
            ai.Agents.Add(assigned);
            ai.ActiveAgentId = active.Id;
            ai.FeatureAgents.Chat = assigned.Id;

            Assert.Same(assigned, AiAgentResolver.ResolveFor(ai, AiFeature.Chat));
        }

        [Fact]
        public void ResolveFor_UnusableAssignedFallsBackToActive()
        {
            var ai = new AiSettings();
            var active = UsableAgent("Active");
            var assigned = UsableAgent("Assigned");
            assigned.Enabled = false;
            ai.Agents.Add(active);
            ai.Agents.Add(assigned);
            ai.ActiveAgentId = active.Id;
            ai.FeatureAgents.Explain = assigned.Id;

            Assert.Same(active, AiAgentResolver.ResolveFor(ai, AiFeature.Explain));
        }

        [Fact]
        public void ResolveFor_NothingUsable_ReturnsNull()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("Off");
            agent.Enabled = false;
            ai.Agents.Add(agent);
            ai.ActiveAgentId = agent.Id;

            Assert.Null(AiAgentResolver.ResolveFor(ai, AiFeature.Chat));
        }

        [Theory]
        [InlineData(AiFeature.Chat)]
        [InlineData(AiFeature.TextToSql)]
        [InlineData(AiFeature.Explain)]
        [InlineData(AiFeature.Fix)]
        [InlineData(AiFeature.Optimize)]
        [InlineData(AiFeature.IndexSuggestions)]
        [InlineData(AiFeature.GhostText)]
        public void ResolveFor_EveryFeature_ReadsItsOwnAssignmentField(AiFeature feature)
        {
            var ai = new AiSettings();
            var active = UsableAgent("Active");
            var assigned = UsableAgent("Assigned", "ollama", "llama3.1");
            ai.Agents.Add(active);
            ai.Agents.Add(assigned);
            ai.ActiveAgentId = active.Id;
            SetAssignment(ai.FeatureAgents, feature, assigned.Id);

            Assert.Same(assigned, AiAgentResolver.ResolveFor(ai, feature));
        }

        [Theory]
        [InlineData(AiFeature.Chat)]
        [InlineData(AiFeature.TextToSql)]
        [InlineData(AiFeature.Explain)]
        [InlineData(AiFeature.Fix)]
        [InlineData(AiFeature.Optimize)]
        [InlineData(AiFeature.IndexSuggestions)]
        [InlineData(AiFeature.GhostText)]
        public void ResolveFor_UnassignedFeature_FollowsActiveAgent(AiFeature feature)
        {
            var ai = new AiSettings();
            var active = UsableAgent("Active");
            ai.Agents.Add(active);
            ai.ActiveAgentId = active.Id;

            Assert.Same(active, AiAgentResolver.ResolveFor(ai, feature));
        }

        // ── Naming ───────────────────────────────────────────────────────────

        [Fact]
        public void SuggestName_EmptyList_IsAgent1()
        {
            Assert.Equal("Agent 1", AiAgentResolver.SuggestName(Array.Empty<AiAgent>()));
        }

        [Fact]
        public void SuggestName_LowestFreeNWins()
        {
            var agents = new[] { UsableAgent("x"), UsableAgent("y") };
            agents[0].Name = "Agent 1";
            agents[1].Name = "Agent 3";
            Assert.Equal("Agent 2", AiAgentResolver.SuggestName(agents));
        }

        [Fact]
        public void SuggestName_ComparisonIsCaseAndTrimInsensitive()
        {
            var agents = new[] { UsableAgent("x") };
            agents[0].Name = "agent 1 ";
            Assert.Equal("Agent 2", AiAgentResolver.SuggestName(agents));
        }

        [Fact]
        public void SuggestCopyName_NoConflict_AppendsCopy()
        {
            Assert.Equal("Kimi (copy)", AiAgentResolver.SuggestCopyName(Array.Empty<AiAgent>(), "Kimi"));
        }

        [Fact]
        public void SuggestCopyName_Conflicts_NumberedUp()
        {
            var agents = new[] { UsableAgent("x"), UsableAgent("y") };
            agents[0].Name = "Kimi (copy)";
            agents[1].Name = "Kimi (copy 2)";
            Assert.Equal("Kimi (copy 3)", AiAgentResolver.SuggestCopyName(agents, "Kimi"));
        }

        // ── MirrorActiveAgent (V18) ──────────────────────────────────────────

        [Fact]
        public void Mirror_ActiveAgentPresent_OverwritesFlatFields()
        {
            var agent = UsableAgent("Primary");
            agent.MaxTokens = 1234;
            agent.Temperature = 0.7;
            agent.Timeout = 45;
            agent.Retries = 4;
            var ai = new AiSettings
            {
                Provider = "stale",
                Model = "stale",
                ApiKey = "stale",
                Endpoint = "stale",
                MaxTokens = 256,
                Temperature = 0.1,
                Timeout = 5,
                Retries = 0,
            };
            ai.Agents.Add(agent);
            ai.ActiveAgentId = agent.Id;

            AiAgentResolver.MirrorActiveAgent(ai);

            Assert.Equal(agent.Provider, ai.Provider);
            Assert.Equal(agent.Model, ai.Model);
            Assert.Equal(agent.ApiKey, ai.ApiKey);
            Assert.Equal(agent.Endpoint, ai.Endpoint);
            Assert.Equal(1234, ai.MaxTokens);
            Assert.Equal(0.7, ai.Temperature);
            Assert.Equal(45, ai.Timeout);
            Assert.Equal(4, ai.Retries);
        }

        [Fact]
        public void Mirror_NothingActive_BlanksFlatStrings_KeepsRequestParams()
        {
            var agent = UsableAgent("Off");
            agent.Enabled = false;               // present but unusable → nothing active
            var ai = new AiSettings
            {
                Provider = "anthropic",
                Model = "x",
                ApiKey = "k",
                Endpoint = "e",
                MaxTokens = 1234,
                Temperature = 0.7,
                Timeout = 45,
                Retries = 4,
            };
            ai.Agents.Add(agent);
            ai.ActiveAgentId = agent.Id;

            AiAgentResolver.MirrorActiveAgent(ai);

            Assert.Equal("", ai.Provider);
            Assert.Equal("", ai.Model);
            Assert.Equal("", ai.ApiKey);
            Assert.Equal("", ai.Endpoint);
            // Request parameters keep their current values — harmless without a provider.
            Assert.Equal(1234, ai.MaxTokens);
            Assert.Equal(0.7, ai.Temperature);
            Assert.Equal(45, ai.Timeout);
            Assert.Equal(4, ai.Retries);
        }

        [Fact]
        public void Mirror_EmptyAgentList_LeavesFlatFieldsUntouched()
        {
            // The load-bearing guard (V18): flat fields with no agent list are the pre-migration
            // shape V14 rescues on the next load — blanking them here would destroy it.
            var ai = new AiSettings
            {
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "dpapi:abc",
                Endpoint = "",
                MaxTokens = 2048,
            };

            AiAgentResolver.MirrorActiveAgent(ai);

            Assert.Equal("anthropic", ai.Provider);
            Assert.Equal("claude-sonnet-4-6", ai.Model);
            Assert.Equal("dpapi:abc", ai.ApiKey);
            Assert.Equal(2048, ai.MaxTokens);
        }

        [Fact]
        public void Mirror_DoesNotTouchEnabled()
        {
            // V19 derives Enabled in Normalize only — Save persists the flag it was handed.
            var ai = new AiSettings { Enabled = false };
            ai.Agents.Add(UsableAgent("Primary"));
            ai.ActiveAgentId = ai.Agents[0].Id;
            AiAgentResolver.MirrorActiveAgent(ai);
            Assert.False(ai.Enabled);

            var blank = new AiSettings { Enabled = true };
            blank.Agents.Add(UsableAgent("Off"));
            blank.Agents[0].Enabled = false;
            AiAgentResolver.MirrorActiveAgent(blank);
            Assert.True(blank.Enabled);
        }

        // ── On-disk invariant: Save mirrors, and does not normalise ──────────

        [Fact]
        public void Save_MirrorsActiveAgentIntoFlatFieldsOnDisk()
        {
            var agent = UsableAgent("Primary");
            agent.MaxTokens = 1234;
            var settings = new AppSettings();
            // Stale flat fields that differ from the active agent in every respect.
            settings.Ai.Provider = "ollama";
            settings.Ai.Model = "stale-model";
            settings.Ai.ApiKey = "stale-key";
            settings.Ai.Endpoint = "http://stale";
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            ConfigManager.Save(settings);

            // Re-read the RAW JSON (not through Load) — the mirror must hold on disk the moment
            // the write completes, for anything that reads the file without ConfigManager.
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            var ai = doc.RootElement.GetProperty("ai");
            Assert.Equal(agent.Provider, ai.GetProperty("provider").GetString());
            Assert.Equal(agent.Model, ai.GetProperty("model").GetString());
            Assert.Equal(agent.ApiKey, ai.GetProperty("apiKey").GetString());
            Assert.Equal(agent.Endpoint, ai.GetProperty("endpoint").GetString());
            Assert.Equal(1234, ai.GetProperty("maxTokens").GetInt32());
            Assert.DoesNotContain("stale-model", json);
            Assert.DoesNotContain("stale-key", json);
        }

        [Fact]
        public void Save_DoesNotNormalize_PersistsEnabledAsHanded()
        {
            // Save must not call Normalize: the write path persists the caller's flag and the
            // next load re-derives it (V19).
            var settings = new AppSettings();
            settings.Ai.Enabled = false;
            settings.Ai.Agents.Add(UsableAgent("Primary"));
            settings.Ai.ActiveAgentId = settings.Ai.Agents[0].Id;

            ConfigManager.Save(settings);

            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("ai").GetProperty("enabled").GetBoolean());
        }

        // ── Derived Enabled (V19) ────────────────────────────────────────────

        [Fact]
        public void Normalize_DerivesEnabledFromUsability()
        {
            var usable = new AiSettings { Enabled = false };
            usable.Agents.Add(UsableAgent("One"));
            AiAgentResolver.Normalize(usable);
            Assert.True(usable.Enabled);

            var unusable = new AiSettings { Enabled = true };   // stale flag is re-derived, not trusted
            unusable.Agents.Add(UsableAgent("Off"));
            unusable.Agents[0].Enabled = false;
            AiAgentResolver.Normalize(unusable);
            Assert.False(unusable.Enabled);

            var empty = new AiSettings { Enabled = true };
            AiAgentResolver.Normalize(empty);
            Assert.False(empty.Enabled);
        }

        [Fact]
        public void Normalize_RunTwice_IsANoOp()
        {
            var ai = new AiSettings { Provider = "ollama", Model = "llama3.1" };

            AiAgentResolver.Normalize(ai);
            var id = ai.ActiveAgentId;
            var count = ai.Agents.Count;
            var enabled = ai.Enabled;

            AiAgentResolver.Normalize(ai);

            Assert.Equal(count, ai.Agents.Count);
            Assert.Equal(id, ai.ActiveAgentId);
            Assert.Equal(enabled, ai.Enabled);
        }

        // ── T015: consumers of the flat fields are unaffected by migration ───

        [Fact]
        public void MigratedConfig_ProducesFlatFieldsFactoryAcceptsUnchanged()
        {
            // T015 — a legacy ollama config migrates to one agent; the mirrored flat fields feed
            // AiProviderFactory.Create exactly as before. Ollama builds a local client — no key
            // required and no network at construction time.
            Directory.CreateDirectory(_tempRoot);
            var path = Path.Combine(_tempRoot, "legacy-ollama.json");
            File.WriteAllText(path, """
                {
                  "ai": {
                    "enabled": true,
                    "provider": "ollama",
                    "model": "llama3.1",
                    "apiKey": "",
                    "endpoint": ""
                  }
                }
                """);

            var settings = ConfigManager.Load(path);

            Assert.Single(settings.Ai.Agents);
            Assert.Equal("ollama", settings.Ai.Provider);
            Assert.Equal("llama3.1", settings.Ai.Model);
            using var client = AiProviderFactory.Create(settings.Ai);
            Assert.NotNull(client);
        }

        [Fact]
        public void Normalize_PreservesEnabledSemantics_ForCommandVisibility()
        {
            // T015 — AiCommandVisibility (src/AkmlSql.Shell.Shared/Commands/AiCommandVisibility.cs:58)
            // shows AI menu items when `settings.Ai.Enabled && PrivacyMode != "disabled"`. The
            // flag's meaning ("AI is usable") must survive the move to agents.
            var migrated = new AiSettings { Provider = "ollama", Model = "llama3.1" };
            AiAgentResolver.Normalize(migrated);
            Assert.True(migrated.Enabled);

            var empty = new AiSettings();
            AiAgentResolver.Normalize(empty);
            Assert.False(empty.Enabled);

            var disabledOnly = new AiSettings();
            disabledOnly.Agents.Add(UsableAgent("Off"));
            disabledOnly.Agents[0].Enabled = false;
            AiAgentResolver.Normalize(disabledOnly);
            Assert.False(disabledOnly.Enabled);

            // The other half of the visibility condition is none of Normalize's business.
            var privacy = new AiSettings { PrivacyMode = "disabled" };
            AiAgentResolver.Normalize(privacy);
            Assert.Equal("disabled", privacy.PrivacyMode);
        }

        // ── T090: V13 — dangling ActiveAgentId repaired ─────────────────────

        [Fact]
        public void Normalize_DanglingActiveAgentId_BecomesFirstUsableAgent()
        {
            var ai = new AiSettings { ActiveAgentId = "does-not-exist" };
            var disabled = UsableAgent("Off");
            disabled.Enabled = false;
            var first = UsableAgent("First");
            ai.Agents.Add(disabled);
            ai.Agents.Add(first);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(first.Id, ai.ActiveAgentId);           // the first USABLE agent, not the first row
            Assert.Equal(first.Provider, ai.Provider);          // the mirror follows (V18)
            Assert.True(ai.Enabled);
        }

        [Fact]
        public void Normalize_DanglingActiveAgentId_NoUsableAgent_BecomesEmpty()
        {
            var ai = new AiSettings { ActiveAgentId = "does-not-exist" };
            var off = UsableAgent("Off");
            off.Enabled = false;
            ai.Agents.Add(off);

            AiAgentResolver.Normalize(ai);

            Assert.Equal("", ai.ActiveAgentId);
            Assert.False(ai.Enabled);                           // V19 — the empty state applies
        }

        [Fact]
        public void Normalize_ActiveAgentIdNamingAPresentButDisabledAgent_IsKept()
        {
            // V13 repairs ids naming NO agent. A present-but-disabled agent is the user's own
            // state; re-pointing it at another agent would surprise.
            var ai = new AiSettings();
            var off = UsableAgent("Off");
            off.Enabled = false;
            ai.Agents.Add(off);
            ai.ActiveAgentId = off.Id;

            AiAgentResolver.Normalize(ai);

            Assert.Equal(off.Id, ai.ActiveAgentId);
        }

        [Fact]
        public void Normalize_EmptyActiveAgentId_StaysEmpty()
        {
            // "" is the product's own default, not corruption — nothing is force-activated.
            var ai = new AiSettings();
            ai.Agents.Add(UsableAgent("One"));

            AiAgentResolver.Normalize(ai);

            Assert.Equal("", ai.ActiveAgentId);
        }

        // ── T090: V16 — dangling feature assignments cleared ────────────────

        [Theory]
        [InlineData(AiFeature.Chat)]
        [InlineData(AiFeature.TextToSql)]
        [InlineData(AiFeature.Explain)]
        [InlineData(AiFeature.Fix)]
        [InlineData(AiFeature.Optimize)]
        [InlineData(AiFeature.IndexSuggestions)]
        [InlineData(AiFeature.GhostText)]
        public void Normalize_DanglingFeatureAssignment_ClearedToEmpty(AiFeature feature)
        {
            var ai = new AiSettings();
            ai.Agents.Add(UsableAgent("One"));
            SetAssignment(ai.FeatureAgents, feature, "does-not-exist");

            AiAgentResolver.Normalize(ai);

            Assert.Equal("", AiAgentResolver.AssignedIdFor(ai, feature));
        }

        [Fact]
        public void Normalize_FeatureAssignmentNamingAnUnusableAgent_ClearedToEmpty()
        {
            // V16 keys on USABLE, unlike V13: an assignment to a disabled agent reverts to
            // "use active agent" (FR-049).
            var ai = new AiSettings();
            var off = UsableAgent("Off");
            off.Enabled = false;
            ai.Agents.Add(off);
            ai.FeatureAgents.GhostText = off.Id;

            AiAgentResolver.Normalize(ai);

            Assert.Equal("", ai.FeatureAgents.GhostText);
        }

        [Fact]
        public void Normalize_UsableFeatureAssignment_Kept()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            ai.Agents.Add(agent);
            ai.FeatureAgents.Chat = agent.Id;

            AiAgentResolver.Normalize(ai);

            Assert.Equal(agent.Id, ai.FeatureAgents.Chat);
        }

        // ── T090: V17 — fallback order pruned ───────────────────────────────

        [Fact]
        public void Normalize_FallbackOrder_RemovesEntriesNamingNoUsableAgent()
        {
            var ai = new AiSettings();
            var good = UsableAgent("Good");
            var off = UsableAgent("Off");
            off.Enabled = false;
            ai.Agents.Add(good);
            ai.Agents.Add(off);
            ai.FallbackOrder.Add("does-not-exist");
            ai.FallbackOrder.Add(good.Id);
            ai.FallbackOrder.Add(off.Id);                       // present but unusable — treated as absent

            AiAgentResolver.Normalize(ai);

            Assert.Equal(new[] { good.Id }, ai.FallbackOrder);
        }

        [Fact]
        public void Normalize_FallbackOrder_RemovesDuplicatesKeepingTheFirst()
        {
            var ai = new AiSettings();
            var a = UsableAgent("A");
            var b = UsableAgent("B");
            ai.Agents.Add(a);
            ai.Agents.Add(b);
            ai.FallbackOrder.Add(a.Id);
            ai.FallbackOrder.Add(b.Id);
            ai.FallbackOrder.Add(a.Id);                         // duplicate — dropped, survivors keep their order

            AiAgentResolver.Normalize(ai);

            Assert.Equal(new[] { a.Id, b.Id }, ai.FallbackOrder);
        }

        [Fact]
        public void Normalize_FallbackOrder_RemovesTheActiveAgentsOwnId()
        {
            var ai = new AiSettings();
            var active = UsableAgent("Active");
            var other = UsableAgent("Other");
            ai.Agents.Add(active);
            ai.Agents.Add(other);
            ai.ActiveAgentId = active.Id;
            ai.FallbackOrder.Add(active.Id);                    // an agent is never its own fallback
            ai.FallbackOrder.Add(other.Id);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(new[] { other.Id }, ai.FallbackOrder);
        }

        [Fact]
        public void Normalize_RepairsAreIdempotent()
        {
            var ai = new AiSettings { ActiveAgentId = "dangling" };
            var usable = UsableAgent("One");
            var off = UsableAgent("Off");
            off.Enabled = false;
            ai.Agents.Add(usable);
            ai.Agents.Add(off);
            ai.FeatureAgents.Chat = "dangling";
            ai.FallbackOrder.Add("dangling");
            ai.FallbackOrder.Add(usable.Id);
            ai.FallbackOrder.Add(usable.Id);

            AiAgentResolver.Normalize(ai);
            var agents = ai.Agents.Count;
            var active = ai.ActiveAgentId;
            var chat = ai.FeatureAgents.Chat;
            var fallback = string.Join(",", ai.FallbackOrder);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(agents, ai.Agents.Count);
            Assert.Equal(active, ai.ActiveAgentId);
            Assert.Equal(chat, ai.FeatureAgents.Chat);
            Assert.Equal(fallback, string.Join(",", ai.FallbackOrder));
        }

        // ── T091: V15 — malformed agents dropped, the rest load ─────────────

        [Fact]
        public void Normalize_AgentWithEmptyId_IsDropped_TheRestSurvive()
        {
            var ai = new AiSettings();
            var good = UsableAgent("Good");
            ai.Agents.Add(good);
            var bad = UsableAgent("Bad");
            bad.Id = "";
            ai.Agents.Add(bad);

            var warnings = CaptureWarnings(() => AiAgentResolver.Normalize(ai));

            Assert.Single(ai.Agents);
            Assert.Same(good, ai.Agents[0]);
            Assert.Contains(warnings, w => w.Contains("ai.agents[1]"));   // the warning names the index
        }

        [Fact]
        public void Normalize_NullAgentEntry_IsDropped_TheRestSurvive()
        {
            // JSON `null` inside ai.agents deserializes to a null list entry — the representable
            // form of V15's "unparseable entry".
            var ai = new AiSettings();
            ai.Agents.Add(null!);
            var good = UsableAgent("Good");
            ai.Agents.Add(good);

            var warnings = CaptureWarnings(() => AiAgentResolver.Normalize(ai));

            Assert.Single(ai.Agents);
            Assert.Same(good, ai.Agents[0]);
            Assert.Contains(warnings, w => w.Contains("ai.agents[0]"));
        }

        [Fact]
        public void Normalize_AgentWithDuplicateId_IsDropped_KeepingTheFirst()
        {
            var ai = new AiSettings();
            var first = UsableAgent("First");
            ai.Agents.Add(first);
            var dupe = UsableAgent("Dupe");
            dupe.Id = first.Id;
            ai.Agents.Add(dupe);
            ai.ActiveAgentId = first.Id;

            var warnings = CaptureWarnings(() => AiAgentResolver.Normalize(ai));

            Assert.Single(ai.Agents);
            Assert.Same(first, ai.Agents[0]);                   // the first occurrence wins
            Assert.Equal(first.Id, ai.ActiveAgentId);           // still valid — V13 must not fire
            Assert.Contains(warnings, w => w.Contains("ai.agents[1]"));
        }

        [Fact]
        public void Load_MalformedAgentEntries_TheRestLoad_AndTheLogNamesThem()
        {
            // Quickstart 66, config-file level: hand-edited garbage entries do not lock the user
            // out — the well-formed agents load and the log names each dropped index.
            Directory.CreateDirectory(_tempRoot);
            var path = Path.Combine(_tempRoot, "malformed-agents.json");
            File.WriteAllText(path, """
                {
                  "ai": {
                    "agents": [
                      { "id": "0123456789abcdef0123456789abcdef", "name": "Good",
                        "provider": "anthropic", "model": "claude-sonnet-4-6", "apiKey": "dpapi:k" },
                      { "id": "", "name": "Broken" },
                      null
                    ],
                    "activeAgentId": "0123456789abcdef0123456789abcdef"
                  }
                }
                """);

            AppSettings settings = null!;
            var warnings = CaptureWarnings(() => settings = ConfigManager.Load(path));

            var agent = Assert.Single(settings.Ai.Agents);
            Assert.Equal("Good", agent.Name);
            Assert.Equal(agent.Id, settings.Ai.ActiveAgentId);
            Assert.True(settings.Ai.Enabled);
            Assert.Contains(warnings, w => w.Contains("ai.agents[1]"));
            Assert.Contains(warnings, w => w.Contains("ai.agents[2]"));
        }

        // ── T091: V20 — unknown health status ───────────────────────────────

        [Fact]
        public void Normalize_UnknownHealthStatus_BecomesUnknown()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            agent.Health = new AgentHealth { Status = "banana" };
            ai.Agents.Add(agent);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(AgentHealthStatus.Unknown, agent.Health.Status);
        }

        [Fact]
        public void Normalize_NullHealthStatus_BecomesUnknown()
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            agent.Health = new AgentHealth { Status = null! };
            ai.Agents.Add(agent);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(AgentHealthStatus.Unknown, agent.Health.Status);
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("ready")]
        [InlineData("needsKey")]
        [InlineData("failed")]
        [InlineData("Ready")]                                   // compared case-insensitively on read — not "outside the four"
        public void Normalize_KnownHealthStatus_Kept(string status)
        {
            var ai = new AiSettings();
            var agent = UsableAgent("One");
            agent.Health = new AgentHealth { Status = status };
            ai.Agents.Add(agent);

            AiAgentResolver.Normalize(ai);

            Assert.Equal(status, agent.Health.Status);
        }

        [Fact]
        public void Normalize_NullHealth_StaysNull()
        {
            var ai = new AiSettings();
            ai.Agents.Add(UsableAgent("One"));                  // Health == null ≡ never tested

            AiAgentResolver.Normalize(ai);

            Assert.Null(ai.Agents[0].Health);
        }

        // ── T091: V21 — the 20-agent ceiling ────────────────────────────────

        [Fact]
        public void Normalize_AgentsBeyondTheTwentieth_AreDropped_WithWarningsNamingTheIndexes()
        {
            var ai = new AiSettings();
            for (var i = 1; i <= 23; i++)
                ai.Agents.Add(UsableAgent("Agent " + i));
            var twentieth = ai.Agents[19];

            var warnings = CaptureWarnings(() => AiAgentResolver.Normalize(ai));

            Assert.Equal(AiAgentResolver.MaxAgents, ai.Agents.Count);
            Assert.Same(twentieth, ai.Agents[19]);              // the first twenty survive in order
            Assert.Contains(warnings, w => w.Contains("ai.agents[20]"));
            Assert.Contains(warnings, w => w.Contains("ai.agents[21]"));
            Assert.Contains(warnings, w => w.Contains("ai.agents[22]"));
        }

        // ── log capture (V15/V21 warnings) ──────────────────────────────────

        /// <summary>
        /// Runs <paramref name="body"/> with Serilog's static logger swapped for an in-memory
        /// sink and returns the rendered messages. Assertions only ever test for containment, so
        /// unrelated warnings from tests running in parallel cannot make them fail.
        /// </summary>
        private static List<string> CaptureWarnings(Action body)
        {
            var sink = new ListSink();
            var prior = Log.Logger;
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            try
            {
                body();
            }
            finally
            {
                Log.Logger = prior;
            }
            return sink.Messages();
        }

        private sealed class ListSink : ILogEventSink
        {
            private readonly List<string> _messages = new();

            public void Emit(LogEvent logEvent)
            {
                lock (_messages) _messages.Add(logEvent.RenderMessage());
            }

            public List<string> Messages()
            {
                lock (_messages) return new List<string>(_messages);
            }
        }
    }
}
