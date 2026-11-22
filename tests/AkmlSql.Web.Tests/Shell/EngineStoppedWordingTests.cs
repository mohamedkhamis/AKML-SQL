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

    [Fact]
    public void With_nothing_answering_the_status_text_does_not_blame_the_pairing()
    {
        _auto.Failure = new(EngineConnectFailureKind.Unreachable,
            "The engine is not responding at localhost:59417 — it may be starting up or stopped.", null);
        _bridge.SetState(BridgeState.Failed);

        var cut = Render<StatusBar>();

        var text = cut.Find(".akml-status-text").TextContent.Trim();
        Assert.Equal("Engine not reachable.", text);
        Assert.Contains("localhost:59417", cut.Find("[data-testid=engine-issue]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=engine-retry]"));
    }

    [Fact]
    public void A_refused_handshake_still_says_the_handshake_failed()
    {
        _auto.Failure = new(EngineConnectFailureKind.Rejected, "The engine declined the connection.", null);
        _bridge.SetState(BridgeState.Failed);

        var cut = Render<StatusBar>();

        Assert.Equal("Handshake failed. Check engine pairing.", cut.Find(".akml-status-text").TextContent.Trim());
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
