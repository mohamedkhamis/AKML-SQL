using System.Net;
using System.Security.Cryptography;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Microsoft.Data.SqlClient;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (review): where a web SQL Server connection's password comes from is decided in one place
/// (<see cref="BridgeSqlPassword.Supply"/>) for ConnectionChanged, TestSqlConnection and ListDatabases — the typed one, else
/// the tab it was opened from, else the remembered one — and a bridge SQL login with none is answered by the engine. A
/// ConnectionChanged that cannot be given a password leaves the session without a connection instead of on the old server, and
/// a test reports whether the password it was asked to remember is really kept.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
public sealed class BridgeSqlPasswordTests : IDisposable
{
    private const string PageA = "page-a-0123456789abcdef";
    private const string PageB = "page-b-0123456789abcdef";

    // A remote server unique to this class (the bridge guard allows a SQL login to it), so the process-wide store never mixes.
    private readonly string _server = "sqlpw-" + Guid.NewGuid().ToString("N")[..8] + ".test";
    private readonly RpcContext _ctx;

    public BridgeSqlPasswordTests()
    {
        var sessions = new SessionManager();
        _ctx = new RpcContext
        {
            Sessions = sessions,
            SchemaCache = new SchemaCacheManager { KindOf = sessions.KindOf },
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
            ParserService = new TsqlParserService(),
            SchemaMetadata = new SchemaMetadataService(),
            SessionConnections = new SessionConnectionRegistry(),
        };
    }

    public void Dispose()
    {
        SqlServerCredentialResolver.SaveToStore = SqlCredentialStore.Save;
        SqlServerCredentialResolver.RemoveFromStore = SqlCredentialStore.Remove;
        SqlServerCredentialResolver.Forget(_server, "alice");
    }

    private string Cs(string? password, string? server = null, bool trust = true, string db = "Northwind") =>
        new SqlConnectionStringBuilder
        {
            DataSource = server ?? _server, InitialCatalog = db, UserID = "alice", Password = password ?? string.Empty,
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = trust, ConnectTimeout = 1,
        }.ConnectionString;

    private static string PasswordOf(string cs) => new SqlConnectionStringBuilder(cs).Password;

    /// <summary>A request from a web page: the bridge's source address and the page's connection, as the transport sets them.</summary>
    private static IDisposable FromPage(string? page, string socket = "socket-1")
    {
        var ip = BridgeSourceIp.Set(IPAddress.Loopback);
        var connection = BridgeConnection.Set(socket);
        BridgeConnection.Adopt(page);
        return new Both(connection, ip);
    }

    private sealed class Both(IDisposable inner, IDisposable outer) : IDisposable
    {
        public void Dispose() { inner.Dispose(); outer.Dispose(); }
    }

    /// <summary>A tab session connected by <paramref name="page"/> with a password.</summary>
    private void ConnectSource(string sessionId, string? page, DatabaseKind kind = DatabaseKind.SqlServer)
    {
        _ctx.Sessions.UpdateSession(new ConnectionInfo { SessionId = sessionId, ConnectionString = Cs("tab-pw"), DatabaseName = "Northwind", Kind = (int)kind });
        using (FromPage(page)) SessionPasswordCopy.NoteConnected(_ctx.Sessions.GetSession(sessionId));
    }

    // ---- Supply -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_typed_password_is_used_as_it_is()
    {
        using (FromPage(PageA))
        {
            var supplied = BridgeSqlPassword.Supply(Cs("typed"), copyFromSessionId: null, _ctx);

            Assert.Equal(Cs("typed"), supplied.ConnectionString);
            Assert.True(supplied.Typed);
            Assert.False(supplied.Missing);
        }
    }

