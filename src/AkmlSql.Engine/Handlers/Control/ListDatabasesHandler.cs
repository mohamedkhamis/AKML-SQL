using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;
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
            if (string.IsNullOrEmpty(request.ConnectionString))
                return new ListDatabasesResponse { Ok = false, ErrorMessage = "No connection string supplied." };

            var connDesc = ConnectionDiagnostics.Describe(request.ConnectionString);
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
    }
}
