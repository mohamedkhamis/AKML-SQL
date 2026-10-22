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
/// </summary>
public sealed class CredentialHandler : IRpcRequestHandler<CredentialRequest, CredentialResponse>
{
    private readonly PgCredentialResolver _resolver;

    public CredentialHandler(PgCredentialResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public int RequestMessageType => MessageTypes.CredentialRequest;
    public int ResponseMessageType => MessageTypes.CredentialResponse;

    public Task<CredentialResponse> HandleAsync(CredentialRequest request, RpcContext ctx, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Server) || string.IsNullOrWhiteSpace(request.Login))
            return Task.FromResult(new CredentialResponse { Ok = true, Has = false });

        try
        {
            bool has;
            if (DatabaseKindNames.FromInt(request.Kind) == DatabaseKind.PostgreSql)
            {
                has = request.Action == CredentialAction.Forget
                    ? _resolver.Forget(request.Server, request.Port, request.Login)
                    : _resolver.Has(request.Server, request.Port, request.Login);
            }
            else
            {
                // SQL Server (follow-up of spec 043): the web edition's remembered logins, keyed by the data source the engine
                // connects to, so an alias is resolved as ConnectionChanged resolves it.
                var dataSource = SqlServerDataSource(request.Server);
                has = request.Action == CredentialAction.Forget
                    ? SqlServerCredentialResolver.Forget(dataSource, request.Login)
                    : SqlServerCredentialResolver.Has(dataSource, request.Login);
            }
            return Task.FromResult(new CredentialResponse { Ok = true, Has = has });
        }
        catch (Exception ex)
        {
            // A store that cannot be read or written is reported, not thrown: the browser would wait for a reply forever.
            Serilog.Log.Warning(ex, "Credential request failed");
            return Task.FromResult(new CredentialResponse { Ok = false, Has = false, Error = "The credential store could not be read." });
        }
    }

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
