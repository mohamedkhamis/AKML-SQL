using System.Data.Common;
using System.Text.Json;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 (T013, research R1, R2, data-model §3) — every query tab has its own engine session. The connection service's
/// members read the active tab's; the overloads that name a session act on that tab whatever is shown. A restored tab
/// reconnects by disconnecting its persisted session id, AWAITING the reply, then connecting (the engine applies a
/// notification without waiting for an earlier request). Closing a tab releases its session.
/// </summary>
public sealed class QueryTabSessionTests
{
    private sealed class ScriptedBridge : IEngineBridge
    {
        public BridgeState State { get; set; } = BridgeState.Open;
        public string? ConnectedUrl { get; set; } = "ws://127.0.0.1:47291/";
        public string[] EngineCapabilities { get; set; } =
            { SqlConnectionService.CapabilityPostgreSql, SqlConnectionService.CapabilitySessionDisconnect };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged;
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        /// <summary>Requests and notifications in the order they were sent.</summary>
        public List<(int Type, object Message)> Log { get; } = new();

        /// <summary>When set, a SessionDisconnect reply waits for it (to prove the connect waits for the reply).</summary>
        public TaskCompletionSource? HoldDisconnect { get; set; }

        public TestSqlConnectionResponse TestReply { get; set; } = new() { Ok = true, ServerVersion = "PostgreSQL 18.1" };

        public void Reopen(string url)
        {
            ConnectedUrl = url;
            StateChanged?.Invoke(BridgeState.Open);
        }

        public void Drop()
        {
            State = BridgeState.Reconnecting;
            StateChanged?.Invoke(BridgeState.Reconnecting);
        }

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());

        public async Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
        {
            Log.Add((type, request));
            switch (type)
            {
                case MessageTypes.TestSqlConnection: return (TResponse)(object)TestReply;
                case MessageTypes.SessionDisconnect:
                    if (HoldDisconnect != null) await HoldDisconnect.Task;
                    return (TResponse)(object)new SessionDisconnectResponse { Ok = true };
                default: throw new InvalidOperationException($"Unexpected request {type}");
            }
        }

        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class
        {
            Log.Add((type, payload));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private sealed class NoDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
        public IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
        public void Clear() { }
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }

    private readonly ScriptedBridge _bridge = new();
    private readonly SqlConnectionService _sql;

    public QueryTabSessionTests() => _sql = new SqlConnectionService(_bridge, new NoDiagnostics());

    private static ConnectionTarget SqlServer(string database = "tempdb", string server = "localhost") =>
        new(DatabaseKind.SqlServer, server, database, WindowsAuth: true);

    private static ConnectionTarget Remote(bool trust) =>
        new(DatabaseKind.SqlServer, "db1.example.com", "sales", WindowsAuth: false, User: "reporting", Password: "pw", TrustServerCertificate: trust,
            SavedConnectionId: "saved-1");

    private IEnumerable<ConnectionInfo> ConnectionChanges => _bridge.Log.Where(e => e.Type == MessageTypes.ConnectionChanged).Select(e => (ConnectionInfo)e.Message);

    [Fact]
    public async Task Each_tab_has_its_own_session_and_connecting_one_leaves_the_other_alone()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        Assert.NotEqual(a.SessionId, b.SessionId);

        _sql.Activate(a);
        Assert.True((await _sql.ConnectAsync(SqlServer("tempdb"), a, CancellationToken.None)).Ok);
        Assert.True((await _sql.ConnectAsync(SqlServer("master"), b, CancellationToken.None)).Ok);

        Assert.Equal(new[] { a.SessionId, b.SessionId }, ConnectionChanges.Select(c => c.SessionId));
        Assert.Equal("tempdb", a.Database);
        Assert.Equal("master", b.Database);
        Assert.Equal("tempdb", _sql.Database);   // the facade reads the active tab
        Assert.Equal(a.SessionId, _sql.SessionId);

