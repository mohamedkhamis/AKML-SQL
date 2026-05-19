using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Handshake;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- M3 task T062. Exercises <see cref="HandshakeHandler"/>'s
/// branches: localhost auto-accept, protocol-mismatch, LAN PIN happy path, LAN PIN
/// invalid, LAN bearer happy path, LAN bearer revoked, LAN no-credentials.
/// </summary>
public sealed class HandshakeHandlerTests
{
    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
    };

    private static HandshakeRequest MakeRequest(
        string? pin = null,
        string? bearer = null,
        int min = Capabilities.ProtocolVersionMin,
        int max = Capabilities.ProtocolVersionMax) =>
        new()
        {
            PairingPin = pin,
            BearerToken = bearer,
            WebVersion = "1.0.0",
            ProtocolVersionMin = min,
            ProtocolVersionMax = max,
            BrowserLabel = "test-browser",
        };

    [Fact]
    public async Task Localhost_mode_accepts_handshake_with_no_credentials()
    {
        var handler = new HandshakeHandler();
        var response = await handler.HandleAsync(MakeRequest(), CreateContext(), CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.False(string.IsNullOrEmpty(response.EngineVersion));
        Assert.Equal(Capabilities.ProtocolVersionMax, response.ChosenProtocolVersion);
        Assert.Contains(Capabilities.CoreFormatV1, response.EngineCapabilities);
        Assert.Contains(Capabilities.CoreAnalysisV1, response.EngineCapabilities);
        Assert.Null(response.NewBearerToken);   // localhost mode mints no token
    }

    [Fact]
    public async Task Protocol_mismatch_when_client_min_exceeds_engine_max()
    {
        var handler = new HandshakeHandler();
        var response = await handler.HandleAsync(
            MakeRequest(min: 999, max: 999),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(HandshakeStatus.ProtocolMismatch, response.Status);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task Lan_mode_PIN_valid_mints_new_bearer_token()
    {
        const string newToken = "minted-token-abc";
        var handler = new HandshakeHandler(
            pairingRequired: () => true,
            pinValidator: pin => pin == "123456",
            bearerValidator: _ => false,
            bearerMinter: _ => newToken,
            serverCanonicalIdentityProvider: () => "SRV01\\SQL2022");

        var response = await handler.HandleAsync(
            MakeRequest(pin: "123456"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.Equal(newToken, response.NewBearerToken);
        Assert.Equal("SRV01\\SQL2022", response.ServerCanonicalIdentity);
    }

    [Fact]
    public async Task Lan_mode_PIN_invalid_returns_pin_invalid()
    {
        var handler = new HandshakeHandler(
            pairingRequired: () => true,
            pinValidator: _ => false,
            bearerValidator: _ => false,
            bearerMinter: _ => null,
            serverCanonicalIdentityProvider: () => null);

        var response = await handler.HandleAsync(
            MakeRequest(pin: "wrong"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(HandshakeStatus.PinInvalid, response.Status);
    }

    [Fact]
    public async Task Lan_mode_bearer_token_valid_returns_ok_with_no_new_token()
    {
        var handler = new HandshakeHandler(
            pairingRequired: () => true,
            pinValidator: _ => false,
            bearerValidator: t => t == "good-token",
            bearerMinter: _ => null,
            serverCanonicalIdentityProvider: () => null);

        var response = await handler.HandleAsync(
            MakeRequest(bearer: "good-token"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.Null(response.NewBearerToken);   // existing token reused
    }

    [Fact]
    public async Task Lan_mode_bearer_token_revoked_returns_pin_required()
    {
        var handler = new HandshakeHandler(
            pairingRequired: () => true,
            pinValidator: _ => false,
            bearerValidator: _ => false,
            bearerMinter: _ => null,
            serverCanonicalIdentityProvider: () => null);

        var response = await handler.HandleAsync(
            MakeRequest(bearer: "revoked-token"),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal(HandshakeStatus.PinRequired, response.Status);
    }

    [Fact]
    public async Task Lan_mode_with_no_credentials_returns_pin_required()
    {
        var handler = new HandshakeHandler(
            pairingRequired: () => true,
            pinValidator: _ => true,
            bearerValidator: _ => true,
            bearerMinter: _ => "anything",
            serverCanonicalIdentityProvider: () => null);

        var response = await handler.HandleAsync(MakeRequest(), CreateContext(), CancellationToken.None);

        Assert.Equal(HandshakeStatus.PinRequired, response.Status);
    }

    [Fact]
    public void Handshake_message_type_constants_are_in_the_reserved_range()
    {
        Assert.Equal(200, MessageTypes.HandshakeRequest);
        Assert.Equal(201, MessageTypes.HandshakeResponse);
    }

    [Fact]
    public void Capabilities_baseline_advertises_format_and_analysis()
    {
        Assert.Contains(Capabilities.CoreFormatV1, Capabilities.Current);
        Assert.Contains(Capabilities.CoreAnalysisV1, Capabilities.Current);
        Assert.Contains(Capabilities.SchemaV2, Capabilities.Current);
    }
}
