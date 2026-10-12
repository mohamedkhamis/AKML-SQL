using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using MessagePack;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. RAW handler (mirrors <c>NavigationRequestHandler</c>) for ExecuteQuery.
    /// Runs the SQL on the persistent per-session <see cref="SessionConnection"/> (so #temp/SET/USE
    /// state persists), clamps caps to engine ceilings, registers a per-execute CTS for cancel, and
    /// ALWAYS returns a status-bearing <see cref="ExecuteQueryResult"/> envelope echoing the request's
    /// RequestId. It NEVER returns null and NEVER lets an exception escape — a dropped/garbled frame
    /// would hang the browser's SendAsync TCS forever (the bridge correlates strictly by RequestId).
    ///
    /// <para>Spec 041 (research R17–R19, R21) — the script runs as SSMS runs it: split on <c>GO</c>
    /// (<see cref="TsqlParserService.SplitBatches"/>), each batch its own command (a <c>GO n</c> batch n
    /// times) inside the one session gate. A failing batch does not stop the script; a closed connection
    /// skips the rest, a timeout or cancellation stops it. Server errors up to severity 16 arrive as info
    /// messages (the reader keeps going, as SSMS shows the sets before and after an error); every message
    /// and row count is recorded in stream order through <see cref="ExecuteOutcome"/>. The sets read so far
    /// are always returned. Parse (219) runs the same batches under <c>SET PARSEONLY ON</c>.</para>
    /// </summary>
    public sealed class ExecuteQueryHandler
    {
        // Engine ceilings (DoS protection — clamped regardless of what the request asks).
        public const int MaxRowsCeiling = 100_000;
        public const int CommandTimeoutCeilingSeconds = ExecuteLimits.CommandTimeoutCeilingSeconds;
        private const int MaxRowsDefault = 1000;
        private const int CommandTimeoutDefaultSeconds = ExecuteLimits.CommandTimeoutDefaultSeconds;

        // CommandBehavior.KeyInfo makes SqlClient prefix the batch with SET NO_BROWSETABLE ON, which a
        // CREATE/ALTER of these must not follow ("must be the first statement in a query batch", N1).
        private static readonly HashSet<string> FirstStatementObjects = new(StringComparer.OrdinalIgnoreCase)
        {
            "PROC", "PROCEDURE", "VIEW", "FUNCTION", "TRIGGER", "SCHEMA", "DEFAULT", "RULE",
        };

        private readonly SessionConnectionRegistry _registry;
        private readonly SchemaCacheManager _schemaCache;
        private readonly TsqlParserService _parser;
        private readonly Action<string, string>? _databaseMoved;

        /// <param name="databaseMoved">Spec 041 (R27): called with (sessionId, database) when a script's <c>USE</c> left
        /// the session in another database, so the session is re-keyed (its schema cache follows).</param>
        public ExecuteQueryHandler(SessionConnectionRegistry registry, SchemaCacheManager schemaCache, TsqlParserService? parser = null,
            Action<string, string>? databaseMoved = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _schemaCache = schemaCache ?? throw new ArgumentNullException(nameof(schemaCache));
            _parser = parser ?? new TsqlParserService();
            _databaseMoved = databaseMoved;
        }

        public Task<RpcMessage?> HandleAsync(
            RpcMessage request,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            CancellationToken ct) => HandleAsync(request, sessionLookup, parseOnly: false, ct);

        /// <param name="parseOnly">Spec 041 (R21) — check the script's syntax on the server without running
        /// it (<see cref="MessageTypes.ExecuteParse"/>); the reply is <see cref="MessageTypes.ExecuteParseResult"/>.</param>
        public async Task<RpcMessage?> HandleAsync(
            RpcMessage request,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            bool parseOnly,
            CancellationToken ct)
        {
            int replyType = parseOnly ? MessageTypes.ExecuteParseResult : MessageTypes.ExecuteQueryResult;
            string queryId = string.Empty;
            // Spec 041 (R27): the database the connection is in when the script ends — captured in a finally inside
            // the gated run, after the reader is disposed, so error, timeout and cancel replies carry it too.
            string? currentDatabase = null;
            try
            {
                if (request.Payload == null)
                    return Respond(request.RequestId, replyType, Err(queryId, ExecuteStatus.Error, "Payload required"));

                var req = MessagePackSerializer.Deserialize<ExecuteQueryRequest>(request.Payload);
                queryId = req.QueryId ?? string.Empty;

                var (connStr, dbName) = sessionLookup(req.SessionId);
                if (string.IsNullOrEmpty(connStr))
                    return Respond(request.RequestId, replyType, Err(queryId, ExecuteStatus.NoConnection, "No active database connection for this session."));

                // A GO line holding anything but a count refuses the whole script, before anything runs; so does a
                // script with more batches than one reply can describe.
                var split = _parser.SplitBatches(req.Sql);
                string? refusal = split.SeparatorError != null ? ExecuteOutcome.SeparatorErrorText
                    : split.Batches.Count > ExecuteOutcome.MaxBatches ? ExecuteOutcome.TooManyBatchesText(split.Batches.Count)
                    : null;
                if (refusal != null)
                {
                    var refused = new ExecuteOutcome();
                    refused.RefuseScript(refusal);
                    var reply = refused.Build(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    reply.QueryId = queryId;
                    return Respond(request.RequestId, replyType, reply);
                }

                // Engine is the authority — clamp caps regardless of the request.
                int maxRows = Clamp(req.MaxRows, 1, MaxRowsCeiling, MaxRowsDefault);
                int timeoutSec = Clamp(req.CommandTimeoutSeconds, 1, CommandTimeoutCeilingSeconds, CommandTimeoutDefaultSeconds);

                // Opportunistic idle eviction (no background timer; SessionManager has no LRU today).
                _registry.EvictIdle();

                var sessionConn = _registry.GetOrCreate(req.SessionId, connStr!);
                // Spec 041 (R27): a script's USE moves it between databases — each set's primary-key check uses
                // the schema of the database it was read from (looked up once per database per execute).
                var caches = new Dictionary<string, DatabaseCache?>(StringComparer.OrdinalIgnoreCase);
                DatabaseCache? CacheFor(string database)
                {
                    if (!caches.TryGetValue(database, out var cache))
                        caches[database] = cache = _schemaCache.GetCache(req.SessionId, database);
                    return cache;
                }

                // The whole script is bounded, not just each command: the engine connection serves one request
                // at a time, so a script without an end would hold up completion, schema refresh and Cancel.
                long commandRuns = parseOnly ? split.Batches.Count : split.Batches.Sum(b => (long)Math.Min(Math.Max(1, b.RepeatCount), ExecuteOutcome.MaxBatchRepeat));
                var scriptLimit = ExecuteLimits.ScriptTimeLimit(timeoutSec, commandRuns);

                // Per-execute CTS — the transport ct is per-CONNECTION, never per-request.
                // IMPORTANT: RegisterQuery must happen INSIDE the gate (after RunExclusiveAsync
                // acquires it), not before. Registering before the gate is entered means a
                // CancelQuery request that arrives while this execute is still queued (waiting
                // for the semaphore) would cancel a not-yet-running request. We register inside
                // the work callback so the CTS is only visible to TryCancel once the request is
                // actually the active one executing on the connection.
                using var cts = new CancellationTokenSource();
                var sw = Stopwatch.StartNew();

                try
                {
                    var (result, wasReset) = await sessionConn.RunExclusiveAsync(
                        connStr!,
                        async (conn, _) =>
                        {
                            // We now hold the gate — register the CTS so TryCancel can reach it.
                            sessionConn.RegisterQuery(queryId, cts);
                            try
                            {
                                return await RunScriptAsync(conn, req, split.Batches, maxRows, timeoutSec, scriptLimit, CacheFor, parseOnly, cts.Token).ConfigureAwait(false);
                            }
                            finally
                            {
                                currentDatabase = CurrentDatabaseOf(conn);
                            }
                        },
                        ct).ConfigureAwait(false);

                    sw.Stop();
                    result.QueryId = queryId;
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    result.ConnectionWasReset = wasReset;
                    result.CurrentDatabase = currentDatabase;
                    NoteDatabase(req.SessionId, dbName, currentDatabase);
                    return Respond(request.RequestId, replyType, result);
                }
                finally
                {
                    sessionConn.CompleteQuery(queryId);
                }
            }
            catch (OperationCanceledException)
            {
                return Respond(request.RequestId, replyType, WithDatabase(Err(queryId, ExecuteStatus.Cancelled, "Query cancelled."), currentDatabase));
            }
            catch (SqlException sqlEx)
            {
                // The connection could not be opened (nothing ran): the old envelope.
                var status = (sqlEx.Number == -2) ? ExecuteStatus.TimedOut : ExecuteStatus.Error;
                return Respond(request.RequestId, replyType, WithDatabase(Err(queryId, status, sqlEx.Message), currentDatabase));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecuteQuery failed");
                return Respond(request.RequestId, replyType, WithDatabase(Err(queryId, ExecuteStatus.Error, ex.Message), currentDatabase));
            }
        }

        private static ExecuteQueryResult WithDatabase(ExecuteQueryResult result, string? database)
        {
            result.CurrentDatabase = database;
            return result;
        }

        /// <summary>The connection's database, or null when it is closed (nothing to report).</summary>
        private static string? CurrentDatabaseOf(SqlConnection conn)
        {
            try { return conn.State == System.Data.ConnectionState.Open ? conn.Database : null; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>A <c>USE</c> moved the session: re-key it so completion, the Schema panel and the next Apply follow.</summary>
        private void NoteDatabase(string sessionId, string? sessionDatabase, string? currentDatabase)
        {
            if (string.IsNullOrEmpty(currentDatabase) || _databaseMoved == null) return;
            if (string.Equals(sessionDatabase, currentDatabase, StringComparison.OrdinalIgnoreCase)) return;
            try { _databaseMoved(sessionId, currentDatabase!); }
            catch (Exception ex) { Log.Warning(ex, "Re-keying session {Session} to {Db} failed", sessionId, currentDatabase); }
        }

        private enum BatchFate { Ran, ConnectionClosed, TimedOut, Cancelled }

        private async Task<ExecuteQueryResult> RunScriptAsync(
            SqlConnection conn,
            ExecuteQueryRequest req,
            IReadOnlyList<SqlBatchSpan> batches,
            int maxRows,
            int timeoutSec,
            TimeSpan scriptLimit,
            Func<string, DatabaseCache?> cacheFor,
            bool parseOnly,
            CancellationToken cancel)
        {
            var outcome = new ExecuteOutcome();
            outcome.ReserveBatches(batches.Count);
            var reader = new ResultSetReader(() => CurrentDatabaseOf(conn), cacheFor);

            // The script's time limit starts once it holds the connection; reaching it stops the script as a
            // timeout (the user's Cancel stays a cancellation).
            using var limit = new CancellationTokenSource(scriptLimit);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, limit.Token);
            var ct = linked.Token;
            bool LimitReached() => limit.IsCancellationRequested && !cancel.IsCancellationRequested;

            void OnInfo(object sender, SqlInfoMessageEventArgs e)
            {
                foreach (SqlError err in e.Errors)
                    outcome.AddServerMessage(err.Number, err.Class, err.State, err.LineNumber, err.Procedure, err.Message);
            }

            // Spec 041 (R18, N5): errors up to severity 16 come through InfoMessage and the reader carries on,
            // as SSMS shows the sets around an error. Scoped to this execute — ApplyChanges relies on
            // exceptions — and safe to toggle because the session gate is held.
            bool previousFireInfo = conn.FireInfoMessageEventOnUserErrors;
            conn.InfoMessage += OnInfo;
            conn.FireInfoMessageEventOnUserErrors = true;
            try
            {
                if (parseOnly)
                {
                    await RunSettingAsync(conn, "SET PARSEONLY ON", timeoutSec, ct).ConfigureAwait(false);
                }

                for (int b = 0; b < batches.Count; b++)
                {
                    var span = batches[b];
                    outcome.BeginBatch(span, parseOnly ? 1 : span.RepeatCount);
                    int repeat = parseOnly ? 1 : outcome.ClampRepeat(span.RepeatCount);
                    bool keyInfo = !parseOnly && req.IncludeProvenance && WantsKeyInfo(span.Text);

                    var sw = Stopwatch.StartNew();
                    if (repeat > 1) outcome.BeginLoop();
                    int completed = 0;
                    var fate = BatchFate.Ran;
                    for (int run = 0; run < repeat; run++)
                    {
                        fate = await RunBatchOnceAsync(conn, span.Text, timeoutSec, maxRows, req.IncludeProvenance, keyInfo, parseOnly, reader, outcome, ct)
                            .ConfigureAwait(false);
                        if (fate != BatchFate.Ran) break;
                        completed++;
                    }
                    if (repeat > 1 && fate == BatchFate.Ran) outcome.EndLoop(completed);
                    outcome.EndBatch(sw.ElapsedMilliseconds);

                    var remaining = batches.Skip(b + 1).ToArray();
                    if (fate == BatchFate.ConnectionClosed)
                    {
                        outcome.SkipRemaining(remaining);
                        break;
                    }
                    if (fate == BatchFate.Cancelled && LimitReached())
                    {
                        outcome.AddEngineError(ExecuteOutcome.ScriptTimeLimitText(scriptLimit));
                        fate = BatchFate.TimedOut;
                    }
                    if (fate == BatchFate.TimedOut || fate == BatchFate.Cancelled)
                    {
                        outcome.Stop(fate == BatchFate.TimedOut ? ExecuteStatus.TimedOut : ExecuteStatus.Cancelled);
                        outcome.SkipAfterStop(remaining);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (LimitReached())
            {
                // Reached the limit outside a batch (SET PARSEONLY ON): the script stops as a timeout.
                outcome.AddEngineError(ExecuteOutcome.ScriptTimeLimitText(scriptLimit));
                outcome.Stop(ExecuteStatus.TimedOut);
            }
            finally
            {
                if (parseOnly && conn.State == ConnectionState.Open)
                {
                    try
                    {
                        await RunSettingAsync(conn, "SET PARSEONLY OFF", timeoutSec, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // PARSEONLY is session-sticky: a connection left in it would parse every later execute.
                        // Close it (we hold the gate); the next execute reopens and reports the reset.
                        Log.Warning(ex, "ExecuteParse: SET PARSEONLY OFF failed; closing the session connection.");
                        try { conn.Close(); } catch { /* already broken */ }
                    }
                }
                conn.FireInfoMessageEventOnUserErrors = previousFireInfo;
                conn.InfoMessage -= OnInfo;
            }

            return outcome.Build(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        private static async Task<BatchFate> RunBatchOnceAsync(
            SqlConnection conn,
            string text,
            int timeoutSec,
            int maxRows,
            bool includeProvenance,
            bool keyInfo,
            bool parseOnly,
            ResultSetReader reader,
            ExecuteOutcome outcome,
            CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = text;
            cmd.CommandTimeout = timeoutSec;

            // Spec 041 (R19): one count per statement, in stream order; nothing under SET NOCOUNT ON.
            void OnStatement(object sender, StatementCompletedEventArgs e) => outcome.AddRowCount(e.RecordCount);
            if (!parseOnly) cmd.StatementCompleted += OnStatement;
            try
            {
                var behavior = keyInfo ? CommandBehavior.KeyInfo : CommandBehavior.Default;
                await using var dataReader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (parseOnly)
                {
                    while (await dataReader.NextResultAsync(ct).ConfigureAwait(false)) { /* nothing runs */ }
                }
                else
                {
                    await reader.ReadAllAsync(dataReader, maxRows, includeProvenance, outcome, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return BatchFate.Cancelled;
            }
            catch (SqlException ex) when (ct.IsCancellationRequested)
            {
                Log.Debug(ex, "ExecuteQuery: cancelled while running.");
                return BatchFate.Cancelled;
            }
            catch (SqlException ex)
            {
                // Errors that still throw: severity 17 and above, and the client's timeout (-2).
                foreach (SqlError err in ex.Errors)
                    outcome.AddServerMessage(err.Number, err.Class, err.State, err.LineNumber, err.Procedure, err.Message);
                if (ex.Number == -2) return BatchFate.TimedOut;
            }
            finally
            {
                if (!parseOnly) cmd.StatementCompleted -= OnStatement;
            }
            return conn.State == ConnectionState.Open ? BatchFate.Ran : BatchFate.ConnectionClosed;
        }

        private static async Task RunSettingAsync(SqlConnection conn, string sql, int timeoutSec, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = timeoutSec;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Spec 041 (X3, N1) — KeyInfo (for the editable grid's provenance) only for a batch that selects
        /// something and does not start with <c>CREATE</c>/<c>ALTER</c> of an object that must come first in
        /// its batch.
        /// </summary>
        internal bool WantsKeyInfo(string batch)
        {
            IList<TSqlParserToken> tokens;
            try { tokens = _parser.GetTokenStream(batch); }
            catch { return false; }

            var significant = tokens.Where(t => t.TokenType != TSqlTokenType.WhiteSpace
                                                && t.TokenType != TSqlTokenType.SingleLineComment
                                                && t.TokenType != TSqlTokenType.MultilineComment
                                                && t.TokenType != TSqlTokenType.EndOfFile);
            // The first four words cover CREATE <object>, ALTER <object> and CREATE OR ALTER <object>.
            var leading = new List<string>(4);
            bool anySelect = false;
            foreach (var t in significant)
            {
                if (leading.Count < 4) leading.Add(t.Text);
                if (t.TokenType == TSqlTokenType.Select) anySelect = true;
                if (anySelect && leading.Count == 4) break;
            }
            if (!anySelect) return false;
            return !CreatesFirstStatementObject(leading);
        }

        /// <summary><c>CREATE</c>, <c>ALTER</c> or <c>CREATE OR ALTER</c> of an object that must be first in its batch.</summary>
        private static bool CreatesFirstStatementObject(IReadOnlyList<string> leading)
        {
            if (leading.Count < 2) return false;
            bool create = leading[0].Equals("CREATE", StringComparison.OrdinalIgnoreCase);
            bool alter = leading[0].Equals("ALTER", StringComparison.OrdinalIgnoreCase);
            if (!create && !alter) return false;
            if (FirstStatementObjects.Contains(leading[1])) return true;
            return create && leading.Count >= 4
                && leading[1].Equals("OR", StringComparison.OrdinalIgnoreCase)
                && leading[2].Equals("ALTER", StringComparison.OrdinalIgnoreCase)
                && FirstStatementObjects.Contains(leading[3]);
        }

        private static int Clamp(int value, int min, int max, int fallbackWhenNonPositive)
        {
            if (value <= 0) value = fallbackWhenNonPositive;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static ExecuteQueryResult Err(string queryId, int status, string message) => new()
        {
            QueryId = queryId,
            Status = status,
            ErrorMessage = message,
        };

        private static RpcMessage Respond(int requestId, int messageType, ExecuteQueryResult result) => new()
        {
            MessageType = messageType,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(result),
        };
    }
}
