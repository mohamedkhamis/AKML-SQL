using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Engine.Transports
{
    /// <summary>
    /// Spec 021 (web edition) — M0 transport abstraction.
    /// One implementation per logical message-type integer code. Handlers must NOT depend on
    /// the transport that delivered the request — they receive an already-deserialised
    /// <typeparamref name="TRequest"/> and the shared per-process <see cref="RpcContext"/>.
    ///
    /// Notes:
    /// * The contract defines <see cref="RequestMessageType"/> and <see cref="ResponseMessageType"/>
    ///   separately because the existing <c>MessageTypes</c> table does not use a uniform offset
    ///   between request and response codes (some pairs are +100, others +98, and some requests
    ///   never produce a typed response).
    /// * Notification-style handlers (no response expected) should set
    ///   <see cref="ResponseMessageType"/> to <c>0</c>; <see cref="RpcRouter"/> treats that as
    ///   "do not write any response frame".
    /// </summary>
    /// <typeparam name="TRequest">MessagePack-serialisable request POCO.</typeparam>
    /// <typeparam name="TResponse">MessagePack-serialisable response POCO. May be a placeholder
    /// type with no fields for notification-style messages.</typeparam>
    public interface IRpcRequestHandler<TRequest, TResponse>
    {
        int RequestMessageType { get; }

        /// <summary>
        /// Integer message-type code emitted on the response frame, or <c>0</c> for
        /// notification-style handlers that do not produce a reply.
        /// </summary>
        int ResponseMessageType { get; }

        /// <summary>
        /// When <c>false</c> (default), an inbound <see cref="RpcMessage"/> with a null or
        /// empty <c>Payload</c> is rejected by the dispatch path with an Error response —
        /// matches the prior behaviour of the inline switch cases. Override to <c>true</c>
        /// for messages that legitimately carry no payload (e.g. <c>ProfileList</c>). The
        /// adapter then passes <c>default(TRequest)</c> to <see cref="HandleAsync"/> instead.
        /// Default-interface-method default-implementation so existing handlers don't have
        /// to opt in.
        /// </summary>
        bool AllowsEmptyPayload => false;

        /// <summary>
        /// When <c>true</c>, the adapter catches <see cref="OperationCanceledException"/>
        /// thrown from <see cref="HandleAsync"/> and returns <see langword="null"/> instead
        /// of propagating. Use for handlers where cancellation is a normal event (e.g.
        /// analysis or refactoring, where typing supersedes an in-flight request) and you
        /// do NOT want the cancellation to tear down the pipe accept loop.
        ///
        /// When <c>false</c> (default), <see cref="OperationCanceledException"/> propagates
        /// up to <c>PipeRpcServer.DispatchAsync</c>'s outer catch, matching the prior
        /// behaviour of inline cases that did NOT explicitly catch cancellation.
        /// </summary>
        bool SwallowCancellation => false;

        Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct);
    }
}
