using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

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

        /// <summary>
        /// Spec 047 (ENG-001): the open connections, so a revoked pairing's connections can be closed at once.
        /// A connection is listed from <see cref="Set"/> until its scope is disposed.
        /// </summary>
        private static readonly ConcurrentDictionary<Holder, byte> Live = new(ReferenceEqualityComparer.Instance);

        /// <summary><c>page:{id}</c> once the page's id was adopted, otherwise <c>socket:{id}</c>; null outside the bridge.</summary>
        public static string? Current => Ambient.Value?.Origin;

        /// <summary>
        /// Spec 047 (ENG-001): the SHA-256 hash of the pairing token the connection authenticated with, recorded by the
        /// handshake (<see cref="SetTokenHash"/>); null outside the bridge and for a connection that needed no pairing.
        /// </summary>
        public static string? CurrentTokenHash => Ambient.Value?.TokenHash;

        /// <summary>Starts a connection's scope with its socket id; disposing it restores the previous value.</summary>
        public static IDisposable Set(string? socketId)
        {
            var previous = Ambient.Value;
            var holder = string.IsNullOrEmpty(socketId) ? null : new Holder(socketId!);
            Ambient.Value = holder;
            if (holder != null) Live[holder] = 0;
            return new Scope(previous, holder);
        }

        /// <summary>
        /// Spec 047 (ENG-001): records the pairing an accepted handshake used. Ignored outside a connection's scope. The
        /// handshake answers in a child async context; the shared holder makes it visible to the transport's loop.
        /// </summary>
        public static void SetTokenHash(string? tokenHash)
        {
            if (Ambient.Value is { } holder) holder.TokenHash = tokenHash;
        }

        /// <summary>Spec 047 (ENG-001): how the transport closes this connection when its pairing is revoked.</summary>
        public static void SetCloser(Func<Task>? close)
        {
            if (Ambient.Value is { } holder) holder.Close = close;
        }

        /// <summary>
        /// Spec 047 (ENG-001): closes every open connection that authenticated with <paramref name="tokenHash"/> and returns
        /// how many were asked to close. A closer that fails is logged and still counted: its connection is being torn down.
        /// </summary>
        public static int CloseConnectionsWithToken(string tokenHash)
        {
            if (string.IsNullOrEmpty(tokenHash)) return 0;
            var closed = 0;
            foreach (var holder in Live.Keys)
            {
                if (!string.Equals(holder.TokenHash, tokenHash, StringComparison.Ordinal)) continue;
                closed++;
                var close = holder.Close;
                if (close == null) continue;
                try
                {
                    var task = close();
                    task.ContinueWith(t => Log.Warning(t.Exception, "BridgeConnection: closing a revoked pairing's connection failed"),
                        TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "BridgeConnection: closing a revoked pairing's connection failed");
                }
            }
            return closed;
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
            private string? _pageId;    // written once, read from any thread-pool thread answering a request
            private string? _tokenHash;
            private Func<Task>? _close;

            public Holder(string socketId) => SocketId = socketId;

            public string SocketId { get; }

            public string Origin => Volatile.Read(ref _pageId) is { } page ? "page:" + page : "socket:" + SocketId;

            public string? TokenHash
            {
                get => Volatile.Read(ref _tokenHash);
                set => Volatile.Write(ref _tokenHash, value);
            }

            public Func<Task>? Close
            {
                get => Volatile.Read(ref _close);
                set => Volatile.Write(ref _close, value);
            }

            public void Adopt(string pageId) => Interlocked.CompareExchange(ref _pageId, pageId, null);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Holder? _previous;
            private readonly Holder? _own;
            private bool _disposed;

            public Scope(Holder? previous, Holder? own)
            {
                _previous = previous;
                _own = own;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_own != null) Live.TryRemove(_own, out _);
                Ambient.Value = _previous;
            }
        }
    }
}
