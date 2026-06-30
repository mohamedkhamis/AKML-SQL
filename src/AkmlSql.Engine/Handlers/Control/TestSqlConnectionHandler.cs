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
    /// Spec 029. Validates a candidate SQL-auth connection string by opening a short-timeout
    /// connection. The shell calls this before persisting a password, so a bad password is never
    /// stored. Never logs the raw connection string — uses ConnectionDiagnostics.Describe.
    /// </summary>
    public sealed class TestSqlConnectionHandler
        : IRpcRequestHandler<TestSqlConnectionRequest, TestSqlConnectionResponse>
    {
        public int RequestMessageType => MessageTypes.TestSqlConnection;
        public int ResponseMessageType => MessageTypes.TestSqlConnectionResult;

        public async Task<TestSqlConnectionResponse> HandleAsync(
            TestSqlConnectionRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.ConnectionString))
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = "No connection string supplied." };

            try
            {
                await using var conn = new SqlConnection(request.ConnectionString);
                await conn.OpenAsync(ct);
                Log.Information("TestSqlConnection ok — {ConnDesc}",
                    ConnectionDiagnostics.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = true };
            }
            catch (SqlException sqlEx)
            {
                Log.Warning("TestSqlConnection failed (err={Num} state={State}) — {ConnDesc}",
                    sqlEx.Number, sqlEx.State, ConnectionDiagnostics.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = sqlEx.Message };
            }
            catch (Exception ex)
            {
                Log.Warning("TestSqlConnection error — {ConnDesc}: {Msg}",
                    ConnectionDiagnostics.Describe(request.ConnectionString), ex.Message);
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = ex.Message };
            }
        }
    }
}
