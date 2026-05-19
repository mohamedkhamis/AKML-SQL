using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Server;

namespace AkmlSql.Engine.Handlers.Ai
{
    // Spec 021 (web edition) -- M0.5 task T019. AI message handlers.
    //
    // The AI dispatch path differs from the rest of the engine in three ways:
    //   1. Each handler needs a "look up session" callback to resolve a session ID to a
    //      (ConnectionString, DatabaseName) tuple for prompt context.
    //   2. The existing AiRequestHandler / AiProviderTestHandler methods already deserialise
    //      the payload themselves -- they take RpcMessage directly so they can handle
    //      streaming and varied response shapes.
    //   3. The "AiHandlerBase consolidation" the M0 PRD calls for has already been done --
    //      the shared boilerplate (prompt construction, provider routing, streaming) lives
    //      in AiRequestHandler. The remaining work is moving the switch-case dispatch into
    //      thin per-message-type handlers.
    //
    // Each handler below is therefore a one-line bridge between PipeRpcServer's
    // _pluggableHandlers dictionary and the existing AiRequestHandler methods. This drops
    // 8 cases out of the switch while preserving exact behaviour.

    internal delegate Task<RpcMessage?> AiHandlerInvocation(RpcMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Generic AI handler bridge -- captures one of AiRequestHandler's HandleXxxAsync
    /// methods as a delegate. Used to register a thin IMessageHandler in
    /// PipeRpcServer._pluggableHandlers without writing 8 separate classes.
    /// </summary>
    internal sealed class AiMessageHandler : IMessageHandler
    {
        private readonly AiHandlerInvocation _invoke;

        public AiMessageHandler(AiHandlerInvocation invoke)
        {
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
            => _invoke(message, cancellationToken);
    }
}
