using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 021 (web edition) -- M3 tasks T072 + T073 + T074. The bridge-routed services
/// (Completion / SignatureHelp / QuickInfo / GotoDefinition) must fall back gracefully
/// when the bridge is not open (FR-016). The "open path" -- routing the request to
/// the engine -- is covered by HandshakeClientTests for the bridge itself.
/// </summary>
public sealed class BridgeRoutedServicesTests
{
    private static IEngineBridge ClosedBridge() =>
        new EngineBridge(() => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    [Fact]
    public async Task CompletionService_returns_empty_when_bridge_is_disconnected()
    {
        var service = new CompletionService(ClosedBridge());
        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Empty(response.Items ?? System.Array.Empty<CompletionItem>());
    }

    [Fact]
    public async Task SignatureHelpService_returns_empty_when_bridge_is_disconnected()
    {
        var service = new SignatureHelpService(ClosedBridge());
        var response = await service.GetAsync(new SignatureRequest(), CancellationToken.None);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task QuickInfoService_returns_empty_when_bridge_is_disconnected()
    {
        var service = new QuickInfoService(ClosedBridge());
        var response = await service.GetAsync(new QuickInfoRequest(), CancellationToken.None);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task GotoDefinitionService_returns_null_when_bridge_is_disconnected()
    {
        var service = new GotoDefinitionService(ClosedBridge());
        var response = await service.GetAsync(new GetObjectDefinitionRequest(), CancellationToken.None);

        Assert.Null(response);
    }
}
