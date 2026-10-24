using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
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
        SettingsLoader = () => new AppSettings(),
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

    [Fact]
    public async Task The_handshake_advertises_execute_v2()
    {
        // Spec 041 (T081, contracts/ipc.md): the web sends Parse and reads the structured execute reply
        // only when the engine says it understands them.
        Assert.Equal("execute.v2", Capabilities.ExecuteV2);
        Assert.Contains(Capabilities.ExecuteV2, Capabilities.Current);
        var response = await new HandshakeHandler().HandleAsync(MakeRequest(), CreateContext(), CancellationToken.None);
        Assert.Contains(Capabilities.ExecuteV2, response.EngineCapabilities);
    }

    // ── Spec 041 (T179, research R29, X4) — how the engine runs, and the database selector's capability ──

    [Fact]
    public async Task Every_ok_handshake_says_how_the_engine_runs_and_as_whom()
    {
        var (kind, runsAs) = (EngineIdentity.HostKind, EngineIdentity.RunsAs);
        try
        {
            EngineIdentity.HostKind = "service";
            EngineIdentity.RunsAs = @"NT SERVICE\AkmlSqlWeb";

            var localhost = await new HandshakeHandler().HandleAsync(MakeRequest(), CreateContext(), CancellationToken.None);
            var pin = await new HandshakeHandler(() => true, p => p == "123456", _ => false, _ => "minted", () => null)
                .HandleAsync(MakeRequest(pin: "123456"), CreateContext(), CancellationToken.None);
            var bearer = await new HandshakeHandler(() => true, _ => false, t => t == "good", _ => null, () => null)
                .HandleAsync(MakeRequest(bearer: "good"), CreateContext(), CancellationToken.None);

            foreach (var response in new[] { localhost, pin, bearer })
            {
                Assert.Equal(HandshakeStatus.Ok, response.Status);
                Assert.Equal("service", response.HostKind);
                Assert.Equal(@"NT SERVICE\AkmlSqlWeb", response.RunsAs);
                Assert.Contains(Capabilities.SessionDatabaseV1, response.EngineCapabilities);
            }
        }
        finally
        {
            EngineIdentity.HostKind = kind;
            EngineIdentity.RunsAs = runsAs;
        }
    }

    [Fact]
    public void The_database_selector_capability_is_advertised()
    {
        Assert.Equal("session.database.v1", Capabilities.SessionDatabaseV1);
        Assert.Contains(Capabilities.SessionDatabaseV1, Capabilities.Current);
    }

    // ── Spec 043 follow-up (review) — an accepted handshake names the page; a refused one names nothing ──

    private const string PageId = "0123456789abcdef0123456789abcdef";

    /// <summary>What the connection's origin is after <paramref name="handshake"/>, inside a connection the transport opened.</summary>
    private static async Task<(string Status, string? Origin)> OriginAfterAsync(HandshakeHandler handler, HandshakeRequest handshake)
    {
        handshake.ClientInstanceId = PageId;
        using (AkmlSql.Engine.Pairing.BridgeConnection.Set("socket-1"))
        {
            // Answered in a child async context, as the router does: the page must still reach the connection.
            var response = await Task.Run(() => handler.HandleAsync(handshake, CreateContext(), CancellationToken.None));
            return (response.Status, AkmlSql.Engine.Pairing.BridgeConnection.Current);
        }
    }

    [Fact]
    public async Task Every_accepted_handshake_adopts_the_page_id()
    {
        var localhost = await OriginAfterAsync(new HandshakeHandler(), MakeRequest());
        var pin = await OriginAfterAsync(new HandshakeHandler(() => true, p => p == "123456", _ => false, _ => "minted", () => null), MakeRequest(pin: "123456"));
        var bearer = await OriginAfterAsync(new HandshakeHandler(() => true, _ => false, t => t == "good", _ => null, () => null), MakeRequest(bearer: "good"));

        foreach (var (status, origin) in new[] { localhost, pin, bearer })
        {
            Assert.Equal(HandshakeStatus.Ok, status);
            Assert.Equal("page:" + PageId, origin);
        }
    }

    [Fact]
    public async Task A_refused_handshake_adopts_nothing()
    {
        var lan = new HandshakeHandler(() => true, p => p == "123456", t => t == "good", _ => "minted", () => null);

        var refused = new[]
        {
            await OriginAfterAsync(new HandshakeHandler(), MakeRequest(min: 999, max: 999)),   // protocol mismatch
            await OriginAfterAsync(lan, MakeRequest(pin: "wrong")),                             // PIN invalid
            await OriginAfterAsync(lan, MakeRequest(bearer: "revoked")),                        // bearer unknown
            await OriginAfterAsync(lan, MakeRequest()),                                         // no credentials
        };

        foreach (var (status, origin) in refused)
        {
            Assert.NotEqual(HandshakeStatus.Ok, status);
            Assert.Equal("socket:socket-1", origin);
        }
    }

    [Fact]
    public async Task A_handshake_without_a_page_id_leaves_the_connection_its_socket()
    {
        using (AkmlSql.Engine.Pairing.BridgeConnection.Set("socket-1"))
        {
            var response = await new HandshakeHandler().HandleAsync(MakeRequest(), CreateContext(), CancellationToken.None);

            Assert.Equal(HandshakeStatus.Ok, response.Status);
            Assert.Equal("socket:socket-1", AkmlSql.Engine.Pairing.BridgeConnection.Current);
        }
    }
}
