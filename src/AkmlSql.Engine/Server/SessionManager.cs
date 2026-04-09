using System.Collections.Concurrent;
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
        string scrubbedDataSource = "(unknown)";
        try
        {
            var cb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(info.ConnectionString ?? string.Empty);
            scrubbedDataSource = cb.DataSource ?? "(unknown)";
        }
        catch { }

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
                    IsConnected = true
                };
            },
            (_, existing) =>
            {
                string oldServer = "(unknown)";
                try
                {
                    var ob = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(existing.ConnectionString ?? string.Empty);
                    oldServer = ob.DataSource ?? "(unknown)";
                }
                catch { }
                Log.Information(
                    "SessionManager.UpdateSession (MODIFY): session={Session} oldServer={OldServer} newServer={NewServer} newDb={Db}",
                    info.SessionId, oldServer, scrubbedDataSource, info.DatabaseName);
                existing.ConnectionString = info.ConnectionString;
                existing.ServerVersion = info.ServerVersion;
                existing.EngineEdition = info.EngineEdition;
                existing.DatabaseName = info.DatabaseName;
                existing.IsConnected = true;
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

    public SessionState? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
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
}
