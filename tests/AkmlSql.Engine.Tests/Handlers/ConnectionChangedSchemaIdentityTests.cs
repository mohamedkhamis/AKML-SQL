using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Microsoft.Data.SqlClient;
using Npgsql;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 043 follow-up (review, round 3): the schema caches are keyed by session and database, not by server. A SQL Server
/// ConnectionChanged that moves a session to another server, login or authentication method kept the previous server's cache of
/// a database with the same name (dev and prod "AppDb", or master) and never loaded the new one's. The caches are now dropped
/// when the identity they were loaded for (<see cref="SessionState.SchemaIdentity"/>) differs — also after a Disconnect, which
/// keeps that identity — and kept for a database change, a new password, or a tab reattached to its own server. Round 4: a
/// PostgreSQL connect notes its own identity (host, port and role), so a move between the kinds always counts as a move, and its
/// load never fills a SQL Server cache.
/// </summary>
/// <remarks>
/// The servers are made up and every connect is a named-pipe request with a password (no bridge guard, nothing to supply), so
/// the background Phase A each connect starts fails on its own. Only which cache object the manager holds is asserted — never
/// its phase, which that background work may change.
/// </remarks>
public sealed class ConnectionChangedSchemaIdentityTests
{
    private readonly SessionManager _sessions = new();
    private readonly SchemaCacheManager _caches;
    private readonly RpcContext _ctx;

    // A session unique to each test: DatabaseProvider's prefetch is process-wide and keyed by session.
    private readonly string _session = "schema-id-" + Guid.NewGuid().ToString("N")[..8];

    // Spec 043 follow-up (review, round 4): the PostgreSQL path runs only with a catalog reader (the feature on). Nothing reaches it
    // here unless a test calls PopulateSchemaAsync itself: the background facts read before it fails against the made-up hosts.
    private readonly CountingPgProvider _pg = new();

    public ConnectionChangedSchemaIdentityTests()
    {
        _caches = new SchemaCacheManager { KindOf = _sessions.KindOf };
        _ctx = new RpcContext
        {
            Sessions = _sessions,
            SchemaCache = _caches,
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
            ParserService = new TsqlParserService(),
            SchemaMetadata = new SchemaMetadataService(),
            SessionConnections = new SessionConnectionRegistry(),
            PostgresSchemaMetadata = _pg,
        };
    }

    private sealed class CountingPgProvider : ISchemaMetadataProvider
    {
        private int _phaseA;
        public int PhaseACalls => Volatile.Read(ref _phaseA);
        public DatabaseKind Kind => DatabaseKind.PostgreSql;

        public Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            Interlocked.Increment(ref _phaseA);
            cache.Phase = PopulationPhase.PhaseA;
            return Task.CompletedTask;
        }

