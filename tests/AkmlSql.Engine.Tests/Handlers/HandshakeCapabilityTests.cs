using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Handshake;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>Spec 042 (FR-006, FR-093): the engine advertises PostgreSQL support unless config switches it off.</summary>
public sealed class HandshakeCapabilityTests
{
    private static RpcContext Context(AppSettings settings) => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => settings,
    };

    private static HandshakeRequest Request() => new()
    {
        WebVersion = "1.0.0",
        ProtocolVersionMin = Capabilities.ProtocolVersionMin,
        ProtocolVersionMax = Capabilities.ProtocolVersionMax,
        BrowserLabel = "test",
    };

    [Fact]
    public async Task The_default_config_advertises_dialect_postgresql_v1()
    {
        var response = await new HandshakeHandler().HandleAsync(Request(), Context(new AppSettings()), CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.Contains("dialect.postgresql.v1", response.EngineCapabilities);
        Assert.Contains("execute.v2", response.EngineCapabilities);
    }

    [Fact]
    public async Task Disabling_PostgreSQL_in_config_removes_only_that_capability()
    {
        var settings = new AppSettings();
        settings.PostgreSql.Enabled = false;

        var response = await new HandshakeHandler().HandleAsync(Request(), Context(settings), CancellationToken.None);

        Assert.DoesNotContain("dialect.postgresql.v1", response.EngineCapabilities);
        Assert.Equal(Capabilities.Current.OrderBy(c => c), response.EngineCapabilities.OrderBy(c => c));
    }

    [Fact]
    public async Task A_loader_that_throws_does_not_fail_the_handshake()
    {
        var context = new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => throw new InvalidOperationException("config unreadable"),
        };

        var response = await new HandshakeHandler().HandleAsync(Request(), context, CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.Contains("dialect.postgresql.v1", response.EngineCapabilities); // defaults: on
    }

    [Fact]
    public void Capabilities_For_null_settings_means_the_defaults()
    {
        Assert.Contains(Capabilities.DialectPostgreSqlV1, Capabilities.For(null));
        Assert.Equal(Capabilities.Current.Count, Capabilities.For(new AppSettings { PostgreSql = new PostgreSqlSettings { Enabled = false } }).Length);
    }
}
