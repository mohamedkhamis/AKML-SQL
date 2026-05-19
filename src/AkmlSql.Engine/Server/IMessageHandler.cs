using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// Phase 10 (spec 019) / US14 FR-080 — hybrid dispatch contract: every
    /// MessageType registered via <see cref="NamedPipeTransport"/>'s pluggable
    /// dictionary implements this interface and returns a fully-formed response
    /// envelope (or <c>null</c> for notifications).
    /// <para>
    /// The hybrid pattern: <see cref="NamedPipeTransport.DispatchAsync"/> consults
    /// the pluggable dictionary first; on miss, it falls through to the
    /// existing switch dispatch. New MessageType integers introduced by future
    /// specs register a handler via the dictionary and require <b>zero</b>
    /// changes to <c>NamedPipeTransport.cs</c>, satisfying FR-080's invariant
    /// without a high-risk rewrite of the existing 53-case switch.
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
