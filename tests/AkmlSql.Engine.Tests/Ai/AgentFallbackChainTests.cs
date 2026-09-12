using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Microsoft.Extensions.AI;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Engine.Tests.Ai;

// Spec 037 (US4) T067 — the fallback chain in AiPipelineServices.ExecuteWithFallbackAsync
// (contracts/agent-resolution.md Part 2): the user's FallbackOrder is walked after the
// selected agent, the offline provider stays last, the chain stops at the first success, and
// cancellation / privacy consent NEVER trigger a fallback. Provider construction and the
// offline construction go through the ClientFactory / FallbackClientFactory seams, so the
// order of attempts is observable without any network.
public class AgentFallbackChainTests
{
    // ── Fakes and builders ─────────────────────────────────────────────────

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _text;
        public FakeChatClient(string text) => _text = text;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _text)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _failure;
        public ThrowingChatClient(Exception failure) => _failure = failure;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(_failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static HttpRequestException Boom() => new("boom");

    /// <summary>Builds a client per model id, recording provider/model/timeout of every attempt.</summary>
    private sealed class ScriptedFactory
    {
        public readonly List<(string Provider, string Model, int Timeout)> Constructed = new();
        private readonly Dictionary<string, Func<IChatClient>> _byModel = new();

        public ScriptedFactory On(string model, Func<IChatClient> make)
        {
            _byModel[model] = make;
            return this;
        }

        public IChatClient Create(AiSettings settings)
        {
            Constructed.Add((settings.Provider ?? "", settings.Model ?? "", settings.Timeout));
            return _byModel.TryGetValue(settings.Model ?? "", out var make)
                ? make()
                : new FakeChatClient("ok");
        }
    }

    private static AiAgent Agent(string name, string model, string provider = "ollama")
    {
        return new AiAgent
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Provider = provider,
            Model = model,
            Enabled = true,
        };
    }

    private sealed class Chain
    {
        public readonly AiAgent Selected = Agent("Selected", "selected-model");
        public readonly AiAgent First = Agent("First", "first-model");
        public readonly AiAgent Second = Agent("Second", "second-model");
        public readonly AiAgent Third = Agent("Third", "third-model");

        public AiSettings Settings()
        {
            var settings = new AiSettings
            {
                Enabled = true,
                ActiveAgentId = Selected.Id,
                OfflineProvider = "ollama",
                OfflineModel = "offline-model",
                OfflineEndpoint = "http://127.0.0.1:1/",
            };
            settings.Agents.Add(Selected);
            settings.Agents.Add(First);
            settings.Agents.Add(Second);
            settings.Agents.Add(Third);
            settings.FallbackOrder.Add(First.Id);
            settings.FallbackOrder.Add(Second.Id);
            settings.FallbackOrder.Add(Third.Id);
            return settings;
        }
    }

    private static readonly List<ChatMessage> Messages = new() { new(ChatRole.User, "hi") };
    private static readonly ChatOptions Options = new();

    private static AiPipelineServices ServicesFor(AiSettings settings, ScriptedFactory factory,
        Func<AiSettings, IChatClient>? offlineFactory = null)
    {
        var svcs = AiPipelineServices.Build(
            new SchemaCacheManager(), new TsqlParserService(), () => settings);
        svcs.ClientFactory = factory.Create;
        if (offlineFactory != null) svcs.FallbackClientFactory = offlineFactory;
        return svcs;
    }

    private static Task<(ChatResponse Response, bool UsedFallback, string? AgentName)> RunAsync(
        AiPipelineServices svcs, AiSettings settings, AiAgent? selected)
    {
        // Production shape: the handler base hands the pipeline the SELECTED agent's projection.
        var effective = selected == null ? settings : AiAgentResolver.Project(settings, selected);
        return svcs.ExecuteWithFallbackAsync(effective, selected, Messages, Options, CancellationToken.None);
    }

    // ── Order, stop-at-first-success, attribution ──────────────────────────

    [Fact]
    public async Task Chain_candidates_are_tried_in_order_and_the_first_success_wins()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(Boom()))
            .On("second-model", () => new FakeChatClient("from-second"));
        var offlineCalled = 0;
        var svcs = ServicesFor(settings, factory, _ => { offlineCalled++; return new FakeChatClient("offline"); });

        var (response, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal("Second", agentName);                       // FR-052: who ACTUALLY answered
        Assert.Equal("from-second", response.Text);
        Assert.Equal(
            new[] { ("ollama", "selected-model", 30), ("ollama", "first-model", 30), ("ollama", "second-model", 30) },
            factory.Constructed);                                 // "Third" never constructed
        Assert.Equal(0, offlineCalled);                           // the chain stopped before the offline resort
    }

    [Fact]
    public async Task A_successful_primary_never_touches_the_chain()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new FakeChatClient("from-selected"));
        var svcs = ServicesFor(settings, factory);

        var (response, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.False(usedFallback);
        Assert.Equal("Selected", agentName);
        Assert.Equal("from-selected", response.Text);
        Assert.Equal(("ollama", "selected-model", 30), Assert.Single(factory.Constructed));
    }

    [Fact]
    public async Task The_selected_agent_never_appears_in_its_own_chain()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        settings.FallbackOrder.Insert(0, chain.Selected.Id);   // a hand-edited self-reference
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory);

        var (_, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal("First", agentName);
        // The selected agent was tried exactly once — its own chain entry was skipped.
        Assert.Single(factory.Constructed, c => c.Model == "selected-model");
        Assert.Equal("first-model", factory.Constructed[1].Model);
    }

    [Fact]
    public async Task Unusable_chain_candidates_are_skipped()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        chain.First.Enabled = false;                           // disabled ≡ absent (S1)
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory);

        var (_, _, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.Equal("Second", agentName);
        Assert.DoesNotContain(factory.Constructed, c => c.Model == "first-model");
    }

    // ── Cancellation and consent never fall back ───────────────────────────

    [Fact]
    public async Task Cancellation_never_falls_back()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(new OperationCanceledException()));
        var svcs = ServicesFor(settings, factory);

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync(svcs, settings, chain.Selected));

        Assert.Equal(("ollama", "selected-model", 30), Assert.Single(factory.Constructed));
    }

    [Fact]
    public async Task Consent_never_falls_back()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(new PrivacyConsentRequiredException("CONSENT_REQUIRED:x")));
        var svcs = ServicesFor(settings, factory);

        await Assert.ThrowsAsync<PrivacyConsentRequiredException>(() => RunAsync(svcs, settings, chain.Selected));

        Assert.Equal(("ollama", "selected-model", 30), Assert.Single(factory.Constructed));
    }

    // ── The offline provider stays last ────────────────────────────────────

    [Fact]
    public async Task The_offline_provider_is_the_last_resort_after_the_chain()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(Boom()))
            .On("second-model", () => new ThrowingChatClient(Boom()))
            .On("third-model", () => new ThrowingChatClient(Boom()));
        var offlineCalled = 0;
        var svcs = ServicesFor(settings, factory, _ => { offlineCalled++; return new FakeChatClient("offline-ok"); });

        var (response, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal(AiProviderIds.DisplayName(settings.OfflineProvider), agentName);
        Assert.Equal("offline-ok", response.Text);
        Assert.Equal(1, offlineCalled);
        Assert.Equal(4, factory.Constructed.Count);   // selected + all three chain candidates first
    }

    [Fact]
    public async Task A_config_with_only_an_offline_provider_behaves_as_before()
    {
        // Legacy shape: flat fields, no agents, no fallback order — FR-051 parity.
        var settings = new AiSettings
        {
            Enabled = true,
            Provider = "ollama",
            Model = "legacy-model",
            OfflineProvider = "ollama",
            OfflineModel = "offline-model",
            OfflineEndpoint = "http://127.0.0.1:1/",
        };
        var factory = new ScriptedFactory()
            .On("legacy-model", () => new ThrowingChatClient(Boom()));
        var offlineCalled = 0;
        var svcs = ServicesFor(settings, factory, _ => { offlineCalled++; return new FakeChatClient("offline-ok"); });

        var (response, usedFallback, agentName) = await RunAsync(svcs, settings, selected: null);

        Assert.True(usedFallback);
        Assert.Equal(AiProviderIds.DisplayName("ollama"), agentName);
        Assert.Equal("offline-ok", response.Text);
        Assert.Equal(1, offlineCalled);
    }

    [Fact]
    public async Task When_everything_fails_the_aggregate_names_every_attempt()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(new HttpRequestException("selected down")))
            .On("first-model", () => new ThrowingChatClient(new HttpRequestException("first down")))
            .On("second-model", () => new ThrowingChatClient(new HttpRequestException("second down")))
            .On("third-model", () => new ThrowingChatClient(new HttpRequestException("third down")));
        var svcs = ServicesFor(settings, factory,
            _ => new ThrowingChatClient(new HttpRequestException("offline down")));

        var ex = await Assert.ThrowsAsync<AggregateException>(() => RunAsync(svcs, settings, chain.Selected));

        // The user sees the outcome of the chain — a concise summary naming every attempt in
        // order (the primary by its agent name, FR-057)…
        Assert.Contains("All 5 AI providers failed", ex.Message);
        Assert.Contains("Selected → First → Second → Third → Ollama", ex.Message);
        // …with the per-attempt detail carried ONCE, on the inner exceptions (the aggregate
        // message re-appends them) — no duplicated wall of text.
        Assert.Contains("Agent 'Selected' failed", ex.Message);
        Assert.Contains("Fallback agent 'First' failed", ex.Message);
        Assert.Contains("Fallback agent 'Second' failed", ex.Message);
        Assert.Contains("Fallback agent 'Third' failed", ex.Message);
        Assert.Contains("Offline fallback 'Ollama' failed", ex.Message);
        // FR-058: raw provider bodies are sanitized to the taxonomy before they reach the
        // user-facing message; the raw exceptions ride inside the inners for diagnosis.
        Assert.DoesNotContain("selected down", ex.Message);
        Assert.All(ex.InnerExceptions,
            inner => Assert.IsType<HttpRequestException>(inner.InnerException));
    }

    [Fact]
    public async Task A_primary_failure_with_no_fallback_configured_propagates_untouched()
    {
        // Parity with the pre-chain shape: nothing to fall back to — the original exception
        // propagates, not an aggregate.
        var settings = new AiSettings { Enabled = true, Provider = "ollama", Model = "legacy-model" };
        var factory = new ScriptedFactory()
            .On("legacy-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory);

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(svcs, settings, selected: null));
    }

    // ── Per-attempt timeout bounding ───────────────────────────────────────

    [Fact]
    public async Task Each_attempt_is_bounded_by_its_own_agents_timeout()
    {
        // A 5 s agent must not inherit a 300 s agent's budget (contract invariant 2).
        Assert.Equal(TimeSpan.FromSeconds(30), AiPipelineServices.RetryBudgetFor(new AiSettings { Timeout = 5 }));
        Assert.Equal(TimeSpan.FromSeconds(300), AiPipelineServices.RetryBudgetFor(new AiSettings { Timeout = 300 }));

        var chain = new Chain();
        var settings = chain.Settings();
        chain.Selected.Timeout = 300;
        chain.First.Timeout = 5;
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new FakeChatClient("fast-ok"));
        var svcs = ServicesFor(settings, factory);

        var (_, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal("First", agentName);
        // Each attempt ran against its OWN agent's projection — the recorded timeouts differ.
        Assert.Equal(
            new[] { ("ollama", "selected-model", 300), ("ollama", "first-model", 5) },
            factory.Constructed.Take(2).ToArray());
    }

    // ── Consent is evaluated against every chain candidate (HIGH review finding) ──

    private sealed class ListSink : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    [Fact]
    public async Task A_cloud_candidate_without_consent_is_never_called_and_the_chain_walks_on()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        settings.PrivacyConsentRequired = true;              // consent WITHHELD
        chain.First.Provider = "anthropic";                  // a cloud candidate…
        chain.First.ApiKey = "sk-test-key";                  // …kept usable, so ONLY consent can skip it
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("second-model", () => new ThrowingChatClient(Boom()))
            .On("third-model", () => new FakeChatClient("from-third"));
        var svcs = ServicesFor(settings, factory);

        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        (ChatResponse Response, bool UsedFallback, string? AgentName) result;
        try
        {
            result = await RunAsync(svcs, settings, chain.Selected);
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.True(result.UsedFallback);
        Assert.Equal("Third", result.AgentName);
        Assert.Equal("from-third", result.Response.Text);
        // The non-consented cloud candidate was never even constructed — no data left for it…
        Assert.DoesNotContain(factory.Constructed, c => c.Provider == "anthropic");
        // …but the local candidates were still tried, in order.
        Assert.Equal(
            new[] { ("ollama", "selected-model", 30), ("ollama", "second-model", 30), ("ollama", "third-model", 30) },
            factory.Constructed);
        // The skip is logged with the agent's name — and never any key material (FR-058).
        List<LogEvent> events;
        lock (sink.Events) events = sink.Events.ToList();
        Assert.Contains(events, e =>
            e.RenderMessage().Contains("skipping agent") && e.RenderMessage().Contains("First"));
        Assert.DoesNotContain(events, e => e.RenderMessage().Contains("sk-test-key"));
    }

    [Fact]
    public async Task A_local_candidate_is_still_tried_when_consent_is_withheld()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        settings.PrivacyConsentRequired = true;
        chain.First.Provider = "lmstudio";                   // local — allow-listed like ollama
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new FakeChatClient("from-first"));
        var svcs = ServicesFor(settings, factory);

        var (_, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal("First", agentName);
        Assert.Equal(("lmstudio", "first-model", 30), factory.Constructed[1]);
    }

    [Fact]
    public async Task With_consent_granted_a_cloud_candidate_is_tried()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        settings.PrivacyConsentRequired = false;             // consent GIVEN
        chain.First.Provider = "anthropic";
        chain.First.ApiKey = "sk-test-key";
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new FakeChatClient("from-cloud"));
        var svcs = ServicesFor(settings, factory);

        var (response, usedFallback, agentName) = await RunAsync(svcs, settings, chain.Selected);

        Assert.True(usedFallback);
        Assert.Equal("First", agentName);
        Assert.Equal("from-cloud", response.Text);
        Assert.Equal(("anthropic", "first-model", 30), factory.Constructed[1]);
    }

    // ── Cancellation and consent propagate out of every attempt ──

    [Fact]
    public async Task A_candidate_cancellation_propagates_and_stops_the_chain()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(new OperationCanceledException()));
        var offlineCalled = 0;
        var svcs = ServicesFor(settings, factory, _ => { offlineCalled++; return new FakeChatClient("offline"); });

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync(svcs, settings, chain.Selected));

        // Selected + First were tried; the chain stopped — Second/Third and the offline
        // provider were never touched.
        Assert.Equal(2, factory.Constructed.Count);
        Assert.Equal(0, offlineCalled);
    }

    [Fact]
    public async Task A_candidate_consent_exception_propagates_and_stops_the_chain()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(new PrivacyConsentRequiredException("CONSENT_REQUIRED:x")));
        var offlineCalled = 0;
        var svcs = ServicesFor(settings, factory, _ => { offlineCalled++; return new FakeChatClient("offline"); });

        await Assert.ThrowsAsync<PrivacyConsentRequiredException>(() => RunAsync(svcs, settings, chain.Selected));

        Assert.Equal(2, factory.Constructed.Count);
        Assert.Equal(0, offlineCalled);
    }

    [Fact]
    public async Task The_offline_attempt_never_swallows_cancellation()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(Boom()))
            .On("second-model", () => new ThrowingChatClient(Boom()))
            .On("third-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory,
            _ => new ThrowingChatClient(new OperationCanceledException()));

        // Not wrapped into the all-fail aggregate — cancellation propagates untouched.
        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync(svcs, settings, chain.Selected));
    }

    [Fact]
    public async Task The_offline_attempt_never_swallows_a_consent_exception()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        var factory = new ScriptedFactory()
            .On("selected-model", () => new ThrowingChatClient(Boom()))
            .On("first-model", () => new ThrowingChatClient(Boom()))
            .On("second-model", () => new ThrowingChatClient(Boom()))
            .On("third-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory,
            _ => new ThrowingChatClient(new PrivacyConsentRequiredException("CONSENT_REQUIRED:x")));

        await Assert.ThrowsAsync<PrivacyConsentRequiredException>(() => RunAsync(svcs, settings, chain.Selected));
    }

    // ── FR-058: the user-facing aggregate is sanitized; FR-057: the primary is named ──

    [Fact]
    public async Task A_provider_auth_error_surfaces_the_taxonomy_message_not_the_raw_body()
    {
        var chain = new Chain();
        var settings = chain.Settings();
        IChatClient AuthError() => new ThrowingChatClient(
            new HttpRequestException("401 Unauthorized — bad key sk-live-secret", null, HttpStatusCode.Unauthorized));
        var factory = new ScriptedFactory()
            .On("selected-model", AuthError)
            .On("first-model", AuthError)
            .On("second-model", AuthError)
            .On("third-model", AuthError);
        var svcs = ServicesFor(settings, factory, _ => AuthError());

        var ex = await Assert.ThrowsAsync<AggregateException>(() => RunAsync(svcs, settings, chain.Selected));

        // The user gets the FR-014 taxonomy row…
        Assert.Contains("The API key was rejected", ex.Message);
        // …never the raw provider body, which may echo credentials (FR-058).
        Assert.DoesNotContain("sk-live-secret", ex.Message);
        Assert.DoesNotContain("401 Unauthorized — bad key", ex.Message);
        // The raw exceptions still ride inside the inners (and the log) for diagnosis.
        Assert.All(ex.InnerExceptions,
            inner => Assert.IsType<HttpRequestException>(inner.InnerException));
    }

    [Fact]
    public async Task The_aggregate_uses_generic_wording_when_the_primary_has_no_name()
    {
        // Legacy shape: flat settings, no selected agent — the FR-057 naming falls back to the
        // pre-chain generic label.
        var settings = new AiSettings
        {
            Enabled = true,
            Provider = "ollama",
            Model = "legacy-model",
            OfflineProvider = "ollama",
            OfflineModel = "offline-model",
            OfflineEndpoint = "http://127.0.0.1:1/",
        };
        var factory = new ScriptedFactory()
            .On("legacy-model", () => new ThrowingChatClient(Boom()));
        var svcs = ServicesFor(settings, factory, _ => new ThrowingChatClient(Boom()));

        var ex = await Assert.ThrowsAsync<AggregateException>(() => RunAsync(svcs, settings, selected: null));

        Assert.Contains("Primary provider failed", ex.Message);
    }
}
