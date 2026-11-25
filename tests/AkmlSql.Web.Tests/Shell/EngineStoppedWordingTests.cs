using System;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 047 (WEB-001), installed-product run F4: with AkmlSqlWebEngine stopped, a page that pairs from akml-engine.json showed
/// "Failed" with the fixed text "Handshake failed. Check engine pairing." next to the right diagnosis ("The engine is not
/// responding…"), and never said which address it tried. Nothing was handshaken and the pairing was fine. The failure is
/// now named with its address, and the status text follows the failure's kind.
/// </summary>
public sealed class EngineStoppedWordingTests : BunitContext
{
    private readonly FakeEngineBridge _bridge = new();
    private readonly FixedFailureAutoConnect _auto = new();

    public EngineStoppedWordingTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IConnectionStore>(new ConnectionStore(adapter));
        Services.AddSingleton<ISqlConnectionService>(new SqlConnectionService(_bridge, new FakeDiagnosticsRingBuffer()));
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(new ConnectionManagerController());
        Services.AddSingleton<IEngineAutoConnect>(_auto);
        Services.AddSingleton<IPageEngine>(new FakePageEngine());
    }

    [Fact]
    public async Task An_engine_that_does_not_answer_is_named_with_the_address_tried()
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => throw new InvalidOperationException("connection refused") };
        var page = new FakePageEngine { Result = new(PageEngineKind.Engine, new("localhost", 59417, "ws")) };
        await using var auto = new EngineAutoConnect(bridge, new FakeConnectionStore(), new FakePairingTokenVault(), new FakeDiagnosticsRingBuffer(), page);

        await auto.StartAsync();

        Assert.Equal(EngineConnectFailureKind.Unreachable, auto.LastFailure?.Kind);
        Assert.Equal("The engine is not responding at localhost:59417 — it may be starting up or stopped.", auto.LastFailure?.Message);
    }

    // Installed run on 1.26.1123.0537: the bar read "Failed  Engine not reachable.  The engine is not responding at
    // localhost:59417 — it may be starting up or stopped.  Retry" — three ways of saying one thing. The pill names the failure,
    // the issue strip explains it once, and nothing blames the pairing.
    [Fact]
    public void With_nothing_answering_the_pill_says_unreachable_and_the_issue_says_it_once()
    {
        _auto.Failure = new(EngineConnectFailureKind.Unreachable,
            "The engine is not responding at localhost:59417 — it may be starting up or stopped.", null);
        _bridge.SetState(BridgeState.Failed);

        var cut = Render<StatusBar>();

        Assert.Equal("Unreachable", cut.Find("[data-testid=status-pill]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=status-text]"));
        Assert.Contains("localhost:59417", cut.Find("[data-testid=engine-issue]").TextContent);
        Assert.DoesNotContain("pairing", cut.Find(".akml-status").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(cut.Find("[data-testid=engine-retry]"));
        Assert.Equal("Engine not reachable. Open Diagnostics for the engine's log.", cut.Find("[data-testid=status-pill]").GetAttribute("title"));
    }

    [Fact]
    public void A_refused_handshake_is_named_by_the_issue_and_the_pill_says_failed()
    {
        _auto.Failure = new(EngineConnectFailureKind.Rejected, "The engine declined the connection.", null);
        _bridge.SetState(BridgeState.Failed);

        var cut = Render<StatusBar>();

        Assert.Equal("Failed", cut.Find("[data-testid=status-pill]").TextContent.Trim());
        Assert.Contains("The engine declined the connection.", cut.Find("[data-testid=engine-issue]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=status-text]"));
    }

    // UX pass 2026-11-23: a page paired from Settings › Connections after auto-connect had stopped at "No engine paired yet.";
    // when that engine stopped, the stale failure came back and the bar said the engine was not paired.
    [Fact]
    public async Task A_bridge_that_opens_clears_the_failure_from_before_it()
    {
        var bridge = new FakeEngineBridge();
        await using var auto = new EngineAutoConnect(bridge, new FakeConnectionStore(), new FakePairingTokenVault(), new FakeDiagnosticsRingBuffer(),
            new FakePageEngine { Result = new(PageEngineKind.Static) });
        await auto.StartAsync();
        Assert.Equal(EngineConnectFailureKind.NotPaired, auto.LastFailure?.Kind);
        int changes = 0;
        auto.FailureChanged += () => changes++;

        bridge.SetState(BridgeState.Open);
        bridge.SetState(BridgeState.Reconnecting);

        Assert.Null(auto.LastFailure);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Reconnecting_keeps_its_words_beside_an_issue()
    {
        _auto.Failure = new(EngineConnectFailureKind.Unreachable,
            "The engine is not responding at localhost:59417 — it may be starting up or stopped.", null);
        _bridge.SetState(BridgeState.Reconnecting);

        var cut = Render<StatusBar>();

        Assert.Equal("Reconnecting", cut.Find("[data-testid=status-pill]").TextContent.Trim());
        Assert.StartsWith("Reconnecting ·", cut.Find("[data-testid=status-text]").TextContent.Trim());
        // The words and the pointer to the log are two sentences, whatever the words end with.
        Assert.Equal("Reconnecting · trying now… Open Diagnostics for the engine's log.", cut.Find("[data-testid=status-pill]").GetAttribute("title"));
    }

    [Fact]
    public void Offline_says_what_still_works_without_repeating_the_pill()
    {
        _bridge.SetState(BridgeState.Disconnected);

        var cut = Render<StatusBar>();

        Assert.Equal("Offline", cut.Find("[data-testid=status-pill]").TextContent.Trim());
        var words = cut.Find("[data-testid=status-text]").TextContent.Trim();
        Assert.DoesNotContain("Offline", words);
        Assert.Equal("Keywords only; Format and Analyse still work.", words);
    }

    [Fact]
    public void An_engine_that_wants_its_PIN_again_says_it_needs_pairing()
    {
        _auto.Failure = new(EngineConnectFailureKind.PairingRequired, "The engine needs pairing again — open Settings to enter a new PIN.", null);
        _bridge.SetState(BridgeState.Failed);

        var cut = Render<StatusBar>();

        Assert.Equal("Needs pairing", cut.Find("[data-testid=status-pill]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=engine-setup]"));
    }

    private sealed class FixedFailureAutoConnect : IEngineAutoConnect
    {
        internal EngineConnectFailure? Failure { get; set; }
        public EngineConnectFailure? LastFailure => Failure;
        public event Action? FailureChanged { add { } remove { } }
        public Task StartAsync() => Task.CompletedTask;
        public Task RetryNowAsync() => Task.CompletedTask;
        public Task<LocalPairResult> PairLocalEngineAsync() =>
            Task.FromResult(new LocalPairResult(false, "localhost:47291", LocalPairFailure.NotReachable));
    }
}
