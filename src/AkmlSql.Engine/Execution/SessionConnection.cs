using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. Holds ONE long-lived <see cref="SqlConnection"/> for a single web session so
    /// <c>#temp</c> tables, <c>SET</c> options, <c>USE</c> database, and explicit transactions persist
    /// across Execute runs exactly like SSMS. A per-session <see cref="SemaphoreSlim"/>(1,1) serializes
    /// every execute and every apply (a SqlConnection runs one command at a time and is not
    /// thread-safe). A <see cref="ConcurrentDictionary{TKey,TValue}"/> of CancellationTokenSources
    /// (keyed by app-level QueryId) is the cancel-registry — the transport ct is per-connection, never
    /// per-request, so each execute creates and registers its own CTS.
    ///
    /// <para>Broken-connection handling: if the connection was previously opened but is no longer
    /// <see cref="ConnectionState.Open"/>, the reopen is SURFACED (logged + a flag returned to the
    /// caller) rather than silently swallowed, because a reopened connection loses all
    /// #temp/SET/USE state and silently breaking the SSMS-like guarantee is worse than reporting it.</para>
    /// </summary>
    public sealed class SessionConnection : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeQueries = new();
        private readonly string _sessionId;

        private SqlConnection? _conn;
        private bool _everOpened;
        private volatile bool _disposeRequested;

        public SessionConnection(string sessionId)
        {
            _sessionId = sessionId ?? string.Empty;
        }

        /// <summary>The connection string this session most recently opened/will open under.</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>UTC timestamp of the last execute/apply — drives idle eviction.</summary>
        public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>
        /// Run <paramref name="work"/> with EXCLUSIVE access to the persistent connection. Awaits the
        /// per-session gate (so concurrent executes queue), lazily opens the connection on first use
        /// (or reopens a broken one), stamps <see cref="LastUsedUtc"/>, and hands the live
        /// <see cref="SqlConnection"/> to the callback. Returns the work result plus a
        /// <c>ConnectionWasReset</c> flag (true when a previously-open connection was found broken and
        /// reopened — #temp/SET/USE state was lost) so the handler can surface it on the same call,
        /// race-free.
        /// </summary>
        public async Task<(T Result, bool ConnectionWasReset)> RunExclusiveAsync<T>(
            string connectionString,
            Func<SqlConnection, CancellationToken, Task<T>> work,
            CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // A waiter that acquired the gate after a dispose was requested must not reopen the
                // connection — bail with a status the handler maps to an error envelope.
                if (_disposeRequested)
                    throw new ObjectDisposedException(nameof(SessionConnection));

                bool wasReset = await EnsureOpenAsync(connectionString, ct).ConfigureAwait(false);
                LastUsedUtc = DateTime.UtcNow;
                var result = await work(_conn!, ct).ConfigureAwait(false);
                LastUsedUtc = DateTime.UtcNow;
                return (result, wasReset);
            }
            finally
            {
                // If a dispose was requested while we held the gate (e.g. a credential change mid-run),
                // tear the connection down HERE — under the gate, after the command finished — rather
                // than letting DisposeAsync yank it out from under the still-running command.
                if (_disposeRequested)
                {
                    try { await DisposeCoreAsync().ConfigureAwait(false); } catch { /* ignore */ }
                }
                _gate.Release();
            }
        }

        /// <summary>
        /// Ensure the connection is open. Returns true when a PREVIOUSLY-opened connection was found
        /// broken and reopened (state loss — surfaced to the user); false for first-open or reuse.
        /// </summary>
        private async Task<bool> EnsureOpenAsync(string connectionString, CancellationToken ct)
        {
            ConnectionString = connectionString;

            if (_conn != null && _conn.State == ConnectionState.Open)
            {
                return false; // reuse — persistence preserved.
            }

            bool wasBroken = _everOpened && _conn != null && _conn.State != ConnectionState.Open;

            if (_conn != null)
            {
                try { await _conn.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
                _conn = null;
            }

            // Open into a LOCAL first; publish to _conn ONLY after a successful open, so a failed open
            // (bad credential / cancel / network) never leaves a half-dead, never-opened connection in
            // the field for the next execute to trip over.
            var fresh = new SqlConnection(connectionString);
            try
            {
                await fresh.OpenAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                try { await fresh.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
                throw;
            }
            _conn = fresh;
            _everOpened = true;

            if (wasBroken)
            {
                Log.Warning(
                    "SessionConnection: reopened a broken connection for session {Session} — #temp/SET/USE state was lost.",
                    _sessionId);
            }

            return wasBroken;
        }

        /// <summary>Register a per-execute CTS so <see cref="TryCancel"/> can signal it by QueryId.</summary>
        public void RegisterQuery(string queryId, CancellationTokenSource cts)
        {
            if (!string.IsNullOrEmpty(queryId))
                _activeQueries[queryId] = cts;
        }

        /// <summary>Drop a completed execute's CTS registration.</summary>
        public void CompleteQuery(string queryId)
        {
            if (!string.IsNullOrEmpty(queryId))
                _activeQueries.TryRemove(queryId, out _);
        }

        /// <summary>Signal cancellation for an in-flight/queued execute. Returns true when found.</summary>
        public bool TryCancel(string queryId)
        {
            if (!string.IsNullOrEmpty(queryId) && _activeQueries.TryGetValue(queryId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempt a non-blocking dispose for idle eviction. Returns false WITHOUT disposing when the
        /// connection is mid-execute (the gate is held), so eviction never tears down a running query.
        /// </summary>
        public bool TryDisposeIfIdle()
        {
            if (!_gate.Wait(0))
            {
                return false; // busy — skip; it will be revisited on the next opportunistic sweep.
            }
            try
            {
                DisposeCoreAsync().GetAwaiter().GetResult();
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposeRequested = true;

            // Best-effort acquire so we don't yank the connection out from under a running command.
            bool acquired = await _gate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (!acquired)
            {
                // A command is still in flight. Do NOT dispose the live connection here — that would
                // tear it down mid-command. Cancel the active queries so it aborts promptly; the
                // RunExclusiveAsync finally observes _disposeRequested and disposes under the gate.
                foreach (var kv in _activeQueries)
                {
                    try { kv.Value.Cancel(); } catch { /* ignore */ }
                }
                return;
            }
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task DisposeCoreAsync()
        {
            foreach (var kv in _activeQueries)
            {
                try { kv.Value.Cancel(); } catch { }
                try { kv.Value.Dispose(); } catch { }
            }
            _activeQueries.Clear();

            if (_conn != null)
            {
                try { await _conn.DisposeAsync().ConfigureAwait(false); } catch { }
                _conn = null;
            }
        }
    }
}
