using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using MessagePack;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// Spec 021 (web edition) — M0.2 bridge.
    /// Adapts an <see cref="IRpcRequestHandler{TRequest,TResponse}"/> (the new spec-021 generic
    /// abstraction) into the existing <see cref="IMessageHandler"/> shape consumed by
    /// <see cref="NamedPipeTransport._pluggableHandlers"/>. Lets us migrate one handler at a time from
    /// the switch into the new generic type without first replacing the named-pipe dispatch loop.
    ///
    /// Lifecycle: the adapter captures an <see cref="RpcContext"/> by reference at construction
    /// time. The context fields (especially <c>Settings</c>) are mutable and the host
    /// (<see cref="NamedPipeTransport"/>) refreshes them on <c>AnalysisSettingsChanged</c>; the
    /// adapter sees the latest values on every request because it dereferences the context per
    /// call.
    /// </summary>
    internal sealed class TypedHandlerAdapter<TRequest, TResponse> : IMessageHandler
    {
        private readonly IRpcRequestHandler<TRequest, TResponse> _handler;
        private readonly RpcContext _ctx;

        public TypedHandlerAdapter(IRpcRequestHandler<TRequest, TResponse> handler, RpcContext ctx)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public async Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
        {
            TRequest request;
            if (message.Payload == null || message.Payload.Length == 0)
            {
                if (!_handler.AllowsEmptyPayload)
                {
                    return RpcResponseFactory.CreateErrorResponse("Payload required", message.RequestId);
                }
                request = default!;
            }
            else
            {
                try
                {
                    request = MessagePackSerializer.Deserialize<TRequest>(message.Payload, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    return RpcResponseFactory.CreateErrorResponse(
                        $"Failed to deserialise {typeof(TRequest).Name}: {ex.Message}",
                        message.RequestId);
                }
            }

            TResponse response;
            try
            {
                response = await _handler.HandleAsync(request, _ctx, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_handler.SwallowCancellation)
            {
                return null;
            }

            if (_handler.ResponseMessageType == 0)
            {
                return null;   // fire-and-forget
            }

            return RpcResponseFactory.CreateResponse(_handler.ResponseMessageType, message.RequestId, response!);
        }
    }
}