    [Fact]
    public void A_bridge_SQL_login_with_no_password_from_anywhere_is_missing_and_windows_authentication_is_not()
    {
        using (FromPage(PageA))
        {
            Assert.True(BridgeSqlPassword.Supply(Cs(null), null, _ctx).Missing);
            Assert.False(BridgeSqlPassword.Supply(new SqlConnectionStringBuilder { DataSource = "localhost", IntegratedSecurity = true }.ConnectionString, null, _ctx).Missing);
        }
        // The SSMS pipe opens its own strings: nothing is filled in and nothing is refused.
        var pipe = BridgeSqlPassword.Supply(Cs(null), null, _ctx);
        Assert.False(pipe.Missing);
        Assert.Equal(Cs(null), pipe.ConnectionString);
    }

    [Fact]
    public void The_tab_it_was_opened_from_gives_its_password_to_the_same_page_only()
    {
        ConnectSource("tab-1", PageA);

        using (FromPage(PageA, socket: "socket-2"))   // the same page, over the socket it reopened after a link drop
        {
            var supplied = BridgeSqlPassword.Supply(Cs(null), "tab-1", _ctx);
            Assert.Equal("tab-pw", PasswordOf(supplied.ConnectionString));
            Assert.False(supplied.Typed);
            Assert.False(supplied.Missing);
        }
        using (FromPage(PageB)) Assert.True(BridgeSqlPassword.Supply(Cs(null), "tab-1", _ctx).Missing);
        using (FromPage(page: null, socket: "socket-1")) Assert.True(BridgeSqlPassword.Supply(Cs(null), "tab-1", _ctx).Missing);
    }

    [Fact]
    public void A_source_of_another_kind_or_an_unknown_session_gives_nothing_and_a_missing_context_does_not_throw()
    {
        ConnectSource("pg-tab", PageA, DatabaseKind.PostgreSql);

        using (FromPage(PageA))
        {
            Assert.True(BridgeSqlPassword.Supply(Cs(null), "pg-tab", _ctx).Missing);
            Assert.True(BridgeSqlPassword.Supply(Cs(null), "no-such-tab", _ctx).Missing);
            Assert.True(BridgeSqlPassword.Supply(Cs(null), "pg-tab", ctx: null).Missing);
        }
    }

    [Fact]
    public void Without_a_tab_to_copy_from_the_remembered_password_is_used()
    {
        Assert.True(SqlServerCredentialResolver.RememberIfRequested(Cs("remembered"), remember: true, fromBridge: true));

        using (FromPage(PageA))
        {
            var supplied = BridgeSqlPassword.Supply(Cs(null), copyFromSessionId: "no-such-tab", _ctx);
            Assert.Equal("remembered", PasswordOf(supplied.ConnectionString));
            Assert.False(supplied.Missing);
        }
    }

    // ---- ConnectionChanged -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_connect_the_engine_cannot_give_a_password_leaves_the_session_without_a_connection()
    {
        // The tab was on server A; the dialog moved it to this server, but the tab it was opened from closed in between.
        _ctx.Sessions.UpdateSession(new ConnectionInfo { SessionId = "tab-2", ConnectionString = Cs("a-pw", server: "server-a.test"), DatabaseName = "Northwind" });

        using (FromPage(PageA))
            await new ConnectionChangedHandler().HandleAsync(
                new ConnectionInfo { SessionId = "tab-2", ConnectionString = Cs(null), DatabaseName = "Northwind", CopyPasswordFromSessionId = "tab-1" }, _ctx, default);

        var session = _ctx.Sessions.GetSession("tab-2")!;
        Assert.False(session.IsConnected);                 // the next run answers "no connection" — never runs on server A
        Assert.Equal(string.Empty, session.ConnectionString);
    }

