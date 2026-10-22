using System;
using System.Threading;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (review): the WebSocket connection a bridge request arrived on, as an ambient id set by the transport
    /// for the life of the connection (as <see cref="BridgeSourceIp"/> is). Null for named-pipe requests. A session's password is
    /// copied only into a request from the same connection that connected that session (<see cref="SessionPasswordCopy"/>).
    /// </summary>
    internal static class BridgeConnection
    {
        private static readonly AsyncLocal<string?> Ambient = new();

        public static string? Current => Ambient.Value;

        public static IDisposable Set(string? id)
        {
            var previous = Ambient.Value;
            Ambient.Value = id;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            public Scope(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Ambient.Value = _previous;
            }
        }
    }
}
