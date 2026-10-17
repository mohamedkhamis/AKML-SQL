using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Web → Engine: the web edition's Disconnect. The engine rolls back the session's open transaction (never commits it),
    /// closes the session's persistent connection — its #temp tables, SET options and locks go with it — and forgets the
    /// session's connection, then replies. Sent only when the engine advertises <c>session.disconnect.v1</c>.
    /// </summary>
    [MessagePackObject]
    public class SessionDisconnectRequest
    {
        /// <summary>The canonical web session id.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>Engine → Web: the result of <see cref="SessionDisconnectRequest"/>.</summary>
    [MessagePackObject]
    public class SessionDisconnectResponse
    {
        /// <summary>The connection is closed and the session holds no connection any more.</summary>
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>Why the connection could not be closed (a statement still running past the wait); null on success.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>An open transaction was rolled back.</summary>
        [Key(2)]
        public bool RolledBack { get; set; }
    }
}