    [Fact]
    public async Task A_connect_the_engine_cannot_give_a_password_moves_the_session_to_the_new_target_and_drops_the_old_schema()
    {
        // Review round 2: the tab was a PostgreSQL tab connected by this page, with a loaded schema of the same database name.
        // Disconnecting alone left its kind, database and schema on the old target: completion kept serving the old server's
        // objects (and a PostgreSQL session kept the PostgreSQL services) while the web showed the new target.
        const string pg = "Host=pg-old.test;Port=5432;Database=Northwind;Username=alice;Password=pg-pw;SSL Mode=Require";
        _ctx.Sessions.UpdateSession(new ConnectionInfo { SessionId = "tab-2", ConnectionString = pg, DatabaseName = "Northwind", Kind = (int)DatabaseKind.PostgreSql });
        using (FromPage(PageA)) SessionPasswordCopy.NoteConnected(_ctx.Sessions.GetSession("tab-2"));
        var oldCache = _ctx.SchemaCache.GetOrCreateCache("tab-2", "Northwind");
        oldCache.Phase = PopulationPhase.PhaseB;

        using (FromPage(PageA))
            await new ConnectionChangedHandler().HandleAsync(
                new ConnectionInfo { SessionId = "tab-2", ConnectionString = Cs(null, db: "Northwind"), DatabaseName = "Northwind", Kind = (int)DatabaseKind.SqlServer }, _ctx, default);

        var session = _ctx.Sessions.GetSession("tab-2")!;
        Assert.False(session.IsConnected);
        Assert.Equal(DatabaseKind.SqlServer, session.Kind);                // the request's kind: the SQL Server services serve it
        Assert.Equal(DatabaseKind.SqlServer, _ctx.Sessions.KindOf("tab-2"));
        Assert.Equal("Northwind", session.DatabaseName);
        Assert.Null(session.BridgeOrigin);
        Assert.Equal(0, _ctx.SchemaCache.CacheCount);                      // the old target's objects are gone, and no load started

        // A new tab of the same page can no longer take anything from it.
        using (FromPage(PageA))
        {
            Assert.False(SessionPasswordCopy.MayCopyFrom(session));
            Assert.True(BridgeSqlPassword.Supply(Cs(null), "tab-2", _ctx).Missing);
        }
    }

    [Fact]
    public async Task A_connect_the_engine_cannot_give_a_password_drops_a_schema_of_the_same_database_name_on_the_old_server()
    {
        _ctx.Sessions.UpdateSession(new ConnectionInfo { SessionId = "tab-3", ConnectionString = Cs("a-pw", server: "server-a.test"), DatabaseName = "master" });
        using (FromPage(PageA)) SessionPasswordCopy.NoteConnected(_ctx.Sessions.GetSession("tab-3"));
        _ctx.SchemaCache.GetOrCreateCache("tab-3", "master").Phase = PopulationPhase.PhaseB;

        using (FromPage(PageA))
            await new ConnectionChangedHandler().HandleAsync(
                new ConnectionInfo { SessionId = "tab-3", ConnectionString = Cs(null, db: "master"), DatabaseName = "master" }, _ctx, default);

        Assert.Null(_ctx.SchemaCache.FindCache("tab-3", "master"));        // never server A's master offered for this server
        using (FromPage(PageA))
            Assert.True(BridgeSqlPassword.Supply(Cs(null, server: "server-a.test"), "tab-3", _ctx).Missing);   // nor server A's password
    }

    [Fact]
    public async Task A_new_tab_connects_with_the_password_of_the_tab_it_was_opened_from_and_is_noted_as_the_pages()
    {
        ConnectSource("tab-1", PageA);

        using (FromPage(PageA))
            await new ConnectionChangedHandler().HandleAsync(
                new ConnectionInfo { SessionId = "tab-2", ConnectionString = Cs(null, db: "master"), DatabaseName = "master", CopyPasswordFromSessionId = "tab-1" }, _ctx, default);

        var session = _ctx.Sessions.GetSession("tab-2")!;
        Assert.True(session.IsConnected);
        Assert.Equal("tab-pw", PasswordOf(session.ConnectionString));
        Assert.Equal("master", new SqlConnectionStringBuilder(session.ConnectionString).InitialCatalog);   // the password only, not the database
        Assert.Equal("page:" + PageA, session.BridgeOrigin);
    }

    // ---- TestSqlConnection -------------------------------------------------------------------------------------------------

    private string? _opened;

