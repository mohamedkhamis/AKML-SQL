using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;
using Xunit;

namespace AkmlSql.Engine.Tests.Server;

/// <summary>Spec 042 (data-model §3): a session knows its database kind, and a connection change can change it.</summary>
public class SessionManagerKindTests
{
    private static ConnectionInfo Info(string id, DatabaseKind kind, string cs) => new()
    {
        SessionId = id,
        ConnectionString = cs,
        DatabaseName = "northwind",
        Kind = (int)kind,
    };

    [Fact]
    public void A_session_defaults_to_SQL_Server_when_the_peer_sends_no_kind()
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo { SessionId = "s1", ConnectionString = "Server=(local)", DatabaseName = "tempdb" });

        Assert.Equal(DatabaseKind.SqlServer, sessions.GetSession("s1")!.Kind);
        Assert.Equal(DatabaseKind.SqlServer, sessions.KindOf("s1"));
    }

    [Fact]
    public void UpdateSession_records_PostgreSQL_and_a_later_update_can_switch_the_kind()
    {
        var sessions = new SessionManager();

        sessions.UpdateSession(Info("s1", DatabaseKind.PostgreSql, "Host=localhost;Port=5432;Database=northwind;Username=u;Password=secret"));
        Assert.Equal(DatabaseKind.PostgreSql, sessions.KindOf("s1"));

        sessions.GetSession("s1")!.SearchPath = new[] { "sales", "public" };
        sessions.UpdateSession(Info("s1", DatabaseKind.SqlServer, "Server=(local);Database=tempdb"));

        Assert.Equal(DatabaseKind.SqlServer, sessions.KindOf("s1"));
        // A new connection starts without the old one's search path (its transaction went with the persistent connection).
        Assert.Empty(sessions.GetSession("s1")!.SearchPath);
    }

    [Fact]
    public void KindOf_an_unknown_session_is_SQL_Server_so_stray_requests_take_the_existing_path()
    {
        Assert.Equal(DatabaseKind.SqlServer, new SessionManager().KindOf("nobody"));
        Assert.Equal(DatabaseKind.SqlServer, new SessionManager().KindOf(null));
        Assert.Equal(DatabaseKind.SqlServer, new SessionManager().KindOf(""));
    }
}
