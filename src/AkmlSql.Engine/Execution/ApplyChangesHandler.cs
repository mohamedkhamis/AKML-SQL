using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. RAW handler for ApplyChanges. Runs every grid edit inside ONE explicit
    /// <see cref="SqlTransaction"/> on the SAME persistent <see cref="SessionConnection"/> (so writes
    /// see/extend the live SET/transaction state). RUN-IMMEDIATELY: no type-to-confirm. On any error
    /// the batch rolls back and the per-edit results say which row failed; on full success it commits.
    /// ALWAYS returns a status-bearing envelope (never null) echoing the request RequestId.
    /// </summary>
    public sealed class ApplyChangesHandler
    {
        private readonly SessionConnectionRegistry _registry;

        public ApplyChangesHandler(SessionConnectionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public async Task<RpcMessage?> HandleAsync(
            RpcMessage request,
            Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
            CancellationToken ct)
        {
            try
            {
                if (request.Payload == null)
                    return Respond(request.RequestId, Err(ExecuteStatus.Error, "Payload required"));

                var req = MessagePackSerializer.Deserialize<ApplyChangesRequest>(request.Payload);

                var (connStr, _) = sessionLookup(req.SessionId);
                if (string.IsNullOrEmpty(connStr))
                    return Respond(request.RequestId, Err(ExecuteStatus.NoConnection, "No active database connection for this session."));

                if (req.Edits.Length == 0)
                    return Respond(request.RequestId, new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = Array.Empty<CrudEditResult>() });

                var sessionConn = _registry.GetOrCreate(req.SessionId, connStr!);

                var (result, wasReset) = await sessionConn.RunExclusiveAsync(
                    connStr!,
                    (conn, token) => ApplyOnConnectionAsync(conn, req, token),
                    ct).ConfigureAwait(false);

                // Surface a silent reopen (the persistent connection was found broken and reopened),
                // which would have lost #temp/SET/transaction state the edits may have assumed.
                result.ConnectionWasReset = wasReset;
                return Respond(request.RequestId, result);
            }
            catch (OperationCanceledException)
            {
                return Respond(request.RequestId, Err(ExecuteStatus.Cancelled, "Apply cancelled."));
            }
            catch (SqlException sqlEx)
            {
                return Respond(request.RequestId, Err(ExecuteStatus.Error, sqlEx.Message));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ApplyChanges failed");
                return Respond(request.RequestId, Err(ExecuteStatus.Error, ex.Message));
            }
        }

        private static async Task<ApplyChangesResult> ApplyOnConnectionAsync(
            SqlConnection conn,
            ApplyChangesRequest req,
            CancellationToken ct)
        {
            var results = new CrudEditResult[req.Edits.Length];
            SqlTransaction? tx = null;
            try
            {
                tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

                for (int i = 0; i < req.Edits.Length; i++)
                {
                    var edit = req.Edits[i];
                    try
                    {
                        await using var cmd = CrudWriteGenerator.BuildCommand(req, edit, conn, tx);

                        if (edit.Op == CrudOp.Insert)
                        {
                            // INSERT ...; SELECT SCOPE_IDENTITY(); — ExecuteScalar returns the identity
                            // (or DBNull when the table has no identity column).
                            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                            string? newId = (scalar is null || scalar is DBNull)
                                ? null
                                : SqlScalarEncoder.Encode(scalar);
                            results[i] = new CrudEditResult { Index = i, Ok = true, RowsAffected = 1, NewIdentity = newId };
                        }
                        else
                        {
                            // The UPDATE/DELETE command ends with "SELECT @@ROWCOUNT" (see CrudWriteGenerator),
                            // so read the affected count via ExecuteScalar. @@ROWCOUNT is unaffected by
                            // SET NOCOUNT ON (which ExecuteNonQuery's return value is NOT — it yields -1).
                            var countScalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                            int affected = (countScalar is null or DBNull) ? 0 : Convert.ToInt32(countScalar);
                            if (affected != 1)
                            {
                                // A keyed UPDATE/DELETE must touch EXACTLY one row. Any other count means
                                // the WHERE key wasn't a unique row identifier — fail (and roll back the
                                // batch) rather than silently writing/clearing multiple or zero rows.
                                var opName = edit.Op == CrudOp.Delete ? "DELETE" : "UPDATE";
                                throw new InvalidOperationException(
                                    $"This {opName} affected {affected} row(s); expected exactly 1 " +
                                    "(the key did not identify a single row).");
                            }
                            results[i] = new CrudEditResult { Index = i, Ok = true, RowsAffected = affected };
                        }
                    }
                    catch (Exception ex)
                    {
                        // All-or-nothing: roll back, then report EVERY edit as not-Ok. The failed edit
                        // carries the real error; the rest a rolled-back note — so the grid never clears
                        // a row tint for a write that did not persist (earlier edits were rolled back;
                        // trailing edits never ran).
                        await SafeRollbackAsync(tx).ConfigureAwait(false);
                        for (int j = 0; j < results.Length; j++)
                        {
                            results[j] = (j == i)
                                ? new CrudEditResult { Index = j, Ok = false, Error = ex.Message }
                                : new CrudEditResult { Index = j, Ok = false, Error = $"Rolled back — edit {i} failed." };
                        }
                        return new ApplyChangesResult
                        {
                            Status = ExecuteStatus.Error,
                            ErrorMessage = $"Edit {i} failed: {ex.Message}",
                            Results = results,
                        };
                    }
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new ApplyChangesResult { Status = ExecuteStatus.Ok, Results = results };
            }
            catch
            {
                await SafeRollbackAsync(tx).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task SafeRollbackAsync(SqlTransaction? tx)
        {
            if (tx == null) return;
            try { await tx.RollbackAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug(ex, "ApplyChanges: rollback failed (non-fatal)."); }
        }

        private static ApplyChangesResult Err(int status, string message) => new()
        {
            Status = status,
            ErrorMessage = message,
            Results = Array.Empty<CrudEditResult>(),
        };

        private static RpcMessage Respond(int requestId, ApplyChangesResult result) => new()
        {
            MessageType = MessageTypes.ApplyChangesResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(result),
        };
    }
}
