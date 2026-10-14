using System;
using System.Linq;
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
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    /// <summary>
    /// Spec 041 (research R26, N7, N27, contracts/ipc.md) — the toolbar's database selector: switch the session's
    /// current database the way SSMS does, on the SAME physical connection (<c>SqlConnection.ChangeDatabase</c>
    /// under the session gate), so #temp tables and SET options survive. Then the session is re-keyed to the new
    /// database and its schema cache is created and populated (Phase A, then B) under a claimed populate, as
    /// <see cref="ConnectionChangedHandler"/> does — never through <c>SchemaRefreshService.Refresh</c>, which
    /// expects a cache that already exists.
    /// </summary>
    public sealed class ChangeDatabaseHandler : IRpcRequestHandler<ChangeDatabaseRequest, ChangeDatabaseResponse>
    {
        public int RequestMessageType => MessageTypes.ChangeDatabase;
        public int ResponseMessageType => MessageTypes.ChangeDatabaseResult;

        /// <summary>
        /// A name SQL Server could have given a database: not blank, at most 128 characters (sysname), no control
        /// characters. Any other character is legal (<c>O'Brien</c>, <c>Sales;2025</c>) and safe:
        /// <see cref="SqlConnectionStringBuilder"/> quotes the value it puts in the connection string and
        /// <c>SqlConnection.ChangeDatabase</c> brackets the identifier it sends.
        /// </summary>
        internal static bool IsSafeDatabaseName(string? name) =>
            !string.IsNullOrWhiteSpace(name)
            && name!.Length <= 128
            && !name.Any(char.IsControl);

        public async Task<ChangeDatabaseResponse> HandleAsync(ChangeDatabaseRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = ctx.Sessions.GetSession(request.SessionId ?? string.Empty);
            if (session == null || !session.IsConnected || string.IsNullOrEmpty(session.ConnectionString))
                return Refuse("The session is not connected to SQL Server.");

            if (!IsSafeDatabaseName(request.DatabaseName))
                return Refuse("That is not a valid database name.");
            var database = request.DatabaseName!;   // as listed: leading and trailing spaces are part of a name

            // Spec 042 (FR-031): PostgreSQL cannot change database on a connection; the session reconnects to the new one.
            if (session.Kind == DatabaseKind.PostgreSql)
                return await SwitchPostgresAsync(request.SessionId!, session, database, ctx, ct).ConfigureAwait(false);

            // The same connection string, pointed at the new database (and its alias resolved), judged by the guard
            // as every other bridge request is.
            string connectionString;
            try
            {
                var builder = new SqlConnectionStringBuilder(SqlAliasResolution.Apply(session.ConnectionString)) { InitialCatalog = database };
                connectionString = builder.ConnectionString;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return Refuse("The session's connection string is not valid.");
            }
            if (BridgeSqlTargetGuard.Check(connectionString) is { } refused)
            {
                Log.Warning("ChangeDatabase refused for a bridge request (session={Session}) — {ConnDesc}",
                    request.SessionId, ConnectionDiagnostics.Describe(connectionString));
                return Refuse(refused);
            }

            var registry = ctx.SessionConnections;
            if (registry == null) return Refuse("This engine cannot switch databases.");

            string current;
            bool wasReset;
            try
            {
                // The persistent connection opens with the session's CURRENT connection string if it never opened
                // (or broke); ChangeDatabase then moves it. Synchronous on purpose: SqlClient has no async variant.
                var connection = registry.GetOrCreate(request.SessionId!, session.ConnectionString);
                (current, wasReset) = await connection.RunExclusiveAsync(session.ConnectionString, (conn, _) =>
                {
                    conn.ChangeDatabase(database);
                    return Task.FromResult(conn.Database);
                }, ct).ConfigureAwait(false);
                connection.ConnectionString = connectionString;   // a later reopen lands in the new database
            }
            catch (SqlException sqlEx)
            {
                Log.Warning("ChangeDatabase failed (err={Num}) for session={Session} db={Db}", sqlEx.Number, request.SessionId, database);
                return Refuse(sqlEx.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                return Refuse(ex.Message);
            }

            Rekey(ctx, request.SessionId!, current, connectionString);
            Log.Information("ChangeDatabase: session={Session} now in {Db} (reset={Reset})", request.SessionId, current, wasReset);
            return new ChangeDatabaseResponse { Ok = true, DatabaseName = current, ConnectionWasReset = wasReset };
        }

        /// <summary>
        /// Spec 042 (FR-031, FR-009): a PostgreSQL connection is to one database for its whole life, so the switch is a
        /// reconnect. The new database is opened first - a name that does not exist, or one the role may not connect to, leaves the
        /// session exactly as it was. Then the old connection goes (its temporary tables and settings with it, and an open
        /// transaction is rolled back, never committed: the web edition asked first) and the session is re-keyed to the new
        /// database, which is read in the background. The reply says the connection was reset and the transaction is gone.
        /// </summary>
        private static async Task<ChangeDatabaseResponse> SwitchPostgresAsync(
            string sessionId, SessionState session, string database, RpcContext ctx, CancellationToken ct)
        {
            string connectionString, host;
            int port;
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(session.ConnectionString) { Database = database };
                connectionString = builder.ConnectionString;
                host = builder.Host ?? string.Empty;
                port = builder.Port;
            }
            catch (ArgumentException)
            {
                return Refuse("The session's connection string is not valid.");
            }

            if (PgTargetGuard.Check(connectionString) is { } refusal)
            {
                Log.Warning("ChangeDatabase (PostgreSQL) refused for a bridge request (session={Session}) - {ConnDesc}",
                    sessionId, PgConnectionString.Describe(connectionString));
                return Refuse(refusal.Message);
            }

            var registry = ctx.SessionConnections;
            if (registry == null) return Refuse("This engine cannot switch databases.");

            PgConnectionFacts facts;
            try
            {
                facts = await PgConnectionFactory.ReadFactsAsync(connectionString, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning("ChangeDatabase (PostgreSQL) failed ({Type}) for session={Session}", ex.GetType().Name, sessionId);
                return Refuse(PgConnectionFailure.Describe(ex, host, port).Message);
            }

            registry.Dispose(sessionId);
            registry.GetOrCreate(sessionId, connectionString, DatabaseKind.PostgreSql);   // the next run opens in the new database

            Rekey(ctx, sessionId, facts.Database, connectionString);
            session.TransactionState = TransactionState.None;
            session.SearchPath = facts.SearchPath;
            Log.Information("ChangeDatabase (PostgreSQL): session={Session} now in {Db}", sessionId, facts.Database);
            return new ChangeDatabaseResponse
            {
                Ok = true, DatabaseName = facts.Database, ConnectionWasReset = true, TransactionState = (int)TransactionState.None,
            };
        }

        /// <summary>
        /// Point the session at <paramref name="database"/> and give it a populated schema cache — used by the switch
        /// and by an execute whose <c>USE</c> moved the connection (R27). Never disposes the connection.
        /// </summary>
        internal static void Rekey(RpcContext ctx, string sessionId, string database, string connectionString)
        {
            ctx.Sessions.SetDatabase(sessionId, database, connectionString);
            DatabaseProvider.InvalidateSession(sessionId);

            var cache = ctx.SchemaCache.GetOrCreateCache(sessionId, database);
            // Spec 042: the catalog reader of the session's kind.
            var metadata = ctx.SchemaMetadataFor(ctx.Sessions.KindOf(sessionId));
            if (cache.PermissionDenied || metadata == null) return;
            if (cache.Phase != PopulationPhase.NotLoaded || !ctx.SchemaCache.TryClaimPopulation(cache.CacheKey)) return;

            var manager = ctx.SchemaCache;
            _ = Task.Run(async () =>
            {
                try
                {
                    await metadata.PopulatePhaseAAsync(cache, connectionString, CancellationToken.None).ConfigureAwait(false);
                    manager.EvictLru();
                    if (cache.Phase == PopulationPhase.PhaseA && !cache.PermissionDenied)
                        await metadata.PopulatePhaseBAsync(cache, connectionString, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Schema population after a database switch failed for {Db}", database);
                }
                finally
                {
                    manager.ReleasePopulation(cache.CacheKey);
                }
            });
        }

        /// <summary>Spec 041 (R27): an execute's <c>USE</c> left the session in <paramref name="database"/> — re-key it.</summary>
        internal static void MoveSession(RpcContext ctx, string sessionId, string database)
        {
            var session = ctx.Sessions.GetSession(sessionId);
            if (session == null || string.IsNullOrEmpty(session.ConnectionString)) return;
            string connectionString;
            try { connectionString = new SqlConnectionStringBuilder(session.ConnectionString) { InitialCatalog = database }.ConnectionString; }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException) { return; }
            ctx.SessionConnections?.GetOrCreate(sessionId, connectionString);   // a reopen lands in the database the script chose
            Rekey(ctx, sessionId, database, connectionString);
        }

        private static ChangeDatabaseResponse Refuse(string message) => new() { Ok = false, ErrorMessage = message };
    }
}