    private TestSqlConnectionHandler AcceptingHandler() => new((cs, _) =>
    {
        _opened = cs;
        return Task.FromResult("16.00.4135");
    });

    private Task<TestSqlConnectionResponse> TestAsync(string cs, bool remember, string? copyFrom = null) =>
        AcceptingHandler().HandleAsync(new TestSqlConnectionRequest { ConnectionString = cs, RememberPassword = remember, CopyPasswordFromSessionId = copyFrom }, _ctx, default);

    [Fact]
    public async Task An_accepted_typed_password_asked_to_be_remembered_is_reported_remembered()
    {
        TestSqlConnectionResponse reply;
        using (FromPage(PageA)) reply = await TestAsync(Cs("typed"), remember: true);

        Assert.True(reply.Ok);
        Assert.True(reply.Remembered);
        Assert.Equal("SQL Server 16.00.4135", reply.ServerVersion);
        Assert.True(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public async Task An_accepted_login_stays_accepted_when_the_store_cannot_keep_the_password()
    {
        TestSqlConnectionResponse unwritten, unencrypted;
        using (FromPage(PageA))
        {
            SqlServerCredentialResolver.SaveToStore = (_, _, _, _) => false;
            unwritten = await TestAsync(Cs("typed"), remember: true);
            SqlServerCredentialResolver.SaveToStore = (_, _, _, _) => throw new CryptographicException("DPAPI is not available");
            unencrypted = await TestAsync(Cs("typed"), remember: true);
        }

        Assert.True(unwritten.Ok);
        Assert.False(unwritten.Remembered);
        Assert.True(unencrypted.Ok);
        Assert.False(unencrypted.Remembered);
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public async Task A_password_that_was_not_typed_or_not_asked_to_be_remembered_is_not_reported_remembered()
    {
        ConnectSource("tab-1", PageA);
        TestSqlConnectionResponse copied, notAsked;
        using (FromPage(PageA))
        {
            copied = await TestAsync(Cs(null), remember: true, copyFrom: "tab-1");
            Assert.Equal("tab-pw", PasswordOf(_opened!));    // tested with the tab's password …
            notAsked = await TestAsync(Cs("typed"), remember: false);
        }

        Assert.True(copied.Ok);
        Assert.False(copied.Remembered);                     // … which is not saved: it was never typed here
        Assert.True(notAsked.Ok);
        Assert.False(notAsked.Remembered);
        Assert.False(SqlServerCredentialResolver.Has(_server, "alice"));
    }

    [Fact]
    public async Task A_test_with_no_password_from_anywhere_is_answered_without_contacting_the_server()
    {
        TestSqlConnectionResponse reply;
        using (FromPage(PageA)) reply = await TestAsync(Cs(null), remember: true, copyFrom: "no-such-tab");

        Assert.False(reply.Ok);
        Assert.Equal(DialectErrors.PasswordRequired, reply.ErrorCode);
        Assert.Null(_opened);
    }

    // ---- ListDatabases -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_list_with_no_password_from_anywhere_is_answered_without_contacting_the_server()
    {
        ListDatabasesResponse reply;
        using (FromPage(PageA))
            reply = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { ConnectionString = Cs(null) }, _ctx, default);

        Assert.False(reply.Ok);
        Assert.Equal(DialectErrors.PasswordRequired, reply.ErrorCode);
    }

    [Fact]
    public async Task A_new_tabs_list_takes_the_password_of_the_tab_it_was_opened_from()
    {
        ConnectSource("tab-1", PageA);

        ListDatabasesResponse reply;
        using (FromPage(PageA))
            reply = await new ListDatabasesHandler().HandleAsync(
                new ListDatabasesRequest { ConnectionString = Cs(null), CopyPasswordFromSessionId = "tab-1" }, _ctx, default);

        // The made-up server cannot be reached, so the list fails — but at the server, with the copied password, not before it.
        Assert.False(reply.Ok);
        Assert.NotEqual(DialectErrors.PasswordRequired, reply.ErrorCode);
    }
}
