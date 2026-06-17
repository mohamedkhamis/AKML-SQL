using System;
using System.Threading;
using System.Threading.Tasks;
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
    /// Tell the paired engine to open a SQL Server connection. <paramref name="windowsAuth"/> = true
    /// builds an Integrated-Security connection string (no password — the engine uses its own Windows
    /// identity); otherwise SQL auth with <paramref name="user"/>/<paramref name="password"/>.
    /// The send is fire-and-forget (ConnectionChanged is a notification); schema population runs
    /// asynchronously engine-side, so a successful return means "request sent", not "schema ready".
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

    /// <summary>Clears the local connected state. (The engine session lingers harmlessly.)</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Pushes the current editor text to the engine session (DocumentChanged) so the next completion
    /// has the live document. No-op when not connected or the bridge is closed.
    /// </summary>
    Task SendDocumentAsync(string documentText, CancellationToken ct);
}

internal sealed class SqlConnectionService : ISqlConnectionService
{
    private readonly IEngineBridge _bridge;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public SqlConnectionService(IEngineBridge bridge, IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    // One stable id per browser session — used by ConnectionChanged, DocumentChanged, and completion.
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public bool IsConnected { get; private set; }
    public string? Server { get; private set; }
    public string? Database { get; private set; }

    public event Action? StateChanged;

    public async Task<(bool Ok, string? Error)> ConnectAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct)
    {
        // Identifier + loopback (SSRF) guard — single-sourced in ValidateTarget so ConnectAsync and
        // TestAsync enforce IDENTICAL rules. Runs before anything touches the bridge.
        var (ok, error) = ValidateTarget(server, database);
        if (!ok) return (false, error);

        if (_bridge.State != BridgeState.Open)
            return (false, "Pair an engine first (Engine connections → Add), then connect to SQL Server.");

        var connStr = BuildConnectionString(server.Trim(), database.Trim(), windowsAuth, user, password);

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
        IsConnected = true;
        _diagnostics.Log(DiagnosticLevel.Info, "sql-connect",
            $"Requested engine connect to {Server}/{Database} (auth={(windowsAuth ? "Windows" : "SQL")}). Schema loads asynchronously.");
        StateChanged?.Invoke();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> TestAsync(
        string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct)
    {
        // SECURITY (Phase 4): run the SAME identifier + loopback guard as ConnectAsync, and run it
        // FIRST — before building the connection string and before sending anything to the engine.
        // TestSqlConnectionHandler opens whatever string it receives with no engine-side host check,
        // so omitting this would make Test an SSRF/confused-deputy hole. Behaviour is unchanged:
        // loopback-only; remote/UNC/Azure targets are rejected here, never reaching the engine.
        var (ok, error) = ValidateTarget(server, database);
        if (!ok) return (false, error);

        if (_bridge.State != BridgeState.Open)
            return (false, "Pair an engine first (Engine connections → Add), then test the SQL connection.");

        var connStr = BuildConnectionString(server.Trim(), database.Trim(), windowsAuth, user, password);

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

    public Task DisconnectAsync()
    {
        IsConnected = false;
        Server = null;
        Database = null;
        StateChanged?.Invoke();
        return Task.CompletedTask;
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
    /// <see cref="ConnectAsync"/> and <see cref="TestAsync"/>. Enforces, in order:
    /// non-empty server/database, no connection-string metacharacters in the identifiers
    /// (<see cref="IsSafeIdentifier"/>), and loopback-only host (<see cref="IsLoopbackServer"/>).
    /// Pure/static (no bridge dependency) so both callers — and a unit test — get identical
    /// behaviour. Returns (true, null) on pass; (false, message) on the first failure. Loopback-
    /// only is unchanged: remote/UNC/named-pipe/Azure targets are rejected.
    /// </summary>
    private static (bool Ok, string? Error) ValidateTarget(string server, string database)
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

        // Confused-deputy / SSRF guard. The engine opens this connection under ITS OWN identity
        // (for Windows auth, its Windows account), so a browser-supplied host is a confused-deputy
        // lever — a malicious/compromised page could make the engine reach an arbitrary SQL/SMB
        // listener and authenticate as the engine host. This build is localhost-scoped, so we hard-
        // restrict to loopback servers (localhost / 127.x / ::1 / . / (local) / (localdb)) and reject
        // UNC/named-pipe/remote targets. Before any LAN exposure: replace this with a configured
        // allow-list AND enforce the same check engine-side (defense in depth — the engine handlers
        // currently apply no host check of their own).
        if (!IsLoopbackServer(server))
            return (false, "This build only connects to a LOCAL SQL Server (localhost, 127.0.0.1, ., (local), (localdb)). " +
                           "Remote/UNC servers are disabled because the engine would connect under its own Windows identity.");

        return (true, null);
    }

    private static string BuildConnectionString(string server, string database, bool windowsAuth, string? user, string? password)
    {
        // Use DbConnectionStringBuilder (System.Data.Common, BCL — WASM-safe, unlike
        // Microsoft.Data.SqlClient) so each value is validated and properly quoted/escaped. This
        // prevents connection-string injection: a value containing ';', '=' or quotes is wrapped
        // rather than treated as a new keyword. TrustServerCertificate keeps a local/dev SQL Server
        // (often self-signed) from failing the TLS handshake; Application Name aids diagnostics.
        var b = new System.Data.Common.DbConnectionStringBuilder
        {
            ["Server"] = server,
            ["Database"] = database,
            ["TrustServerCertificate"] = true,
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
    private static bool IsLoopbackServer(string server)
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
