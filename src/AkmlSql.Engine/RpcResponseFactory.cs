using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.6 task T020 finishing step. Stateless factory for
    /// the two canonical <see cref="RpcMessage"/> envelopes used across the engine:
    /// the typed-response envelope and the standardised <c>Error</c> envelope. Previously
    /// lived as internal static helpers on <c>NamedPipeTransport</c>; moved to its own class so
    /// (a) the named-pipe transport file stays focused on lifecycle + dispatch, and
    /// (b) future non-named-pipe transports do not have to depend on NamedPipeTransport.
    /// </summary>
    public static class RpcResponseFactory
    {
        /// <summary>
        /// Build a standardised error response envelope. The payload is an
        /// <see cref="ErrorInfo"/> with <c>Code = -1</c> and the supplied message.
        /// </summary>
        public static RpcMessage CreateErrorResponse(string message, int requestId)
        {
            var errorInfo = new ErrorInfo { Code = -1, Message = message };
            return CreateResponse(MessageTypes.Error, requestId, errorInfo);
        }

        /// <summary>
        /// Build a typed response envelope: serialises the payload to MessagePack and wraps
        /// it in an <see cref="RpcMessage"/> with the supplied message type and request id.
        /// </summary>
        public static RpcMessage CreateResponse<T>(int messageType, int requestId, T payload)
        {
            return new RpcMessage
            {
                MessageType = messageType,
                RequestId = requestId,
                Payload = MessagePackSerializer.Serialize(payload),
            };
        }
    }
}
