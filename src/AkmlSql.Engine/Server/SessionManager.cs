using System.Collections.Concurrent;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using Serilog;

namespace AkmlSql.Engine.Server;

public class SessionManager
{
    private const int MaxDocumentSizeChars = 10 * 1024 * 1024; // 10 MB
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public int SessionCount => _sessions.Count;

    public void UpdateSession(ConnectionInfo info)
    {
        // Scrub password before logging the connection string so the log is safe
        // to share. This is our primary diagnostic for diagnosing which connection
        // info is being associated with each session.
        var kind = DatabaseKindNames.FromInt(info.Kind);
        string scrubbedDataSource = ScrubbedDataSource(kind, info.ConnectionString);

        _sessions.AddOrUpdate(info.SessionId,
            _ =>
            {
                Log.Information(
                    "SessionManager.UpdateSession (CREATE): session={Session} server={Server} db={Db}",
                    info.SessionId, scrubbedDataSource, info.DatabaseName);
                return new SessionState
                {
                    SessionId = info.SessionId,
                    ConnectionString = info.ConnectionString,
                    ServerVersion = info.ServerVersion,
                    EngineEdition = info.EngineEdition,
                    DatabaseName = info.DatabaseName,
                    IsConnected = true,
                    Kind = kind
                };
            },
            (_, existing) =>
            {
                string oldServer = ScrubbedDataSource(existing.Kind, existing.ConnectionString);
                Log.Information(
                    "SessionManager.UpdateSession (MODIFY): session={Session} oldServer={OldServer} newServer={NewServer} newDb={Db}",
                    info.SessionId, oldServer, scrubbedDataSource, info.DatabaseName);
                existing.ConnectionString = info.ConnectionString;
                existing.ServerVersion = info.ServerVersion;
                existing.EngineEdition = info.EngineEdition;
                existing.DatabaseName = info.DatabaseName;
                existing.IsConnected = true;
                // Spec 042: a new connection starts clean. Whatever search path the previous connection held
                // (possibly of the other kind) does not carry over; its transaction went with the persistent
                // connection, which the connection-changed handler disposes.
                existing.Kind = kind;
                existing.SearchPath = System.Array.Empty<string>();
                existing.ServerVersionText = string.Empty;
                // Spec 043 follow-up (review): who connected it is noted again by the caller (SessionPasswordCopy.NoteConnected);
                // until then no page may copy the new connection's password.
                existing.BridgeOrigin = null;
                return existing;
            });
    }

    public void UpdateDocument(DocumentChange change)
    {
        if (_sessions.TryGetValue(change.SessionId, out var session))
        {
            if (change is { ChangeType: 0, FullText: not null }) // Full
            {
                if (change.FullText.Length > MaxDocumentSizeChars)
                {
                    Log.Warning("DocumentChange for session {SessionId} exceeds size limit ({Size:N0} chars), update ignored",
                        change.SessionId, change.FullText.Length);
                    return;
                }
                session.DocumentText = change.FullText;
            }
        }
    }

    /// <summary>
    /// Spec 041 (R26, R27): the session moved to another database on the same connection (the database selector,
    /// or a script's <c>USE</c>). Updates the database and connection string only — unlike
    /// <see cref="UpdateSession"/>, nothing here is a new connection, so the persistent one is kept.
    /// </summary>
    public void SetDatabase(string sessionId, string databaseName, string connectionString)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        Log.Information("SessionManager.SetDatabase: session={Session} {OldDb} -> {NewDb}", sessionId, session.DatabaseName, databaseName);
        session.DatabaseName = databaseName;
        session.ConnectionString = connectionString;
    }

    public SessionState? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Spec 042: the database kind of a session. A missing, empty or unknown session is SQL Server, so a stray
    /// request, or one from a client that predates the feature, takes the existing path.
    /// </summary>
    public DatabaseKind KindOf(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return DatabaseKind.SqlServer;
        return _sessions.TryGetValue(sessionId, out var session) ? session.Kind : DatabaseKind.SqlServer;
    }

    /// <summary>
    /// The host (data source) of a connection string for logging, never the whole string: it may hold a
    /// password. Falls back to "(unknown)" when the string does not parse as the session's kind.
    /// </summary>
    private static string ScrubbedDataSource(DatabaseKind kind, string? connectionString)
    {
        try
        {
            if (kind == DatabaseKind.PostgreSql)
            {
                var pg = new Npgsql.NpgsqlConnectionStringBuilder(connectionString ?? string.Empty);
                return string.IsNullOrEmpty(pg.Host) ? "(unknown)" : pg.Host + ":" + pg.Port;
            }

            var cb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString ?? string.Empty);
            return cb.DataSource ?? "(unknown)";
        }
        catch
        {
            return "(unknown)";
        }
    }

    /// <summary>
    /// The session no longer has a database connection: requests that need one answer "no connection" until the next
    /// <c>ConnectionChanged</c>. Its document is kept (completion still works offline).
    /// </summary>
    public void Disconnect(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        session.IsConnected = false;
        session.ConnectionString = string.Empty;
    }

    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }
}

public class SessionState
{
    public string SessionId { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public int ServerVersion { get; set; }
    public int EngineEdition { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string DocumentText { get; set; } = string.Empty;
    public int DocumentVersion { get; set; }

    /// <summary>Spec 042: SQL Server or PostgreSQL; decides which implementation serves the session's requests.</summary>
    public DatabaseKind Kind { get; set; } = DatabaseKind.SqlServer;

    /// <summary>Spec 042: PostgreSQL's <c>server_version</c> text (for example <c>18.1</c>) for the status bar; empty for SQL Server.</summary>
    public string ServerVersionText { get; set; } = string.Empty;

    /// <summary>Spec 042 (FR-061): the PostgreSQL search path (<c>current_schemas(false)</c>) at connect time and after <c>SET search_path</c>.</summary>
    public string[] SearchPath { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Spec 043 follow-up (review): the web page that connected the session over the bridge (<c>BridgeConnection.Current</c>:
    /// <c>page:{id}</c>, or <c>socket:{id}</c> for a client that sends no page id); null when it was connected over the named
    /// pipe. A new query tab may take this session's password only from the same page (<c>SessionPasswordCopy</c>). It goes
    /// with the session when the tab is closed (<see cref="SessionManager.RemoveSession"/>).
    /// </summary>
    public string? BridgeOrigin { get; set; }

    /// <summary>
    /// Spec 043 follow-up (review, round 3): the SQL Server server, login and authentication the session's schema caches were
    /// loaded for (<c>ConnectionChangedHandler.ServerIdentity</c>: never the password or the database); null until the first
    /// <c>ConnectionChanged</c>. A connect whose identity differs drops the session's caches, or a database of the same
    /// name on the previous server would be offered for the new one. It survives <see cref="SessionManager.Disconnect"/>, which
    /// clears only the connection string, so a tab reattached to its own server keeps its caches and a tab disconnected and then
    /// connected elsewhere does not. Spec 043 follow-up (review, round 4): a PostgreSQL connect notes its own
    /// (<c>ConnectionChangedHandler.PostgresIdentity</c>: <c>pg://host:port</c> and the role), which never equals a SQL Server
    /// one, so a move between the kinds always counts as a move to another server.
    /// </summary>
    public string? SchemaIdentity { get; set; }

    // The PostgreSQL transaction state (FR-016) lives on the session's persistent connection
    // (SessionConnection.TransactionState): it belongs to the connection that holds the transaction.
}
