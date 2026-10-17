using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 — the browser-side "Connect to SQL Server" service. Holds the ONE canonical web
/// <see cref="SessionId"/> (the source of truth the advisor flagged) used for
/// <c>ConnectionChanged</c>, <c>DocumentChanged</c> and every completion request, so the engine
/// associates them with a single session. On connect it tells the paired engine (over the bridge)
/// which SQL Server + database to open; the engine connects under ITS OWN Windows identity when
/// Windows auth is chosen, then loads the schema (Phase A/B) so live IntelliSense works.
/// </summary>
public interface ISqlConnectionService
{
    /// <summary>The canonical engine-side session id for this browser session (stable for its lifetime).</summary>
    string SessionId { get; }

    bool IsConnected { get; }
    string? Server { get; }
    string? Database { get; }

    /// <summary>Raised when the connected state changes.</summary>
    event Action? StateChanged;

    /// <summary>
    /// Review finding WEB-01: changes whenever the session's target does — a connect, a disconnect, another database, or the
    /// bridge reopening on another engine. Results read under one generation are never edited under another. A fake that does
    /// not implement it never changes.
    /// </summary>
    int ConnectionGeneration => 0;

    /// <summary>
    /// Tell the paired engine to open a SQL Server connection. <paramref name="windowsAuth"/> = true
    /// builds an Integrated-Security connection string (no password — the engine uses its own Windows
    /// identity); otherwise SQL auth with <paramref name="user"/>/<paramref name="password"/>.
    /// The engine first opens the connection (the TestSqlConnection round trip), so a wrong password or an
    /// unreachable server fails here and the session is left as it was (review WEB-19). Only then is the
    /// ConnectionChanged notification sent; schema population runs asynchronously engine-side, so a
    /// successful return means "the server accepted the login", not "schema ready".
    /// </summary>
    Task<(bool Ok, string? Error)> ConnectAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct);

    /// <summary>
    /// Phase 4 — validate credentials WITHOUT changing the active session. Runs the SAME
    /// identifier + loopback (SSRF) guard as <see cref="ConnectAsync"/> FIRST, then sends the
    /// existing <c>TestSqlConnection</c> request/response IPC so the engine actually opens the
    /// connection and reports success/failure. Unlike ConnectAsync this is a real round-trip
    /// (the engine replies), so it is the only path that truly validates a SQL-auth password.
    ///
    /// SECURITY: the guard MUST run before the send because <c>TestSqlConnectionHandler</c> has
    /// no engine-side host check — it opens whatever connection string it is handed under the
    /// engine's identity. Skipping the guard here would re-open the confused-deputy/SSRF hole.
    /// </summary>
    Task<(bool Ok, string? Error)> TestAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct);

    /// <summary>
    /// Spec 030 — enumerate the user-accessible databases on <paramref name="server"/> so the connect
    /// dialog can offer a dropdown. Runs the SAME identifier + loopback (SSRF) guard as
    /// <see cref="ConnectAsync"/>/<see cref="TestAsync"/> FIRST (the engine opens the connection under
    /// its own identity), then round-trips a <c>ListDatabases</c> request against <c>master</c>. Does
    /// NOT change the active session. Returns the sorted names on success; a message on failure.
    /// </summary>
    Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(
        string server, bool windowsAuth, string? user, string? password, CancellationToken ct);

    /// <summary>Disconnects (see <see cref="DisconnectSessionAsync"/>) and ignores the outcome.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Review finding WEB-06: end the session's database connection. An engine that advertises
    /// <c>session.disconnect.v1</c> rolls back an open transaction, closes the connection and confirms; only then is the
    /// browser's state cleared. When the engine cannot be reached (or is too old) the browser still forgets the
    /// connection, and the result says the engine's session could not be closed.
    /// </summary>
    async Task<DisconnectResult> DisconnectSessionAsync(CancellationToken ct)
    {
        await DisconnectAsync().ConfigureAwait(false);
        return new DisconnectResult(true, false, null);
    }

    /// <summary>Review finding WEB-02: list databases with the target's certificate choice (see <see cref="ConnectionTarget"/>).</summary>
    Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(ConnectionTarget target, CancellationToken ct) =>
        ListDatabasesAsync(target.Server, target.WindowsAuth, target.User, target.Password, ct);

    /// <summary>
    /// Pushes the current editor text to the engine session (DocumentChanged) so the next completion
    /// has the live document. No-op when not connected or the bridge is closed.
    /// </summary>
    Task SendDocumentAsync(string documentText, CancellationToken ct);

    // ── Spec 041 (research R25–R28) — the toolbar's database selector ────────────────────────────────────
    // Default members: a fake that does not implement them reads "not available", as an older engine does.

    /// <summary>The connection uses Windows authentication (kept from connect; the guard needs it).</summary>
    bool WindowsAuth => true;

    /// <summary>The SQL Server login for SQL Server authentication, null for Windows; never the password.</summary>
    string? Login => null;

    /// <summary>The database selector can work: connected, and the engine advertises <c>session.database.v1</c>.</summary>
    bool CanChangeDatabase => false;

    /// <summary>The databases of the connected server, listed with the connection the engine holds.</summary>
    Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesForSessionAsync(CancellationToken ct) =>
        Task.FromResult<(bool, IReadOnlyList<string>, string?)>((false, Array.Empty<string>(), "Switching database needs a newer engine."));

    /// <summary>Switch the session's database on its connection (#temp tables and SET options survive).</summary>
    Task<(bool Ok, string? Error, bool ConnectionWasReset)> ChangeDatabaseAsync(string database, CancellationToken ct) =>
        Task.FromResult<(bool, string?, bool)>((false, "Switching database needs a newer engine.", false));

    /// <summary>The database an execute ended in (a script's <c>USE</c>); raises <see cref="StateChanged"/> on a change.</summary>
    void ReportCurrentDatabase(string? database) { }

    // ── Spec 042 (PostgreSQL support) ────────────────────────────────────────────────────────────────────
    // Default members, as above: a fake that does not implement them is a SQL Server connection on an engine
    // without PostgreSQL support.

    /// <summary>The kind of the active connection (SQL Server until a PostgreSQL connection is made).</summary>
    AkmlSql.Core.DatabaseKind Kind => AkmlSql.Core.DatabaseKind.SqlServer;

    /// <summary>PostgreSQL's <c>server_version</c> text of the connected server (for example <c>18.1</c>), or null.</summary>
    string? ServerVersionText => null;

    /// <summary>The PostgreSQL transaction state after the last run (FR-016); always None for SQL Server.</summary>
    AkmlSql.Core.TransactionState TransactionState => AkmlSql.Core.TransactionState.None;

    /// <summary>
    /// Spec 042 (T085): the name the browser's schema cache is keyed by. A SQL Server's is its server name as typed; a
    /// PostgreSQL server's is <c>pg://host:port</c>, which no SQL Server name can equal, so the two kinds never share a cache.
    /// </summary>
    string? SchemaIdentity => Server;

    /// <summary>The paired engine advertises <c>dialect.postgresql.v1</c>, so the PostgreSQL choice can be offered (FR-006).</summary>
    bool EngineSupportsPostgres => false;

    /// <summary>Record the transaction state a run ended in; raises <see cref="StateChanged"/> on a change.</summary>
    void ReportTransactionState(AkmlSql.Core.TransactionState state) { }

    /// <summary>
    /// Spec 042 (T063): test a connection of either kind without changing the session. For SQL Server this is the
    /// existing test; for PostgreSQL the engine opens the connection, reads the server version and closes it. A server
    /// that wants a password the engine does not hold comes back as <see cref="ConnectResult.PasswordRequired"/>.
    /// </summary>
    async Task<ConnectResult> TestAsync(ConnectionTarget target, CancellationToken ct)
    {
        if (target.Kind == AkmlSql.Core.DatabaseKind.PostgreSql) return ConnectResult.Failed(SqlConnectionService.PostgresUnavailable);
        var (ok, error) = await TestAsync(target.Server, target.Database, target.WindowsAuth, target.User, target.Password, ct).ConfigureAwait(false);
        return new ConnectResult(ok, error);
    }

    /// <summary>Spec 042 (T063): connect to a server of either kind; see <see cref="TestAsync(ConnectionTarget, CancellationToken)"/>.</summary>
    async Task<ConnectResult> ConnectAsync(ConnectionTarget target, CancellationToken ct)
    {
        if (target.Kind == AkmlSql.Core.DatabaseKind.PostgreSql) return ConnectResult.Failed(SqlConnectionService.PostgresUnavailable);
        var (ok, error) = await ConnectAsync(target.Server, target.Database, target.WindowsAuth, target.User, target.Password, ct).ConfigureAwait(false);
        return new ConnectResult(ok, error);
    }

    /// <summary>Spec 042 (FR-105): does the engine hold a remembered PostgreSQL password for this server and login?</summary>
    Task<bool> HasRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) => Task.FromResult(false);

    /// <summary>Spec 042 (FR-106): ask the engine to forget the remembered PostgreSQL password; true when it had one.</summary>
    Task<bool> ForgetRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) => Task.FromResult(false);
}

