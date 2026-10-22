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

        public CredentialResponse CredentialReply { get; set; } = new() { Ok = true, Has = true };

        /// <summary>When true, a credential request waits until it is cancelled: an engine that never answers.</summary>
        public bool HangCredentials { get; set; }

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
                case MessageTypes.ListDatabases: return (TResponse)(object)new ListDatabasesResponse { Ok = true, Databases = new List<string> { "master" } };
                case MessageTypes.CredentialRequest:
                    if (HangCredentials) await Task.Delay(Timeout.Infinite, ct);
                    return (TResponse)(object)CredentialReply;
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

    // Spec 043 follow-up (reported 2026-10-22): a SQL login whose password the engine remembers reconnects like Windows
    // authentication — the connect asks the engine to remember a typed password, and a restored tab sends none.
    [Fact]
    public async Task A_SQL_login_remembered_on_the_engine_asks_it_to_remember_and_reconnects_without_a_password()
    {
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySqlServerCredentials };
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true, ServerVersion = "SQL Server 16.0", Remembered = true };   // the engine kept it
        var remembered = Remote(trust: true) with { RememberPassword = true };
        var tab = _sql.CreateSession("remembered-login");
        var connected = await _sql.ConnectAsync(remembered, tab, CancellationToken.None);
        Assert.True(connected.Ok);
        Assert.True(connected.Remembered);
        // The connect's test is the open that proves the password, so it carries the request to remember it.
        var test = (TestSqlConnectionRequest)_bridge.Log.First(e => e.Type == MessageTypes.TestSqlConnection).Message;
        Assert.True(test.RememberPassword);
        Assert.Single(ConnectionChanges);
        Assert.True(tab.Binding!.NeedsNoPassword);
        Assert.True(tab.Binding.TrustServerCertificate);

        var restored = _sql.CreateSession("restored-login");
        restored.Restore(tab.Binding.Clone());
        Assert.True((await _sql.ReattachAsync(restored, null, CancellationToken.None)).Ok);
        var reattach = ConnectionChanges.Last();
        Assert.Equal("restored-login", reattach.SessionId);
        Assert.False(((TestSqlConnectionRequest)_bridge.Log.Last(e => e.Type == MessageTypes.TestSqlConnection).Message).RememberPassword);   // nothing typed
        var sent = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = reattach.ConnectionString };
        Assert.Equal(string.Empty, sent.TryGetValue("Password", out var pw) ? pw?.ToString() ?? string.Empty : string.Empty);
        Assert.Equal("reporting", sent["User ID"]);
    }

    [Fact]
    public async Task A_SQL_login_with_no_typed_password_that_the_engine_refuses_asks_for_the_password()
    {
        var tab = _sql.CreateSession("refused-remembered");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySqlServerCredentials };
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "The server needs the password for this login.", ErrorCode = DialectErrors.PasswordRequired };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.True(result.PasswordRequired);
        Assert.Empty(ConnectionChanges);
        Assert.False(tab.CanReconnectAlone);   // no quiet retries: the next run asks
    }

    [Fact]
    public async Task A_server_that_is_down_is_reported_as_it_is_not_as_a_password_prompt()
    {
        var tab = _sql.CreateSession("server-down");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySqlServerCredentials };
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "A network-related or instance-specific error occurred." };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.PasswordRequired);
        Assert.Contains("network-related", result.Error);
    }

    [Fact]
    public async Task An_engine_that_cannot_supply_a_password_is_not_sent_an_empty_one()
    {
        var tab = _sql.CreateSession("old-engine");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));   // capabilities: neither

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.True(result.PasswordRequired);
        Assert.DoesNotContain(_bridge.Log, e => e.Type == MessageTypes.TestSqlConnection);
    }

    [Fact]
    public async Task New_query_names_the_tab_it_was_opened_from_and_the_engine_copies_its_password()
    {
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySessionCopy };
        var source = _sql.CreateSession("source-tab");
        Assert.True((await _sql.ConnectAsync(Remote(trust: true), source, CancellationToken.None)).Ok);
        var copy = _sql.CreateSession("new-tab");
        copy.Restore(source.Binding!.Clone());
        copy.CopyPasswordFrom = source.SessionId;
        Assert.True(copy.CanReconnectAlone);

        Assert.True((await _sql.ReattachAsync(copy, null, CancellationToken.None)).Ok);

        var test = (TestSqlConnectionRequest)_bridge.Log.Last(e => e.Type == MessageTypes.TestSqlConnection).Message;
        Assert.Equal("source-tab", test.CopyPasswordFromSessionId);
        Assert.Equal("source-tab", ConnectionChanges.Last().CopyPasswordFromSessionId);
        Assert.Null(copy.CopyPasswordFrom);   // connected: it has its own session now
    }

    [Fact]
    public void A_SQL_login_is_free_of_a_typed_password_only_when_remembered()
    {
        Assert.False(ConnectionBinding.FromTarget(Remote(trust: false), null).NeedsNoPassword);
        Assert.True(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, null).NeedsNoPassword);
        Assert.True(ConnectionBinding.FromTarget(SqlServer(), null).NeedsNoPassword);
    }

    // ── Spec 043 follow-up (review) ──────────────────────────────────────────────────────────────────────────────────────────

    private void EngineRemembersAndCopies() => _bridge.EngineCapabilities = new[]
    {
        SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySqlServerCredentials,
        SqlConnectionService.CapabilityPostgreSql, SqlConnectionService.CapabilitySessionCopy,
        SqlConnectionService.CapabilitySqlServerCredentialsV2, SqlConnectionService.CapabilitySessionCopyV2,
    };

    // Review, round 2: engines 1.26.1022.0927 and 1.26.1022.1042 — the v1 ids only. They never send Remembered (key 4), ignore a
    // database list's copy source (key 3) and the page's id (key 6), and send no postgresql.login-failed.
    private void ReleasedV1Engine() => _bridge.EngineCapabilities = new[]
    {
        SqlConnectionService.CapabilitySessionDisconnect, SqlConnectionService.CapabilitySqlServerCredentials,
        SqlConnectionService.CapabilityPostgreSql, SqlConnectionService.CapabilitySessionCopy,
    };

    // [1] A remembered (or copied) password the server refuses: every quiet retry would be another failed login.
    [Fact]
    public async Task A_remembered_SQL_password_the_server_refuses_stops_the_tab_reconnecting_on_its_own()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("stale-password");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        Assert.True(tab.CanReconnectAlone);
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user 'reporting'.", ErrorCode = DialectErrors.LoginFailed };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(result.PasswordRequired);   // worded as the server's refusal, not a prompt
        Assert.True(tab.PasswordMissing);
        Assert.False(tab.CanReconnectAlone);     // the next run asks through the dialog
        Assert.True(tab.NeedsReattach);
        Assert.Empty(ConnectionChanges);
    }

    [Fact]
    public async Task A_typed_SQL_password_the_server_refuses_does_not_mark_the_tab()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("typo");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user 'reporting'.", ErrorCode = DialectErrors.LoginFailed };

        var result = await _sql.ConnectAsync(tab.Binding!.ToTarget("wrong-pw"), tab, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(tab.PasswordMissing);
        Assert.True(tab.CanReconnectAlone);   // the engine's own password was never tried
    }

    [Fact]
    public async Task A_copied_SQL_password_the_server_refuses_marks_the_tab_too()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("copy-refused");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false), "saved-1"));
        tab.CopyPasswordFrom = "source-tab";
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed.", ErrorCode = DialectErrors.LoginFailed };

        await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.True(tab.PasswordMissing);
        Assert.Null(tab.CopyPasswordFrom);
        Assert.False(tab.CanReconnectAlone);
    }

    [Fact]
    public async Task A_remembered_PostgreSQL_password_the_server_refuses_by_its_code_stops_the_tab_reconnecting_on_its_own()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("pg-stale");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", RememberPassword: true), null));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "authentification échouée", ErrorCode = DialectErrors.PgLoginFailed };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(DialectErrors.PgLoginFailed, result.ErrorCode);   // the code reaches the dialog's wording too
        Assert.True(tab.PasswordMissing);
        Assert.False(tab.CanReconnectAlone);
    }

    [Fact]
    public async Task A_typed_PostgreSQL_password_the_server_refuses_does_not_mark_the_tab()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("pg-typo");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", RememberPassword: true), null));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "password authentication failed", ErrorCode = DialectErrors.PgLoginFailed };

        await _sql.ConnectAsync(tab.Binding!.ToTarget("wrong-pw"), tab, CancellationToken.None);

        Assert.False(tab.PasswordMissing);
        Assert.True(tab.CanReconnectAlone);
    }

    // [9] Remember off: the engine's copy is not used, whatever it holds.
    [Fact]
    public async Task A_SQL_login_not_asked_to_be_remembered_with_nothing_typed_asks_without_a_round_trip()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("not-remembered");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { Password = null }, tab, CancellationToken.None);

        Assert.True(result.PasswordRequired);
        Assert.True(tab.PasswordMissing);
        Assert.Empty(_bridge.Log);
    }

    // [5] The tab is remembered only when the engine says it stored the password.
    [Fact]
    public async Task A_password_the_engine_could_not_keep_leaves_the_tab_asking_after_a_reload()
    {
        EngineRemembersAndCopies();
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true, Remembered = false };   // the store could not be written
        var tab = _sql.CreateSession("not-kept");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { RememberPassword = true }, tab, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(result.Remembered);
        Assert.True(((TestSqlConnectionRequest)_bridge.Log.First(e => e.Type == MessageTypes.TestSqlConnection).Message).RememberPassword);
        Assert.False(tab.Binding!.RememberPassword);
        Assert.False(tab.Binding.NeedsNoPassword);
    }

    [Fact]
    public async Task A_password_the_engine_cannot_remember_at_all_is_not_claimed_remembered()
    {
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true, Remembered = true };   // an engine without credentials.sqlserver.v1
        var tab = _sql.CreateSession("old-engine-typed");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { RememberPassword = true }, tab, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(result.Remembered);
        Assert.False(tab.Binding!.RememberPassword);
    }

    // [12] A new tab's dialog tests and lists with the other tab's password.
    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public async Task Test_and_list_send_the_copy_source_only_when_nothing_is_typed_and_the_engine_can_copy(DatabaseKind kind)
    {
        var target = kind == DatabaseKind.PostgreSql
            ? new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", CopyPasswordFrom: "source-tab")
            : Remote(trust: false) with { Password = null, CopyPasswordFrom = "source-tab" };
        IEnumerable<string?> Sources() => _bridge.Log.Select(e => e.Message switch
        {
            TestSqlConnectionRequest t => t.CopyPasswordFromSessionId,
            ListDatabasesRequest l => l.CopyPasswordFromSessionId,
            _ => "?",
        });

        // An engine without session.copy.v1 is never named a session to copy from.
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilityPostgreSql };
        await _sql.TestAsync(target, CancellationToken.None);
        await _sql.ListDatabasesAsync(target, CancellationToken.None);
        Assert.Equal(new string?[] { null, null }, Sources());

        // Review, round 2: nor is one with only v1 — it would list with no password at all (it ignores key 3).
        _bridge.Log.Clear();
        ReleasedV1Engine();
        Assert.False(_sql.EngineCopiesSessionPasswords);
        await _sql.TestAsync(target, CancellationToken.None);
        await _sql.ListDatabasesAsync(target, CancellationToken.None);
        Assert.Equal(new string?[] { null, null }, Sources());

        _bridge.Log.Clear();
        EngineRemembersAndCopies();
        Assert.True(_sql.EngineCopiesSessionPasswords);
        await _sql.TestAsync(target, CancellationToken.None);
        await _sql.ListDatabasesAsync(target, CancellationToken.None);
        Assert.Equal(new[] { "source-tab", "source-tab" }, Sources());

        _bridge.Log.Clear();
        await _sql.TestAsync(target with { Password = "typed" }, CancellationToken.None);
        await _sql.ListDatabasesAsync(target with { Password = "typed" }, CancellationToken.None);
        Assert.Equal(new string?[] { null, null }, Sources());
    }

    [Fact]
    public async Task A_connect_takes_the_targets_copy_source_before_the_tabs()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("dialog-copy");

        Assert.True((await _sql.ConnectAsync(Remote(trust: false) with { Password = null, CopyPasswordFrom = "from-dialog" }, tab, CancellationToken.None)).Ok);

        Assert.Equal("from-dialog", ((TestSqlConnectionRequest)_bridge.Log.First(e => e.Type == MessageTypes.TestSqlConnection).Message).CopyPasswordFromSessionId);
        Assert.Equal("from-dialog", ConnectionChanges.Single().CopyPasswordFromSessionId);
    }

    // [2], [8] A question about a remembered password that gets no answer.
    [Fact]
    public async Task A_forget_the_engine_never_answers_gives_up_and_says_there_was_no_answer()
    {
        EngineRemembersAndCopies();
        _bridge.HangCredentials = true;
        _sql.CredentialAnswerTimeout = TimeSpan.FromMilliseconds(50);

        var forgot = await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None);
        var forgotPg = await _sql.ForgetRememberedPasswordAsync("db1", 5432, "app", CancellationToken.None);
        var has = await _sql.HasRememberedSqlServerPasswordAsync("sql01", "app", trustServerCertificate: false, CancellationToken.None);

        Assert.Null(forgot);
        Assert.Null(forgotPg);
        Assert.False(has);
    }

    [Fact]
    public async Task A_forget_is_answered_true_false_or_not_at_all()
    {
        EngineRemembersAndCopies();
        _bridge.CredentialReply = new CredentialResponse { Ok = true, Has = true };
        Assert.True(await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None));
        _bridge.CredentialReply = new CredentialResponse { Ok = true, Has = false };
        Assert.False(await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None));
        _bridge.CredentialReply = new CredentialResponse { Ok = false };   // the engine refused the request
        Assert.Null(await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None));

        _bridge.State = BridgeState.Reconnecting;   // not paired right now
        Assert.Null(await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None));
        _bridge.State = BridgeState.Open;
        _bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDisconnect };   // too old to be asked
        Assert.Null(await _sql.ForgetRememberedSqlServerPasswordAsync("sql01", "app", CancellationToken.None));
    }

    // ── Spec 043 follow-up (review, round 2) ─────────────────────────────────────────────────────────────────────────────────

    // [1] Version gates: what an engine with only the v1 ids says is read as it meant it.
    [Fact]
    public async Task An_engine_with_only_credentials_v1_is_taken_at_its_word_that_it_kept_the_password()
    {
        ReleasedV1Engine();
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true };   // key 4 never sent: false on the wire
        var tab = _sql.CreateSession("v1-remember");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { RememberPassword = true }, tab, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(((TestSqlConnectionRequest)_bridge.Log.First(e => e.Type == MessageTypes.TestSqlConnection).Message).RememberPassword);
        Assert.True(result.Remembered);              // no "not kept" warning in the dialog, the saved row keeps its flag
        Assert.True(tab.Binding!.RememberPassword);   // and the tab reconnects without asking, as before round 1
        Assert.True(tab.Binding.NeedsNoPassword);
    }

    [Fact]
    public async Task An_engine_with_credentials_v2_is_believed_when_it_says_it_did_not_keep_the_password()
    {
        ReleasedV1Engine();
        _bridge.EngineCapabilities = _bridge.EngineCapabilities.Append(SqlConnectionService.CapabilitySqlServerCredentialsV2).ToArray();
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true, Remembered = false };
        var tab = _sql.CreateSession("v2-not-kept");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { RememberPassword = true }, tab, CancellationToken.None);

        Assert.False(result.Remembered);
        Assert.False(tab.Binding!.RememberPassword);
    }

    [Fact]
    public async Task On_an_engine_with_only_session_copy_v1_a_dialogs_copy_source_is_not_sent_but_the_tabs_own_still_is()
    {
        ReleasedV1Engine();
        var dialog = _sql.CreateSession("dialog-v1");

        // The dialog's own source (it never sets one on such an engine) is not trusted to it: nothing typed and nothing remembered
        // is a question, not a login with an empty password.
        var asked = await _sql.ConnectAsync(Remote(trust: false) with { Password = null, CopyPasswordFrom = "from-dialog" }, dialog, CancellationToken.None);
        Assert.True(asked.PasswordRequired);
        Assert.DoesNotContain(_bridge.Log, e => e.Type == MessageTypes.TestSqlConnection);

        // New query's own reconnect copies as it did (ConnectionInfo key 7, TestSqlConnectionRequest key 2: v1 honours both).
        var tab = _sql.CreateSession("new-tab-v1");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false), "saved-1"));
        tab.CopyPasswordFrom = "source-tab";
        Assert.True((await _sql.ReattachAsync(tab, null, CancellationToken.None)).Ok);
        Assert.Equal("source-tab", ((TestSqlConnectionRequest)_bridge.Log.Last(e => e.Type == MessageTypes.TestSqlConnection).Message).CopyPasswordFromSessionId);
        Assert.Equal("source-tab", ConnectionChanges.Last().CopyPasswordFromSessionId);
    }

    [Fact]
    public async Task A_PostgreSQL_password_refused_by_an_engine_without_the_code_stops_the_tab_reconnecting_on_its_own()
    {
        ReleasedV1Engine();
        var tab = _sql.CreateSession("pg-old-engine");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", RememberPassword: true), null));
        _bridge.TestReply = new TestSqlConnectionResponse
        {
            Ok = false, ErrorMessage = "password authentication failed for user \"app\" (SQLSTATE 28P01).",   // no ErrorCode
        };

        var result = await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Null(result.ErrorCode);
        Assert.True(tab.PasswordMissing);
        Assert.False(tab.CanReconnectAlone);
    }

    [Fact]
    public async Task Another_PostgreSQL_failure_without_a_code_does_not_mark_the_tab()
    {
        ReleasedV1Engine();
        var tab = _sql.CreateSession("pg-down");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", RememberPassword: true), null));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Could not reach db1:5432: connection refused." };

        await _sql.ReattachAsync(tab, null, CancellationToken.None);

        Assert.False(tab.PasswordMissing);
        Assert.True(tab.CanReconnectAlone);
    }

    [Theory]
    [InlineData(DialectErrors.PgLoginFailed, "anything", true)]
    [InlineData(null, "password authentication failed for user \"app\"", true)]
    [InlineData(null, "FATAL: 28P01", true)]
    [InlineData(null, "connection refused", false)]
    [InlineData(DialectErrors.PasswordRequired, "password authentication failed", false)]   // a code that says otherwise wins
    public void A_refused_PostgreSQL_password_is_read_from_the_code_or_from_an_older_engines_words(string? code, string error, bool refused) =>
        Assert.Equal(refused, SqlConnectionService.IsPgPasswordRefused(new ConnectResult(false, error, ErrorCode: code)));

    // [2] The mark is for the tab's own sign-in only.
    [Fact]
    public async Task Another_server_tried_for_a_connected_tab_does_not_mark_the_tabs_password_missing()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("connected-elsewhere");
        Assert.True((await _sql.ConnectAsync(Remote(trust: false) with { RememberPassword = true }, tab, CancellationToken.None)).Ok);
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user 'reporting'.", ErrorCode = DialectErrors.LoginFailed };

        // The dialog tries another server with the login's remembered password, which that server refuses.
        var other = Remote(trust: false) with { Server = "db2.example.com", Password = null, RememberPassword = true };
        var refused = await _sql.ConnectAsync(other, tab, CancellationToken.None);

        Assert.False(refused.Ok);
        Assert.False(tab.PasswordMissing);
        Assert.True(tab.IsConnected);                  // still on its own server
        Assert.Equal("db1.example.com", tab.Binding!.Server);

        // The same refusal for the tab's own server and login (any case, spaces around) marks it.
        await _sql.ConnectAsync(Remote(trust: false) with { Server = " DB1.example.com ", User = "REPORTING", Password = null, RememberPassword = true },
            tab, CancellationToken.None);
        Assert.True(tab.PasswordMissing);
    }

    [Fact]
    public async Task A_new_tab_whose_dialog_tries_another_server_keeps_the_tab_it_copies_from()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("new-tab");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false), "saved-1"));
        tab.CopyPasswordFrom = "source-tab";
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Enter the password.", ErrorCode = DialectErrors.PasswordRequired };

        var result = await _sql.ConnectAsync(Remote(trust: false) with { Server = "db2.example.com", Password = null }, tab, CancellationToken.None);

        Assert.True(result.PasswordRequired);
        Assert.False(tab.PasswordMissing);
        Assert.Equal("source-tab", tab.CopyPasswordFrom);
        Assert.True(tab.CanReconnectAlone);
    }

    [Fact]
    public async Task A_PostgreSQL_tab_is_marked_only_for_its_own_host_port_and_user()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("pg-own");
        tab.Restore(ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1:6000", "sales", User: "app", RememberPassword: true), null));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "refused", ErrorCode = DialectErrors.PgLoginFailed };

        await _sql.ConnectAsync(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", Port: 5432, RememberPassword: true), tab, CancellationToken.None);
        Assert.False(tab.PasswordMissing);   // another port

        await _sql.ConnectAsync(new ConnectionTarget(DatabaseKind.PostgreSql, "DB1", "postgres", User: "app", Port: 6000, RememberPassword: true), tab, CancellationToken.None);
        Assert.True(tab.PasswordMissing);    // its own host and port, written another way; the database is not part of it
    }

    [Fact]
    public async Task A_tab_with_no_connection_yet_is_marked_as_before()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("no-binding");

        var result = await _sql.ConnectAsync(Remote(trust: false) with { Password = null }, tab, CancellationToken.None);

        Assert.True(result.PasswordRequired);
        Assert.True(tab.PasswordMissing);
    }

    // [4] The dialog looks up the tab a new tab was opened from.
    [Fact]
    public void A_session_is_found_by_its_id_while_its_tab_is_open()
    {
        var source = _sql.CreateSession("source-tab");

        Assert.Same(source, _sql.FindSession("source-tab"));
        Assert.Null(_sql.FindSession("nobody"));

        _sql.ForgetSession(source);   // the tab was closed
        Assert.Null(_sql.FindSession("source-tab"));
    }

    [Fact]
    public void A_binding_names_the_same_sign_in_whatever_the_case_spaces_database_or_security_choices()
    {
        var sql = ConnectionBinding.FromTarget(Remote(trust: false), "saved-1");
        Assert.True(sql.SameSignInAs(new ConnectionTarget(DatabaseKind.SqlServer, " DB1.EXAMPLE.COM ", "master", WindowsAuth: false, User: "Reporting",
            TrustServerCertificate: true)));
        Assert.False(sql.SameSignInAs(Remote(trust: false) with { User = "other" }));
        Assert.False(sql.SameSignInAs(Remote(trust: false) with { Server = "db2.example.com" }));
        Assert.False(sql.SameSignInAs(Remote(trust: false) with { WindowsAuth = true }));
        Assert.False(sql.SameSignInAs(new ConnectionTarget(DatabaseKind.PostgreSql, "db1.example.com", "sales", User: "reporting")));

        var windows = ConnectionBinding.FromTarget(SqlServer(), null);
        Assert.True(windows.SameSignInAs(SqlServer("master", server: "LOCALHOST")));

        var pg = ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", Port: 5433), null);
        Assert.True(pg.SameSignInAs(new ConnectionTarget(DatabaseKind.PostgreSql, "db1:5433", "postgres", User: "APP")));
        Assert.False(pg.SameSignInAs(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app")));   // 5432
    }

    // ── Spec 043 follow-up (review, round 3) ─────────────────────────────────────────────────────────────────────────────────

    private CredentialRequest LastCredentialRequest => (CredentialRequest)_bridge.Log.Last(e => e.Type == MessageTypes.CredentialRequest).Message;

    // [1] A question about a remembered SQL login carries the certificate checking the engine fills a password by.
    [Fact]
    public async Task A_question_about_a_SQL_logins_password_carries_the_effective_trust_choice_to_an_engine_that_reads_it()
    {
        EngineRemembersAndCopies();

        await _sql.HasRememberedSqlServerPasswordAsync("db1.example.com", "app", trustServerCertificate: true, CancellationToken.None);
        Assert.True(LastCredentialRequest.TrustServerCertificate);
        await _sql.HasRememberedSqlServerPasswordAsync("db1.example.com", "app", trustServerCertificate: false, CancellationToken.None);
        Assert.False(LastCredentialRequest.TrustServerCertificate);

        // A server on the engine's own machine is always trusted, as the connection string says, whatever the checkbox.
        await _sql.HasRememberedSqlServerPasswordAsync("localhost", "app", trustServerCertificate: false, CancellationToken.None);
        Assert.True(LastCredentialRequest.TrustServerCertificate);
        await _sql.HasRememberedSqlServerPasswordAsync(@".\SQLEXPRESS", "app", trustServerCertificate: false, CancellationToken.None);
        Assert.True(LastCredentialRequest.TrustServerCertificate);

        // A forget removes the login's password whatever it was saved with, and PostgreSQL has no such choice: no key 5.
        await _sql.ForgetRememberedSqlServerPasswordAsync("db1.example.com", "app", CancellationToken.None);
        Assert.Null(LastCredentialRequest.TrustServerCertificate);
        await _sql.HasRememberedPasswordAsync("db1", 5432, "app", CancellationToken.None);
        Assert.Null(LastCredentialRequest.TrustServerCertificate);
    }

    [Fact]
    public async Task An_engine_with_only_credentials_v1_is_not_sent_the_trust_choice()
    {
        ReleasedV1Engine();

        Assert.True(await _sql.HasRememberedSqlServerPasswordAsync("db1.example.com", "app", trustServerCertificate: true, CancellationToken.None));

        Assert.Null(LastCredentialRequest.TrustServerCertificate);   // it answers for any remembered password, as before
    }

    // [2] The mark names the sign-in it is about and why, so a tab with no binding is covered too.
    [Fact]
    public async Task A_remembered_SQL_password_refused_for_a_tab_with_no_binding_records_the_sign_in_and_that_it_was_refused()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("fresh-page");   // the Status bar's restore of a remembered login, on a fresh page
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user 'reporting'.", ErrorCode = DialectErrors.LoginFailed };

        await _sql.ConnectAsync(Remote(trust: true) with { Password = null, RememberPassword = true }, tab, CancellationToken.None);

        Assert.Null(tab.Binding);
        Assert.True(tab.PasswordMissing);
        Assert.True(tab.PasswordRefused);
        var signIn = tab.PasswordMissingFor!;
        Assert.True(signIn.SameSignInAs(Remote(trust: false)));
        Assert.Equal(("saved-1", true), (signIn.SavedConnectionId, signIn.TrustServerCertificate));
    }

    [Fact]
    public async Task A_SQL_login_the_engine_has_no_password_for_records_the_sign_in_and_that_none_was_refused()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("none-held");
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Enter the password.", ErrorCode = DialectErrors.PasswordRequired };

        await _sql.ConnectAsync(Remote(trust: false) with { Password = null, RememberPassword = true }, tab, CancellationToken.None);

        Assert.True(tab.PasswordMissing);
        Assert.False(tab.PasswordRefused);
        Assert.True(tab.PasswordMissingFor!.SameSignInAs(Remote(trust: false)));

        // Nothing asked of the engine at all (Remember off, nothing typed): the same, without a round trip.
        var other = _sql.CreateSession("not-asked");
        await _sql.ConnectAsync(Remote(trust: false) with { Password = null }, other, CancellationToken.None);
        Assert.Equal((true, false), (other.PasswordMissing, other.PasswordRefused));
        Assert.NotNull(other.PasswordMissingFor);
    }

    [Theory]
    [InlineData(DialectErrors.PgLoginFailed, false, true)]
    [InlineData(DialectErrors.PasswordRequired, true, false)]
    public async Task A_PostgreSQL_tab_with_no_binding_records_the_sign_in_and_why(string code, bool passwordRequired, bool refused)
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("pg-fresh");
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "no", ErrorCode = code };

        var result = await _sql.ConnectAsync(new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", Port: 6000, RememberPassword: true),
            tab, CancellationToken.None);

        Assert.Equal(passwordRequired, result.PasswordRequired);
        Assert.Equal((true, refused), (tab.PasswordMissing, tab.PasswordRefused));
        Assert.True(tab.PasswordMissingFor!.SameSignInAs(new ConnectionTarget(DatabaseKind.PostgreSql, "db1:6000", "postgres", User: "app")));
    }

    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public async Task A_successful_connect_clears_the_mark_its_sign_in_and_its_reason(DatabaseKind kind)
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("cleared");
        var target = kind == DatabaseKind.PostgreSql
            ? new ConnectionTarget(DatabaseKind.PostgreSql, "db1", "sales", User: "app", RememberPassword: true)
            : Remote(trust: false) with { Password = null, RememberPassword = true };
        _bridge.TestReply = new TestSqlConnectionResponse
        {
            Ok = false, ErrorMessage = "refused", ErrorCode = kind == DatabaseKind.PostgreSql ? DialectErrors.PgLoginFailed : DialectErrors.LoginFailed,
        };
        await _sql.ConnectAsync(target, tab, CancellationToken.None);
        Assert.True(tab.PasswordRefused);

        _bridge.TestReply = new TestSqlConnectionResponse { Ok = true, Remembered = true, ServerVersion = "PostgreSQL 18.1" };
        Assert.True((await _sql.ConnectAsync(target with { Password = "current" }, tab, CancellationToken.None)).Ok);

        Assert.Equal((false, false), (tab.PasswordMissing, tab.PasswordRefused));
        Assert.Null(tab.PasswordMissingFor);
    }

    [Fact]
    public async Task Another_sign_in_tried_for_a_tab_with_a_binding_leaves_its_mark_as_it_was()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("marked");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed.", ErrorCode = DialectErrors.LoginFailed };
        await _sql.ReattachAsync(tab, null, CancellationToken.None);
        Assert.True(tab.PasswordRefused);

        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Enter the password.", ErrorCode = DialectErrors.PasswordRequired };
        await _sql.ConnectAsync(Remote(trust: false) with { Server = "db2.example.com", Password = null, RememberPassword = true }, tab, CancellationToken.None);

        Assert.True(tab.PasswordRefused);
        Assert.Equal("db1.example.com", tab.PasswordMissingFor!.Server);
    }

    // [4] Another tab's password is offered only where the engine would copy it.
    [Fact]
    public void A_SQL_Server_password_is_copied_only_with_the_same_effective_certificate_checking()
    {
        var remote = ConnectionBinding.FromTarget(Remote(trust: false), "saved-1");
        Assert.True(remote.CopiesPasswordTo(Remote(trust: false) with { Server = " DB1.example.com ", User = "REPORTING", Database = "master" }));
        Assert.False(remote.CopiesPasswordTo(Remote(trust: true)));                       // a validated password to a trusting connection
        Assert.False(ConnectionBinding.FromTarget(Remote(trust: true), null).CopiesPasswordTo(Remote(trust: false)));
        Assert.False(remote.CopiesPasswordTo(Remote(trust: false) with { User = "other" }));

        // On the engine's own machine both connection strings trust the certificate, whatever the checkbox said.
        var local = ConnectionBinding.FromTarget(Remote(trust: false) with { Server = "localhost" }, null);
        Assert.True(local.CopiesPasswordTo(Remote(trust: true) with { Server = "LOCALHOST" }));

        // Windows authentication has no password to copy.
        Assert.False(ConnectionBinding.FromTarget(SqlServer(), null).CopiesPasswordTo(SqlServer()));
    }

    [Fact]
    public void A_PostgreSQL_password_is_copied_only_with_the_same_TLS_mode_root_certificate_and_user_name()
    {
        ConnectionTarget Pg(string? ssl = null, string? cert = null, string user = "app") =>
            new(DatabaseKind.PostgreSql, "db1", "sales", User: user, Port: 5432, SslMode: ssl, RootCertificatePath: cert);
        var source = ConnectionBinding.FromTarget(Pg("verify-full", @"C:\certs\root.crt"), null);

        Assert.True(source.CopiesPasswordTo(Pg("Verify-Full ", @" c:\CERTS\root.crt ")));
        Assert.False(source.CopiesPasswordTo(Pg("require", @"C:\certs\root.crt")));
        Assert.False(source.CopiesPasswordTo(Pg("verify-full", @"C:\certs\other.crt")));
        Assert.False(source.CopiesPasswordTo(Pg("verify-full", null)));
        Assert.False(source.CopiesPasswordTo(Pg("verify-full", @"C:\certs\root.crt", user: "APP")));   // the engine compares it exactly

        // No mode is prefer, as on the wire.
        Assert.True(ConnectionBinding.FromTarget(Pg(), null).CopiesPasswordTo(Pg("prefer")));
    }

    // ── Spec 043 follow-up (review, round 4) ─────────────────────────────────────────────────────────────────────────────────

    // [5] The user's Disconnect ends the tab's connection and the mark of the password it was missing, of either reason.
    [Theory]
    [InlineData(DialectErrors.LoginFailed, true)]
    [InlineData(DialectErrors.PasswordRequired, false)]
    public async Task The_users_disconnect_clears_the_mark_its_sign_in_and_its_reason(string code, bool refused)
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("disconnected");
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "no", ErrorCode = code };
        await _sql.ConnectAsync(Remote(trust: false) with { Password = null, RememberPassword = true }, tab, CancellationToken.None);
        Assert.Equal((true, refused), (tab.PasswordMissing, tab.PasswordRefused));
        Assert.NotNull(tab.PasswordMissingFor);

        await _sql.DisconnectSessionAsync(tab, release: false, CancellationToken.None);

        Assert.Equal((false, false), (tab.PasswordMissing, tab.PasswordRefused));
        Assert.Null(tab.PasswordMissingFor);
        Assert.True(tab.DisconnectedByUser);
    }

    [Fact]
    public async Task The_users_disconnect_of_a_restored_tab_whose_password_was_refused_clears_the_mark_with_the_binding()
    {
        EngineRemembersAndCopies();
        var tab = _sql.CreateSession("bound");
        tab.Restore(ConnectionBinding.FromTarget(Remote(trust: false) with { RememberPassword = true }, "saved-1"));
        _bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed.", ErrorCode = DialectErrors.LoginFailed };
        await _sql.ReattachAsync(tab, null, CancellationToken.None);
        Assert.True(tab.PasswordMissing);

        await _sql.DisconnectSessionAsync(tab, release: false, CancellationToken.None);

        Assert.Null(tab.Binding);
        Assert.Equal((false, false), (tab.PasswordMissing, tab.PasswordRefused));
        Assert.Null(tab.PasswordMissingFor);
    }
}
