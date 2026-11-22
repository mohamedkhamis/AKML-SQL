using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 047 (WEB-001, contracts/engine-pairing.md §3): "Use this computer's engine" says which address it tried and why it
/// failed, so the status bar can offer Add engine… instead of sending the user to Settings with "Handshake failed".
/// </summary>
public sealed class LocalPairResultTests
{
    private static EngineAutoConnect Auto(FakeEngineBridge bridge, FakeConnectionStore store, IPageEngine? page) =>
        new(bridge, store, new FakePairingTokenVault(), new FakeDiagnosticsRingBuffer(), page);

    private static FakePageEngine Manifest(string host = "localhost", int port = 59417, string scheme = "ws") =>
        new() { Result = new(PageEngineKind.Engine, new(host, port, scheme)) { Source = PageEngineSource.InstallManifest } };

    [Fact]
    public async Task The_engine_the_installer_published_is_tried_and_named()
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => new() { Status = HandshakeStatus.Ok, EngineVersion = "1" } };
        await using var auto = Auto(bridge, new FakeConnectionStore(), Manifest());

        var result = await auto.PairLocalEngineAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Failure);
        Assert.Equal("localhost:59417", result.TriedAddress);
        Assert.Equal(59417, Assert.Single(bridge.Handshakes).Port);
    }

    [Fact]
    public async Task Without_a_page_engine_the_default_port_is_tried_and_named()
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => throw new InvalidOperationException("nothing listening") };
        await using var auto = Auto(bridge, new FakeConnectionStore(), new FakePageEngine());

        var result = await auto.PairLocalEngineAsync();

        Assert.False(result.Ok);
        Assert.Equal(LocalPairFailure.NotReachable, result.Failure);
        Assert.Equal($"localhost:{EngineEndpoint.DefaultPort}", result.TriedAddress);
    }

    [Theory]
    [InlineData(HandshakeStatus.PinRequired)]
    [InlineData(HandshakeStatus.PinInvalid)]
    public async Task An_engine_that_needs_its_PIN_reports_it_and_prefills_the_pairing_form(string status)
    {
        var page = Manifest("sql-host", 59417, "wss");
        var bridge = new FakeEngineBridge { Answer = (_, _) => new() { Status = status } };
        await using var auto = Auto(bridge, new FakeConnectionStore(), page);

        var result = await auto.PairLocalEngineAsync();

        Assert.Equal(LocalPairFailure.PinRequired, result.Failure);
        Assert.Equal("sql-host:59417", result.TriedAddress);
        Assert.Equal(new PageEngineInfo("sql-host", 59417, "wss"), page.PairingPrefill);
    }

    [Fact]
    public async Task Any_other_refusal_is_reported_as_rejected()
    {
        var bridge = new FakeEngineBridge { Answer = (_, _) => new() { Status = HandshakeStatus.ProtocolMismatch, ErrorMessage = "old" } };
        await using var auto = Auto(bridge, new FakeConnectionStore(), Manifest());

        var result = await auto.PairLocalEngineAsync();

        Assert.False(result.Ok);
        Assert.Equal(LocalPairFailure.Rejected, result.Failure);
    }
}
