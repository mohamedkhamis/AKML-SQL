using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Server;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (reported 2026-10-22): a new query tab takes the password of the tab it was opened from — only for the
/// same server, login and security options, only into a plain SQL login without one, only from a session the same web page
/// connected, and nothing but the password.
/// </summary>
public class SessionPasswordCopyTests
{
    private const string PageA = "page-a-0123456789abcdef";
    private const string PageB = "page-b-0123456789abcdef";

    private static string Sql(string server, string login, string? password, string db = "Northwind") =>
        new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = db, UserID = login, Password = password ?? string.Empty }.ConnectionString;

    private static string Pg(string host, int port, string user, string? password, string db = "postgres") =>
        new NpgsqlConnectionStringBuilder { Host = host, Port = port, Username = user, Password = password, Database = db }.ConnectionString;

    private static string CopySql(string target, string source) =>
        SessionPasswordCopy.Apply(target, DatabaseKind.SqlServer, source, DatabaseKind.SqlServer);

    [Fact]
    public void The_same_server_and_login_take_the_password_and_keep_their_own_database()
    {
        var copied = new SqlConnectionStringBuilder(CopySql(Sql("LOCALHOST", "Sales", null, db: "master"), Sql("localhost", "sales", "pw")));

        Assert.Equal("pw", copied.Password);
        Assert.Equal("master", copied.InitialCatalog);
    }

    [Theory]
    [InlineData("other-server", "sales")]
    [InlineData("localhost", "other-login")]
    public void Another_server_or_login_gets_nothing(string server, string login)
    {
        var target = Sql(server, login, null);
        Assert.Equal(target, CopySql(target, Sql("localhost", "sales", "pw")));
    }

    [Fact]
    public void A_typed_password_windows_authentication_and_another_kind_are_left_alone()
    {
        var typed = Sql("localhost", "sales", "typed");
        var windows = new SqlConnectionStringBuilder { DataSource = "localhost", IntegratedSecurity = true }.ConnectionString;
        var source = Sql("localhost", "sales", "pw");

        Assert.Equal(typed, CopySql(typed, source));
        Assert.Equal(windows, CopySql(windows, source));
        Assert.Equal(Sql("localhost", "sales", null), SessionPasswordCopy.Apply(Sql("localhost", "sales", null), DatabaseKind.SqlServer, source, DatabaseKind.PostgreSql));
    }

    [Fact]
    public void Weaker_certificate_checking_or_encryption_gets_nothing()
    {
        var source = new SqlConnectionStringBuilder { DataSource = "db1", UserID = "sales", Password = "pw", Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = false }.ConnectionString;
        var trusting = new SqlConnectionStringBuilder { DataSource = "db1", UserID = "sales", Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true }.ConnectionString;
        var plain = new SqlConnectionStringBuilder { DataSource = "db1", UserID = "sales", Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = false }.ConnectionString;

        Assert.Equal(trusting, CopySql(trusting, source));
        Assert.Equal(plain, CopySql(plain, source));
        Assert.Null(new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(
            new NpgsqlConnectionStringBuilder { Host = "db1", Username = "app", SslMode = SslMode.Disable }.ConnectionString, DatabaseKind.PostgreSql,
            new NpgsqlConnectionStringBuilder { Host = "db1", Username = "app", Password = "pw", SslMode = SslMode.VerifyFull }.ConnectionString, DatabaseKind.PostgreSql)).Password);
    }

    // Spec 043 follow-up (review): every option that decides where the password goes, or how the server is checked, is
    // compared — and a request that sets one of its own is no plain SQL login, so it gets nothing even when the source matches.
    [Theory]
    [InlineData("HostNameInCertificate=evil.example")]
    [InlineData("Server Certificate=C:\\certs\\evil.cer")]
    [InlineData("Failover Partner=evil.example")]
    [InlineData("Server SPN=MSSQLSvc/evil.example")]
    [InlineData("Failover Partner SPN=MSSQLSvc/evil.example")]
    [InlineData("Authentication=ActiveDirectoryPassword")]
    public void Each_further_security_option_blocks_the_copy(string option)
    {
        var source = Sql("db1", "sales", "pw");
        var target = Sql("db1", "sales", null) + ";" + option;
        _ = new SqlConnectionStringBuilder(target);   // SqlClient knows the keyword: the refusal is ours, not a parse error

        Assert.Equal(target, CopySql(target, source));                              // the request alone sets it
        Assert.Equal(target, CopySql(target, source + ";" + option));               // both set it: still not a plain SQL login
        Assert.Equal(Sql("db1", "sales", null), CopySql(Sql("db1", "sales", null), source + ";" + option));   // only the source
    }

    [Fact]
    public void Strict_encryption_on_both_sides_copies_and_a_different_one_does_not()
    {
        var strict = ";Encrypt=Strict";

        Assert.Equal("pw", new SqlConnectionStringBuilder(CopySql(Sql("db1", "sales", null) + strict, Sql("db1", "sales", "pw") + strict)).Password);
        Assert.Equal(string.Empty, new SqlConnectionStringBuilder(CopySql(Sql("db1", "sales", null) + strict, Sql("db1", "sales", "pw"))).Password);
        Assert.Equal(string.Empty, new SqlConnectionStringBuilder(CopySql(Sql("db1", "sales", null) + ";Encrypt=false", Sql("db1", "sales", "pw") + ";Encrypt=false")).Password);
    }

    // ---- the origin is the web page (review): a reconnect keeps it, another page or the pipe never has it -------------------

    private static SessionState ConnectedOver(string? socket, string? page)
    {
        var session = new SessionState { SessionId = "s", ConnectionString = Sql("db1", "sales", "pw"), IsConnected = true };
        if (socket is null)
        {
            SessionPasswordCopy.NoteConnected(session);   // the named pipe
            return session;
        }
        using (BridgeConnection.Set(socket))
        {
            BridgeConnection.Adopt(page);
            SessionPasswordCopy.NoteConnected(session);
        }
        return session;
    }

    private static bool MayCopy(SessionState source, string? socket, string? page)
    {
        if (socket is null) return SessionPasswordCopy.MayCopyFrom(source);
        using (BridgeConnection.Set(socket))
        {
            BridgeConnection.Adopt(page);
            return SessionPasswordCopy.MayCopyFrom(source);
        }
    }

    [Fact]
    public void The_same_page_may_copy_on_the_same_socket_and_over_a_new_one_after_a_reconnect()
    {
        var session = ConnectedOver("socket-1", PageA);

        Assert.Equal("page:" + PageA, session.BridgeOrigin);
        Assert.True(MayCopy(session, "socket-1", PageA));
        Assert.True(MayCopy(session, "socket-2", PageA));   // the link dropped and the page reconnected
    }

    [Fact]
    public void Another_page_a_socket_without_a_page_id_and_the_named_pipe_may_not()
    {
        var session = ConnectedOver("socket-1", PageA);

        Assert.False(MayCopy(session, "socket-2", PageB));           // another page (another browser, or another window)
        Assert.False(MayCopy(session, "socket-2", page: null));      // a client that names no page is its socket
        Assert.False(MayCopy(session, socket: null, page: null));    // the named pipe
    }

    [Fact]
    public void A_socket_that_never_named_its_page_copies_only_from_its_own_sessions()
    {
        var session = ConnectedOver("socket-1", page: null);

        Assert.Equal("socket:socket-1", session.BridgeOrigin);
        Assert.True(MayCopy(session, "socket-1", page: null));
        Assert.False(MayCopy(session, "socket-2", page: null));
    }

    [Fact]
    public void A_session_connected_over_the_named_pipe_is_never_a_source()
    {
        var ssms = ConnectedOver(socket: null, page: null);

        Assert.Null(ssms.BridgeOrigin);
        Assert.False(MayCopy(ssms, "socket-1", PageA));
    }

    [Fact]
    public void A_disconnected_source_is_not_copied_and_a_new_connect_forgets_who_made_the_old_one()
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo { SessionId = "tab", ConnectionString = Sql("db1", "sales", "pw") });
        var session = sessions.GetSession("tab")!;
        using (BridgeConnection.Set("socket-1"))
        {
            BridgeConnection.Adopt(PageA);
            SessionPasswordCopy.NoteConnected(session);
        }
        Assert.True(MayCopy(session, "socket-1", PageA));

        sessions.Disconnect("tab");
        Assert.False(MayCopy(session, "socket-1", PageA));

        sessions.UpdateSession(new ConnectionInfo { SessionId = "tab", ConnectionString = Sql("db1", "sales", "pw") });
        Assert.Null(session.BridgeOrigin);                 // until the caller notes who connected it again
        Assert.False(MayCopy(session, "socket-1", PageA));
    }

    [Fact]
    public void PostgreSQL_matches_host_port_and_user()
    {
        var source = Pg("db1", 5432, "app", "pw");

        Assert.Equal("pw", new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("DB1", 5432, "app", null, "sales"), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
        Assert.Null(new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("db1", 5433, "app", null), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
        Assert.Null(new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("db1", 5432, "App", null), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
    }
}
