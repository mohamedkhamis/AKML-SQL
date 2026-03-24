using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;
using Xunit;

namespace AkmlSql.Core.Tests.Server;

public class SessionManagerTests
{
    private static ConnectionInfo MakeConnection(string sessionId = "s1",
                                                  string db = "TestDb",
                                                  string connStr = "Server=.;Database=TestDb;") =>
        new()
        {
            SessionId = sessionId,
            ConnectionString = connStr,
            DatabaseName = db,
            ServerVersion = 16,
            EngineEdition = 3
        };

    // ── UpdateSession / GetSession ────────────────────────────────────────────

    [Fact]
    public void UpdateSession_CreatesNewSession()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("abc"));

        var session = mgr.GetSession("abc");
        Assert.NotNull(session);
        Assert.Equal("abc", session.SessionId);
        Assert.Equal("TestDb", session.DatabaseName);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public void UpdateSession_UpdatesExistingSession()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("abc", db: "OldDb"));
        mgr.UpdateSession(MakeConnection("abc", db: "NewDb"));

        var session = mgr.GetSession("abc");
        Assert.Equal("NewDb", session!.DatabaseName);
        Assert.Equal(1, mgr.SessionCount);
    }

    [Fact]
    public void UpdateSession_IncrementsSessionCount()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));
        mgr.UpdateSession(MakeConnection("s2"));
        Assert.Equal(2, mgr.SessionCount);
    }

    [Fact]
    public void GetSession_ReturnsNull_ForUnknownSession()
    {
        var mgr = new SessionManager();
        Assert.Null(mgr.GetSession("nonexistent"));
    }

    // ── RemoveSession ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSession_DeletesSession()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));
        mgr.RemoveSession("s1");

        Assert.Null(mgr.GetSession("s1"));
        Assert.Equal(0, mgr.SessionCount);
    }

    [Fact]
    public void RemoveSession_IsIdempotent_ForMissingSession()
    {
        var mgr = new SessionManager();
        // Should not throw
        mgr.RemoveSession("nonexistent");
        Assert.Equal(0, mgr.SessionCount);
    }

    // ── UpdateDocument ────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDocument_SetsDocumentText_WhenSessionExists()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0,
            FullText = "SELECT 1"
        });

        var session = mgr.GetSession("s1");
        Assert.Equal("SELECT 1", session!.DocumentText);
    }

    [Fact]
    public void UpdateDocument_IsIgnored_WhenSessionDoesNotExist()
    {
        var mgr = new SessionManager();
        // Should not throw — session doesn't exist
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "ghost",
            ChangeType = 0,
            FullText = "SELECT 1"
        });
    }

    [Fact]
    public void UpdateDocument_IgnoresUpdate_WhenTextExceedsSizeLimit()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));

        // Set an initial value
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0,
            FullText = "SELECT 1"
        });

        // Attempt to set a value exceeding 10 MB
        var oversized = new string('x', 10 * 1024 * 1024 + 1);
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0,
            FullText = oversized
        });

        // Previous value should be preserved
        var session = mgr.GetSession("s1");
        Assert.Equal("SELECT 1", session!.DocumentText);
    }

    [Fact]
    public void UpdateDocument_AcceptsText_ExactlyAtSizeLimit()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));

        var atLimit = new string('y', 10 * 1024 * 1024);
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0,
            FullText = atLimit
        });

        var session = mgr.GetSession("s1");
        Assert.Equal(10 * 1024 * 1024, session!.DocumentText.Length);
    }

    [Fact]
    public void UpdateDocument_DoesNotUpdate_WhenChangeTypeIsNotFull()
    {
        var mgr = new SessionManager();
        mgr.UpdateSession(MakeConnection("s1"));

        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 0,
            FullText = "SELECT 1"
        });

        // ChangeType = 1 (incremental) — not handled, text stays unchanged
        mgr.UpdateDocument(new DocumentChange
        {
            SessionId = "s1",
            ChangeType = 1,
            FullText = "SELECT 2"
        });

        var session = mgr.GetSession("s1");
        Assert.Equal("SELECT 1", session!.DocumentText);
    }

    // ── SessionCount ──────────────────────────────────────────────────────────

    [Fact]
    public void SessionCount_IsZero_Initially()
    {
        var mgr = new SessionManager();
        Assert.Equal(0, mgr.SessionCount);
    }
}
