using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;
using Npgsql;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    /// <summary>
    /// Spec 030. Enumerates the user-accessible databases on a server for the Connect-to-SQL-Server
    /// dialog's database dropdown. Opens a short-timeout connection (typically to <c>master</c>) and
    /// returns the online, accessible catalogs. Surfaces auth/connectivity errors precisely (unlike
    /// <see cref="SchemaMetadataService.ListDatabasesAsync"/>, which swallows them for the completion
    /// path) so the dialog can tell "no databases" from "wrong password". Never logs the raw
    /// connection string — uses ConnectionDiagnostics.Describe.
    /// </summary>
    public sealed class ListDatabasesHandler
        : IRpcRequestHandler<ListDatabasesRequest, ListDatabasesResponse>
    {
        public int RequestMessageType => MessageTypes.ListDatabases;
        public int ResponseMessageType => MessageTypes.ListDatabasesResult;

        public async Task<ListDatabasesResponse> HandleAsync(
            ListDatabasesRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Spec 042 (T081): the kind is the session's when there is one, otherwise the request's.
            var kind = !string.IsNullOrEmpty(request.SessionId) && ctx?.Sessions.GetSession(request.SessionId!) is { } known
                ? known.Kind
                : DatabaseKindNames.FromInt(request.Kind);
            if (kind == DatabaseKind.PostgreSql)
            {
                if (ctx != null && ctx.PostgresSchemaMetadata == null)                   // FR-093: the feature was off when the engine started
                    return new ListDatabasesResponse { Ok = false, ErrorMessage = DialectErrors.FeatureOffMessage };
                return await ListPostgresAsync(request, ctx, ct).ConfigureAwait(false);
            }

            // Spec 041 (R25): the database selector lists with the connection the engine already holds, so the
            // browser never resends a password. The alias and the guard below judge it like any other request.
            if (string.IsNullOrEmpty(request.ConnectionString) && !string.IsNullOrEmpty(request.SessionId))
            {
                var session = ctx?.Sessions.GetSession(request.SessionId!);
                if (session == null || !session.IsConnected || string.IsNullOrEmpty(session.ConnectionString))
                    return new ListDatabasesResponse { Ok = false, ErrorMessage = "The session is not connected to SQL Server." };
                request.ConnectionString = session.ConnectionString;
            }

            if (string.IsNullOrEmpty(request.ConnectionString))
                return new ListDatabasesResponse { Ok = false, ErrorMessage = "No connection string supplied." };

            var connDesc = ConnectionDiagnostics.Describe(request.ConnectionString);
            // A SQL Server client alias becomes the server it points at (the guard judges the real target).
            request.ConnectionString = SqlAliasResolution.Apply(request.ConnectionString);

            if (BridgeSqlTargetGuard.Check(request.ConnectionString) is { } refused)
            {
                Log.Warning("ListDatabases refused for a bridge request — {ConnDesc}", connDesc);
                return new ListDatabasesResponse { Ok = false, ErrorMessage = refused };
            }

            try
            {
                await using var conn = new SqlConnection(request.ConnectionString);
                await conn.OpenAsync(ct);

                // Shared projection with USE-completion — single home for the query in
                // SchemaMetadataService: online + accessible only, user DBs before the system four.
                var names = await SchemaMetadataService.QueryDatabaseNamesAsync(conn, ct);

                Log.Information("ListDatabases ok — {Count} databases ({ConnDesc})", names.Count, connDesc);
                return new ListDatabasesResponse { Ok = true, Databases = names };
            }
            catch (SqlException sqlEx)
            {
                Log.Warning("ListDatabases failed (err={Num} state={State}) — {ConnDesc}",
                    sqlEx.Number, sqlEx.State, connDesc);
                return new ListDatabasesResponse { Ok = false, ErrorMessage = sqlEx.Message };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning("ListDatabases error — {ConnDesc}: {Msg}", connDesc, ex.Message);
                return new ListDatabasesResponse { Ok = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Spec 042 (FR-030): the databases of a PostgreSQL server the role may connect to, templates left out. With a session the
        /// engine's own connection string is used (the browser never resends a password); without one the request's string is,
        /// after the same guard as every bridge request, with a remembered password filled in. A failure is the server's words
        /// and a hint, never the connection string.
        /// </summary>
        private static async Task<ListDatabasesResponse> ListPostgresAsync(ListDatabasesRequest request, RpcContext? ctx, CancellationToken ct)
        {
            var connectionString = request.ConnectionString;
            if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(request.SessionId))
            {
                var session = ctx?.Sessions.GetSession(request.SessionId!);
                if (session == null || !session.IsConnected || string.IsNullOrEmpty(session.ConnectionString))
                    return new ListDatabasesResponse { Ok = false, ErrorMessage = "The session is not connected to a PostgreSQL server." };
                connectionString = session.ConnectionString;
            }
            if (string.IsNullOrEmpty(connectionString))
                return new ListDatabasesResponse { Ok = false, ErrorMessage = "No connection string supplied." };

            if (PgTargetGuard.Check(connectionString) is { } refusal)
            {
                Log.Warning("ListDatabases (PostgreSQL) refused for a bridge request - {ConnDesc}", PgConnectionString.Describe(connectionString));
                return new ListDatabasesResponse { Ok = false, ErrorMessage = refusal.Message, ErrorCode = refusal.Code };
            }

            string host = string.Empty;
            int port = 0;
            var attempted = connectionString;
            try
            {
                var resolved = new PgCredentialResolver().Resolve(connectionString).ConnectionString;
                attempted = resolved;
                var builder = new NpgsqlConnectionStringBuilder(resolved);
                host = builder.Host ?? string.Empty;
                port = builder.Port;

                var names = await new PgSchemaMetadataService().ListDatabasesStrictAsync(resolved, ct).ConfigureAwait(false);
                Log.Information("ListDatabases (PostgreSQL) ok - {Count} databases ({ConnDesc})", names.Count, PgConnectionString.Describe(connectionString));
                return new ListDatabasesResponse { Ok = true, Databases = names };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = await PgConnectionFailure.DescribeAsync(ex, attempted, host, port, ct).ConfigureAwait(false);
                Log.Warning("ListDatabases (PostgreSQL) failed ({Type}) - {ConnDesc}", ex.GetType().Name, PgConnectionString.Describe(connectionString));
                return new ListDatabasesResponse { Ok = false, ErrorMessage = failure.Message, ErrorCode = failure.ErrorCode };
            }
        }
    }
}