internal sealed class SqlConnectionService : ISqlConnectionService
{
    private readonly IEngineBridge _bridge;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public SqlConnectionService(IEngineBridge bridge, IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _bridge.StateChanged += OnBridgeStateChanged;
    }

    public int ConnectionGeneration { get; private set; }

    // The engine the session was opened on: the bridge reopening on another one is a new target.
    private string? _engineUrl;

    private void OnBridgeStateChanged(BridgeState state)
    {
        if (state != BridgeState.Open || !IsConnected) return;
        var url = _bridge.ConnectedUrl;
        if (string.Equals(url, _engineUrl, StringComparison.OrdinalIgnoreCase)) return;
        _engineUrl = url;
        ConnectionGeneration++;
        StateChanged?.Invoke();
    }

    /// <summary>A new target: the session now writes to a different server, database, login, kind or engine.</summary>
    private void NewTarget()
    {
        ConnectionGeneration++;
        _engineUrl = _bridge.ConnectedUrl;
    }

    // One stable id per browser session — used by ConnectionChanged, DocumentChanged, and completion.
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public bool IsConnected { get; private set; }
    public string? Server { get; private set; }
    public string? Database { get; private set; }

    /// <summary>Spec 041: kept from connect — the guard judges a switch with the same authentication.</summary>
    public bool WindowsAuth { get; private set; } = true;

    /// <summary>Spec 041: the SQL Server login (shown in the status bar); never the password.</summary>
    public string? Login { get; private set; }

    /// <summary>The engine capability the database selector needs (contracts/ipc.md).</summary>
    public const string CapabilitySessionDatabase = "session.database.v1";

    public bool CanChangeDatabase =>
        IsConnected && _bridge.State == BridgeState.Open && Array.IndexOf(_bridge.EngineCapabilities ?? Array.Empty<string>(), CapabilitySessionDatabase) >= 0;

    public event Action? StateChanged;

    // ── Spec 042 ──────────────────────────────────────────────────────────────────────────────────────────
    public const string CapabilityPostgreSql = "dialect.postgresql.v1";

