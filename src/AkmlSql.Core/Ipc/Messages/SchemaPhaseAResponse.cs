using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Engine's response to
    /// <see cref="SchemaPhaseARequest"/>. The payload is an opaque MessagePack-
    /// serialised <c>SchemaPhasePayload</c> (schemas + object names + types) that
    /// the browser stores in <c>SchemaSnapshot.PhaseA</c> verbatim. The browser
    /// deserialises it only when it needs to serve a completion offline.
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaPhaseAResponse
    {
        /// <summary>Echoed from the request for correlation.</summary>
        [Key(0)] public string SessionId { get; set; } = string.Empty;

        /// <summary>Echoed from the request.</summary>
        [Key(1)] public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// MessagePack-of-<c>SchemaPhasePayload</c>. Empty when the engine has no
        /// cache for this session/db combination. The browser stores this byte[]
        /// directly into <c>SchemaSnapshot.PhaseA</c>.
        /// </summary>
        [Key(2)] public byte[] PhaseA { get; set; } = System.Array.Empty<byte>();

        /// <summary>
        /// Cache identity checksum at the time of serialisation. The browser pairs
        /// this with <c>SchemaSnapshot.Checksum</c> so a subsequent drift check
        /// against <see cref="SchemaChecksumResponse.Checksum"/> can avoid a redundant
        /// fetch when the cache is already current.
        /// </summary>
        [Key(3)] public string Checksum { get; set; } = string.Empty;

        /// <summary>
        /// True when the engine had a populated cache and produced a payload. False
        /// means the cache was empty / not yet populated; the browser MUST NOT
        /// overwrite an existing snapshot from a non-connection response.
        /// </summary>
        [Key(4)] public bool HasConnection { get; set; }

        /// <summary>Human-readable detail when <see cref="HasConnection"/> is false.</summary>
        [Key(5)] public string? ErrorMessage { get; set; }
    }
}
