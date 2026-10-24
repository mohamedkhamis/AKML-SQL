using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 043 (T022, research R5, contracts/ipc.md §1) — closing a query tab releases its engine session. Before, nothing ever
/// removed a session: <see cref="SessionManager.RemoveSession"/> had no caller and each tab would have left its entry, its
/// document and its schema caches behind for the life of the engine. A plain disconnect keeps them, as it always did (a
/// restored tab reattaches to the same session).
/// </summary>
public sealed class SessionDisconnectReleaseTests
{
    private readonly SessionManager _sessions = new();
    private readonly SchemaCacheManager _caches;
    private readonly RpcContext _ctx;

    public SessionDisconnectReleaseTests()
    {
        _caches = new SchemaCacheManager { KindOf = _sessions.KindOf };
        _ctx = new RpcContext
        {
            Sessions = _sessions,
            SchemaCache = _caches,
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
            SessionConnections = new SessionConnectionRegistry(),
        };
    }

    private void Seed(string sessionId)
    {
        _sessions.UpdateSession(new ConnectionInfo { SessionId = sessionId, ConnectionString = "Server=localhost;Database=tempdb", DatabaseName = "tempdb" });
        _sessions.UpdateDocument(new DocumentChange { SessionId = sessionId, ChangeType = 0, FullText = "SELECT 1" });
        _caches.GetOrCreateCache(sessionId, "tempdb");
        _caches.GetOrCreateCache(sessionId, "master");   // a USE moved the tab once: its caches are all its own
    }

    private Task<SessionDisconnectResponse> DisconnectAsync(string sessionId, bool release) =>
        new SessionDisconnectHandler().HandleAsync(new SessionDisconnectRequest { SessionId = sessionId, Release = release }, _ctx, CancellationToken.None);

    [Fact]
    public async Task Release_forgets_the_session_its_document_and_every_cache_of_it()
    {
        Seed("tab-1");
        Seed("tab-2");

        var reply = await DisconnectAsync("tab-1", release: true);

        Assert.True(reply.Ok);
        Assert.Null(_sessions.GetSession("tab-1"));
        Assert.Null(_caches.FindCache("tab-1", "tempdb"));
        Assert.Null(_caches.FindCache("tab-1", "master"));
        // Another tab, even on the same server and database, keeps everything.
        Assert.NotNull(_sessions.GetSession("tab-2"));
        Assert.NotNull(_caches.FindCache("tab-2", "tempdb"));
        Assert.NotNull(_caches.FindCache("tab-2", "master"));
    }

    [Fact]
    public async Task A_plain_disconnect_keeps_the_session_entry_and_its_caches()
    {
        Seed("tab-1");

        var reply = await DisconnectAsync("tab-1", release: false);

        Assert.True(reply.Ok);
        var session = Assert.IsType<SessionState>(_sessions.GetSession("tab-1"));
        Assert.False(session.IsConnected);
        Assert.NotNull(_caches.FindCache("tab-1", "tempdb"));
    }

    [Fact]
    public async Task Releasing_a_session_the_engine_never_saw_is_a_quiet_success()
    {
        var reply = await DisconnectAsync("never-seen", release: true);

        Assert.True(reply.Ok);
        Assert.Equal(0, _sessions.SessionCount);
    }

    [Fact]
    public async Task A_released_session_id_can_start_again()
    {
        Seed("tab-1");
        await DisconnectAsync("tab-1", release: true);

        Seed("tab-1");

        Assert.True(_sessions.GetSession("tab-1")!.IsConnected);
        Assert.NotNull(_caches.FindCache("tab-1", "tempdb"));
    }
}
