using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US14 — request to scan a database for objects with broken
    /// references (missing tables / columns / synonyms etc.).
    /// Sent Shell → Engine as <see cref="MessageTypes.FindInvalidObjects"/> (90).
    /// </summary>
    [MessagePackObject]
    public class FindInvalidObjectsRequest
    {
        /// <summary>Session id used to look up the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Target database to scan.</summary>
        [Key(1)]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// Number of records emitted per progress chunk. The engine yields results
        /// incrementally so the shell tool window can render rows as they arrive.
        /// Default 50.
        /// </summary>
        [Key(2)]
        public int ChunkSize { get; set; } = 50;
    }
}
