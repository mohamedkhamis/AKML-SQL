using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using MessagePack;
using Microsoft.Data.SqlClient;
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
    /// </summary>
    public sealed class ExecuteQueryHandler
    {
        // Engine ceilings (DoS protection — clamped regardless of what the request asks).
        public const int MaxRowsCeiling = 100_000;
        public const int CommandTimeoutCeilingSeconds = 600;
        private const int MaxRowsDefault = 1000;
        private const int CommandTimeoutDefaultSeconds = 30;

        private readonly SessionConnectionRegistry _registry;
        private readonly SchemaCacheManager _schemaCache;

        public ExecuteQueryHandler(SessionConnectionRegistry registry, SchemaCacheManager schemaCache)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _schemaCache = schemaCache ?? throw new ArgumentNullException(nameof(schemaCache));
        }

        public async Task<RpcMessage?> HandleAsync(
            RpcMessage request,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            CancellationToken ct)
        {
            string queryId = string.Empty;
            try
            {
                if (request.Payload == null)
                    return Respond(request.RequestId, Err(queryId, ExecuteStatus.Error, "Payload required"));

                var req = MessagePackSerializer.Deserialize<ExecuteQueryRequest>(request.Payload);
                queryId = req.QueryId ?? string.Empty;

                var (connStr, dbName) = sessionLookup(req.SessionId);
                if (string.IsNullOrEmpty(connStr))
                    return Respond(request.RequestId, Err(queryId, ExecuteStatus.NoConnection, "No active database connection for this session."));

                // Engine is the authority — clamp caps regardless of the request.
                int maxRows = Clamp(req.MaxRows, 1, MaxRowsCeiling, MaxRowsDefault);
                int timeoutSec = Clamp(req.CommandTimeoutSeconds, 1, CommandTimeoutCeilingSeconds, CommandTimeoutDefaultSeconds);

                // Opportunistic idle eviction (no background timer; SessionManager has no LRU today).
                _registry.EvictIdle();

                var sessionConn = _registry.GetOrCreate(req.SessionId, connStr!);
                var dbCache = !string.IsNullOrEmpty(dbName) ? _schemaCache.GetCache(req.SessionId, dbName!) : null;

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
                        (conn, _) =>
                        {
                            // We now hold the gate — register the CTS so TryCancel can reach it.
                            sessionConn.RegisterQuery(queryId, cts);
                            return ExecuteOnConnectionAsync(conn, req, maxRows, timeoutSec, dbCache, cts.Token);
                        },
                        ct).ConfigureAwait(false);

                    sw.Stop();
                    result.QueryId = queryId;
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    result.ConnectionWasReset = wasReset;
                    return Respond(request.RequestId, result);
                }
                finally
                {
                    sessionConn.CompleteQuery(queryId);
                }
            }
            catch (OperationCanceledException)
            {
                return Respond(request.RequestId, Err(queryId, ExecuteStatus.Cancelled, "Query cancelled."));
            }
            catch (SqlException sqlEx)
            {
                var status = (sqlEx.Number == -2) ? ExecuteStatus.TimedOut : ExecuteStatus.Error;
                return Respond(request.RequestId, Err(queryId, status, sqlEx.Message));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecuteQuery failed");
                return Respond(request.RequestId, Err(queryId, ExecuteStatus.Error, ex.Message));
            }
        }

        private async Task<ExecuteQueryResult> ExecuteOnConnectionAsync(
            SqlConnection conn,
            ExecuteQueryRequest req,
            int maxRows,
            int timeoutSec,
            DatabaseCache? dbCache,
            CancellationToken ct)
        {
            var messages = new List<string>();

            void OnInfo(object sender, SqlInfoMessageEventArgs e)
            {
                foreach (SqlError err in e.Errors)
                {
                    messages.Add(err.Message);
                }
            }

            conn.InfoMessage += OnInfo;
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = req.Sql;
                cmd.CommandTimeout = timeoutSec;

                // KeyInfo on the DATA reader so GetColumnSchema() yields base-table/PK provenance in
                // ONE pass — no separate SchemaOnly/FMTONLY pass (which would re-run the batch and
                // break #temp persistence). KeyInfo is harmless when provenance isn't requested.
                var behavior = req.IncludeProvenance ? CommandBehavior.KeyInfo : CommandBehavior.Default;
                await using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

                var rsReader = new ResultSetReader(dbCache);
                var sets = await rsReader.ReadAllAsync(reader, maxRows, req.IncludeProvenance, ct).ConfigureAwait(false);

                int rowsAffected = reader.RecordsAffected;
                if (rowsAffected > 0)
                {
                    messages.Add($"({rowsAffected} row{(rowsAffected == 1 ? "" : "s")} affected)");
                }

                return new ExecuteQueryResult
                {
                    Status = ExecuteStatus.Ok,
                    ResultSets = sets.ToArray(),
                    Messages = messages.ToArray(),
                    TotalRowsAffected = rowsAffected < 0 ? 0 : rowsAffected,
                };
            }
            finally
            {
                conn.InfoMessage -= OnInfo;
            }
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

        private static RpcMessage Respond(int requestId, ExecuteQueryResult result) => new()
        {
            MessageType = MessageTypes.ExecuteQueryResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(result),
        };
    }
}