        public Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            cache.Phase = PopulationPhase.Complete;
            return Task.CompletedTask;
        }

        public Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct) => Task.FromResult(new List<string>());
    }

    // A named-pipe request with a password: no bridge guard, and nothing is read from the machine's credential store.
    private static string Pg(string host, string db = "AppDb", string role = "alice", string password = "pw", int port = 5432) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Database = db, Username = role, Password = password, Timeout = 1,
        }.ConnectionString;

    private ConnectionInfo PgInfo(string connectionString, string db = "AppDb") =>
        new() { SessionId = _session, ConnectionString = connectionString, DatabaseName = db, Kind = (int)DatabaseKind.PostgreSql };

    private Task ConnectPgAsync(string connectionString, string db = "AppDb") =>
        new ConnectionChangedHandler().HandleAsync(PgInfo(connectionString, db), _ctx, CancellationToken.None);

    private static string Sql(string server, string db = "AppDb", string login = "alice", string password = "pw", bool windows = false)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = db, Encrypt = SqlConnectionEncryptOption.Mandatory, ConnectTimeout = 1,
        };
        if (windows) b.IntegratedSecurity = true;
        else { b.UserID = login; b.Password = password; }
        return b.ConnectionString;
    }

    private Task ConnectAsync(string connectionString, string db = "AppDb") =>
        new ConnectionChangedHandler().HandleAsync(
            new ConnectionInfo { SessionId = _session, ConnectionString = connectionString, DatabaseName = db }, _ctx, CancellationToken.None);

    /// <summary>Connects the session to server A and gives it a cache of <paramref name="db"/> as if it had been loaded.</summary>
    private async Task<DatabaseCache> LoadedOnServerAAsync(string db = "AppDb")
    {
        await ConnectAsync(Sql("server-a.test", db), db);
        var cache = _caches.FindCache(_session, db);
        Assert.NotNull(cache);
        return cache!;
    }

    [Fact]
    public async Task Another_server_with_a_database_of_the_same_name_gets_a_new_cache()
    {
        var serverA = await LoadedOnServerAAsync();

        await ConnectAsync(Sql("server-b.test"));

        Assert.NotSame(serverA, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task Every_cache_of_the_previous_server_goes_not_only_the_one_connected_to()
    {
        await LoadedOnServerAAsync();
        var masterOnA = _caches.GetOrCreateCache(_session, "master");   // a USE or the database selector moved the tab once

        await ConnectAsync(Sql("server-b.test"));

        Assert.Null(_caches.FindCache(_session, "master"));
        Assert.NotSame(masterOnA, _caches.GetOrCreateCache(_session, "master"));
    }

    [Fact]
    public async Task Another_login_or_windows_authentication_on_the_same_server_gets_a_new_cache()
    {
        var asAlice = await LoadedOnServerAAsync();

        await ConnectAsync(Sql("server-a.test", login: "bob"));
        var asBob = _caches.FindCache(_session, "AppDb");
        Assert.NotSame(asAlice, asBob);

        await ConnectAsync(Sql("server-a.test", windows: true));
        Assert.NotSame(asBob, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task Another_database_or_a_new_password_on_the_same_server_keeps_the_caches()
    {
        var appDb = await LoadedOnServerAAsync();

        await ConnectAsync(Sql("server-a.test", db: "master"), db: "master");
        Assert.Same(appDb, _caches.FindCache(_session, "AppDb"));

        // The server named in another case and with spaces around it is the same server.
        await ConnectAsync(Sql(" SERVER-A.test ", password: "new-pw"));
        Assert.Same(appDb, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task A_tab_reattached_to_its_own_server_after_a_disconnect_keeps_its_caches()
    {
        var serverA = await LoadedOnServerAAsync();
        _sessions.Disconnect(_session);   // the restored tab's SessionDisconnect: the connection string is gone, the identity kept

        await ConnectAsync(Sql("server-a.test"));

        Assert.Same(serverA, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task A_tab_disconnected_and_then_connected_to_another_server_gets_a_new_cache()
    {
        var serverA = await LoadedOnServerAAsync();
        _sessions.Disconnect(_session);

        await ConnectAsync(Sql("server-b.test"));

        Assert.NotSame(serverA, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task The_identity_is_noted_on_the_session_and_never_holds_the_password()
    {
        await ConnectAsync(Sql("Server-A.test", password: "secret-pw"));

        var identity = _sessions.GetSession(_session)!.SchemaIdentity;
        Assert.Equal(ConnectionChangedHandler.ServerIdentity(Sql("server-a.test", password: "other")), identity);
        Assert.DoesNotContain("secret-pw", identity);
        Assert.Contains("server-a.test", identity);
        Assert.Null(ConnectionChangedHandler.ServerIdentity("this is not = a ; connection = string ;;; ="));
    }

    // ── Spec 043 follow-up (review, round 4): the PostgreSQL path notes its identity too ─────────────────────────────

    [Fact]
    public async Task A_round_trip_through_PostgreSQL_back_to_the_same_SQL_Server_gets_a_new_cache()
    {
        // The PostgreSQL connect left the SQL Server identity on the session, so coming back to the same SQL Server did not count
        // as a move and the tab kept the caches of before the trip.
        var serverA = await LoadedOnServerAAsync();

        await ConnectPgAsync(Pg("pg-a.test"));
        await ConnectAsync(Sql("server-a.test"));

        Assert.NotSame(serverA, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task A_move_from_SQL_Server_to_PostgreSQL_drops_the_SQL_Server_caches()
    {
        var serverA = await LoadedOnServerAAsync();

        await ConnectPgAsync(Pg("pg-a.test"));

        Assert.Equal(0, _caches.CacheCount);
        Assert.NotSame(serverA, _caches.GetOrCreateCache(_session, "AppDb"));   // the PostgreSQL one, from now on
    }

    [Fact]
    public async Task Another_PostgreSQL_server_port_or_role_drops_every_cache_a_new_password_or_database_keeps_them()
    {
        await ConnectPgAsync(Pg("pg-a.test"));
        var other = _caches.GetOrCreateCache(_session, "reports");   // a database the tab moved to once (the selector)

        await ConnectPgAsync(Pg(" PG-A.test ", password: "new-pw"));
        Assert.Same(other, _caches.FindCache(_session, "reports"));
        await ConnectPgAsync(Pg("pg-a.test", db: "reports"), db: "reports");
        Assert.Same(other, _caches.FindCache(_session, "reports"));

        await ConnectPgAsync(Pg("pg-a.test", role: "bob"));
        Assert.Null(_caches.FindCache(_session, "reports"));

        _caches.GetOrCreateCache(_session, "reports");
        await ConnectPgAsync(Pg("pg-a.test", role: "bob", port: 5433));
        Assert.Null(_caches.FindCache(_session, "reports"));

        _caches.GetOrCreateCache(_session, "reports");
        await ConnectPgAsync(Pg("pg-b.test", role: "bob", port: 5433));
        Assert.Null(_caches.FindCache(_session, "reports"));
    }

    [Fact]
    public async Task A_PostgreSQL_tab_reattached_to_its_own_server_after_a_disconnect_keeps_its_caches()
    {
        await ConnectPgAsync(Pg("pg-a.test"));
        var cache = _caches.GetOrCreateCache(_session, "AppDb");
        _sessions.Disconnect(_session);

        await ConnectPgAsync(Pg("pg-a.test"));

        Assert.Same(cache, _caches.FindCache(_session, "AppDb"));
    }

    [Fact]
    public async Task The_PostgreSQL_identity_is_noted_on_the_session_never_holds_the_password_and_never_equals_a_SQL_Server_one()
    {
        await ConnectPgAsync(Pg("PG-A.test", password: "secret-pw"));

        var identity = _sessions.GetSession(_session)!.SchemaIdentity;
        Assert.Equal(ConnectionChangedHandler.PostgresIdentity(Pg("pg-a.test", password: "other")), identity);
        Assert.Contains("pg://pg-a.test:5432", identity);
        Assert.Contains("alice", identity);
        Assert.DoesNotContain("secret-pw", identity);
        Assert.DoesNotContain("AppDb", identity);
        Assert.NotEqual(ConnectionChangedHandler.ServerIdentity(Sql("pg-a.test")), identity);
        Assert.Null(ConnectionChangedHandler.PostgresIdentity(null));
    }

    [Fact]
    public async Task A_PostgreSQL_load_that_finds_the_tab_back_on_SQL_Server_leaves_the_SQL_Server_cache_alone()
    {
        // The PostgreSQL load runs once the server's facts are read; by then the tab may be on SQL Server again, and the cache is
        // looked up by the session's kind now. It must not clear the SQL Server cache and fill it with the PostgreSQL catalog.
        // The session is put on SQL Server directly, so no SQL Server load runs in the background to change the cache's phase.
        _sessions.UpdateSession(new ConnectionInfo { SessionId = _session, ConnectionString = Sql("server-a.test"), DatabaseName = "AppDb" });
        var sqlServerCache = _caches.GetOrCreateCache(_session, "AppDb");
        sqlServerCache.Phase = PopulationPhase.Complete;   // loaded

        await ConnectionChangedHandler.PopulateSchemaAsync(_ctx, PgInfo(Pg("pg-a.test")), identityChanged: true);

        Assert.Equal(0, _pg.PhaseACalls);
        Assert.Same(sqlServerCache, _caches.FindCache(_session, "AppDb"));
        Assert.Equal(DatabaseKind.SqlServer, sqlServerCache.Kind);
        Assert.Equal(PopulationPhase.Complete, sqlServerCache.Phase);
        Assert.False(_caches.IsPopulating(sqlServerCache));
    }

    [Fact]
    public async Task A_late_PostgreSQL_load_of_a_connection_the_tab_has_left_does_not_load_the_new_connections_cache()
    {
        // Alice's facts were read after the tab had reconnected as bob: her load would create bob's cache, take its claim first and
        // fill it with what alice may see, and bob's own load would find it claimed and give up.
        await ConnectPgAsync(Pg("pg-a.test"));
        await ConnectPgAsync(Pg("pg-a.test", role: "bob"));

        await ConnectionChangedHandler.PopulateSchemaAsync(_ctx, PgInfo(Pg("pg-a.test")), identityChanged: true);
        Assert.Equal(0, _pg.PhaseACalls);
        Assert.Null(_caches.FindCache(_session, "AppDb"));

        await ConnectionChangedHandler.PopulateSchemaAsync(_ctx, PgInfo(Pg("pg-a.test", role: "bob")), identityChanged: true);
        Assert.Equal(1, _pg.PhaseACalls);
        Assert.Equal(PopulationPhase.Complete, _caches.FindCache(_session, "AppDb")!.Phase);
    }

    [Fact]
    public async Task A_PostgreSQL_load_still_fills_the_PostgreSQL_cache()
    {
        await ConnectPgAsync(Pg("pg-a.test"));

        await ConnectionChangedHandler.PopulateSchemaAsync(_ctx, PgInfo(Pg("pg-a.test")), identityChanged: false);

        Assert.Equal(1, _pg.PhaseACalls);
        var cache = _caches.FindCache(_session, "AppDb");
        Assert.Equal(DatabaseKind.PostgreSql, cache!.Kind);
        Assert.Equal(PopulationPhase.Complete, cache.Phase);
        Assert.False(_caches.IsPopulating(cache));
    }
}
