using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. Owns one <see cref="SessionConnection"/> per session id. Built once in
    /// <c>EngineComposition.Build</c> and stored on <c>RpcContext.SessionConnections</c> so all three
    /// transports (named-pipe, in-process, WebSocket) and both the ExecuteQuery and ConnectionChanged
    /// handlers share the same per-session persistent connections.
    /// </summary>
    public sealed class SessionConnectionRegistry
    {
        /// <summary>Connections idle longer than this are evicted (opportunistically, on each execute).</summary>
        public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(20);

        private readonly ConcurrentDictionary<string, SessionConnection> _connections = new();

        /// <summary>Get the existing <see cref="SessionConnection"/> for a session, or create one.</summary>
        public SessionConnection GetOrCreate(string sessionId, string connectionString)
        {
            var conn = _connections.GetOrAdd(sessionId, id => new SessionConnection(id));
            conn.ConnectionString = connectionString;
            return conn;
        }

        /// <summary>Look up an existing connection WITHOUT creating one (used by the cancel handler so
        /// a stray cancel never spawns a phantom connection or clobbers the stored connection string).</summary>
        public SessionConnection? TryGet(string sessionId)
        {
            _connections.TryGetValue(sessionId, out var conn);
            return conn;
        }

        /// <summary>
        /// Dispose and remove the connection for a session — the ConnectionChanged credential-change
        /// hook so a changed credential drops the stale connection (with its now-wrong identity /
        /// #temp / SET state) and the next execute lazily reopens under the new identity.
        /// </summary>
        public async Task DisposeAsync(string sessionId)
        {
            if (_connections.TryRemove(sessionId, out var conn))
            {
                try { await conn.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "SessionConnectionRegistry: dispose failed for session {Session}", sessionId); }
            }
        }

        /// <summary>
        /// Synchronous fire-and-forget dispose for the ConnectionChanged handler (which is synchronous).
        /// Removes the entry immediately so a fresh GetOrCreate makes a new connection; the actual
        /// SqlConnection teardown runs on a background task.
        /// </summary>
        public void Dispose(string sessionId)
        {
            if (_connections.TryRemove(sessionId, out var conn))
            {
                _ = Task.Run(async () =>
                {
                    try { await conn.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Log.Warning(ex, "SessionConnectionRegistry: async dispose failed for session {Session}", sessionId); }
                });
            }
        }

        /// <summary>
        /// Evict connections unused beyond <paramref name="idleTimeout"/>. Called opportunistically on
        /// each execute. A busy connection (mid-execute) is skipped — <see cref="SessionConnection.TryDisposeIfIdle"/>
        /// uses a non-blocking gate acquire so eviction never tears down a running query.
        /// </summary>
        public void EvictIdle(TimeSpan? idleTimeout = null)
        {
            var cutoff = DateTime.UtcNow - (idleTimeout ?? DefaultIdleTimeout);
            foreach (var kv in _connections)
            {
                if (kv.Value.LastUsedUtc >= cutoff) continue;   // not idle

                // Decide WHILE STILL IN THE DICT. TryDisposeIfIdle uses a non-blocking gate acquire: a
                // BUSY (mid-execute) connection returns false and is LEFT in place — never removed, so
                // a live query's connection can't be orphaned (the original remove-then-readd bug). Only
                // a genuinely-idle connection is disposed; then remove THAT SAME instance via the atomic
                // compare-remove so a newer connection a concurrent GetOrCreate may have installed under
                // the same key is not dropped.
                if (!kv.Value.TryDisposeIfIdle()) continue;
                _connections.TryRemove(kv);
                Log.Debug("SessionConnectionRegistry: evicted idle connection for session {Session}", kv.Key);
            }
        }

        /// <summary>Dispose every held connection (engine shutdown).</summary>
        public async Task DisposeAllAsync()
        {
            foreach (var kv in _connections)
            {
                if (_connections.TryRemove(kv.Key, out var conn))
                {
                    try { await conn.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
        }
    }
}
