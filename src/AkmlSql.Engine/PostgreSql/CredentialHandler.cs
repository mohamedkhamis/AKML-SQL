using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-105, FR-106, contracts/ipc.md 221 to 222): the web edition asks whether a password is remembered for a server
/// and login, so it can skip the prompt, and asks the engine to forget it. The reply says yes or no and never carries the
/// password. PostgreSQL passwords (<see cref="PgCredentialResolver"/>) and, since the spec 043 follow-up, the web edition's
/// SQL Server logins (<see cref="SqlServerCredentialResolver"/>); a request without a server or login finds nothing and
/// touches nothing.
///
/// <para>
/// Spec 043 follow-up (review): registered whatever <c>postgreSql.enabled</c> says, because the SQL Server half is advertised
/// unconditionally (<c>credentials.sqlserver.v1</c>) and a request no handler answers leaves the browser waiting forever. With
/// the feature off a PostgreSQL request is answered, not served: <c>Ok = false</c> with the feature-off message. A forget whose
/// removal could not be written is answered <c>Ok = false</c>, "The credential store could not be written.", never as forgotten.
/// The released engines that advertise only <c>credentials.sqlserver.v1</c> registered this handler only with the feature on.
/// </para>
/// </summary>
public sealed class CredentialHandler : IRpcRequestHandler<CredentialRequest, CredentialResponse>
{
    private readonly PgCredentialResolver _resolver;
    private readonly bool _postgresEnabled;

    /// <param name="postgresEnabled"><c>postgreSql.enabled</c> when the engine started: whether PostgreSQL requests are served.</param>
    public CredentialHandler(PgCredentialResolver resolver, bool postgresEnabled = true)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _postgresEnabled = postgresEnabled;
    }

    public int RequestMessageType => MessageTypes.CredentialRequest;
    public int ResponseMessageType => MessageTypes.CredentialResponse;

    public Task<CredentialResponse> HandleAsync(CredentialRequest request, RpcContext ctx, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var postgres = DatabaseKindNames.FromInt(request.Kind) == DatabaseKind.PostgreSql;
        if (postgres && !_postgresEnabled)   // FR-093: nothing about PostgreSQL is read or written with the feature off
            return Task.FromResult(new CredentialResponse { Ok = false, Has = false, Error = DialectErrors.FeatureOffMessage });

        if (string.IsNullOrWhiteSpace(request.Server) || string.IsNullOrWhiteSpace(request.Login))
            return Task.FromResult(new CredentialResponse { Ok = true, Has = false });

        try
        {
            bool has;
            if (postgres)
            {
                has = request.Action == CredentialAction.Forget
                    ? _resolver.Forget(request.Server, request.Port, request.Login)
                    : _resolver.Has(request.Server, request.Port, request.Login);
            }
            else
            {
                // SQL Server (follow-up of spec 043): the web edition's remembered logins, keyed by the data source the engine
                // connects to, so an alias is resolved as ConnectionChanged resolves it. Review round 3: Has answers for the
                // connection's "Trust server certificate" choice (key 5) by the rule the password is filled by; Forget ignores
                // it and removes the login's password whatever it was saved with.
                var dataSource = SqlServerDataSource(request.Server);
                has = request.Action == CredentialAction.Forget
                    ? SqlServerCredentialResolver.Forget(dataSource, request.Login)
                    : SqlServerCredentialResolver.Has(dataSource, request.Login, request.TrustServerCertificate);
            }
            return Task.FromResult(new CredentialResponse { Ok = true, Has = has });
        }
        catch (CredentialStoreWriteException)
        {
            // Spec 043 follow-up (review): the removal could not be written, so the password is still remembered — never "forgotten".
            return Task.FromResult(new CredentialResponse { Ok = false, Has = false, Error = CredentialStoreWriteException.UserMessage });
        }
        catch (Exception ex)
        {
            // A store that cannot be read or written is reported, not thrown: the browser would wait for a reply forever.
            Serilog.Log.Warning(ex, "Credential request failed");
            return Task.FromResult(new CredentialResponse { Ok = false, Has = false, Error = StoreFailedMessage });
        }
    }

    /// <summary>The answer when the store failed in any other way (reading it, or an unexpected error while writing it).</summary>
    internal const string StoreFailedMessage = "The credential store could not be read or written.";

    /// <summary>The data source a SQL Server name connects to: a client alias becomes its target, as in ConnectionChanged.</summary>
    private static string SqlServerDataSource(string server)
    {
        try
        {
            var resolved = SqlAliasResolution.Apply(new SqlConnectionStringBuilder { DataSource = server.Trim() }.ConnectionString);
            return new SqlConnectionStringBuilder(resolved).DataSource;
        }
        catch (ArgumentException)
        {
            return server.Trim();
        }
    }
}
