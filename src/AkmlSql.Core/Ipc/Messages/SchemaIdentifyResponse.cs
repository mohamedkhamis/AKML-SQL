using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 task T104. Reply to <see cref="SchemaIdentifyRequest"/>.
    /// The pair <see cref="ServerCanonicalIdentity"/> + <see cref="DatabaseName"/> is the
    /// composite cache key for the web edition's IndexedDB schema cache (see
    /// <c>specs/021-web-edition/contracts/schema-cache-shape.md</c>).
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaIdentifyResponse
    {
        /// <summary>
        /// Echoed from the request so the browser can correlate the response when several
        /// SchemaIdentify calls are in flight for different sessions.
        /// </summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Stable identifier for the SQL Server instance. Resolved from
        /// <c>@@SERVERNAME</c> with a fallback to <c>SERVERPROPERTY('ServerName')</c>;
        /// empty when the engine has no DB connection for this session.
        /// </summary>
        [Key(1)] public string ServerCanonicalIdentity { get; set; } = string.Empty;

        /// <summary>Current database name on the session. Empty when no connection.</summary>
        [Key(2)] public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// True when the engine successfully resolved an identity. False means the session
        /// has no live connection and the browser should NOT cache against this response.
        /// </summary>
        [Key(3)] public bool HasConnection { get; set; }

        /// <summary>Human-readable detail when <see cref="HasConnection"/> is false.</summary>
        [Key(4)] public string? ErrorMessage { get; set; }
    }
}
