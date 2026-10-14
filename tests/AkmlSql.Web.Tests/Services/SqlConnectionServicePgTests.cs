using System.Data.Common;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 042 (task T050, FR-002, FR-007, FR-102, FR-104): the web connection service for PostgreSQL - the target guard, the
/// connection string it sends, what a successful connect changes, the password-required result and the remembered-password
/// calls. The engine is a scripted bridge; the real engine is covered by the engine and E2E tests.
/// </summary>
public sealed class SqlConnectionServicePgTests
{
    private sealed class ScriptedBridge : IEngineBridge
    {
        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities { get; set; } = { SqlConnectionService.CapabilityPostgreSql };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public List<(int Type, object Request)> Sent { get; } = new();
        public List<(int Type, object Payload)> Notifications { get; } = new();
        public TestSqlConnectionResponse TestReply { get; set; } = new() { Ok = true, ServerVersion = "PostgreSQL 18.1" };
        public CredentialResponse CredentialReply { get; set; } = new() { Ok = true, Has = true };

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());

        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
        {
            Sent.Add((type, request));
            object reply = type switch
            {
                MessageTypes.TestSqlConnection => TestReply,
                MessageTypes.CredentialRequest => CredentialReply,
                _ => throw new InvalidOperationException($"Unexpected request {type}"),
            };
            return Task.FromResult((TResponse)reply);
        }

        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class
        {
            Notifications.Add((type, payload));
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

    private static (SqlConnectionService Sql, ScriptedBridge Bridge) Create(ScriptedBridge? bridge = null)
    {
        bridge ??= new ScriptedBridge();
        return (new SqlConnectionService(bridge, new NoDiagnostics()), bridge);
    }

    private static ConnectionTarget Target(string server = "db1", string password = "s3cret", bool remember = false, int? port = null,
        string? sslMode = null, string? rootCertificate = null, string user = "app") =>
        new(DatabaseKind.PostgreSql, server, "northwind", User: user, Password: password, Port: port, SslMode: sslMode,
            RootCertificatePath: rootCertificate, RememberPassword: remember);

    private static DbConnectionStringBuilder Parse(string connectionString) => new() { ConnectionString = connectionString };

    // ── The connection string (contracts/ipc.md) ────────────────────────────────────────────────

    [Fact]
    public async Task Test_sends_the_documented_Npgsql_string_with_the_kind()
    {
        var (sql, bridge) = Create();

        var result = await sql.TestAsync(Target(sslMode: "verify-full", rootCertificate: @"C:\certs\root.crt", port: 5433), default);

        Assert.True(result.Ok, result.Error);
        var (type, request) = Assert.Single(bridge.Sent);
        Assert.Equal(MessageTypes.TestSqlConnection, type);
        var typed = Assert.IsType<TestSqlConnectionRequest>(request);
        Assert.Equal((int)DatabaseKind.PostgreSql, typed.Kind);
        var cs = Parse(typed.ConnectionString);
        Assert.Equal("db1", cs["Host"]);
        Assert.Equal("5433", cs["Port"]);
        Assert.Equal("northwind", cs["Database"]);
        Assert.Equal("app", cs["Username"]);
        Assert.Equal("s3cret", cs["Password"]);
        Assert.Equal("VerifyFull", cs["SSL Mode"]);
        Assert.Equal(@"C:\certs\root.crt", cs["Root Certificate"]);
        Assert.Equal("AKML SQL", cs["Application Name"]);
    }

    [Fact]
    public async Task A_blank_password_sends_no_Password_key_so_the_engine_can_use_the_remembered_one()
    {
        var (sql, bridge) = Create();

        await sql.TestAsync(Target(password: "", remember: true), default);

        var cs = Parse(((TestSqlConnectionRequest)bridge.Sent.Single().Request).ConnectionString);
        Assert.False(cs.ContainsKey("Password"));
        Assert.Equal("Prefer", cs["SSL Mode"]);
        Assert.Equal("5432", cs["Port"]);
    }

    [Fact]
    public async Task Test_changes_nothing_about_the_session()
    {
        var (sql, bridge) = Create();

        await sql.TestAsync(Target(), default);

        Assert.False(sql.IsConnected);
        Assert.Empty(bridge.Notifications);
        Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
    }

    // ── Host and port ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("db1", null, "db1", 5432)]
    [InlineData("db1:5433", null, "db1", 5433)]
    [InlineData("db1", 6000, "db1", 6000)]
    [InlineData("db1:5433", 6000, "db1", 5433)]
    [InlineData("[::1]:5433", null, "::1", 5433)]
    [InlineData("[2001:db8::1]", null, "2001:db8::1", 5432)]
    [InlineData("2001:db8::1", null, "2001:db8::1", 5432)]
    [InlineData("  db1.example.com ", null, "db1.example.com", 5432)]
    public void Host_and_port_are_read_from_the_forms_a_person_types(string text, int? explicitPort, string host, int port)
    {
        var (parsedHost, parsedPort, error) = SqlConnectionService.ParseHostAndPort(text, explicitPort);

        Assert.Null(error);
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("db1:abc")]
    [InlineData("db1:0")]
    [InlineData("db1:70000")]
    [InlineData("[::1")]
    [InlineData("/var/run/postgresql")]
    [InlineData(@"\\fileserver\share")]
    [InlineData("db1;Integrated Security=true")]
    [InlineData("db1'x")]
    public void A_host_that_is_not_a_host_is_refused(string text)
    {
        var (host, _, error) = SqlConnectionService.ParseHostAndPort(text, null);

        Assert.Null(host);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public async Task A_user_name_a_tls_mode_and_a_certificate_path_are_checked_before_anything_is_sent()
    {
        var (sql, bridge) = Create();

        var noUser = await sql.TestAsync(Target(user: "  "), default);
        var badMode = await sql.TestAsync(Target(sslMode: "sometimes"), default);
        var relative = await sql.TestAsync(Target(sslMode: "verify-ca", rootCertificate: @"..\root.crt"), default);
        var unc = await sql.TestAsync(Target(sslMode: "verify-ca", rootCertificate: @"\\share\root.crt"), default);
        var injected = await sql.TestAsync(Target(user: "app;Integrated Security=true"), default);

        Assert.All(new[] { noUser, badMode, relative, unc, injected }, r => Assert.False(r.Ok));
        Assert.Contains("user name", noUser.Error);
        Assert.Contains("TLS mode", badMode.Error);
        Assert.Contains("full local path", relative.Error);
        Assert.Contains("full local path", unc.Error);
        Assert.Empty(bridge.Sent);
    }

    [Theory]
    [InlineData(@"C:\certs\root.crt", true)]
    [InlineData("D:/certs/root.crt", true)]
    [InlineData("/etc/ssl/root.crt", true)]
    [InlineData(@"certs\root.crt", false)]
    [InlineData(@"C:\certs\..\root.crt", false)]
    [InlineData(@"\\server\certs\root.crt", false)]
    [InlineData("C:root.crt", false)]
    public void Only_a_full_local_path_is_a_certificate_path(string path, bool expected) =>
        Assert.Equal(expected, SqlConnectionService.IsLocalAbsolutePath(path));

    // ── Connect ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_tests_first_then_makes_the_server_the_session()
    {
        var (sql, bridge) = Create();
        var changed = 0;
        sql.StateChanged += () => changed++;

        var result = await sql.ConnectAsync(Target(remember: true), default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("PostgreSQL 18.1", result.ServerVersion);
        Assert.Equal(MessageTypes.TestSqlConnection, Assert.Single(bridge.Sent).Type);
        var (type, payload) = Assert.Single(bridge.Notifications);
        Assert.Equal(MessageTypes.ConnectionChanged, type);
        var info = Assert.IsType<ConnectionInfo>(payload);
        Assert.Equal(sql.SessionId, info.SessionId);
        Assert.Equal((int)DatabaseKind.PostgreSql, info.Kind);
        Assert.True(info.RememberPassword);
        Assert.Equal("northwind", info.DatabaseName);

        Assert.True(sql.IsConnected);
        Assert.Equal(DatabaseKind.PostgreSql, sql.Kind);
        Assert.Equal("db1:5432", sql.Server);
        Assert.Equal("northwind", sql.Database);
        Assert.Equal("app", sql.Login);
        Assert.False(sql.WindowsAuth);
        Assert.Equal("18.1", sql.ServerVersionText);
        Assert.Equal(TransactionState.None, sql.TransactionState);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task A_server_that_wants_a_password_is_a_password_required_result_and_connects_nothing()
    {
        var (sql, bridge) = Create();
        bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorCode = DialectErrors.PasswordRequired, ErrorMessage = "The server asked for a password." };

        var result = await sql.ConnectAsync(Target(password: ""), default);

        Assert.False(result.Ok);
        Assert.True(result.PasswordRequired);
        Assert.Empty(bridge.Notifications);
        Assert.False(sql.IsConnected);
    }

    [Fact]
    public async Task A_failed_test_shows_the_servers_text_and_connects_nothing()
    {
        var (sql, bridge) = Create();
        bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "password authentication failed (SQLSTATE 28P01). Check the user name and password." };

        var result = await sql.ConnectAsync(Target(), default);

        Assert.False(result.Ok);
        Assert.False(result.PasswordRequired);
        Assert.Contains("28P01", result.Error);
        Assert.Empty(bridge.Notifications);
        Assert.False(sql.IsConnected);
    }

    [Fact]
    public async Task An_engine_without_PostgreSQL_support_is_told_so_and_nothing_is_sent()
    {
        var (sql, bridge) = Create(new ScriptedBridge { EngineCapabilities = Array.Empty<string>() });

        var result = await sql.ConnectAsync(Target(), default);

        Assert.False(result.Ok);
        Assert.Contains("dialect.postgresql.v1", result.Error);
        Assert.False(sql.EngineSupportsPostgres);
        Assert.Empty(bridge.Sent);
        Assert.Empty(bridge.Notifications);
    }

    [Fact]
    public async Task Without_a_paired_engine_the_message_says_to_pair_one()
    {
        var (sql, _) = Create(new ScriptedBridge { State = BridgeState.Disconnected });

        var result = await sql.TestAsync(Target(), default);

        Assert.False(result.Ok);
        Assert.Contains("Pair an engine first", result.Error);
    }

    [Fact]
    public async Task Disconnect_returns_the_service_to_SQL_Server_with_no_transaction()
    {
        var (sql, _) = Create();
        await sql.ConnectAsync(Target(), default);
        sql.ReportTransactionState(TransactionState.Aborted);

        await sql.DisconnectAsync();

        Assert.False(sql.IsConnected);
        Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
        Assert.Null(sql.ServerVersionText);
        Assert.Equal(TransactionState.None, sql.TransactionState);
    }

    // ── Transaction state ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_run_reports_the_transaction_state_and_a_change_raises_state_changed()
    {
        var (sql, _) = Create();
        await sql.ConnectAsync(Target(), default);
        var changed = 0;
        sql.StateChanged += () => changed++;

        sql.ReportTransactionState(TransactionState.Open);
        sql.ReportTransactionState(TransactionState.Open);
        sql.ReportTransactionState(TransactionState.Aborted);

        Assert.Equal(TransactionState.Aborted, sql.TransactionState);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void A_state_reported_while_not_connected_is_ignored()
    {
        var (sql, _) = Create();

        sql.ReportTransactionState(TransactionState.Open);

        Assert.Equal(TransactionState.None, sql.TransactionState);
    }

    // ── Remembered passwords (FR-105, FR-106) ───────────────────────────────────────────────────

    [Fact]
    public async Task Has_and_Forget_ask_the_engine_by_server_port_and_login_and_never_send_a_password()
    {
        var (sql, bridge) = Create();

        var has = await sql.HasRememberedPasswordAsync("db1", 5433, "app", default);
        var forgot = await sql.ForgetRememberedPasswordAsync("db1", null, "app", default);

        Assert.True(has);
        Assert.True(forgot);
        var asked = bridge.Sent.Select(s => (CredentialRequest)s.Request).ToArray();
        Assert.Equal(CredentialAction.Has, asked[0].Action);
        Assert.Equal(("db1", 5433, "app"), (asked[0].Server, asked[0].Port, asked[0].Login));
        Assert.Equal(CredentialAction.Forget, asked[1].Action);
        Assert.Equal(5432, asked[1].Port);
        Assert.All(asked, a => Assert.Equal((int)DatabaseKind.PostgreSql, a.Kind));
    }

    [Fact]
    public async Task Has_is_false_without_engine_support_or_a_login()
    {
        var (unsupported, bridge) = Create(new ScriptedBridge { EngineCapabilities = Array.Empty<string>() });
        Assert.False(await unsupported.HasRememberedPasswordAsync("db1", 5432, "app", default));
        Assert.Empty(bridge.Sent);

        var (sql, supported) = Create();
        Assert.False(await sql.HasRememberedPasswordAsync("db1", 5432, " ", default));
        Assert.Empty(supported.Sent);
    }

    // ── Wording (FR-007) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_message_the_service_itself_words_for_PostgreSQL_names_SQL_Server()
    {
        var (sql, _) = Create();
        var (noEngine, _) = Create(new ScriptedBridge { EngineCapabilities = Array.Empty<string>() });
        var (closed, _) = Create(new ScriptedBridge { State = BridgeState.Disconnected });

        var messages = new List<string?>
        {
            (await noEngine.TestAsync(Target(), default)).Error,
            (await closed.TestAsync(Target(), default)).Error,
            (await sql.TestAsync(Target(server: ""), default)).Error,
            (await sql.TestAsync(Target(server: "db1:abc"), default)).Error,
            (await sql.TestAsync(Target(user: ""), default)).Error,
            (await sql.TestAsync(Target(sslMode: "x"), default)).Error,
            (await sql.TestAsync(Target(sslMode: "verify-ca", rootCertificate: "root.crt"), default)).Error,
        };

        Assert.All(messages, m =>
        {
            Assert.False(string.IsNullOrEmpty(m));
            // Ordinal on purpose: "PostgreSQL server" is fine, the product name "SQL Server" is not.
            Assert.DoesNotContain("SQL Server", m, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("PostgreSQL 18.1", "18.1")]
    [InlineData("PostgreSQL 9.6.24", "9.6.24")]
    [InlineData("18.1", "18.1")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_status_bar_shows_the_number_without_the_product_name(string? reported, string? expected) =>
        Assert.Equal(expected, SqlConnectionService.VersionNumberOf(reported));
}
