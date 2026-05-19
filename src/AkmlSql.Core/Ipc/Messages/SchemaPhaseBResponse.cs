using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Engine's response to
    /// <see cref="SchemaPhaseBRequest"/>. Same shape as <see cref="SchemaPhaseAResponse"/>
    /// but the payload contains the Phase B view (columns + foreign keys).
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaPhaseBResponse
    {
        [Key(0)] public string SessionId { get; set; } = string.Empty;
        [Key(1)] public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// MessagePack-of-<c>SchemaPhasePayload</c> at the Phase B detail level
        /// (columns + foreign keys included). Empty when the engine cache hasn't
        /// reached Phase B yet — the browser will keep its existing PhaseB blob,
        /// if any.
        /// </summary>
        [Key(2)] public byte[] PhaseB { get; set; } = System.Array.Empty<byte>();

        [Key(3)] public string Checksum { get; set; } = string.Empty;
        [Key(4)] public bool HasConnection { get; set; }
        [Key(5)] public string? ErrorMessage { get; set; }
    }
}
