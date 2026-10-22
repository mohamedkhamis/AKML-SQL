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

            // Spec 042: a PostgreSQL string is tested with the PostgreSQL driver and worded its own way; not at all with the feature off (FR-093).
            if (DatabaseKindNames.FromInt(request.Kind) == DatabaseKind.PostgreSql)
            {
                if (ctx != null && ctx.PostgresSchemaMetadata == null)          // the feature was off when the engine started
                    return new TestSqlConnectionResponse { Ok = false, ErrorMessage = DialectErrors.FeatureOffMessage };
                // Spec 043 follow-up: a new tab's connect is tested with the password of the tab it was opened from (that tab's
                // session, this bridge connection, the same server, user and TLS options); the guard in TestPostgresAsync still runs.
                request.ConnectionString = ConnectionChangedHandler.CopyPassword(request.ConnectionString, request.Kind, request.CopyPasswordFromSessionId, ctx);
                return await TestPostgresAsync(request, ct).ConfigureAwait(false);
            }

            // A SQL Server client alias becomes the server it points at (the guard judges the real target).
            request.ConnectionString = SqlAliasResolution.Apply(request.ConnectionString);

            if (BridgeSqlTargetGuard.Check(request.ConnectionString) is { } refused)
            {
                Log.Warning("TestSqlConnection refused for a bridge request — {ConnDesc}",
                    ConnectionDiagnostics.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = refused };
            }

            // Spec 043 follow-up: a web SQL login with no password typed takes it from the tab it was opened from, or from the
            // engine's remembered logins; with neither the engine says so itself and never sends an empty password to the server.
            var typedPassword = HasPassword(request.ConnectionString);
            request.ConnectionString = ConnectionChangedHandler.CopyPassword(request.ConnectionString, request.Kind, request.CopyPasswordFromSessionId, ctx);
            request.ConnectionString = SqlServerCredentialResolver.Resolve(request.ConnectionString);
            if (SessionPasswordCopy.IsSqlLoginWithoutPassword(request.ConnectionString))
                return new TestSqlConnectionResponse
                {
                    Ok = false, ErrorCode = DialectErrors.PasswordRequired,
                    ErrorMessage = "The server needs the password for this login.",
                };

            try
            {
                await using var conn = new SqlConnection(request.ConnectionString);
                await conn.OpenAsync(ct);
                Log.Information("TestSqlConnection ok — {ConnDesc}",
                    ConnectionDiagnostics.Describe(request.ConnectionString));
                // The connect's own test is where a typed password is known to be accepted: remember it now if asked.
                if (typedPassword) SqlServerCredentialResolver.RememberIfRequested(request.ConnectionString, request.RememberPassword);
                return new TestSqlConnectionResponse { Ok = true, ServerVersion = "SQL Server " + conn.ServerVersion };
            }
            catch (SqlException sqlEx)
            {
                Log.Warning("TestSqlConnection failed (err={Num} state={State}) — {ConnDesc}",
                    sqlEx.Number, sqlEx.State, ConnectionDiagnostics.Describe(request.ConnectionString));
                // Spec 042 (FR-008): a network failure against PostgreSQL's usual port says to check the kind.
                var wrongKind = PgConnectionFailure.WrongKindForSqlServer(sqlEx.Number, DataSourceOf(request.ConnectionString));
                return new TestSqlConnectionResponse
                {
                    Ok = false, ErrorMessage = wrongKind ?? sqlEx.Message,
                    ErrorCode = sqlEx.Number == 18456 ? DialectErrors.LoginFailed : null,   // the server refused the login
                };
            }
            catch (Exception ex)
            {
                Log.Warning("TestSqlConnection error — {ConnDesc}: {Msg}",
                    ConnectionDiagnostics.Describe(request.ConnectionString), ex.Message);
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Spec 042 (T055, FR-002, FR-007): open a PostgreSQL connection, ask the server who it is, close it. A remembered
        /// password is used when the string has none. A bridge request the engine would refuse to open is refused here too,
        /// with the same id as ConnectionChanged. Failures are the server's words plus a hint, never the connection string.
        /// </summary>
        private static async Task<TestSqlConnectionResponse> TestPostgresAsync(TestSqlConnectionRequest request, CancellationToken ct)
        {
            var refusal = PgTargetGuard.Check(request.ConnectionString);
            if (refusal is not null)
            {
                Log.Warning("TestSqlConnection (PostgreSQL) refused for a bridge request — {ConnDesc}",
                    PgConnectionString.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = refusal.Message, ErrorCode = refusal.Code };
            }

            string host = string.Empty;
            int port = 0;
            var attempted = request.ConnectionString;
            try
            {
                var resolved = new PgCredentialResolver().Resolve(request.ConnectionString);
                attempted = resolved.ConnectionString;
                var builder = new NpgsqlConnectionStringBuilder(resolved.ConnectionString);
                host = builder.Host ?? string.Empty;
                port = builder.Port;

                var facts = await PgConnectionFactory.ReadFactsAsync(resolved.ConnectionString, ct).ConfigureAwait(false);
                Log.Information("TestSqlConnection (PostgreSQL) ok — {ConnDesc}", PgConnectionString.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = true, ServerVersion = "PostgreSQL " + facts.ServerVersionText };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = await PgConnectionFailure.DescribeAsync(ex, attempted, host, port, ct).ConfigureAwait(false);
                Log.Warning("TestSqlConnection (PostgreSQL) failed ({Type}) — {ConnDesc}",
                    ex.GetType().Name, PgConnectionString.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = failure.Message, ErrorCode = failure.ErrorCode };
            }
        }

        private static bool HasPassword(string connectionString)
        {
            try { return !string.IsNullOrEmpty(new SqlConnectionStringBuilder(connectionString).Password); }
            catch (ArgumentException) { return false; }
        }

        private static string? DataSourceOf(string connectionString)
        {
            try { return new SqlConnectionStringBuilder(connectionString).DataSource; }
            catch (ArgumentException) { return null; }
        }
    }
}
