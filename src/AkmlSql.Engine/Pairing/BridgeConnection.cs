using System;
using System.Threading;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (review): which web page a bridge request came from, as an ambient set by the transport for the life
    /// of the WebSocket connection (as <see cref="BridgeSourceIp"/> is). Null for named-pipe requests. A session's password is
    /// copied only into a request from the page that connected that session (<see cref="SessionPasswordCopy"/>).
    ///
    /// <para>
    /// The origin is the page, not the socket: after a link drop the page opens a new socket and its tabs keep their sessions,
    /// so the socket id alone would refuse every copy until a reload. The page makes one random id and sends it on each
    /// handshake (<c>HandshakeRequest.ClientInstanceId</c>); <see cref="Adopt"/> records it once the handshake is accepted. A
    /// client that sends none (an older build) stays the socket, as before.
    /// </para>
    ///
    /// <para>
    /// The ambient holds a mutable per-connection object: the handshake is answered in a child async context, where assigning
    /// an <see cref="AsyncLocal{T}"/> would not flow back to the transport's read loop. Setting a field on the shared object
    /// does, and every request the loop dispatches afterwards sees it.
    /// </para>
    /// </summary>
    internal static class BridgeConnection
    {
        private static readonly AsyncLocal<Holder?> Ambient = new();

        /// <summary><c>page:{id}</c> once the page's id was adopted, otherwise <c>socket:{id}</c>; null outside the bridge.</summary>
        public static string? Current => Ambient.Value?.Origin;

        /// <summary>Starts a connection's scope with its socket id; disposing it restores the previous value.</summary>
        public static IDisposable Set(string? socketId)
        {
            var previous = Ambient.Value;
            Ambient.Value = string.IsNullOrEmpty(socketId) ? null : new Holder(socketId!);
            return new Scope(previous);
        }

        /// <summary>
        /// Records the page id an accepted handshake sent. Ignored outside a connection's scope, when the id does not look like
        /// one (16 to 64 characters of letters, digits, '-' and '_'), and once the connection already has one: a socket belongs
        /// to one page.
        /// </summary>
        public static void Adopt(string? clientInstanceId)
        {
            if (Ambient.Value is { } holder && IsValidId(clientInstanceId))
                holder.Adopt(clientInstanceId!);
        }

        internal static bool IsValidId(string? id)
        {
            if (id is null || id.Length < 16 || id.Length > 64) return false;
            foreach (var c in id)
            {
                if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')) return false;
            }
            return true;
        }

        private sealed class Holder
        {
            private readonly string _socketId;
            private string? _pageId;    // written once, read from any thread-pool thread answering a request

            public Holder(string socketId) => _socketId = socketId;

            public string Origin => Volatile.Read(ref _pageId) is { } page ? "page:" + page : "socket:" + _socketId;

            public void Adopt(string pageId) => Interlocked.CompareExchange(ref _pageId, pageId, null);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Holder? _previous;
            private bool _disposed;

            public Scope(Holder? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Ambient.Value = _previous;
            }
        }
    }
}
