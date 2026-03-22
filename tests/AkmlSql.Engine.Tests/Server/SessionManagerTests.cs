using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;

namespace AkmlSql.Engine.Tests.Server;

public class SessionManagerTests
{
    private readonly SessionManager _mgr = new();

    private static ConnectionInfo MakeConnection(string sessionId, string db = "TestDb") => new()
    {
        SessionId = sessionId,
        ConnectionString = $"Server=.;Database={db};",
        DatabaseName = db,
        ServerVersion = 170,
        EngineEdition = 3
    };

    // ── UpdateSession ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateSession_NewSession_Stored()
    {
        _mgr.UpdateSession(MakeConnection("s1"));

        var session = _mgr.GetSession("s1");

        Assert.NotNull(session);
        Assert.Equal("s1", session.SessionId);
    }

    [Fact]
    public void UpdateSession_ExistingSession_UpdatesConnectionString()
    {
        _mgr.UpdateSession(MakeConnection("s1", "OldDb"));
        _mgr.UpdateSession(MakeConnection("s1", "NewDb"));

        var session = _mgr.GetSession("s1");

        Assert.NotNull(session);
        Assert.Equal("NewDb", session.DatabaseName);
    }

    [Fact]
    public void UpdateSession_SetsIsConnectedTrue()
    {
        _mgr.UpdateSession(MakeConnection("s1"));

        var session = _mgr.GetSession("s1");

        Assert.True(session!.IsConnected);
    }

    [Fact]
    public void UpdateSession_SetsServerVersion()
    {
        _mgr.UpdateSession(MakeConnection("s1"));

        var session = _mgr.GetSession("s1");

        Assert.Equal(170, session!.ServerVersion);
    }

    // ── UpdateDocument ────────────────────────────────────────────────────

    [Fact]
    public void UpdateDocument_UpdatesDocumentText()
    {
        _mgr.UpdateSession(MakeConnection("s1"));

        _mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0, // Full
            FullText = "SELECT 1"
        });

        var session = _mgr.GetSession("s1");
        Assert.Equal("SELECT 1", session!.DocumentText);
    }

    [Fact]
    public void UpdateDocument_UnknownSession_NoThrow()
    {
        var ex = Record.Exception(() => _mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "ghost",
            ChangeType = 0,
            FullText = "SELECT 1"
        }));

        Assert.Null(ex);
    }

    // ── GetSession ────────────────────────────────────────────────────────

    [Fact]
    public void GetSession_Unknown_ReturnsNull()
    {
        var session = _mgr.GetSession("unknown-id");

        Assert.Null(session);
    }

    // ── RemoveSession ─────────────────────────────────────────────────────

    [Fact]
    public void RemoveSession_ExistingSession_Removed()
    {
        _mgr.UpdateSession(MakeConnection("s1"));
        _mgr.RemoveSession("s1");

        var session = _mgr.GetSession("s1");

        Assert.Null(session);
    }

    [Fact]
    public void RemoveSession_UnknownSession_NoThrow()
    {
        var ex = Record.Exception(() => _mgr.RemoveSession("nonexistent"));

        Assert.Null(ex);
    }

    // ── SessionCount ──────────────────────────────────────────────────────

    [Fact]
    public void SessionCount_StartsAtZero()
    {
        Assert.Equal(0, _mgr.SessionCount);
    }

    [Fact]
    public void SessionCount_IncreasesAfterAdd()
    {
        _mgr.UpdateSession(MakeConnection("s1"));
        _mgr.UpdateSession(MakeConnection("s2"));
        _mgr.UpdateSession(MakeConnection("s3"));

        Assert.Equal(3, _mgr.SessionCount);
    }

    [Fact]
    public void SessionCount_DecreasesAfterRemove()
    {
        _mgr.UpdateSession(MakeConnection("s1"));
        _mgr.UpdateSession(MakeConnection("s2"));
        _mgr.RemoveSession("s1");

        Assert.Equal(1, _mgr.SessionCount);
    }

    // ── Thread safety ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSession_Concurrent_NoException()
    {
        const int count = 50;
        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(() => _mgr.UpdateSession(MakeConnection($"session-{i}"))))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(count, _mgr.SessionCount);
    }
}
