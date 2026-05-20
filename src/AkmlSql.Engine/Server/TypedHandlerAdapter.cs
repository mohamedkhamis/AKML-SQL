using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using MessagePack;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// Adapts an <see cref="IRpcRequestHandler{TRequest,TResponse}"/> into the
    /// <see cref="IMessageHandler"/> raw-envelope shape: deserialises the request payload,
    /// invokes the handler, and serialises the typed response.
    /// <para>
    /// Spec 021 (web edition) M0.2 introduced this as the bridge that let handlers migrate to the
    /// generic abstraction one at a time. After the spec 022 M0 closure, production dispatch runs
    /// through <c>RpcRouter</c> (which carries its own equivalent typed adapter); this class is
    /// retained for the engine handler tests, which exercise each handler through the
    /// <see cref="IMessageHandler"/> shape.
    /// </para>
    /// <para>
    /// Lifecycle: the adapter captures an <see cref="RpcContext"/> by reference at construction
    /// time and dereferences it per call, so a handler always observes the context's current
    /// settings (<see cref="RpcContext"/> is the sole owner of the cached <c>AppSettings</c>
    /// after the closure).
    /// </para>
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
