using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (task T125, FR-093, SC-010). PostgreSQL support is one switch: with <c>postgreSql.enabled</c> false the engine
/// registers no PostgreSQL-only handler (the redaction pair 223 exists only for PostgreSQL), so a client that sends one gets
/// the answer an engine built before the feature would give. With it on, it is there.
///
/// <para>
/// Spec 043 follow-up (review): the credential pair 221 also answers the SQL Server kind, which <c>credentials.sqlserver.v1</c>
/// always advertises, so it is registered whatever the switch says; with the switch off a PostgreSQL request gets the
/// feature-off answer and nothing is read or written.
/// </para>
/// </summary>
public sealed class PostgresFlagRegistrationTests
{
    private static RpcContext Context(bool postgresEnabled)
    {
        var settings = new AppSettings();
        settings.PostgreSql.Enabled = postgresEnabled;
        var sessions = new SessionManager();
        return new RpcContext
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
    }

    private static HashSet<int> Registered(bool postgresEnabled)
    {
        var router = new RpcRouter { DialectGuard = AkmlSql.Engine.PostgreSql.DialectGuard.Create() };

        EngineHandlerRegistry.RegisterAllHandlers(router, Context(postgresEnabled), null);

        return new HashSet<int>(router.RegisteredMessageTypes);
    }

    // Port 1: nothing answers there, and with the feature off nothing may even be asked.
    private const string PgString = "Host=127.0.0.1;Port=1;Database=postgres;Username=u;Password=p;Timeout=3";

    [Fact]
    public void With_PostgreSQL_off_no_PostgreSQL_only_handler_is_registered_but_the_credential_pair_is()
    {
        var registered = Registered(postgresEnabled: false);

        Assert.DoesNotContain(MessageTypes.RedactRequest, registered);
        Assert.Contains(MessageTypes.CredentialRequest, registered);   // the SQL Server kind is served with the switch off
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

        Assert.Equal(new[] { MessageTypes.RedactRequest }, removed);
        Assert.Empty(off.Except(on));
    }

    [Fact]
    public async Task With_PostgreSQL_off_a_SQL_Server_credential_request_is_answered_and_a_PostgreSQL_one_says_the_switch_is_off()
    {
        // Before the review the pair was registered only with the switch on: the router had no handler, the WebSocket transport
        // dropped the null reply, and the browser waited forever for an answer about a SQL Server login.
        var ctx = Context(postgresEnabled: false);
        var router = new RpcRouter { DialectGuard = AkmlSql.Engine.PostgreSql.DialectGuard.Create() };
        EngineHandlerRegistry.RegisterAllHandlers(router, ctx, null);

        async Task<CredentialResponse> AskAsync(int kind)
        {
            var reply = await router.RouteAsync(new RpcMessage
            {
                MessageType = MessageTypes.CredentialRequest,
                RequestId = 7,
                Payload = MessagePack.MessagePackSerializer.Serialize(new CredentialRequest
                {
                    Action = CredentialAction.Has, Kind = kind, Server = "nobody-" + Guid.NewGuid().ToString("N")[..8] + ".test", Port = 5432, Login = "alice",
                }),
            }, ctx, default);
            Assert.NotNull(reply);
            Assert.Equal(MessageTypes.CredentialResponse, reply!.MessageType);
            return MessagePack.MessagePackSerializer.Deserialize<CredentialResponse>(reply.Payload!);
        }

        var sqlServer = await AskAsync((int)DatabaseKind.SqlServer);
        var postgres = await AskAsync((int)DatabaseKind.PostgreSql);

        Assert.True(sqlServer.Ok);
        Assert.False(sqlServer.Has);
        Assert.False(postgres.Ok);
        Assert.Equal(DialectErrors.FeatureOffMessage, postgres.Error);
    }

    // ---- the switch gates the connection, not only the registrations (FR-093) ----------------------------------

    [Fact]
    public async Task With_PostgreSQL_off_a_PostgreSQL_connection_never_becomes_a_session()
    {
        var ctx = Context(postgresEnabled: false);

        await new ConnectionChangedHandler().HandleAsync(
            new ConnectionInfo { SessionId = "s", ConnectionString = PgString, DatabaseName = "postgres", Kind = (int)DatabaseKind.PostgreSql }, ctx, default);

        Assert.Null(ctx.Sessions.GetSession("s"));                       // the session keeps what it had: nothing
        Assert.Equal(DatabaseKind.SqlServer, ctx.Sessions.KindOf("s"));  // so no handler reads PostgreSQL text with the T-SQL parser
    }

    [Fact]
    public async Task With_PostgreSQL_off_a_connection_test_and_a_database_list_name_the_switch_without_connecting()
    {
        var test = await new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest { ConnectionString = PgString, Kind = (int)DatabaseKind.PostgreSql }, Context(postgresEnabled: false), default);
        Assert.False(test.Ok);
        Assert.Equal(DialectErrors.FeatureOffMessage, test.ErrorMessage);

        var list = await new ListDatabasesHandler().HandleAsync(
            new ListDatabasesRequest { ConnectionString = PgString, Kind = (int)DatabaseKind.PostgreSql }, Context(postgresEnabled: false), default);
        Assert.False(list.Ok);
        Assert.Equal(DialectErrors.FeatureOffMessage, list.ErrorMessage);
    }

    [Fact]
    public async Task With_PostgreSQL_on_the_same_request_is_tried_against_the_server()
    {
        var test = await new TestSqlConnectionHandler().HandleAsync(
            new TestSqlConnectionRequest { ConnectionString = PgString, Kind = (int)DatabaseKind.PostgreSql }, Context(postgresEnabled: true), default);

        Assert.False(test.Ok);                                            // nothing listens on port 1
        Assert.NotEqual(DialectErrors.FeatureOffMessage, test.ErrorMessage);
    }
}
