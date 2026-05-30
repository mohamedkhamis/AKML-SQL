using System;
using System.Net;
using System.Threading;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 026 (M4 closure) FR-013a. Carries a WebSocket connection's remote IP to the
    /// handshake <c>pinValidator</c> as a per-connection ambient.
    ///
    /// <para><see cref="RpcContext"/> is a per-process *shared singleton* (it is passed to every
    /// handler invocation), so it cannot hold per-connection state. The source IP — needed as
    /// <see cref="PairingService"/>'s per-source rate-limit bucket key — therefore flows through
    /// this static <see cref="AsyncLocal{T}"/>. <c>WebSocketTransport</c> sets it per connection
    /// (<see cref="Set"/> returns an <see cref="IDisposable"/> that restores the previous value on
    /// connection end, so values never leak across connections); the singleton
    /// <c>HandshakeHandler</c>'s <c>pinValidator</c> closure reads <see cref="Current"/>.</para>
    ///
    /// <para>The field is per-process; the value it carries is per-connection — <see cref="AsyncLocal{T}"/>
    /// isolates each connection because each accepted socket runs in its own logical async flow.</para>
    /// </summary>
    internal static class BridgeSourceIp
    {
        private static readonly AsyncLocal<IPAddress?> Ambient = new();

        /// <summary>The remote IP of the connection currently being served, or null.</summary>
        public static IPAddress? Current => Ambient.Value;

        /// <summary>
        /// Set the ambient source IP for the enclosing async flow. Disposing the returned scope
        /// restores the previous value (prevents leakage across connections / callsites).
        /// </summary>
        public static IDisposable Set(IPAddress? ip)
        {
            var previous = Ambient.Value;
            Ambient.Value = ip;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly IPAddress? _previous;
            private bool _disposed;

            public Scope(IPAddress? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Ambient.Value = _previous;
            }
        }
    }
}
