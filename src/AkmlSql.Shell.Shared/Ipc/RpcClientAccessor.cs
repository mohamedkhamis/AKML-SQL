#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Shell.Shared.Ipc
{
    /// <summary>
    /// Spec 033 (T002) — injectable seam over the engine RPC client. View-models take this
    /// instead of reading the static <see cref="EngineLifecycle.Manager"/> chain directly, so
    /// their IPC flows are testable with a fake accessor (no pipe, no engine process).
    /// </summary>
    internal interface IRpcClientAccessor
    {
        /// <summary>True when the engine pipe is up and requests can be sent.</summary>
        bool IsConnected { get; }

        /// <summary>Sends a request and awaits its typed response. Mirrors
        /// <see cref="PipeRpcClient.SendRequestAsync{T,TPayload}"/> exactly.</summary>
        Task<T> SendRequestAsync<T, TPayload>(int messageType, TPayload payload, int timeoutMs = 5000, CancellationToken ct = default);
    }

    /// <summary>
    /// Production accessor: delegates to <c>EngineLifecycle.Manager.Client</c> at call time
    /// (the manager is null until the engine is launched, and the client can reconnect —
    /// resolving late keeps the pre-seam semantics byte-identical).
    /// </summary>
    internal sealed class EngineRpcClientAccessor : IRpcClientAccessor
    {
        public static readonly EngineRpcClientAccessor Instance = new EngineRpcClientAccessor();

        private EngineRpcClientAccessor() { }

        public bool IsConnected => EngineLifecycle.Manager?.Client?.IsConnected == true;

        public Task<T> SendRequestAsync<T, TPayload>(int messageType, TPayload payload, int timeoutMs = 5000, CancellationToken ct = default)
        {
            var client = EngineLifecycle.Manager?.Client;
            if (client == null)
                throw new InvalidOperationException("Engine not connected.");
            return client.SendRequestAsync<T, TPayload>(messageType, payload, timeoutMs, ct);
        }
    }
}
