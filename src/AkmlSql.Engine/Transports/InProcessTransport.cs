using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Transports
{
    /// <summary>
    /// Spec 021 (web edition) — M0 transport abstraction.
    /// In-process transport: a method-call dispatch path with zero serialisation. Two consumers:
    /// 1. Blazor WASM (spec 021 M2 onwards) — the browser tab runs engine logic directly.
    /// 2. Engine unit tests — exercise handlers without spinning up named-pipe accept loops.
    ///
    /// Lifecycle: no listener, no accept loop. <see cref="StartAsync"/> is a no-op. Callers
    /// invoke <see cref="SendAsync"/> directly and receive the response from the handler that
    /// <see cref="RequestReceived"/> subscribers (specifically <see cref="RpcRouter.RouteAsync"/>)
    /// return.
    ///
    /// Cancellation: callers pass their own <see cref="CancellationToken"/> per request. The
    /// transport-level token from <see cref="StartAsync"/> is honoured only for the implicit
    /// "transport is started" sentinel.
    /// </summary>
    public sealed class InProcessTransport : IRpcTransport
    {
        private volatile bool _started;

        public Task StartAsync(CancellationToken ct)
        {
            _started = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived;

        /// <summary>
        /// Send one request through the transport and return the (possibly <see langword="null"/>)
        /// response. Thread-safe: multiple in-flight requests are permitted; the handler chain is
        /// responsible for its own thread safety.
        /// </summary>
        public async Task<RpcMessage?> SendAsync(RpcMessage msg, CancellationToken ct)
        {
            if (!_started)
            {
                throw new InvalidOperationException(
                    "InProcessTransport.SendAsync called before StartAsync.");
            }
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            var handler = RequestReceived;
            if (handler == null)
            {
                throw new InvalidOperationException(
                    "InProcessTransport has no RequestReceived subscriber — register an RpcRouter first.");
            }

            return await handler(msg, ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _started = false;
            return default;
        }
    }
}
