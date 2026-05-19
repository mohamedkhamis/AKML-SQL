using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 schema-cache change detection. Browser polls the
    /// engine with a (server, db) tuple; the engine returns a checksum the browser
    /// compares to its cached value to decide whether to trigger a Phase A refresh.
    /// See <c>specs/021-web-edition/contracts/schema-cache-shape.md</c>.
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaChecksumRequest
    {
        /// <summary>The session whose connection to query. Identifies which engine connection.</summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;

        /// <summary>The canonical server identity. Used purely for response correlation -- the engine doesn't dispatch on this; the SessionId already selects the open connection.</summary>
        [Key(1)] public string ServerCanonicalIdentity { get; set; } = string.Empty;

        /// <summary>The database name to compute the checksum against.</summary>
        [Key(2)] public string DatabaseName { get; set; } = string.Empty;
    }
}