        _sql.Activate(b);
        Assert.Equal("master", _sql.Database);
        Assert.Equal(b.SessionId, _sql.SessionId);
    }

    [Fact]
    public async Task Activating_a_tab_raises_the_facade_event_and_an_inactive_tab_raises_only_its_own()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        _sql.Activate(a);
        int facade = 0, ofB = 0;
        _sql.StateChanged += () => facade++;
        b.StateChanged += () => ofB++;

        await _sql.ConnectAsync(SqlServer(), b, CancellationToken.None);
        Assert.Equal(0, facade);
        Assert.Equal(1, ofB);

        _sql.Activate(b);
        Assert.Equal(1, facade);
    }

    [Fact]
    public async Task A_connect_leaves_a_binding_without_the_password_and_with_the_trust_choice_and_saved_id()
    {
        var tab = _sql.CreateSession();

        await _sql.ConnectAsync(Remote(trust: true), tab, CancellationToken.None);

        var binding = Assert.IsType<ConnectionBinding>(tab.Binding);
        Assert.Equal("db1.example.com", binding.Server);
        Assert.Equal("reporting", binding.Login);
        Assert.True(binding.TrustServerCertificate);
        Assert.Equal("saved-1", binding.SavedConnectionId);
        Assert.DoesNotContain("pw", JsonSerializer.Serialize(binding));
        Assert.DoesNotContain("assword", JsonSerializer.Serialize(binding), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_restored_tab_disconnects_its_old_session_awaits_the_reply_then_connects_with_its_binding()
    {
        var tab = _sql.CreateSession("persisted-session-id");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: true), "saved-1"));
        Assert.True(tab.NeedsReattach);
        _bridge.HoldDisconnect = new TaskCompletionSource();

        var reattach = _sql.ReattachAsync(tab, "pw", CancellationToken.None);
        await Task.Delay(50);
        Assert.Equal(MessageTypes.SessionDisconnect, Assert.Single(_bridge.Log).Type);   // nothing else until the reply
        var disconnect = (SessionDisconnectRequest)_bridge.Log[0].Message;
        Assert.Equal("persisted-session-id", disconnect.SessionId);
        Assert.False(disconnect.Release);

        _bridge.HoldDisconnect.SetResult();
        Assert.True((await reattach).Ok);

        var change = Assert.Single(ConnectionChanges);
        Assert.Equal("persisted-session-id", change.SessionId);
        var cs = new DbConnectionStringBuilder { ConnectionString = change.ConnectionString };
        Assert.Equal("True", cs["TrustServerCertificate"].ToString(), ignoreCase: true);   // the trust choice survives the reload
        Assert.False(tab.NeedsReattach);
        Assert.True(tab.IsConnected);
    }

    [Fact]
    public async Task A_restored_PostgreSQL_tab_reattaches_with_its_TLS_mode_and_CA_path()
    {
        var tab = _sql.CreateSession("pg-tab");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "pg043.example.com", "northwind",
            User: "reporting", SslMode: "verify-ca", RootCertificatePath: @"C:\certs\root.crt", RememberPassword: true), null));

        Assert.True((await _sql.ReattachAsync(tab, null, CancellationToken.None)).Ok);

        var cs = new DbConnectionStringBuilder { ConnectionString = Assert.Single(ConnectionChanges).ConnectionString };
        Assert.Equal("verifyca", cs["SSL Mode"].ToString()!.Replace("-", "").ToLowerInvariant());   // the driver spells it VerifyCA
        Assert.Equal(@"C:\certs\root.crt", cs["Root Certificate"].ToString());
        Assert.Equal(DatabaseKind.PostgreSql, tab.Kind);
    }

    [Fact]
    public async Task Closing_a_tab_releases_its_session_even_when_it_never_connected_in_this_page()
    {
        var tab = _sql.CreateSession("restored-never-used");
        tab.Restore(ConnectionBinding.FromTarget(SqlServer(), null));

        await _sql.DisconnectSessionAsync(tab, release: true, CancellationToken.None);

        var request = (SessionDisconnectRequest)Assert.Single(_bridge.Log).Message;
        Assert.Equal("restored-never-used", request.SessionId);
        Assert.True(request.Release);
    }

    [Fact]
    public async Task The_bridge_reopening_on_another_engine_is_a_new_target_for_every_connected_tab()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        var idle = _sql.CreateSession();
        await _sql.ConnectAsync(SqlServer(), a, CancellationToken.None);
        await _sql.ConnectAsync(SqlServer(), b, CancellationToken.None);
        int genA = a.ConnectionGeneration, genB = b.ConnectionGeneration, genIdle = idle.ConnectionGeneration;

        _bridge.Reopen("ws://10.0.0.5:47291/");

        Assert.Equal(genA + 1, a.ConnectionGeneration);
        Assert.Equal(genB + 1, b.ConnectionGeneration);
        Assert.Equal(genIdle, idle.ConnectionGeneration);
    }

    [Fact]
    public async Task A_link_drop_alone_keeps_the_tabs_as_they_were_so_a_blip_rolls_nothing_back()
    {
        var a = _sql.CreateSession();
        await _sql.ConnectAsync(SqlServer("tempdb"), a, CancellationToken.None);

        _bridge.Drop();

        Assert.True(a.IsConnected);
        Assert.False(a.NeedsReattach);
    }

    [Fact]
    public async Task A_run_that_finds_no_connection_leaves_the_tab_disconnected_with_its_binding_to_reconnect()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        _sql.Activate(a);
        await _sql.ConnectAsync(SqlServer("tempdb"), a, CancellationToken.None);
        await _sql.ConnectAsync(Remote(trust: true), b, CancellationToken.None);
        int facade = 0;
        _sql.StateChanged += () => facade++;
        int generation = a.ConnectionGeneration;

        _sql.MarkConnectionLost(a);   // the engine restarted and answered "no connection" for tab a

        Assert.False(a.IsConnected);
        Assert.True(a.NeedsReattach);
        Assert.Equal("tempdb", a.Binding!.Database);
        Assert.Equal(generation + 1, a.ConnectionGeneration);
        Assert.False(_sql.IsConnected);
        Assert.Equal(1, facade);   // the active tab's status bar follows
        Assert.True(b.IsConnected);   // another tab is not touched

        Assert.True((await _sql.ReattachAsync(a, null, CancellationToken.None)).Ok);   // its next run
        Assert.True(a.IsConnected);
        Assert.False(a.NeedsReattach);
    }

    [Fact]
    public async Task A_USE_reported_for_a_tab_moves_that_tab_and_its_binding_only()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        _sql.Activate(b);
        await _sql.ConnectAsync(SqlServer("tempdb"), a, CancellationToken.None);
        await _sql.ConnectAsync(SqlServer("tempdb"), b, CancellationToken.None);

        _sql.ReportCurrentDatabase(a, "master");

        Assert.Equal("master", a.Database);
        Assert.Equal("master", a.Binding!.Database);   // a reload reconnects to where the script moved it
        Assert.Equal("tempdb", b.Database);
        Assert.Equal("tempdb", _sql.Database);
    }

    [Fact]
    public async Task A_transaction_state_reported_for_a_tab_stays_with_that_tab()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        _sql.Activate(b);
        var pg = new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "northwind", User: "app", Password: "pw");
        await _sql.ConnectAsync(pg, a, CancellationToken.None);
        await _sql.ConnectAsync(pg, b, CancellationToken.None);

        _sql.ReportTransactionState(a, TransactionState.Open);

        Assert.Equal(TransactionState.Open, a.TransactionState);
        Assert.Equal(TransactionState.None, b.TransactionState);
        Assert.Equal(TransactionState.None, _sql.TransactionState);
    }

    // ── T013: the rest of data-model §3 ─────────────────────────────────────────────────────────────────────────

    private sealed class NoDownloads : IFileDownloader
    {
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_restored_tab_sends_nothing_at_load()
    {
        var store = new EditorSessionStore(new InMemoryIndexedDbAdapter());
        await store.SaveTabAsync("t1", new EditorSessionRecord
        {
            TabId = "t1", DocumentText = "SELECT 1;", DocumentName = "SQLQuery1", SessionKey = "k1",
            EngineSessionId = "persisted-s1", Binding = ConnectionBinding.FromTarget(SqlServer(), null),
        });
        await store.SaveSetAsync(new EditorTabSetRecord { Order = new List<string> { "t1" }, ActiveId = "t1", NextDefaultNumber = 2 });
        await store.FlushAsync();
        var tabs = new QueryTabs(store, _sql, new FakeDialogService(), new FakeNotificationService(), new NoDownloads());

        await tabs.LoadAsync();

        Assert.Empty(_bridge.Log);   // the page reconnects the tab when it is shown or run, never at load
        var session = tabs.Active!.Session;
        Assert.Equal("persisted-s1", session.SessionId);
        Assert.True(session.NeedsReattach);
        Assert.False(session.IsConnected);
        Assert.Same(session, _sql.ActiveSession);
    }

    [Fact]
    public async Task An_engine_refusal_keeps_the_binding_for_the_next_try()
    {
        var tab = _sql.CreateSession("refused");
        var binding = ConnectionBinding.FromTarget(SqlServer("Northwind"), "saved-7");
        tab.Restore(binding);
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user." };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(tab.IsConnected);
        Assert.True(tab.NeedsReattach);
        Assert.Equal("Northwind", tab.Binding!.Database);
        Assert.Equal("saved-7", tab.Binding.SavedConnectionId);
        Assert.Empty(ConnectionChanges);
    }

    [Fact]
    public async Task A_restored_SQL_login_tab_connects_with_the_typed_password_after_releasing_its_old_session()
    {
        // The Connect to database dialog (filled from the binding) connects the restored tab: the stale session goes first.
        var tab = _sql.CreateSession("sql-login-tab");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false), "saved-1"));
        Assert.False(tab.Binding!.NeedsNoPassword);

        Assert.True((await _sql.ConnectAsync(tab.Binding.ToTarget("typed-pw"), tab, CancellationToken.None)).Ok);

        var types = _bridge.Log.Select(e => e.Type).ToList();
        Assert.Equal(MessageTypes.SessionDisconnect, types[0]);
        Assert.True(types.IndexOf(MessageTypes.ConnectionChanged) > 0);
        Assert.Equal("sql-login-tab", Assert.Single(ConnectionChanges).SessionId);
        Assert.False(tab.NeedsReattach);
        Assert.DoesNotContain("typed-pw", JsonSerializer.Serialize(tab.Binding));   // the password is never kept
    }

    [Fact]
    public async Task A_tab_already_live_in_this_page_connects_elsewhere_without_a_disconnect_first()
    {
        var tab = _sql.CreateSession();
        Assert.True((await _sql.ConnectAsync(SqlServer("tempdb"), tab, CancellationToken.None)).Ok);
        _bridge.Log.Clear();

        Assert.True((await _sql.ConnectAsync(SqlServer("master"), tab, CancellationToken.None)).Ok);

        Assert.DoesNotContain(_bridge.Log, e => e.Type == MessageTypes.SessionDisconnect);
    }

    [Fact]
    public async Task Two_reattaches_of_one_tab_at_once_send_one_disconnect_and_one_connect()
    {
        // The status bar (a reload on any page) and the editor page (showing the tab) may both ask at once.
        var tab = _sql.CreateSession("shared");
        tab.Restore(ConnectionBinding.FromTarget(SqlServer(), null));
        _bridge.HoldDisconnect = new TaskCompletionSource();

        var first = _sql.ReattachAsync(tab, null, CancellationToken.None);
        var second = _sql.ReattachAsync(tab, null, CancellationToken.None);
        _bridge.HoldDisconnect.SetResult();

        Assert.True((await first).Ok);
        Assert.True((await second).Ok);
        Assert.Same(first, second);
        Assert.Single(_bridge.Log, e => e.Type == MessageTypes.SessionDisconnect);
        Assert.Single(ConnectionChanges);

        tab.NeedsReattach = true;   // a later reattach (after the first ended) is a new one
        Assert.True((await _sql.ReattachAsync(tab, null, CancellationToken.None)).Ok);
        Assert.Equal(2, _bridge.Log.Count(e => e.Type == MessageTypes.SessionDisconnect));
    }
}
