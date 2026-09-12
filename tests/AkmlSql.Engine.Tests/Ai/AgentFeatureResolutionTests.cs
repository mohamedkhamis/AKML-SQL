using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Handlers.Ai;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Microsoft.Extensions.AI;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Engine.Tests.Ai;

// Spec 037 (US4) T064–T066 — per-feature agent resolution in AiHandlerBase
// (contracts/agent-resolution.md Part 1). Resolution happens once in the handler base, BEFORE
// the consent gate; each handler declares its AiFeature. The provider calls are intercepted by
// the AiPipelineServices.ClientFactory seam, which records the (projected) settings each
// attempt was constructed from — the observable proof of WHICH agent served the request.
public class AgentFeatureResolutionTests
{
    // ── Fakes and builders ─────────────────────────────────────────────────

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _text;
        public FakeChatClient(string text = "PURPOSE: ok") => _text = text;
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Records the provider/model of every (projected) settings a client was built from.</summary>
    private sealed class RecordingFactory
    {
        public readonly List<(string Provider, string Model)> Constructed = new();
        public IChatClient Create(AiSettings settings)
        {
            Constructed.Add((settings.Provider ?? "", settings.Model ?? ""));
            return new FakeChatClient();
        }
    }

    private sealed class ListSink : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    private static AiAgent Agent(string name, string provider, string model,
        string key = "", string endpoint = "")
    {
        return new AiAgent
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Provider = provider,
            Model = model,
            ApiKey = key,
            Endpoint = endpoint,
            Enabled = true,
        };
    }

    /// <summary>One usable, distinct agent per feature, plus a distinct active agent.</summary>
    private sealed class FeatureAgents
    {
        public readonly AiAgent Active = Agent("Active Agent", "ollama", "llama3.1");
        public readonly AiAgent Chat = Agent("Chat Agent", "anthropic", "claude-sonnet-4-6", key: "k");
        public readonly AiAgent TextToSql = Agent("T2S Agent", "openai", "gpt-4o", key: "k");
        public readonly AiAgent Explain = Agent("Explain Agent", "gemini", "gemini-flash-latest", key: "k");
        public readonly AiAgent Fix = Agent("Fix Agent", "kimi", "moonshot-v1-8k", key: "k");
        public readonly AiAgent Optimize = Agent("Optimize Agent", "ollama", "llama3.2");
        public readonly AiAgent Index = Agent("Index Agent", "lmstudio", "qwen2.5-coder");
        public readonly AiAgent Ghost = Agent("Ghost Agent", "custom", "fast-model",
            endpoint: "http://127.0.0.1:9/");

        public IEnumerable<AiAgent> All()
        {
            yield return Active;
            yield return Chat;
            yield return TextToSql;
            yield return Explain;
            yield return Fix;
            yield return Optimize;
            yield return Index;
            yield return Ghost;
        }

        public AiAgent For(AiFeature feature) => feature switch
        {
            AiFeature.Chat => Chat,
            AiFeature.TextToSql => TextToSql,
            AiFeature.Explain => Explain,
            AiFeature.Fix => Fix,
            AiFeature.Optimize => Optimize,
            AiFeature.IndexSuggestions => Index,
            AiFeature.GhostText => Ghost,
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };
    }

    private static readonly AiFeature[] AllFeatures =
    {
        AiFeature.Chat, AiFeature.TextToSql, AiFeature.Explain, AiFeature.Fix,
        AiFeature.Optimize, AiFeature.IndexSuggestions, AiFeature.GhostText,
    };

    private static AiSettings SettingsWith(FeatureAgents agents, bool assignEveryFeature = true)
    {
        var settings = new AiSettings
        {
            Enabled = true,
            InlineCompletion = true,   // ghost text's own toggle — not the resolution under test
            PrivacyConsentRequired = false,   // consent GIVEN — the consent gate has its own tests
            ActiveAgentId = agents.Active.Id,
        };
        foreach (var agent in agents.All()) settings.Agents.Add(agent);
        if (assignEveryFeature)
        {
            settings.FeatureAgents.Chat = agents.Chat.Id;
            settings.FeatureAgents.TextToSql = agents.TextToSql.Id;
            settings.FeatureAgents.Explain = agents.Explain.Id;
            settings.FeatureAgents.Fix = agents.Fix.Id;
            settings.FeatureAgents.Optimize = agents.Optimize.Id;
            settings.FeatureAgents.IndexSuggestions = agents.Index.Id;
            settings.FeatureAgents.GhostText = agents.Ghost.Id;
        }
        return settings;
    }

    private static RpcContext NewContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        ParserService = new TsqlParserService(),
        SettingsLoader = () => new AppSettings(),
    };

    private static AiPipelineServices ServicesFor(AiSettings settings, RecordingFactory factory)
    {
        var svcs = AiPipelineServices.Build(
            new SchemaCacheManager(), new TsqlParserService(), () => settings);
        svcs.ClientFactory = factory.Create;
        return svcs;
    }

    /// <summary>Runs one request for <paramref name="feature"/> through its REAL handler.</summary>
    private static async Task<(bool Success, string Error, string? AgentName)> RunAsync(
        AiFeature feature, AiPipelineServices svcs)
    {
        var ctx = NewContext();
        switch (feature)
        {
            case AiFeature.Chat:
            {
                var r = await new AiChatHandler(svcs).HandleAsync(
                    new AiChatRequest { SessionId = "s", Message = "hi" }, ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", r.AgentName);
            }
            case AiFeature.TextToSql:
            {
                var r = await new AiTextToSqlHandler(svcs).HandleAsync(
                    new AiTextToSqlRequest { SessionId = "s", Prompt = "list users" }, ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            case AiFeature.Explain:
            {
                var r = await new AiExplainHandler(svcs).HandleAsync(
                    new AiExplainRequest { SessionId = "s", SelectedSql = "select 1" }, ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            case AiFeature.Fix:
            {
                var r = await new AiFixHandler(svcs).HandleAsync(
                    new AiFixRequest { SessionId = "s", FailingSql = "select 1", ErrorMessage = "boom" },
                    ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            case AiFeature.Optimize:
            {
                var r = await new AiOptimizeHandler(svcs).HandleAsync(
                    new AiOptimizeRequest { SessionId = "s", SelectedSql = "select 1" }, ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            case AiFeature.IndexSuggestions:
            {
                var r = await new AiIndexAnalysisHandler(svcs).HandleAsync(
                    new AiIndexAnalysisRequest { SessionId = "s", SelectedSql = "select 1" },
                    ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            case AiFeature.GhostText:
            {
                var r = await new AiGhostTextHandler(svcs).HandleAsync(
                    new AiGhostTextRequest { SessionId = "s", PrecedingText = "select ", CursorOffset = 7 },
                    ctx, CancellationToken.None);
                return (r.Success, r.ErrorMessage ?? "", null);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(feature));
        }
    }

    // ── T064: assigned agent wins; unassigned follows active; active change moves ──

    [Fact]
    public async Task Each_feature_resolves_to_its_assigned_agent()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        foreach (var feature in AllFeatures)
        {
            factory.Constructed.Clear();
            var (success, error, agentName) = await RunAsync(feature, svcs);

            var assigned = agents.For(feature);
            Assert.True(success, $"{feature}: request failed: {error}");
            // FR-048: the client was constructed from the ASSIGNED agent's projection.
            Assert.Equal((assigned.Provider, assigned.Model), Assert.Single(factory.Constructed));
            if (feature == AiFeature.Chat)
            {
                // FR-052: the answer is attributed to the agent that produced it.
                Assert.Equal(assigned.Name, agentName);
            }
        }
    }

    [Fact]
    public async Task Unassigned_features_follow_the_active_agent()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        foreach (var feature in AllFeatures)
        {
            factory.Constructed.Clear();
            var (success, error, _) = await RunAsync(feature, svcs);

            Assert.True(success, $"{feature}: request failed: {error}");
            // FR-047: no assignment — the ACTIVE agent serves the feature.
            Assert.Equal((agents.Active.Provider, agents.Active.Model), Assert.Single(factory.Constructed));
        }
    }

    [Fact]
    public async Task An_active_agent_change_moves_unassigned_features_without_a_restart()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var handler = new AiChatHandler(svcs);   // one handler instance, as registered in the engine
        var first = await handler.HandleAsync(
            new AiChatRequest { SessionId = "s", Message = "hi" }, NewContext(), CancellationToken.None);
        Assert.True(first.Success);
        Assert.Equal(agents.Active.Model, factory.Constructed[^1].Model);

        // FR-047: settings are read fresh per request — flipping the active agent moves every
        // unassigned feature with no engine restart.
        var promoted = agents.Chat;
        settings.ActiveAgentId = promoted.Id;
        var second = await handler.HandleAsync(
            new AiChatRequest { SessionId = "s", Message = "again" }, NewContext(), CancellationToken.None);

        Assert.True(second.Success);
        Assert.Equal((promoted.Provider, promoted.Model), factory.Constructed[^1]);
        Assert.Equal(promoted.Name, second.AgentName);
    }

    // ── T065: the ordering — consent is evaluated against the RESOLVED provider ──

    [Fact]
    public async Task A_cloud_agent_assigned_to_a_feature_trips_consent_even_when_the_active_is_local()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents);          // active = ollama (local, allow-listed)
        settings.PrivacyConsentRequired = true;        // consent withheld
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var (success, error, _) = await RunAsync(AiFeature.Chat, svcs);

        // The assigned agent is anthropic: consent must be evaluated against IT, not against
        // the local active agent. Projecting AFTER the gate would let this slip through.
        Assert.False(success);
        Assert.StartsWith("CONSENT_REQUIRED:", error);
        Assert.Contains("anthropic", error);
        Assert.Empty(factory.Constructed);   // the gate fires before any client is built
    }

    [Fact]
    public async Task A_local_assigned_agent_passes_the_consent_gate()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        settings.PrivacyConsentRequired = true;        // consent withheld — local providers skip it
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var (success, error, _) = await RunAsync(AiFeature.Chat, svcs);

        Assert.True(success, error);
        Assert.Single(factory.Constructed);
    }

    // ── T066: missing/disabled assigned agent → active + one notice per feature (V22) ──

    private static List<LogEvent> FallbackNotices(ListSink sink)
    {
        lock (sink.Events)
        {
            return sink.Events
                .Where(e => e.Level == LogEventLevel.Information
                            && e.RenderMessage().Contains("falling back to the active agent"))
                .ToList();
        }
    }

    [Fact]
    public async Task A_cleared_assignment_falls_back_to_the_active_agent_and_notices_once_per_feature()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        var dangling = new string('d', 32);
        settings.FeatureAgents.Chat = dangling;   // no agent with this id exists
        settings.FeatureAgents.Fix = dangling;
        // Production shape: settings arrive NORMALIZED — V16 has already cleared the dangling
        // assignments and recorded the features as the post-load signal for the V22 notice.
        AiAgentResolver.Normalize(settings);
        Assert.Equal("", settings.FeatureAgents.Chat);
        Assert.Equal("", settings.FeatureAgents.Fix);
        Assert.Contains(nameof(AiFeature.Chat), settings.ClearedFeatureAssignments);
        Assert.Contains(nameof(AiFeature.Fix), settings.ClearedFeatureAssignments);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // FR-049: two requests for the SAME feature — the active agent serves both…
            var first = await RunAsync(AiFeature.Chat, svcs);
            var second = await RunAsync(AiFeature.Chat, svcs);
            // …and a DIFFERENT feature with a cleared assignment gets its own notice.
            var fix = await RunAsync(AiFeature.Fix, svcs);

            Assert.True(first.Success, first.Error);
            Assert.True(second.Success, second.Error);
            Assert.True(fix.Success, fix.Error);
        }
        finally
        {
            Log.Logger = previous;
        }

        // Every attempt ran against the ACTIVE agent's projection.
        Assert.Equal(3, factory.Constructed.Count);
        Assert.All(factory.Constructed, c => Assert.Equal((agents.Active.Provider, agents.Active.Model), c));

        // V22: logged at Information ONCE per feature per engine process — the second chat
        // request produced no further notice; the fix request produced its own.
        var notices = FallbackNotices(sink);
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, n => n.RenderMessage().Contains("Chat"));
        Assert.Contains(notices, n => n.RenderMessage().Contains("Fix"));
    }

    [Fact]
    public async Task A_disabled_assigned_agent_falls_back_the_same_way()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        agents.Chat.Enabled = false;              // present but disabled ≡ absent (S1)
        settings.FeatureAgents.Chat = agents.Chat.Id;
        // Production shape: Normalize clears the disabled agent's assignment (V16) and records it.
        AiAgentResolver.Normalize(settings);
        Assert.Equal("", settings.FeatureAgents.Chat);
        Assert.Contains(nameof(AiFeature.Chat), settings.ClearedFeatureAssignments);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var first = await RunAsync(AiFeature.Chat, svcs);
            var second = await RunAsync(AiFeature.Chat, svcs);
            Assert.True(first.Success, first.Error);
            Assert.True(second.Success, second.Error);
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.All(factory.Constructed, c => Assert.Equal((agents.Active.Provider, agents.Active.Model), c));
        Assert.Single(FallbackNotices(sink));   // one notice across both requests (V22)
    }

    [Fact]
    public async Task A_dangling_assignment_in_unnormalized_settings_still_notices_once_per_feature()
    {
        // Legacy signal: a host may hand the handler settings that never went through
        // Normalize (tests, a caller that assembled them by hand) — the dangling assigned id
        // is still present, and the notice keys on it directly.
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        settings.FeatureAgents.Chat = new string('d', 32);   // no agent with this id exists
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var first = await RunAsync(AiFeature.Chat, svcs);
            var second = await RunAsync(AiFeature.Chat, svcs);
            Assert.True(first.Success, first.Error);
            Assert.True(second.Success, second.Error);
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.All(factory.Constructed, c => Assert.Equal((agents.Active.Provider, agents.Active.Model), c));
        Assert.Single(FallbackNotices(sink));
    }

    [Fact]
    public async Task A_consent_killed_request_does_not_burn_the_once_per_feature_notice()
    {
        var agents = new FeatureAgents();
        var settings = SettingsWith(agents, assignEveryFeature: false);
        settings.FeatureAgents.Chat = new string('d', 32);
        settings.ActiveAgentId = agents.Chat.Id;   // the fallback target is CLOUD (anthropic)
        settings.PrivacyConsentRequired = true;     // consent withheld
        AiAgentResolver.Normalize(settings);
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // The consent gate kills the request BEFORE the notice…
            var blocked = await RunAsync(AiFeature.Chat, svcs);
            Assert.False(blocked.Success);
            Assert.StartsWith("CONSENT_REQUIRED:", blocked.Error);
            Assert.Empty(factory.Constructed);
            Assert.Empty(FallbackNotices(sink));

            // …so the flag is not burned: once consent is granted the notice still fires, once.
            settings.PrivacyConsentRequired = false;
            var allowed1 = await RunAsync(AiFeature.Chat, svcs);
            var allowed2 = await RunAsync(AiFeature.Chat, svcs);
            Assert.True(allowed1.Success, allowed1.Error);
            Assert.True(allowed2.Success, allowed2.Error);
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.Single(FallbackNotices(sink));
    }

    [Fact]
    public async Task When_no_active_agent_is_usable_the_notice_says_no_fallback_is_available()
    {
        var settings = new AiSettings
        {
            PrivacyConsentRequired = false,
            FeatureAgents = { Chat = new string('d', 32) },
        };
        AiAgentResolver.Normalize(settings);   // V16 clears + records; V19: Enabled = false
        var factory = new RecordingFactory();
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var (success, error, _) = await RunAsync(AiFeature.Chat, svcs);
            Assert.False(success);
            Assert.Equal("AI assistance is disabled", error);
        }
        finally
        {
            Log.Logger = previous;
        }

        // The assigned agent is unusable AND there is no active agent to fall back to — the
        // log must say so instead of naming a fallback that does not exist.
        List<LogEvent> events;
        lock (sink.Events) events = sink.Events.ToList();
        Assert.Single(events, e => e.RenderMessage().Contains("no fallback is available"));
        Assert.DoesNotContain(events, e => e.RenderMessage().Contains("falling back to the active agent"));
    }

    [Fact]
    public void MarkAssignmentFallbackNoticed_admits_exactly_one_notice_per_feature_under_parallel_calls()
    {
        var svcs = ServicesFor(new AiSettings(), new RecordingFactory());
        var admitted = new ConcurrentBag<bool>();
        Parallel.For(0, 64, _ => admitted.Add(svcs.MarkAssignmentFallbackNoticed(AiFeature.Chat)));

        Assert.Equal(1, admitted.Count(first => first));
        // A different feature gets its own single admission.
        Assert.True(svcs.MarkAssignmentFallbackNoticed(AiFeature.Fix));
        Assert.False(svcs.MarkAssignmentFallbackNoticed(AiFeature.Fix));
    }
}
