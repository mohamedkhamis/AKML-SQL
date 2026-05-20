using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// General-purpose <see cref="IMessageHandler"/> adapter that wraps a
    /// <c>Func&lt;RpcMessage, CancellationToken, Task&lt;RpcMessage?&gt;&gt;</c> delegate.
    /// Introduced in spec 021 (web edition) M0.3 / M0.4 so raw-envelope handlers could be
    /// registered without a dedicated wrapper class per message type.
    /// <para>
    /// After the spec 022 M0 closure, raw handlers register directly via <c>RpcRouter.RegisterRaw</c>
    /// (which accepts the same delegate shape), so this adapter is no longer wired into the
    /// dispatch path. It is retained as a standalone <see cref="IMessageHandler"/> implementation;
    /// <see cref="TypedHandlerAdapter{TRequest,TResponse}"/> remains the path for typed handlers.
    /// </para>
    /// </summary>
    internal sealed class DelegatingMessageHandler : IMessageHandler
    {
        private readonly Func<RpcMessage, CancellationToken, Task<RpcMessage?>> _invoke;

        public DelegatingMessageHandler(Func<RpcMessage, CancellationToken, Task<RpcMessage?>> invoke)
        {
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
            => _invoke(message, cancellationToken);
    }
}