    public AkmlSql.Core.DatabaseKind Kind { get; private set; } = AkmlSql.Core.DatabaseKind.SqlServer;

    public string? ServerVersionText { get; private set; }

    public AkmlSql.Core.TransactionState TransactionState { get; private set; } = AkmlSql.Core.TransactionState.None;

    private string? _schemaIdentity;

    public string? SchemaIdentity => Kind == AkmlSql.Core.DatabaseKind.PostgreSql && _schemaIdentity != null ? _schemaIdentity : Server;

    public bool EngineSupportsPostgres => _bridge.Advertises(CapabilityPostgreSql);

    public void ReportTransactionState(AkmlSql.Core.TransactionState state)
    {
        if (!IsConnected || TransactionState == state) return;
        TransactionState = state;
        StateChanged?.Invoke();
    }

    /// <summary>The text when a PostgreSQL connection is attempted against an engine that does not advertise it (FR-006).</summary>
    internal const string PostgresUnavailable =
        "The paired engine does not support PostgreSQL. Update the engine to a version that advertises dialect.postgresql.v1.";

    public Task<ConnectResult> TestAsync(ConnectionTarget target, CancellationToken ct) =>
        target.Kind == AkmlSql.Core.DatabaseKind.PostgreSql
            ? TestPostgresAsync(target, ct)
            : AdaptAsync(TestSqlServerAsync(target.Server, target.Database, target.WindowsAuth, target.User, target.Password, target.TrustServerCertificate, ct));

    public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(ConnectionTarget target, CancellationToken ct) =>
        ListSqlServerDatabasesAsync(target.Server, target.WindowsAuth, target.User, target.Password, target.TrustServerCertificate, ct);

    public async Task<ConnectResult> ConnectAsync(ConnectionTarget target, CancellationToken ct)
    {
        if (target.Kind != AkmlSql.Core.DatabaseKind.PostgreSql)
            return await AdaptAsync(ConnectSqlServerAsync(target.Server, target.Database, target.WindowsAuth, target.User, target.Password, target.TrustServerCertificate, ct)).ConfigureAwait(false);

        // ConnectionChanged is a notification: the engine cannot say the password was wrong. So a PostgreSQL connect first
        // asks the engine to open the connection (the test), and only a server that accepted it becomes the session.
        var tested = await TestPostgresAsync(target, ct).ConfigureAwait(false);
        if (!tested.Ok) return tested;

        var (pg, _) = ParsePostgresTarget(target);
        var connectionString = BuildPostgresConnectionString(pg!);
        try
        {
            await _bridge.SendNotificationAsync(
                MessageTypes.ConnectionChanged,
                new ConnectionInfo
                {
                    SessionId = SessionId,
                    ConnectionString = connectionString,
                    DatabaseName = pg!.Database,
                    Kind = (int)AkmlSql.Core.DatabaseKind.PostgreSql,
                    RememberPassword = pg.RememberPassword,
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"ConnectionChanged (PostgreSQL) send failed: {ex.Message}");
            return ConnectResult.Failed("Could not reach the engine: " + ex.Message);
        }

        Server = pg.Host + ":" + pg.Port;
        _schemaIdentity = PgConnectionString.ServerKey(pg.Host, pg.Port);
        Database = pg.Database;
        WindowsAuth = false;
        Login = pg.User;
        Kind = AkmlSql.Core.DatabaseKind.PostgreSql;
        ServerVersionText = VersionNumberOf(tested.ServerVersion);
        TransactionState = AkmlSql.Core.TransactionState.None;
        IsConnected = true;
        NewTarget();
        _diagnostics.Log(DiagnosticLevel.Info, "sql-connect",
            $"Requested engine connect to PostgreSQL {Server}/{Database} as {pg.User} (tls={pg.SslMode}). Schema loads asynchronously.");
        StateChanged?.Invoke();
        return tested;
    }

    public Task<bool> HasRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) =>
        AskCredentialAsync(CredentialAction.Has, host, port, user, ct);

