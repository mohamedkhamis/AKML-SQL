using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.4 wave 3 task T020. Connection-changed notification:
    /// the shell tells the engine a new database connection has been made, and the engine
    /// kicks off Phase A/B schema population in the background.
    ///
    /// This handler is the heaviest of the migrated set -- it touches the parser service
    /// (server version), the DatabaseProvider static cache, the schema cache, and the schema
    /// metadata service. It requires RpcContext to carry both ParserService and SchemaMetadata.
    /// </summary>
    public sealed class ConnectionChangedHandler : IRpcRequestHandler<ConnectionInfo, ConnectionInfo>
    {
        public int RequestMessageType => MessageTypes.ConnectionChanged;
        public int ResponseMessageType => 0;    // notification, no reply

        public Task<ConnectionInfo> HandleAsync(ConnectionInfo request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.ParserService == null)
                throw new InvalidOperationException("RpcContext.ParserService is required for ConnectionChanged dispatch.");
            if (ctx.SchemaMetadata == null)
                throw new InvalidOperationException("RpcContext.SchemaMetadata is required for ConnectionChanged dispatch.");

            // Spec 029: capture the previous connection string before UpdateSession overwrites it
            // so we can detect a credential change (different password / user) below.
            var previousConnStr = ctx.Sessions.GetSession(request.SessionId)?.ConnectionString;

            ctx.Sessions.UpdateSession(request);
            ctx.ParserService.SetServerVersion(request.ServerVersion);

            // Invalidate any cached database list for this session -- a new connection may be
            // to a different server and we must not leak the previous server's databases into
            // USE-completion.
            DatabaseProvider.InvalidateSession(request.SessionId);

            // Look up (or create) the schema cache for this session+database so we can consult
            // PermissionDenied BEFORE scheduling any SQL round-trip. If a prior Phase A already
            // hit 4060/18456/..., every subsequent ConnectionChanged against the same session:db
            // would otherwise re-run Phase A, re-fire SchedulePrefetch, and re-log the warning.
            var schemaCache = ctx.SchemaCache.GetOrCreateCache(request.SessionId, request.DatabaseName);

            // Spec 029: a corrected credential (or any reconnect with a different identity) produces a
            // different connection string. A PermissionDenied cache is otherwise terminal and would
            // never retry Phase A — reset it so the new credential gets a fresh attempt.
            if (schemaCache.PermissionDenied &&
                !string.Equals(previousConnStr, request.ConnectionString, StringComparison.Ordinal))
            {
                schemaCache.Schemas.Clear();
                schemaCache.ForeignKeys.Clear();
                schemaCache.Phase = PopulationPhase.NotLoaded;
                schemaCache.PermissionDenied = false;
                Log.Information("ConnectionChanged: connection string changed for session={Session} db={Db} — reset permission-denied cache for a fresh Phase A",
                    request.SessionId, request.DatabaseName);
            }

            // Warm the database-list cache in the background so the first USE-completion keystroke
            // finds it already populated and does not have to block on a SQL round trip. Skip when
            // the cache is marked permission-denied -- the prefetch would fail the same way.
            if (!schemaCache.PermissionDenied)
            {
                var s = ctx.Sessions.GetSession(request.SessionId);
                if (s != null && !string.IsNullOrEmpty(s.ConnectionString))
                {
                    DatabaseProvider.SchedulePrefetch(request.SessionId, s.ConnectionString);
                }
            }

            ctx.Logger.Information(
                "Connection changed: session={Session} db={Db} permissionDenied={Denied} -- {ConnDesc}",
                request.SessionId, request.DatabaseName, schemaCache.PermissionDenied,
                ConnectionDiagnostics.Describe(request.ConnectionString));

            // Fire-and-forget: populate schema cache Phase A (then Phase B).
            var captured = (ctx.SchemaCache, ctx.SchemaMetadata, schemaCache, request);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (captured.schemaCache.PermissionDenied)
                    {
                        // Terminal state -- a previous Phase A confirmed the current identity
                        // can't open this DB. Quiet noop until the user reconnects (which creates
                        // a fresh cache via new sessionId).
                        return;
                    }

                    if (captured.schemaCache.Phase == PopulationPhase.NotLoaded)
                    {
                        Log.Information("Starting Phase A schema population for {Db}", captured.request.DatabaseName);
                        await captured.SchemaMetadata!.PopulatePhaseAAsync(
                            captured.schemaCache, captured.request.ConnectionString, CancellationToken.None);
                        captured.SchemaCache.EvictLru();

                        // Phase B: load columns, FKs, parameters in background. Required for JOIN
                        // completions and column suggestions. Skip if Phase A ended up
                        // permission-denied (terminal).
                        if (captured.schemaCache.Phase == PopulationPhase.PhaseA
                            && !captured.schemaCache.PermissionDenied)
                        {
                            Log.Information("Starting Phase B for {Db}", captured.request.DatabaseName);
                            await captured.SchemaMetadata.PopulatePhaseBAsync(
                                captured.schemaCache, captured.request.ConnectionString, CancellationToken.None);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background Phase A population failed for {Db}", captured.request.DatabaseName);
                }
            });

            return Task.FromResult(request);    // placeholder; ResponseMessageType=0 means it is dropped
        }
    }
}
