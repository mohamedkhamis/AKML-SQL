using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using MessagePack;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) — M0 transport abstraction.
    /// Per-process router that owns the message-type → handler map. Transports forward inbound
    /// <see cref="RpcMessage"/> frames here via <see cref="RouteAsync"/>; the router
    /// deserialises the payload, invokes the handler, and re-serialises the response.
    ///
    /// Registration is explicit via <see cref="Register{TReq,TResp}"/>. A future task (T021)
    /// adds reflection-based scanning that mirrors the analyser's <c>RuleRegistry</c> pattern;
    /// for now M0.1 leaves registration manual so handlers are migrated incrementally without
    /// hidden side effects.
    ///
    /// Threading: handler entries are stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// so concurrent transports may dispatch in parallel. Registration is expected to happen
    /// once during engine startup (single-threaded), but the dictionary tolerates concurrent
    /// writes too.
    /// </summary>
    public sealed class RpcRouter
    {
        private readonly ConcurrentDictionary<int, IHandlerAdapter> _adapters = new();

        public void Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var adapter = new TypedHandlerAdapter<TReq, TResp>(handler);
            if (!_adapters.TryAdd(handler.RequestMessageType, adapter))
            {
                throw new InvalidOperationException(
                    $"RpcRouter: a handler for MessageType {handler.RequestMessageType} is already registered.");
            }
        }

        public bool IsRegistered(int requestMessageType) => _adapters.ContainsKey(requestMessageType);

        /// <summary>The set of message-type codes currently registered with this router.</summary>
        public IReadOnlyCollection<int> RegisteredMessageTypes => (IReadOnlyCollection<int>)_adapters.Keys;

        /// <summary>
        /// Spec 021 (web edition) -- M0 task T021. Reflectively discover and register every
        /// concrete <see cref="IRpcRequestHandler{TReq,TResp}"/> in <paramref name="assembly"/>
        /// (or the calling assembly when null) that has a public parameterless constructor.
        /// Mirrors the <c>RuleRegistry</c> pattern used by the analyser.
        ///
        /// <para>
        /// Handlers with constructor dependencies (e.g. <c>CompletionHandler</c> which needs a
        /// <c>CompletionEngine</c>) are silently skipped -- they must be registered via the
        /// explicit <see cref="Register{TReq,TResp}"/> path. <c>RegisterAllInAssembly</c> is
        /// therefore additive: callers typically call it first for the dependency-free
        /// handlers, then explicitly register the rest.
        /// </para>
        ///
        /// <para>
        /// Already-registered message-types are NOT overwritten: a subsequent explicit
        /// <see cref="Register{TReq,TResp}"/> call for the same code will throw, but an
        /// earlier explicit registration wins over this method silently.
        /// </para>
        /// </summary>
        /// <returns>The number of handlers actually registered.</returns>
        public int RegisterAllInAssembly(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            var handlerOpenGeneric = typeof(IRpcRequestHandler<,>);
            var registerMethod = typeof(RpcRouter)
                .GetMethod(nameof(Register), BindingFlags.Public | BindingFlags.Instance)!;

            int registered = 0;
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                // Find the closed IRpcRequestHandler<TReq,TResp> the type implements (if any).
                var handlerInterface = type.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == handlerOpenGeneric);
                if (handlerInterface == null) continue;

                object instance;
                try { instance = Activator.CreateInstance(type)!; }
                catch { continue; }

                // Read RequestMessageType up front to decide whether to clobber.
                var requestType = (int)handlerInterface.GetProperty(nameof(IRpcRequestHandler<int, int>.RequestMessageType))!
                    .GetValue(instance)!;
                if (_adapters.ContainsKey(requestType)) continue;

                var typeArgs = handlerInterface.GetGenericArguments();
                registerMethod.MakeGenericMethod(typeArgs).Invoke(this, new[] { instance });
                registered++;
            }
            return registered;
        }

        /// <summary>
        /// Resolve the message-type, deserialise the payload, dispatch to the handler, and
        /// return the response frame (or <see langword="null"/> if the request has no handler
        /// or the handler is notification-only).
        /// </summary>
        public async Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            if (!_adapters.TryGetValue(msg.MessageType, out var adapter))
            {
                return null;
            }
            return await adapter.RouteAsync(msg, ctx, ct).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // Internal adapters — keep dictionary value type non-generic so all
        // registered handlers can sit in one map.
        // ------------------------------------------------------------------

        private interface IHandlerAdapter
        {
            Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct);
        }

        private sealed class TypedHandlerAdapter<TReq, TResp> : IHandlerAdapter
        {
            private readonly IRpcRequestHandler<TReq, TResp> _handler;
            public TypedHandlerAdapter(IRpcRequestHandler<TReq, TResp> handler) => _handler = handler;

            public async Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct)
            {
                var payload = msg.Payload ?? Array.Empty<byte>();
                var request = payload.Length == 0
                    ? default!
                    : MessagePackSerializer.Deserialize<TReq>(payload, cancellationToken: ct);

                var response = await _handler.HandleAsync(request, ctx, ct).ConfigureAwait(false);

                if (_handler.ResponseMessageType == 0)
                {
                    // Notification-style — handler ran but no response frame is written.
                    return null;
                }

                var respPayload = MessagePackSerializer.Serialize(response, cancellationToken: ct);
                return new RpcMessage
                {
                    MessageType = _handler.ResponseMessageType,
                    RequestId = msg.RequestId,
                    Payload = respPayload,
                };
            }
        }
    }
}
