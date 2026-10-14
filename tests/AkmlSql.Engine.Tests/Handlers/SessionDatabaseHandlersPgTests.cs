using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Tests.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (task T075, FR-009, FR-030, FR-031): the database list and the database switch for a PostgreSQL session, against a
/// real server. The list leaves out templates; the switch is a reconnect that keeps the session untouched when the target cannot
/// be opened, and when it succeeds says the connection was reset and that no transaction survives.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class SessionDatabaseHandlersPgTests
{
    private static ConnectionInfo Info(string sessionId, string connectionString, string database) => new()
    {
        SessionId = sessionId, ConnectionString = connectionString, DatabaseName = database, Kind = (int)DatabaseKind.PostgreSql,
    };

    private static async Task<(EngineComposition Engine, string Session)> SessionInAsync(PgTestDatabase database)
    {
        var engine = EngineComposition.Build();
        var session = Guid.NewGuid().ToString("N");
        engine.Context.Sessions.UpdateSession(Info(session, database.ConnectionString, database.Name));
        return (engine, session);
    }

    // ── The list ────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task The_list_of_a_PostgreSQL_session_comes_from_its_own_connection_and_has_no_templates()
    {
        await using var database = await PgTestServer.CreateDatabaseAsync();
        var (engine, session) = await SessionInAsync(database);

        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = session }, engine.Context, default);

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.Contains(database.Name, response.Databases);
        Assert.Contains("postgres", response.Databases);
        Assert.DoesNotContain("template0", response.Databases);
        Assert.DoesNotContain("template1", response.Databases);
    }

    [SkippableFact]
    public async Task Without_a_session_the_kind_on_the_request_decides_and_a_remembered_password_is_used()
    {
        PgTestServer.SkipUnlessAvailable();
        var admin = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        var resolver = new Engine.PostgreSql.PgCredentialResolver();
        resolver.Save(admin.Host!, admin.Port, admin.Username!, admin.Password!);
        try
        {
            var withoutPassword = PgConnectionString.Build(admin.Host!, admin.Port, "postgres", admin.Username!, null, "prefer", null);

            var response = await new ListDatabasesHandler().HandleAsync(
                new ListDatabasesRequest { ConnectionString = withoutPassword, Kind = (int)DatabaseKind.PostgreSql }, EngineComposition.Build().Context, default);

            Assert.True(response.Ok, response.ErrorMessage);
            Assert.Contains("postgres", response.Databases);
        }
        finally
        {
            resolver.Forget(admin.Host!, admin.Port, admin.Username!);
        }
    }

    [SkippableFact]
    public async Task A_wrong_password_is_reported_in_the_servers_words_without_the_connection_string()
    {
        PgTestServer.SkipUnlessAvailable();
        var admin = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        var wrong = PgConnectionString.Build(admin.Host!, admin.Port, "postgres", admin.Username!, "not-the-password", "prefer", null);

        var response = await new ListDatabasesHandler().HandleAsync(
            new ListDatabasesRequest { ConnectionString = wrong, Kind = (int)DatabaseKind.PostgreSql }, EngineComposition.Build().Context, default);

        Assert.False(response.Ok);
        Assert.Contains("28P01", response.ErrorMessage);
        Assert.DoesNotContain("not-the-password", response.ErrorMessage);
    }

    [SkippableFact]
    public async Task A_session_that_is_not_connected_says_so_without_naming_SQL_Server()
    {
        PgTestServer.SkipUnlessAvailable();
        var engine = EngineComposition.Build();
        engine.Context.Sessions.UpdateSession(Info("s", string.Empty, "postgres"));
        engine.Context.Sessions.GetSession("s")!.IsConnected = false;

        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = "s" }, engine.Context, default);

        Assert.False(response.Ok);
        Assert.DoesNotContain("SQL Server", response.ErrorMessage, StringComparison.Ordinal);
    }

    // ── The switch ──────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Switching_reconnects_resets_the_transaction_and_follows_the_new_database()
    {
        await using var first = await PgTestServer.CreateDatabaseAsync();
        await using var second = await PgTestServer.CreateDatabaseAsync();
        var (engine, session) = await SessionInAsync(first);
        var state = engine.Context.Sessions.GetSession(session)!;
        state.TransactionState = TransactionState.Open;   // as a run that began one would have left it
        var firstConnection = engine.Context.SessionConnections!.GetOrCreate(session, first.ConnectionString, DatabaseKind.PostgreSql);

        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = session, DatabaseName = second.Name }, engine.Context, default);

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.Equal(second.Name, response.DatabaseName);
        Assert.True(response.ConnectionWasReset);
        Assert.Equal((int)TransactionState.None, response.TransactionState);
        Assert.Equal(second.Name, state.DatabaseName);
        Assert.Equal(TransactionState.None, state.TransactionState);
        Assert.Contains(second.Name, state.ConnectionString);

        // The persistent connection is a new one, and it is in the new database.
        var connection = engine.Context.SessionConnections.TryGet(session)!;
        Assert.NotSame(firstConnection, connection);
        var (current, _) = await connection.RunExclusivePostgresAsync(state.ConnectionString, async (conn, ct) =>
        {
            await using var command = new NpgsqlCommand("SELECT current_database()", conn);
            return (string)(await command.ExecuteScalarAsync(ct))!;
        }, default);
        Assert.Equal(second.Name, current);
    }

    [SkippableFact]
    public async Task A_database_that_does_not_exist_leaves_the_session_as_it_was()
    {
        await using var database = await PgTestServer.CreateDatabaseAsync();
        var (engine, session) = await SessionInAsync(database);
        var state = engine.Context.Sessions.GetSession(session)!;
        state.TransactionState = TransactionState.Open;

        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = session, DatabaseName = "akml_no_such_database" }, engine.Context, default);

        Assert.False(response.Ok);
        Assert.Contains("3D000", response.ErrorMessage);
        Assert.DoesNotContain("SQL Server", response.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(database.Name, state.DatabaseName);
        Assert.Equal(TransactionState.Open, state.TransactionState);   // nothing was rolled back for a switch that did not happen
    }

    [SkippableFact]
    public async Task A_name_with_a_control_character_is_refused_before_any_connection()
    {
        await using var database = await PgTestServer.CreateDatabaseAsync();
        var (engine, session) = await SessionInAsync(database);

        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = session, DatabaseName = "bad\nname" }, engine.Context, default);

        Assert.False(response.Ok);
        Assert.Equal("That is not a valid database name.", response.ErrorMessage);
    }
}
