using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M5. Engine's response to a
    /// <see cref="SchemaChecksumRequest"/>. The browser compares
    /// <see cref="Checksum"/> against its cached value to decide whether the local
    /// snapshot is stale.
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaChecksumResponse
    {
        /// <summary>Echoed from the request.</summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;
        [Key(1)] public string ServerCanonicalIdentity { get; set; } = string.Empty;
        [Key(2)] public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// Opaque string -- the engine computes it from
        /// <c>SELECT CHECKSUM_AGG(BINARY_CHECKSUM(name, schema_id, modify_date))
        /// FROM sys.objects WHERE is_ms_shipped = 0</c>. The browser stores this
        /// value and compares string-equal on the next poll.
        /// </summary>
        [Key(3)] public string Checksum { get; set; } = string.Empty;

        /// <summary>True when the engine successfully queried the database. False = no live connection or query failed; browser MUST NOT update its cached checksum.</summary>
        [Key(4)] public bool HasConnection { get; set; }

        [Key(5)] public string? ErrorMessage { get; set; }
    }
}
