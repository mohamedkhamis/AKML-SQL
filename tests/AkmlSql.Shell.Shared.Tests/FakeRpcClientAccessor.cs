#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Shell.Shared.Ipc;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 (T003) — canned-response IPC double for view-model tests. No pipe, no engine,
    /// no WPF/ThemeRegistry dependency, so it is usable from plain [Fact]s.
    /// </summary>
    internal sealed class FakeRpcClientAccessor : IRpcClientAccessor
    {
        public bool IsConnected { get; set; } = true;

        /// <summary>Every request sent, in order (message type + payload), for assertions.</summary>
        public List<(int MessageType, object? Payload)> Requests { get; } = new List<(int, object?)>();

        private readonly Dictionary<int, Func<object?, object?>> _handlers =
            new Dictionary<int, Func<object?, object?>>();

        /// <summary>Registers a fixed response object for a message type.</summary>
        public void Respond(int messageType, object? response) =>
            _handlers[messageType] = _ => response;

        /// <summary>Registers a payload-inspecting responder for a message type.</summary>
        public void Respond<TPayload>(int messageType, Func<TPayload, object?> handler) =>
            _handlers[messageType] = p => handler((TPayload)p!);

        /// <summary>Registers an exception to throw for a message type (timeout simulation etc.).</summary>
        public void Throw(int messageType, Exception ex) =>
            _handlers[messageType] = _ => ex;

        public Task<T> SendRequestAsync<T, TPayload>(int messageType, TPayload payload, int timeoutMs = 5000, CancellationToken ct = default)
        {
            Requests.Add((messageType, payload));

            if (!_handlers.TryGetValue(messageType, out var handler))
                throw new InvalidOperationException(
                    $"FakeRpcClientAccessor: no canned response registered for message type {messageType}.");

            var result = handler(payload);
            if (result is Exception ex) throw ex;
            return Task.FromResult((T)result!);
        }
    }
}
