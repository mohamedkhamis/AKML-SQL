using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to search for database objects by name pattern.
    /// Sent Shell -> Engine as MessageType 62 (ObjectSearch).
    /// </summary>
    [MessagePackObject]
    public class ObjectSearchRequest
    {
        /// <summary>Session identifier to look up the active connection and schema cache.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Search text (fuzzy matched against object names).</summary>
        [Key(1)]
        public string SearchText { get; set; } = string.Empty;

        /// <summary>Maximum number of results to return. Default 50.</summary>
        [Key(2)]
        public int MaxResults { get; set; } = 50;
    }
}
