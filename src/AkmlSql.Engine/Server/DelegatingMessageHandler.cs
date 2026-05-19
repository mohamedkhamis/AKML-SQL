using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Engine.Server
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.3 / M0.4. General-purpose <see cref="IMessageHandler"/>
    /// adapter for cases where the legacy switch already delegated to a handler-class method
    /// with the shape <c>Task&lt;RpcMessage?&gt; Handle(RpcMessage, CancellationToken)</c>.
    /// Captures one such method as a delegate so PipeRpcServer can register it into
    /// <c>_pluggableHandlers</c> without writing a dedicated wrapper class per message type.
    ///
    /// Used by:
    ///   * AI handlers (T019) -- 8 message types via <c>_aiHandler.Handle*Async</c>.
    ///   * Session / History / Safety / Navigation / Productivity / CRUD / ScriptAs / Grid
    ///     export (T020 wave 1) -- 14 message types via the corresponding handler classes.
    ///
    /// Handlers that need typed (TRequest, TResponse) deserialisation and the new
    /// <see cref="AkmlSql.Engine.Transports.IRpcRequestHandler{TRequest,TResponse}"/> contract
    /// keep going through <see cref="TypedHandlerAdapter{TRequest,TResponse}"/> instead.
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
