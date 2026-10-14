using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (task T125, FR-093, SC-010). PostgreSQL support is one switch: with <c>postgreSql.enabled</c> false the engine
/// registers no PostgreSQL handler (the credential pair 221 and the redaction pair 223 exist only for PostgreSQL), so a client
/// that sends one gets the answer an engine built before the feature would give. With it on, both are there.
/// </summary>
public sealed class PostgresFlagRegistrationTests
{
    private static HashSet<int> Registered(bool postgresEnabled)
    {
        var settings = new AppSettings();
        settings.PostgreSql.Enabled = postgresEnabled;
        var sessions = new SessionManager();
        var ctx = new RpcContext
        {
            Sessions = sessions,
            SchemaCache = new SchemaCacheManager { KindOf = sessions.KindOf },
            Logger = Log.Logger,
            SettingsLoader = () => settings,
            ParserService = new TsqlParserService(),
            SchemaMetadata = new SchemaMetadataService(),
            PostgresSchemaMetadata = postgresEnabled ? new AkmlSql.Engine.PostgreSql.PgSchemaMetadataService() : null,
            SessionConnections = new AkmlSql.Engine.Execution.SessionConnectionRegistry(),
        };
        var router = new RpcRouter { DialectGuard = AkmlSql.Engine.PostgreSql.DialectGuard.Create() };

        EngineHandlerRegistry.RegisterAllHandlers(router, ctx, null);

        return new HashSet<int>(router.RegisteredMessageTypes);
    }

    [Fact]
    public void With_PostgreSQL_off_no_PostgreSQL_only_handler_is_registered()
    {
        var registered = Registered(postgresEnabled: false);

        Assert.DoesNotContain(MessageTypes.CredentialRequest, registered);
        Assert.DoesNotContain(MessageTypes.RedactRequest, registered);
    }

    [Fact]
    public void With_PostgreSQL_on_the_credential_and_redaction_handlers_are_registered()
    {
        var registered = Registered(postgresEnabled: true);

        Assert.Contains(MessageTypes.CredentialRequest, registered);
        Assert.Contains(MessageTypes.RedactRequest, registered);
    }

    [Fact]
    public void Switching_the_feature_off_removes_nothing_else()
    {
        var on = Registered(postgresEnabled: true);
        var off = Registered(postgresEnabled: false);

        var removed = on.Except(off).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { MessageTypes.CredentialRequest, MessageTypes.RedactRequest }, removed);
        Assert.Empty(off.Except(on));
    }
}
