using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides database-name completions after a <c>USE</c> keyword.
/// Always queries ONLY the current session's connection — never leaks
/// databases from other servers or other sessions. Results are inserted
/// as <c>[DatabaseName]</c> (SQL Server bracketed identifier).
///
/// Caching is scoped per session AND verified against the exact connection
/// string that was used when the entries were cached. If the session's
/// connection string changes (e.g. user reconnects to a different server in
/// the same query window), the cache entry is invalidated and the databases
/// are re-queried from the new server.
/// </summary>
public class DatabaseProvider : ICompletionProvider
{
    public string Name => "Database";

    private sealed class CachedEntry
    {
        public string ConnectionString { get; set; } = string.Empty;
        public List<string> Databases { get; set; } = new();
        public DateTime CachedAtUtc { get; set; }
    }

    // Per-session cache. Key = sessionId. Each entry remembers the exact
    // connection string it was built from so stale entries can't bleed through.
    private static readonly Dictionary<string, CachedEntry> _cache
        = new(StringComparer.Ordinal);
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Fire only in USE clause context. The cache parameter is the schema
        // cache (not the database-name list) — irrelevant here.
        return context.ClauseType == ClauseType.Use;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        var sessionId = context.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            Serilog.Log.Debug("DatabaseProvider: no sessionId on completion request, skipping");
            yield break;
        }

        var connInfo = SessionTrackerBridge.GetConnectionInfo(sessionId);
        if (connInfo == null || string.IsNullOrEmpty(connInfo.ConnectionString))
        {
            Serilog.Log.Debug("DatabaseProvider: no connection info for session {Session}", sessionId);
            yield break;
        }

        // Scrub password from connection string before logging
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connInfo.ConnectionString);
        var scrubbedServer = builder.DataSource ?? "(unknown)";
        Serilog.Log.Information(
            "DatabaseProvider.GetCompletions: session={Session} server={Server} currentDb={Db}",
            sessionId, scrubbedServer, connInfo.DatabaseName);

        // Cache-only lookup: never block the completion thread on SQL I/O.
        // On a miss, schedule a background prefetch so the next keystroke
        // will find the cache populated.
        var databases = TryGetCached(sessionId, connInfo.ConnectionString);
        if (databases == null)
        {
            SchedulePrefetch(sessionId, connInfo.ConnectionString);
            Serilog.Log.Debug(
                "DatabaseProvider: cache miss for session {Session} on server {Server} — prefetching",
                sessionId, scrubbedServer);
            yield break;
        }

        Serilog.Log.Information(
            "DatabaseProvider: returning {Count} databases for server {Server}: {Dbs}",
            databases.Count, scrubbedServer, string.Join(", ", databases.Take(15)));
        foreach (var db in databases)
        {
            // Insert the name wrapped in [ ] — matches SSMS/SQL Prompt convention
            // for identifiers, so 'USE [MyDb With Space]' works out of the box.
            yield return new CompletionItem
            {
                DisplayText = db,
                InsertText = "[" + db + "]",
                ObjectType = (int)CompletionObjectType.Database,
                SecondaryText = "Database",
                SourceObject = db,
                SortPriority = db.Equals("master", StringComparison.OrdinalIgnoreCase) ? 200 : 100
            };
        }
    }

    /// <summary>
    /// Returns the cached database list if it is fresh AND was built from the
    /// same connection string, else <c>null</c>. This is the only code path that
    /// completion providers are allowed to hit during a keystroke — SQL I/O is
    /// deferred to <see cref="SchedulePrefetch"/>.
    /// </summary>
    private static List<string>? TryGetCached(string sessionId, string connectionString)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(sessionId, out var cached))
            {
                bool sameConnection = string.Equals(cached.ConnectionString, connectionString, StringComparison.Ordinal);
                bool fresh = DateTime.UtcNow - cached.CachedAtUtc < _cacheTtl;
                if (sameConnection && fresh)
                {
                    return cached.Databases;
                }
                // Stale or connection changed — drop the entry so the next
                // prefetch rebuilds it.
                _cache.Remove(sessionId);
            }
        }
        return null;
    }

    // Track in-flight prefetches so repeated completion requests don't pile up
    // concurrent ListDatabasesAsync calls for the same session.
    private static readonly HashSet<string> _prefetchesInFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Kicks off a background task that queries <c>sys.databases</c> for the
    /// given session's connection and populates the cache. Idempotent per
    /// session: if a prefetch is already running, this is a no-op.
    /// </summary>
    public static void SchedulePrefetch(string sessionId, string connectionString)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(connectionString)) return;

        lock (_cacheLock)
        {
            if (!_prefetchesInFlight.Add(sessionId))
            {
                // Already fetching for this session — coalesce.
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            List<string> list;
            try
            {
                var svc = new SchemaMetadataService();
                list = await svc.ListDatabasesAsync(connectionString, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DatabaseProvider: ListDatabasesAsync failed for session {Session}", sessionId);
                list = new List<string>();
            }

            lock (_cacheLock)
            {
                _cache[sessionId] = new CachedEntry
                {
                    ConnectionString = connectionString,
                    Databases = list,
                    CachedAtUtc = DateTime.UtcNow
                };
                _prefetchesInFlight.Remove(sessionId);
            }
            Serilog.Log.Information(
                "DatabaseProvider: prefetch complete for session {Session} — {Count} databases cached",
                sessionId, list.Count);
        });
    }

    /// <summary>
    /// Invalidates the cached database list for a specific session. Called
    /// when the session's connection changes so the next USE completion
    /// hits the new server.
    /// </summary>
    public static void InvalidateSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_cacheLock)
        {
            _cache.Remove(sessionId);
        }
    }
}

/// <summary>
/// Lightweight bridge that lets providers access session connection metadata
/// without creating a circular dependency on the Server namespace. Populated
/// by <c>NamedPipeTransport</c> at construction time.
/// </summary>
public static class SessionTrackerBridge
{
    // volatile ensures the startup-time write by Configure() is visible to all
    // completion threads without an explicit memory barrier.
    private static volatile Func<string, ConnectionLookupResult?>? _getConnectionInfo;

    public static void Configure(Func<string, ConnectionLookupResult?> getConnectionInfo)
    {
        _getConnectionInfo = getConnectionInfo;
    }

    public static ConnectionLookupResult? GetConnectionInfo(string sessionId)
        => _getConnectionInfo?.Invoke(sessionId);
}

/// <summary>
/// Minimal connection info record exposed to completion providers via
/// <see cref="SessionTrackerBridge"/>. Deliberately excludes anything that
/// would require a reference back to the Server namespace.
/// </summary>
public sealed class ConnectionLookupResult
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
}
