using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-105, FR-106, contracts/ipc.md 221 to 222): the web edition asks whether a PostgreSQL password is remembered
/// for a server and login, so it can skip the prompt, and asks the engine to forget it. The reply says yes or no and never
/// carries the password. Only PostgreSQL is served: a request for any other kind, or without a login, finds nothing and
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

        if (DatabaseKindNames.FromInt(request.Kind) != DatabaseKind.PostgreSql
            || string.IsNullOrWhiteSpace(request.Server) || string.IsNullOrWhiteSpace(request.Login))
        {
            return Task.FromResult(new CredentialResponse { Ok = true, Has = false });
        }

        try
        {
            var has = request.Action == CredentialAction.Forget
                ? _resolver.Forget(request.Server, request.Port, request.Login)
                : _resolver.Has(request.Server, request.Port, request.Login);
            return Task.FromResult(new CredentialResponse { Ok = true, Has = has });
        }
        catch (Exception ex)
        {
            // A store that cannot be read or written is reported, not thrown: the browser would wait for a reply forever.
            Serilog.Log.Warning(ex, "Credential request failed");
            return Task.FromResult(new CredentialResponse { Ok = false, Has = false, Error = "The credential store could not be read." });
        }
    }
}
