using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

public sealed class EngineAutoConnectPrecedenceTests
{
    [Fact]
    public async Task An_HTTPS_page_engine_failure_never_dials_ws()
    {
        var urls = new List<string>();
        var store = new FakeConnectionStore();
        var vault = new FakePairingTokenVault();
        var diagnostics = new FakeDiagnosticsRingBuffer();
        await using var bridge = new EngineBridge(() => new FailingSocket(urls), diagnostics, vault, store);
        await using var auto = new EngineAutoConnect(bridge, store, vault, diagnostics, Page(scheme: "wss"));
        await auto.StartAsync();
        Assert.Equal("wss://localhost:59417/akmlsql", Assert.Single(urls));
        Assert.Empty(await store.ListAsync());
        Assert.Equal(EngineConnectFailureKind.Unreachable, auto.LastFailure?.Kind);
    }

    private sealed class FailingSocket(List<string> urls) : IBridgeWebSocket
    {
        public BridgeWebSocketState State => BridgeWebSocketState.Closed;
        public Task ConnectAsync(string url, CancellationToken ct) { urls.Add(url); return Task.FromException(new HttpRequestException("down")); }
        public Task SendAsync(byte[] frame, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> ReceiveAsync(CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public ValueTask DisposeAsync() => default;
    }

    private static FakePageEngine Page(string host = "localhost", string scheme = "ws") =>
        new() { Result = new(PageEngineKind.Engine, new(host, 59417, scheme)) };

    private static EngineAutoConnect Auto(FakeEngineBridge bridge, FakeConnectionStore store, IPageEngine page) =>
        new(bridge, store, new FakePairingTokenVault(), new FakeDiagnosticsRingBuffer(), page);

    [Theory]
    [InlineData("localhost", "ws", true)]
    [InlineData("sql-box", "wss", false)]
    public async Task The_page_engine_is_saved_only_after_OK_with_its_address_and_metadata(string host, string scheme, bool local)
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => new()
            { Status = HandshakeStatus.Ok, EngineVersion = "test-version", EngineCapabilities = new[] { "execute.v2" }, NewBearerToken = local ? null : "minted-token" } };
        var store = new FakeConnectionStore();
        await using var auto = Auto(bridge, store, Page(host, scheme));
        await auto.StartAsync();
        var saved = Assert.Single(await store.ListAsync());
        Assert.Equal(host, saved.Host);
        Assert.Equal(59417, saved.Port);
        Assert.Equal(local, saved.IsLocalhost);
        Assert.Equal(scheme, saved.ResolvedScheme);
        Assert.Equal(scheme == "wss", saved.StrictScheme);
        Assert.Equal(local ? "This computer" : host, saved.Name);
        Assert.Equal("test-version", saved.LastKnownEngineVersion);
        Assert.Equal(new[] { "execute.v2" }, saved.LastKnownCapabilities);
        Assert.Equal(saved.Id, await store.GetActiveIdAsync());
        Assert.Null(auto.LastFailure);
        Assert.Null(Assert.Single(bridge.Handshakes).PairingPin);
    }

    [Fact]
    public async Task A_saved_active_record_wins_even_when_down()
    {
        var page = Page();
        var bridge = new FakeEngineBridge { Answer = (_, _) => throw new InvalidOperationException("down") };
        var store = new FakeConnectionStore();
        await store.AddAsync(new() { Id = "chosen", Host = "another", Port = 60000 });
        await store.SetActiveIdAsync("chosen");
        await using var auto = Auto(bridge, store, page);
        await auto.StartAsync();
        Assert.Equal("another", Assert.Single(bridge.Handshakes).Host);
        Assert.Equal(0, page.Calls);
        Assert.Equal(EngineConnectFailureKind.Unreachable, auto.LastFailure?.Kind);
    }

    [Fact]
    public async Task A_dangling_active_id_is_cleared_before_using_the_page()
    {
        var store = new FakeConnectionStore();
        await store.SetActiveIdAsync("missing");
        await using var auto = Auto(new(), store, Page());
        await auto.StartAsync();
        Assert.Null(store.ActiveChanges[1]);
        Assert.Equal(Assert.Single(await store.ListAsync()).Id, await store.GetActiveIdAsync());
    }

