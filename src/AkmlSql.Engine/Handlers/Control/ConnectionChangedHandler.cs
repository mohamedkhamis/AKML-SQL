using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;
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

            // Spec 042 (T056): a PostgreSQL connection takes its own path. None of what follows is meaningful for it: the
            // alias resolver and the T-SQL parser's server version are SQL Server's, and the parser setting is process-wide.
            if (DatabaseKindNames.FromInt(request.Kind) == DatabaseKind.PostgreSql)
            {
                // FR-093: with the feature off no session may become a PostgreSQL session, or the handlers that have no
                // PostgreSQL service would read its text with the T-SQL parser. The session keeps its previous connection.
                // The sign is the catalog reader, made at start-up only when the feature was on: the services and handlers are
                // fixed then too, so a switch flipped while the engine runs waits for a restart, as they do.
                if (ctx.PostgresSchemaMetadata == null)
                {
                    Log.Warning("ConnectionChanged (PostgreSQL) ignored: {Reason} (session={Session})", DialectErrors.FeatureOffMessage, request.SessionId);
                    return Task.FromResult(request);
                }
                return Task.FromResult(HandlePostgres(request, ctx));
            }

            // A SQL Server client alias ("ServerDemo") becomes the server it points at before anything
            // else — the guard below must judge the real target, and SqlClient can't resolve aliases.
            request.ConnectionString = SqlAliasResolution.Apply(request.ConnectionString);

            // A notification has no reply to carry a refusal, so a bridge request for a target the
            // engine will not open under its own identity is logged and ignored: the session keeps
            // its previous connection. The web client runs the same check first and shows the reason.
            if (BridgeSqlTargetGuard.Check(request.ConnectionString) is not null)
            {
                Log.Warning("ConnectionChanged refused for a bridge request (session={Session}) — {ConnDesc}",
                    request.SessionId, ConnectionDiagnostics.Describe(request.ConnectionString));
                return Task.FromResult(request);
            }

            // A web tab on SQL authentication whose password the engine remembers sends none: fill it in here, so the session
            // (runs, schema, database list) uses it. The browser never holds it.
            request.ConnectionString = CopyPassword(request, ctx);
            request.ConnectionString = SqlServerCredentialResolver.Resolve(request.ConnectionString);

            // Spec 029: capture the previous connection string before UpdateSession overwrites it
            // so we can detect a credential change (different password / user) below.
            var previousConnStr = ctx.Sessions.GetSession(request.SessionId)?.ConnectionString;

            ctx.Sessions.UpdateSession(request);
            ctx.ParserService.SetServerVersion(request.ServerVersion);

            // Spec 030 — Phase 5: dispose the persistent execute connection when the credential changes.
            // The stale SqlConnection carries the now-wrong identity + #temp/SET/USE state; the next
            // ExecuteQuery lazily reopens under the new identity. This is the canonical disposal hook
            // (NOT socket close — a reconnect reuses the same SessionId). Null-safe: SessionConnections
            // is non-required, so older/test contexts without it skip this branch.
            if (!string.Equals(previousConnStr, request.ConnectionString, StringComparison.Ordinal))
            {
                ctx.SessionConnections?.Dispose(request.SessionId);
            }

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
                    // The password is saved only once the server has accepted it (as for PostgreSQL).
                    if (captured.request.RememberPassword)
                    {
                        try
                        {
                            await using var probe = new SqlConnection(captured.request.ConnectionString);
                            await probe.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                            SqlServerCredentialResolver.RememberIfRequested(captured.request.ConnectionString, remember: true);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("ConnectionChanged: the password was not remembered, the connection did not open ({Type})", ex.GetType().Name);
                        }
                    }

                    if (captured.schemaCache.PermissionDenied)
                    {
                        // Terminal state -- a previous Phase A confirmed the current identity
                        // can't open this DB. Quiet noop until the user reconnects (which creates
                        // a fresh cache via new sessionId).
                        return;
                    }

                    // A reload of the same cache (SchemaCacheManager.ReloadMissing) may be running.
                    if (captured.schemaCache.Phase == PopulationPhase.NotLoaded
                        && captured.SchemaCache.TryClaimPopulation(captured.schemaCache.CacheKey))
                    {
                        try
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
                        finally
                        {
                            captured.SchemaCache.ReleasePopulation(captured.schemaCache.CacheKey);
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

        /// <summary>
        /// Spec 042 (T056, FR-003, FR-102 to FR-104): the session now talks to a PostgreSQL server. The request is judged by
        /// the same guard as a test (a bridge request the engine would not open is ignored; the web client shows the reason
        /// first), a remembered password is put into the string when it has none, and the session's persistent connection is
        /// replaced when the string changed. The server's version and search path are read in the background, a password the
        /// user asked to remember is saved only once the server has accepted it, and the schema is loaded when a PostgreSQL
        /// catalog provider is registered.
        /// </summary>
        /// <summary>
        /// Spec 043 follow-up: a new query tab opened on another tab's connection takes that session's password (same server and
        /// login only, <see cref="SessionPasswordCopy"/>), as SSMS's New Query reuses the open connection.
        /// </summary>
        internal static string CopyPassword(ConnectionInfo request, RpcContext ctx)
        {
            if (string.IsNullOrEmpty(request.CopyPasswordFromSessionId)
                || ctx.Sessions.GetSession(request.CopyPasswordFromSessionId!) is not { } source) return request.ConnectionString;
            return SessionPasswordCopy.Apply(request.ConnectionString, DatabaseKindNames.FromInt(request.Kind), source.ConnectionString, source.Kind);
        }

        private static ConnectionInfo HandlePostgres(ConnectionInfo request, RpcContext ctx)
        {
            var refusal = PgTargetGuard.Check(request.ConnectionString);
            if (refusal is not null)
            {
                Log.Warning("ConnectionChanged (PostgreSQL) refused for a bridge request (session={Session}) — {ConnDesc}: {Code}",
                    request.SessionId, PgConnectionString.Describe(request.ConnectionString), refusal.Code);
                return request;
            }

            var resolver = new PgCredentialResolver();
            request.ConnectionString = CopyPassword(request, ctx);
            request.ConnectionString = resolver.Resolve(request.ConnectionString).ConnectionString;

            var previousConnStr = ctx.Sessions.GetSession(request.SessionId)?.ConnectionString;
            ctx.Sessions.UpdateSession(request);
            var identityChanged = !string.Equals(previousConnStr, request.ConnectionString, StringComparison.Ordinal);

            // The persistent connection carries the old identity, session settings and any transaction: the next execute
            // reopens under the new string (the web client asked the user before this point, FR-009).
            if (!string.Equals(previousConnStr, request.ConnectionString, StringComparison.Ordinal))
                ctx.SessionConnections?.Dispose(request.SessionId);

            ctx.Logger.Information("Connection changed (PostgreSQL): session={Session} db={Db} -- {ConnDesc}",
                request.SessionId, request.DatabaseName, PgConnectionString.Describe(request.ConnectionString));

            _ = Task.Run(async () =>
            {
                try
                {
                    var facts = await PgConnectionFactory.ReadFactsAsync(request.ConnectionString, CancellationToken.None).ConfigureAwait(false);

                    var session = ctx.Sessions.GetSession(request.SessionId);
                    if (session != null && string.Equals(session.ConnectionString, request.ConnectionString, StringComparison.Ordinal))
                    {
                        session.ServerVersionText = facts.ServerVersionText;
                        session.SearchPath = facts.SearchPath;
                    }

                    // The server accepted the password: now it is safe to remember it, if the user asked.
                    resolver.RememberIfRequested(request.ConnectionString, request.RememberPassword);

                    await PopulateSchemaAsync(ctx, request, identityChanged).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning("ConnectionChanged (PostgreSQL) could not read the server facts ({Type}) — {ConnDesc}",
                        ex.GetType().Name, PgConnectionString.Describe(request.ConnectionString));
                }
            });

            return request;
        }

        // Phase A, then Phase B, through the catalog provider of the kind. Until a PostgreSQL provider is registered there is
        // nothing to load and the cache stays empty without error.
        private static async Task PopulateSchemaAsync(RpcContext ctx, ConnectionInfo request, bool identityChanged)
        {
            var provider = ctx.SchemaMetadataFor(DatabaseKind.PostgreSql);
            if (provider == null) return;

            var cache = ctx.SchemaCache.GetOrCreateCache(request.SessionId, request.DatabaseName);
            if (!ctx.SchemaCache.TryClaimPopulation(cache.CacheKey)) return;
            try
            {
                // What a role may see is part of what was read: a catalog loaded as one role is wrong for another, which may
                // have more or fewer schemas it can use. A new connection string starts the cache over - including a cache
                // whose load was refused (PermissionDenied, still NotLoaded), or the new credentials would never be tried.
                if (identityChanged && (cache.Phase != PopulationPhase.NotLoaded || cache.PermissionDenied))
                {
                    cache.Schemas.Clear();
                    cache.ForeignKeys.Clear();
                    cache.InaccessibleSchemas = new List<string>();
                    cache.HiddenSchemas = new List<string>();
                    cache.SearchPath = new List<string>();
                    cache.Phase = PopulationPhase.NotLoaded;
                    cache.PermissionDenied = false;
                    cache.IsStale = false;
                }
                if (cache.PermissionDenied || cache.Phase != PopulationPhase.NotLoaded) return;

                await provider.PopulatePhaseAAsync(cache, request.ConnectionString, CancellationToken.None).ConfigureAwait(false);
                ctx.SchemaCache.EvictLru();
                if (cache.Phase == PopulationPhase.PhaseA && !cache.PermissionDenied)
                    await provider.PopulatePhaseBAsync(cache, request.ConnectionString, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                ctx.SchemaCache.ReleasePopulation(cache.CacheKey);
            }
        }
    }
}