    public Task<bool> ForgetRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct) =>
        AskCredentialAsync(CredentialAction.Forget, host, port, user, ct);

    private async Task<bool> AskCredentialAsync(int action, string host, int? port, string user, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || !EngineSupportsPostgres) return false;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user)) return false;
        try
        {
            var response = await _bridge.SendAsync<CredentialRequest, CredentialResponse>(
                MessageTypes.CredentialRequest,
                new CredentialRequest
                {
                    Action = action,
                    Kind = (int)AkmlSql.Core.DatabaseKind.PostgreSql,
                    Server = host.Trim(),
                    Port = port ?? SavedSqlConnection.DefaultPostgresPort,
                    Login = user.Trim(),
                },
                ct).ConfigureAwait(false);
            return response is { Ok: true, Has: true };
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"Credential request failed: {ex.Message}");
            return false;
        }
    }

    private async Task<ConnectResult> TestPostgresAsync(ConnectionTarget target, CancellationToken ct)
    {
        var (pg, error) = ParsePostgresTarget(target);
        if (pg is null) return ConnectResult.Failed(error!);

        if (_bridge.State != BridgeState.Open)
            return ConnectResult.Failed("Pair an engine first (Engine connections → Add), then test the PostgreSQL connection.");
        if (!EngineSupportsPostgres) return ConnectResult.Failed(PostgresUnavailable);

        try
        {
            var response = await _bridge.SendAsync<TestSqlConnectionRequest, TestSqlConnectionResponse>(
                MessageTypes.TestSqlConnection,
                new TestSqlConnectionRequest { ConnectionString = BuildPostgresConnectionString(pg), Kind = (int)AkmlSql.Core.DatabaseKind.PostgreSql },
                ct).ConfigureAwait(false);

            if (response == null) return ConnectResult.Failed("The engine did not return a result for the connection test.");
            if (response.Ok)
            {
                _diagnostics.Log(DiagnosticLevel.Info, "sql-connect",
                    $"Test connection to PostgreSQL {pg.Host}:{pg.Port}/{pg.Database} succeeded ({response.ServerVersion}).");
                return new ConnectResult(true, null, false, response.ServerVersion);
            }

            var passwordRequired = response.ErrorCode == DialectErrors.PasswordRequired;
            return new ConnectResult(false,
                string.IsNullOrEmpty(response.ErrorMessage) ? "The connection test failed." : response.ErrorMessage, passwordRequired);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"TestSqlConnection (PostgreSQL) send failed: {ex.Message}");
            return ConnectResult.Failed("Could not reach the engine: " + ex.Message);
        }
    }

    private static async Task<ConnectResult> AdaptAsync(Task<(bool Ok, string? Error)> inner)
    {
        var (ok, error) = await inner.ConfigureAwait(false);
        return new ConnectResult(ok, error);
    }

    /// <summary>"PostgreSQL 18.1" becomes "18.1"; anything else is kept as the engine sent it.</summary>
    internal static string? VersionNumberOf(string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion)) return null;
        const string prefix = "PostgreSQL ";
        return serverVersion!.StartsWith(prefix, StringComparison.Ordinal) ? serverVersion.Substring(prefix.Length) : serverVersion;
    }

    // ── PostgreSQL target guard and connection string (spec 042, FR-102, FR-103) ──────────────────────────

    /// <summary>A PostgreSQL target after validation: every value checked and normalised, ready for the connection string.</summary>
    internal sealed record PgTarget(string Host, int Port, string Database, string User, string? Password, string SslMode,
        string? RootCertificatePath, bool RememberPassword);

    /// <summary>
    /// SECURITY (spec 042): the browser-side half of the PostgreSQL target guard, shared by test and connect (the engine
    /// repeats the rule in <c>PgTargetGuard</c>; this copy explains the refusal before a round trip). A PostgreSQL
    /// connection is always an explicit user and password, so a remote host is fine: the engine forwards what it is
    /// given and signs in as nothing of its own. What is refused is anything that would make it read a path or a
    /// setting the page chose: metacharacters in an identifier, a socket or UNC path as the host, a relative or network
    /// root certificate path, an unknown TLS mode.
    /// </summary>
    internal static (PgTarget? Target, string? Error) ParsePostgresTarget(ConnectionTarget target)
    {
        var (host, port, hostError) = ParseHostAndPort(target.Server, target.Port);
        if (host is null) return (null, hostError);

        var database = string.IsNullOrWhiteSpace(target.Database) ? "postgres" : target.Database.Trim();
        if (!IsSafeIdentifier(database)) return (null, "Database name contains invalid characters.");

        var user = (target.User ?? string.Empty).Trim();
        if (user.Length == 0) return (null, "A user name is required for PostgreSQL.");
        if (!IsSafeIdentifier(user)) return (null, "User name contains invalid characters.");

        var sslMode = string.IsNullOrWhiteSpace(target.SslMode) ? "prefer" : target.SslMode!.Trim().ToLowerInvariant();
        if (Array.IndexOf(SavedSqlConnection.SslModes, sslMode) < 0)
            return (null, "The TLS mode must be disable, prefer, require, verify-ca or verify-full.");

        var certificate = string.IsNullOrWhiteSpace(target.RootCertificatePath) ? null : target.RootCertificatePath!.Trim();
        if (certificate != null && !IsLocalAbsolutePath(certificate))
            return (null, "The root certificate must be a full local path on the engine machine, for example C:\\certs\\root.crt.");
        // verify-ca / verify-full without a certificate path are allowed: the engine machine's own trust store is used.

        // An empty password is "none": the engine fills in a remembered one, or asks for it.
        var password = string.IsNullOrEmpty(target.Password) ? null : target.Password;
        return (new PgTarget(host, port, database, user, password, sslMode, certificate, target.RememberPassword), null);
    }

    /// <summary>
    /// <c>host</c>, <c>host:port</c>, <c>[ipv6]</c> or <c>[ipv6]:port</c>; an unbracketed text with several colons is
    /// an IPv6 literal without a port. A port in the text wins over <paramref name="explicitPort"/>.
    /// </summary>
    internal static (string? Host, int Port, string? Error) ParseHostAndPort(string? server, int? explicitPort)
    {
        var text = (server ?? string.Empty).Trim();
        if (text.Length == 0) return (null, 0, "Host is required.");
        if (!IsSafeIdentifier(text)) return (null, 0, "Host name contains invalid characters.");
        if (text[0] == '/' || text[0] == '\\' || text.Contains('\\') || text.Contains('/'))
            return (null, 0, "A PostgreSQL server is addressed by host name or IP address, not by a path or a socket.");

        string host = text;
        string? portText = null;
        if (text[0] == '[')
        {
            var close = text.IndexOf(']');
            if (close < 0) return (null, 0, "The IPv6 address is missing its closing bracket.");
            host = text.Substring(1, close - 1);
            var rest = text.Substring(close + 1);
            if (rest.Length > 0)
            {
                if (rest[0] != ':') return (null, 0, "Host name contains invalid characters.");
                portText = rest.Substring(1);
            }
        }
        else
        {
            var firstColon = text.IndexOf(':');
            if (firstColon >= 0 && firstColon == text.LastIndexOf(':'))
            {
                host = text.Substring(0, firstColon);
                portText = text.Substring(firstColon + 1);
            }
        }

        if (host.Length == 0) return (null, 0, "Host is required.");

        var port = explicitPort ?? SavedSqlConnection.DefaultPostgresPort;
        if (portText != null)
        {
            if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port))
                return (null, 0, "The port must be a number between 1 and 65535.");
        }
        if (port is < 1 or > 65535) return (null, 0, "The port must be between 1 and 65535.");
        return (host, port, null);
    }

    /// <summary>A drive-rooted or slash-rooted local path without relative segments; network and device paths are not local.</summary>
    internal static bool IsLocalAbsolutePath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
        var rooted = (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                     || path[0] == '/';
        if (!rooted) return false;
        foreach (var segment in path.Split('\\', '/'))
        {
            if (segment == ".." || segment == ".") return false;
        }
        return !path.Any(char.IsControl);
    }

    internal static string BuildPostgresConnectionString(PgTarget target) =>
        PgConnectionString.Build(target.Host, target.Port, target.Database, target.User, target.Password, target.SslMode, target.RootCertificatePath);

    public Task<(bool Ok, string? Error)> ConnectAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        ConnectSqlServerAsync(server, database, windowsAuth, user, password, trustServerCertificate: false, ct);

    private async Task<(bool Ok, string? Error)> ConnectSqlServerAsync(
        string server, string database, bool windowsAuth, string? user, string? password, bool trustServerCertificate, CancellationToken ct)
    {
        // Identifier + loopback (SSRF) guard — single-sourced in ValidateTarget so ConnectAsync and
        // TestAsync enforce IDENTICAL rules. Runs before anything touches the bridge.
        var (ok, error) = ValidateTarget(server, database, windowsAuth);
        if (!ok) return (false, error);

        if (_bridge.State != BridgeState.Open)
            return (false, "Pair an engine first (Engine connections → Add), then connect to SQL Server.");

        // Review WEB-19: ConnectionChanged is a notification — the engine cannot say the password was wrong or the server
        // is down. So, as for PostgreSQL, the engine first opens the connection (the test) and only a server that accepted
        // it becomes the session; on failure the session and the form stay as they were. (A server that goes away between
        // the two shows up on the first run, as any later drop does.)
        var (tested, testError) = await TestSqlServerAsync(server, database, windowsAuth, user, password, trustServerCertificate, ct).ConfigureAwait(false);
        if (!tested) return (false, testError);

        var connStr = BuildConnectionString(server.Trim(), database.Trim(), windowsAuth, user, password, trustServerCertificate);

        try
        {
            await _bridge.SendNotificationAsync(
                MessageTypes.ConnectionChanged,
                new ConnectionInfo
                {
                    SessionId = SessionId,
                    ConnectionString = connStr,
                    DatabaseName = database.Trim(),
                    ServerVersion = 0,
                    EngineEdition = 0,
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"ConnectionChanged send failed: {ex.Message}");
            return (false, "Could not reach the engine: " + ex.Message);
        }

        Server = server.Trim();
        Database = database.Trim();
        WindowsAuth = windowsAuth;
        Login = windowsAuth ? null : user?.Trim();
        Kind = AkmlSql.Core.DatabaseKind.SqlServer;
        _schemaIdentity = null;
        ServerVersionText = null;
        TransactionState = AkmlSql.Core.TransactionState.None;
        IsConnected = true;
        NewTarget();
        _diagnostics.Log(DiagnosticLevel.Info, "sql-connect",
            $"Requested engine connect to {Server}/{Database} (auth={(windowsAuth ? "Windows" : "SQL")}). Schema loads asynchronously.");
        StateChanged?.Invoke();
        return (true, null);
    }

    public Task<(bool Ok, string? Error)> TestAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        TestSqlServerAsync(server, database, windowsAuth, user, password, trustServerCertificate: false, ct);

    private async Task<(bool Ok, string? Error)> TestSqlServerAsync(
        string server, string database, bool windowsAuth, string? user, string? password, bool trustServerCertificate, CancellationToken ct)
    {
        // SECURITY (Phase 4): run the SAME identifier + loopback guard as ConnectAsync, and run it
        // FIRST — before building the connection string and before sending anything to the engine.
        // TestSqlConnectionHandler opens whatever string it receives with no engine-side host check,
        // so omitting this would make Test an SSRF/confused-deputy hole. Behaviour is unchanged:
        // loopback-only; remote/UNC/Azure targets are rejected here, never reaching the engine.
        var (ok, error) = ValidateTarget(server, database, windowsAuth);
        if (!ok) return (false, error);

        if (_bridge.State != BridgeState.Open)
            return (false, "Pair an engine first (Engine connections → Add), then test the SQL connection.");

        var connStr = BuildConnectionString(server.Trim(), database.Trim(), windowsAuth, user, password, trustServerCertificate);

        try
        {
            // Reuse the EXISTING TestSqlConnection request/response pair (the same the desktop shell
            // uses) — no new connect/test IPC. The engine opens the connection, then closes it, and
            // replies Ok / ErrorMessage. This is the only path that truly validates a SQL password.
            var response = await _bridge.SendAsync<TestSqlConnectionRequest, TestSqlConnectionResponse>(
                MessageTypes.TestSqlConnection,
                new TestSqlConnectionRequest { ConnectionString = connStr },
                ct).ConfigureAwait(false);

            if (response == null)
                return (false, "The engine did not return a result for the connection test.");

            if (response.Ok)
            {
                _diagnostics.Log(DiagnosticLevel.Info, "sql-connect",
                    $"Test connection to {server.Trim()}/{database.Trim()} succeeded (auth={(windowsAuth ? "Windows" : "SQL")}).");
                return (true, null);
            }

            return (false, string.IsNullOrEmpty(response.ErrorMessage) ? "The connection test failed." : response.ErrorMessage);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"TestSqlConnection send failed: {ex.Message}");
            return (false, "Could not reach the engine: " + ex.Message);
        }
    }

    public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(
        string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        ListSqlServerDatabasesAsync(server, windowsAuth, user, password, trustServerCertificate: false, ct);

    private async Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListSqlServerDatabasesAsync(
        string server, bool windowsAuth, string? user, string? password, bool trustServerCertificate, CancellationToken ct)
    {
        // SECURITY: identical loopback/identifier guard as Connect/Test, run FIRST — the engine opens
        // this connection under its own identity, so an unguarded server is an SSRF/confused-deputy
        // lever. Enumerate against "master" (always present, and the guard requires a non-empty db).
        var (ok, error) = ValidateTarget(server, "master", windowsAuth);
        if (!ok) return (false, Array.Empty<string>(), error);

        if (_bridge.State != BridgeState.Open)
            return (false, Array.Empty<string>(), "Pair an engine first (Engine connections → Add), then list databases.");

        var connStr = BuildConnectionString(server.Trim(), "master", windowsAuth, user, password, trustServerCertificate);

        try
        {
            var response = await _bridge.SendAsync<ListDatabasesRequest, ListDatabasesResponse>(
                MessageTypes.ListDatabases,
                new ListDatabasesRequest { ConnectionString = connStr },
                ct).ConfigureAwait(false);

            if (response == null)
                return (false, Array.Empty<string>(), "The engine did not return a database list.");
            if (response.Ok)
                return (true, response.Databases ?? new List<string>(), null);
            return (false, Array.Empty<string>(),
                string.IsNullOrEmpty(response.ErrorMessage) ? "Could not list databases." : response.ErrorMessage);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"ListDatabases send failed: {ex.Message}");
            return (false, Array.Empty<string>(), "Could not reach the engine: " + ex.Message);
        }
    }

    /// <summary>The engine capability that closes the session's connection on Disconnect (review WEB-06).</summary>
    public const string CapabilitySessionDisconnect = "session.disconnect.v1";

    internal const string DisconnectUnconfirmed =
        "The engine could not be reached, so its connection for this session may stay open — with any open transaction and " +
        "its locks — until the engine closes idle connections (20 minutes) or restarts.";

    internal const string DisconnectNeedsNewerEngine =
        "This engine is too old to close its connection on Disconnect, so it may stay open — with any open transaction and " +
        "its locks — until the engine closes idle connections (20 minutes) or restarts.";

    public async Task DisconnectAsync() => await DisconnectSessionAsync(CancellationToken.None).ConfigureAwait(false);

    public async Task<DisconnectResult> DisconnectSessionAsync(CancellationToken ct)
    {
        if (!IsConnected) return new DisconnectResult(true, false, null);

        if (_bridge.State != BridgeState.Open)
        {
            ForgetConnection();
            return new DisconnectResult(false, false, DisconnectUnconfirmed);
        }
        if (!_bridge.Advertises(CapabilitySessionDisconnect))
        {
            ForgetConnection();
            return new DisconnectResult(false, false, DisconnectNeedsNewerEngine);
        }

        SessionDisconnectResponse? response;
        try
        {
            response = await _bridge.SendAsync<SessionDisconnectRequest, SessionDisconnectResponse>(
                MessageTypes.SessionDisconnect, new SessionDisconnectRequest { SessionId = SessionId }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"SessionDisconnect failed: {ex.GetType().Name}");
            ForgetConnection();
            return new DisconnectResult(false, false, DisconnectUnconfirmed);
        }

        // Any reply means the engine has already forgotten the session's connection; a refusal only says the close
        // itself waits for a statement that is still running.
        ForgetConnection();
        if (response == null) return new DisconnectResult(false, false, DisconnectUnconfirmed);
        _diagnostics.Log(DiagnosticLevel.Info, "sql-connect", $"Disconnected (rolledBack={response.RolledBack}, ok={response.Ok}).");
        return new DisconnectResult(response.Ok, response.RolledBack, response.Ok ? null : response.ErrorMessage);
    }

    private void ForgetConnection()
    {
        ConnectionGeneration++;
        IsConnected = false;
        Server = null;
        Database = null;
        Login = null;
        WindowsAuth = true;
        Kind = AkmlSql.Core.DatabaseKind.SqlServer;
        ServerVersionText = null;
        _schemaIdentity = null;
        TransactionState = AkmlSql.Core.TransactionState.None;
        StateChanged?.Invoke();
    }

    // ── Spec 041 (research R25–R28) — the database selector ─────────────────────────────────────────────

    public async Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesForSessionAsync(CancellationToken ct)
    {
        if (!IsConnected || Server == null) return (false, Array.Empty<string>(), "Connect to a database server first.");
        // The same guard as connect, first — the engine lists under the session's identity. Spec 042: the PostgreSQL session was
        // judged by the PostgreSQL guard when it connected, and its server text (host:port) is not a SQL Server address.
        if (Kind != AkmlSql.Core.DatabaseKind.PostgreSql)
        {
            var (ok, error) = ValidateTarget(Server, Database ?? "master", WindowsAuth);
            if (!ok) return (false, Array.Empty<string>(), error);
        }
        if (!CanChangeDatabase) return (false, Array.Empty<string>(), "Switching database needs a newer engine.");
        try
        {
            var response = await _bridge.SendAsync<ListDatabasesRequest, ListDatabasesResponse>(
                MessageTypes.ListDatabases, new ListDatabasesRequest { SessionId = SessionId }, ct).ConfigureAwait(false);
            if (response == null) return (false, Array.Empty<string>(), "The engine did not return a database list.");
            return response.Ok
                ? (true, response.Databases ?? new List<string>(), null)
                : (false, Array.Empty<string>(), string.IsNullOrEmpty(response.ErrorMessage) ? "Could not list databases." : response.ErrorMessage);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"ListDatabases (session) failed: {ex.Message}");
            return (false, Array.Empty<string>(), "Could not reach the engine: " + ex.Message);
        }
    }

    public async Task<(bool Ok, string? Error, bool ConnectionWasReset)> ChangeDatabaseAsync(string database, CancellationToken ct)
    {
        if (!IsConnected || Server == null) return (false, "Connect to a database server first.", false);
        if (Kind == AkmlSql.Core.DatabaseKind.PostgreSql)
        {
            var name = (database ?? string.Empty).Trim();
            if (name.Length == 0) return (false, "Database is required.", false);
            if (!IsSafeIdentifier(name)) return (false, "Database name contains invalid characters.", false);
        }
        else
        {
            var (ok, error) = ValidateTarget(Server, database ?? string.Empty, WindowsAuth);
            if (!ok) return (false, error, false);
        }
        if (!CanChangeDatabase) return (false, "Switching database needs a newer engine.", false);
        try
        {
            var response = await _bridge.SendAsync<ChangeDatabaseRequest, ChangeDatabaseResponse>(
                MessageTypes.ChangeDatabase, new ChangeDatabaseRequest { SessionId = SessionId, DatabaseName = database!.Trim() }, ct).ConfigureAwait(false);
            if (response == null) return (false, "The engine did not answer.", false);
            if (!response.Ok) return (false, string.IsNullOrEmpty(response.ErrorMessage) ? "Could not switch database." : response.ErrorMessage, false);
            _diagnostics.Log(DiagnosticLevel.Info, "sql-connect", $"Switched to {response.DatabaseName}" + (response.ConnectionWasReset ? " (the connection was reopened)." : "."));
            ReportCurrentDatabase(string.IsNullOrEmpty(response.DatabaseName) ? database!.Trim() : response.DatabaseName);
            // Spec 042 (FR-009): a PostgreSQL switch reconnects, and no transaction survives it.
            if (Kind == AkmlSql.Core.DatabaseKind.PostgreSql) ReportTransactionState((AkmlSql.Core.TransactionState)response.TransactionState);
            return (true, null, response.ConnectionWasReset);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "sql-connect", $"ChangeDatabase failed: {ex.Message}");
            return (false, "Could not reach the engine: " + ex.Message, false);
        }
    }

    public void ReportCurrentDatabase(string? database)
    {
        if (!IsConnected || string.IsNullOrEmpty(database)) return;
        if (string.Equals(Database, database, StringComparison.Ordinal)) return;
        Database = database;
        NewTarget();
        StateChanged?.Invoke();
    }

    public async Task SendDocumentAsync(string documentText, CancellationToken ct)
    {
        if (!IsConnected || _bridge.State != BridgeState.Open) return;
        try
        {
            await _bridge.SendNotificationAsync(
                MessageTypes.DocumentChanged,
                new DocumentChange
                {
                    SessionId = SessionId,
                    ChangeType = 0,
                    FullText = documentText ?? string.Empty,
                },
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — a dropped DocumentChanged just means the next completion is stale.
        }
    }

    /// <summary>
    /// SECURITY (Phase 4): the SINGLE source of truth for the SQL-target guard, shared by
    /// <see cref="ConnectAsync"/>, <see cref="TestAsync"/> and <see cref="ListDatabasesAsync"/>.
    /// Enforces, in order: non-empty server/database, no connection-string metacharacters in the
    /// identifiers (<see cref="IsSafeIdentifier"/>), and the identity rule below. Pure/static (no
    /// bridge dependency) so every caller — and a unit test — gets identical behaviour. Returns
    /// (true, null) on pass; (false, message) on the first failure.
    ///
    /// <para>
    /// A remote SQL Server is allowed with SQL Server authentication: the engine forwards the login
    /// the user typed, so no identity of its own is at stake. Windows authentication, and any
    /// named-pipe/UNC address, stay limited to the engine's own machine, because both would sign in
    /// as the engine's service account. The engine enforces the same rule itself
    /// (<c>BridgeSqlTargetGuard</c>); this copy exists to explain the refusal before a round trip.
    /// </para>
    /// </summary>
    internal static (bool Ok, string? Error) ValidateTarget(string server, string database, bool windowsAuth)
    {
        if (string.IsNullOrWhiteSpace(server))
            return (false, "Server is required.");
        if (string.IsNullOrWhiteSpace(database))
            return (false, "Database is required.");

        // Reject connection-string metacharacters in the identifiers up front. The builder would
        // safely quote them anyway, but a ';' / quote / control char in a server or database name is
        // never legitimate — it can only be an attempt to inject extra keywords (e.g. flip Integrated
        // Security), so we refuse rather than silently quote it. (Passwords may legally contain ';'
        // and are left to the builder's quoting.)
        if (!IsSafeIdentifier(server))
            return (false, "Server name contains invalid characters.");
        if (!IsSafeIdentifier(database))
            return (false, "Database name contains invalid characters.");

        // Confused-deputy guard. With Windows authentication — or over a named pipe, which is SMB —
        // the engine signs in as ITSELF, so a browser-supplied remote host would let a hostile page
        // make the engine authenticate to a machine of its choosing (and leak NTLM credentials).
        // Those stay limited to the engine's own machine. A remote server with SQL Server
        // authentication uses only the login the user typed, so it is allowed.
        if (IsLoopbackServer(server))
            return (true, null);

        if (!IsRemoteTcpServer(server))
            return (false, "A remote SQL Server must be addressed over TCP: host, host\\instance or host,port. " +
                           "Named-pipe and UNC addresses would sign in as the engine's service account.");

        if (windowsAuth)
            return (false, "Windows authentication only works for a SQL Server on the engine's own machine (enter it as localhost). " +
                           "For a remote server, choose SQL Server authentication — the engine runs as a service and would sign in " +
                           "as that service account, not as you.");

        return (true, null);
    }

    /// <summary>
    /// True for a remote server reached over TCP — a bare host, <c>host\instance</c>,
    /// <c>host,port</c>, optionally with a <c>tcp:</c> prefix. False for UNC paths and the
    /// named-pipe / shared-memory / DAC protocol prefixes, which are not plain TCP.
    /// </summary>
    private static bool IsRemoteTcpServer(string server)
    {
        var s = (server ?? string.Empty).Trim();
        if (s.Length == 0 || s.StartsWith("\\\\")) return false;
        if (s.StartsWith("np:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("admin:", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    internal static string BuildConnectionString(string server, string database, bool windowsAuth, string? user, string? password,
        bool trustServerCertificate = false)
    {
        // Use DbConnectionStringBuilder (System.Data.Common, BCL — WASM-safe, unlike
        // Microsoft.Data.SqlClient) so each value is validated and properly quoted/escaped. This
        // prevents connection-string injection: a value containing ';', '=' or quotes is wrapped
        // rather than treated as a new keyword. Application Name aids diagnostics.
        //
        // Review WEB-02: the engine reaches a remote server over the network, where the bridge's TLS does not reach, so
        // the connection is always encrypted and the server's certificate is validated — unless the user ticked "Trust
        // server certificate" for this connection (a self-signed server they know). A SQL Server on the engine's own
        // machine (often a self-signed dev instance) keeps skipping validation: that hop never leaves the computer.
        bool loopback = IsLoopbackServer(server);
        var b = new System.Data.Common.DbConnectionStringBuilder
        {
            ["Server"] = server,
            ["Database"] = database,
            ["Encrypt"] = true,
            ["TrustServerCertificate"] = loopback || trustServerCertificate,
            ["Application Name"] = "AKML SQL Web",
            ["Connect Timeout"] = 15,
        };
        if (windowsAuth)
        {
            b["Integrated Security"] = true;
        }
        else
        {
            b["User ID"] = user ?? string.Empty;
            b["Password"] = password ?? string.Empty;
        }
        return b.ConnectionString;
    }

    /// <summary>
    /// SECURITY (spec 030): true only for a LOOPBACK SQL Server target. Strips an optional
    /// <c>tcp:</c> protocol prefix, the <c>\instance</c> suffix and the <c>,port</c> suffix, then
    /// matches the host against the loopback aliases. Rejects UNC (<c>\\</c>), named-pipe/other
    /// protocol prefixes, and any non-loopback host. This is the confused-deputy/SSRF guard for the
    /// localhost-scoped build; a LAN deployment must replace it with a configured allow-list.
    /// </summary>
    internal static bool IsLoopbackServer(string server)
    {
        var s = (server ?? string.Empty).Trim();
        if (s.Length == 0) return false;
        if (s.StartsWith("\\\\")) return false; // UNC path → not a loopback TCP host
        if (s.StartsWith("np:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("admin:", StringComparison.OrdinalIgnoreCase))
            return false; // non-TCP / DAC protocol prefixes
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4).Trim();

        var host = s;
        int slash = host.IndexOf('\\'); if (slash >= 0) host = host.Substring(0, slash); // drop \instance
        int comma = host.IndexOf(','); if (comma >= 0) host = host.Substring(0, comma);   // drop ,port
        host = host.Trim().Trim('[', ']').ToLowerInvariant();

        // Named local aliases — EXACT match only. A substring test like host.StartsWith("127.")
        // is a loopback-guard bypass: "127.0.0.1.attacker.com" begins with "127." yet is a
        // DNS-resolvable REMOTE host, so the engine would connect (under its own Windows identity)
        // to the attacker and leak NTLM credentials. "(localdb)" is a local instance moniker, not a
        // network host, so it is safe to allow exactly.
        if (host is "localhost" or "." or "(local)" or "(localdb)")
            return true;

        // IP literals — accept ONLY true loopback: 127.0.0.0/8 and ::1. IPAddress.IsLoopback
        // covers the whole 127/8 block and IPv6 ::1; IPAddress.TryParse fails for any FQDN, so a
        // hostname like "127.0.0.1.attacker.com" falls through to false here.
        return System.Net.IPAddress.TryParse(host, out var addr) && System.Net.IPAddress.IsLoopback(addr);
    }

    /// <summary>
    /// True when an identifier (server / database) is free of connection-string metacharacters and
    /// control characters. Server/database names never legitimately contain ';', '=', quotes or
    /// control chars, so their presence is treated as an injection attempt.
    /// </summary>
    private static bool IsSafeIdentifier(string value)
    {
        foreach (var ch in value)
        {
            if (ch == ';' || ch == '=' || ch == '"' || ch == '\'' || char.IsControl(ch))
                return false;
        }
        return true;
    }
}
