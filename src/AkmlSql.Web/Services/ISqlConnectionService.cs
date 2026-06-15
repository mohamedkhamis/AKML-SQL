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
        if (string.IsNullOrWhiteSpace(server))
            return (false, "Server is required.");
        if (string.IsNullOrWhiteSpace(database))
            return (false, "Database is required.");

        if (_bridge.State != BridgeState.Open)
            return (false, "Pair an engine first (Engine connections → Add), then connect to SQL Server.");

        // SECURITY NOTE (spec 030): in a LAN deployment a remote browser should NOT be able to make
        // the engine open an arbitrary server under the engine host's identity. This first cut targets
        // the localhost engine; a LAN guard (e.g. restrict to the engine host's own SQL instances, or
        // require an allow-list) is a follow-up before LAN exposure.
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

    private static string BuildConnectionString(string server, string database, bool windowsAuth, string? user, string? password)
    {
        // TrustServerCertificate=True keeps a local/dev SQL Server (often self-signed) from failing
        // the TLS handshake. Application Name aids server-side diagnostics.
        var baseStr = $"Server={server};Database={database};TrustServerCertificate=True;Application Name=AKML SQL Web;Connect Timeout=15";
        return windowsAuth
            ? baseStr + ";Integrated Security=True"
            : baseStr + $";User ID={user};Password={password}";
    }
}