    [Fact]
    public async Task A_matching_saved_record_is_reused_and_an_HTTPS_page_keeps_its_scheme()
    {
        var store = new FakeConnectionStore();
        await store.AddAsync(new() { Id = "same", Host = "LOCALHOST", Port = 59417, ResolvedScheme = "ws" });
        await using var auto = Auto(new(), store, Page(scheme: "wss"));
        await auto.StartAsync();
        var saved = Assert.Single(await store.ListAsync());
        Assert.Equal("same", saved.Id);
        Assert.True(saved.StrictScheme);
        Assert.Equal("wss", saved.ResolvedScheme);
    }

    [Theory]
    [InlineData(PageEngineKind.Static, EngineConnectFailureKind.NotPaired, "No engine paired yet.")]
    [InlineData(PageEngineKind.Unknown, EngineConnectFailureKind.Unreachable, "Checking this page's engine…")]
    public async Task Other_page_answers_do_not_dial(PageEngineKind kind, EngineConnectFailureKind failure, string message)
    {
        var bridge = new FakeEngineBridge();
        var store = new FakeConnectionStore();
        await using var auto = Auto(bridge, store, new FakePageEngine { Result = new(kind) });
        await auto.StartAsync();
        Assert.Empty(bridge.Handshakes);
        Assert.Equal(failure, auto.LastFailure?.Kind);
        Assert.Equal(message, auto.LastFailure?.Message);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task PIN_required_prefills_pairing_and_stores_nothing()
    {
        var page = Page(scheme: "wss");
        var store = new FakeConnectionStore();
        var bridge = new FakeEngineBridge { Answer = (_, _) => new() { Status = HandshakeStatus.PinRequired } };
        await using var auto = Auto(bridge, store, page);
        await auto.StartAsync();
        Assert.Empty(await store.ListAsync());
        Assert.Equal(page.Result.Engine, page.PairingPrefill);
        Assert.Equal(EngineConnectFailureKind.PairingRequired, auto.LastFailure?.Kind);
        Assert.Equal("This engine needs its pairing PIN. Pair it in Settings › Connections.", auto.LastFailure?.Message);
    }

    [Fact]
    public async Task A_socket_failure_does_not_save_a_connection()
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => throw new InvalidOperationException("down") };
        var store = new FakeConnectionStore();
        await using var auto = Auto(bridge, store, Page());
        await auto.StartAsync();
        Assert.Empty(await store.ListAsync());
        Assert.Equal(EngineConnectFailureKind.Unreachable, auto.LastFailure?.Kind);
    }

    [Fact]
    public async Task Pair_local_reuses_the_page_engines_saved_bearer()
    {
        var bridge = new FakeEngineBridge();
        var store = new FakeConnectionStore();
        var vault = new FakePairingTokenVault();
        await store.AddAsync(new() { Id = "paired", Host = "sql-box", Port = 59417, IsLocalhost = false });
        await vault.StoreAsync("paired", "saved-bearer", DateTimeOffset.UtcNow.AddDays(1));
        await using var auto = new EngineAutoConnect(bridge, store, vault, new FakeDiagnosticsRingBuffer(), Page("sql-box", "wss"));
        Assert.True(await auto.PairLocalEngineAsync());
        Assert.Equal("saved-bearer", Assert.Single(bridge.Handshakes).BearerToken);
        Assert.Equal("paired", Assert.Single(await store.ListAsync()).Id);
    }

    [Fact]
    public async Task Pair_local_uses_the_page_engine_address()
    {
        var bridge = new FakeEngineBridge();
        var store = new FakeConnectionStore();
        await using var auto = Auto(bridge, store, Page(scheme: "wss"));
        Assert.True(await auto.PairLocalEngineAsync());
        Assert.Equal(59417, Assert.Single(bridge.Handshakes).Port);
        Assert.True(Assert.Single(await store.ListAsync()).StrictScheme);
    }
}
