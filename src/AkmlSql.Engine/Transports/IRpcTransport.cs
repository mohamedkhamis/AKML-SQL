using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Transports
{
    /// <summary>
    /// Spec 021 (web edition) — M0 transport abstraction.
    /// One implementation per medium: <c>NamedPipeTransport</c> (IDE plugins, today),
    /// <c>InProcessTransport</c> (Blazor WASM running engine logic in the browser tab; engine
    /// unit tests with zero serialisation), <c>WebSocketTransport</c> (M3 browser ↔ engine).
    ///
    /// Implementers own frame I/O and connection lifecycle. Dispatch and handler invocation
    /// belong to <see cref="RpcRouter"/> — transports forward inbound frames via the
    /// <see cref="RequestReceived"/> event and write the returned <see cref="RpcMessage"/>
    /// back over the same channel.
    /// </summary>
    public interface IRpcTransport : IAsyncDisposable
    {
        /// <summary>
        /// Begin accepting connections / frames. Returns once the transport is listening.
        /// The returned <see cref="Task"/> completes when the listener is up; long-running
        /// accept loops are started in the background.
        /// </summary>
        Task StartAsync(CancellationToken ct);

        /// <summary>
        /// Raised when a complete <see cref="RpcMessage"/> frame has been received and
        /// deserialised. The transport awaits the returned <see cref="Task{RpcMessage}"/>
        /// and writes the response (or omits writing when the handler returns
        /// <see langword="null"/> for fire-and-forget notifications) back over the same
        /// transport.
        /// </summary>
        event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived;
    }
}
