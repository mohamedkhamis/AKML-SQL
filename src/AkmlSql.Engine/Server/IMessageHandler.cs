using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// Raw-envelope handler contract: an implementation handles one inbound
    /// <see cref="RpcMessage"/> and returns the fully-formed response envelope (or
    /// <c>null</c> for notifications). Introduced in Phase 10 (spec 019) / US14 FR-080.
    /// <para>
    /// After the spec 022 M0 closure, dispatch flows through <c>RpcRouter</c>: raw handlers are
    /// registered via <c>RpcRouter.RegisterRaw</c> and the engine's pre-M0 53-case switch is gone.
    /// <see cref="IMessageHandler"/> remains the shape used by the spec-014 stub handlers and by
    /// <see cref="TypedHandlerAdapter{TRequest,TResponse}"/>.
    /// </para>
    /// <para>
    /// Implementations may use the static helpers
    /// <see cref="AkmlSql.Engine.RpcResponseFactory.CreateResponse{T}(int, int, T)"/> and
    /// <see cref="AkmlSql.Engine.RpcResponseFactory.CreateErrorResponse(string, int)"/> to
    /// emit standardised response envelopes.
    /// </para>
    /// </summary>
    internal interface IMessageHandler
    {
        /// <summary>
        /// Handle one inbound <see cref="RpcMessage"/> and return the response
        /// to send back, or <c>null</c> for fire-and-forget notifications that
        /// produce no reply (matches <c>NamedPipeTransport.DispatchAsync</c>'s own
        /// <c>Task&lt;RpcMessage?&gt;</c> return contract).
        /// </summary>
        Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken);
    }
}
