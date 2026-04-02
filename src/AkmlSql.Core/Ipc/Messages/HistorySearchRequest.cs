using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to search execution history with filters and pagination.
    /// Sent Shell -> Engine as MessageType 41 (HistorySearch).
    /// </summary>
    [MessagePackObject]
    public class HistorySearchRequest
    {
        /// <summary>Full-text search query applied against SQL text via FTS5 MATCH. Null for no text filter.</summary>
        [Key(0)]
        public string? SearchText { get; set; }

        /// <summary>Filter to entries executed against a specific server. Null for all servers.</summary>
        [Key(1)]
        public string? Server { get; set; }

        /// <summary>Filter to entries executed against a specific database. Null for all databases.</summary>
        [Key(2)]
        public string? Database { get; set; }

        /// <summary>Filter to a specific execution status (maps to <see cref="Core.Models.History.ExecutionStatus"/>). Null for all statuses.</summary>
        [Key(3)]
        public int? Status { get; set; }

        /// <summary>Include only entries executed at or after this date/time (ISO 8601 UTC). Null for no lower bound.</summary>
        [Key(4)]
        public string? DateFrom { get; set; }

        /// <summary>Include only entries executed at or before this date/time (ISO 8601 UTC). Null for no upper bound.</summary>
        [Key(5)]
        public string? DateTo { get; set; }

        /// <summary>When true, include only entries marked as favorites.</summary>
        [Key(6)]
        public bool FavoritesOnly { get; set; }

        /// <summary>When true, group by content_hash and return only the most recent execution of each unique query.</summary>
        [Key(7)]
        public bool Deduplicate { get; set; }

        /// <summary>Number of entries to skip for pagination.</summary>
        [Key(8)]
        public int Offset { get; set; }

        /// <summary>Maximum number of entries to return.</summary>
        [Key(9)]
        public int Limit { get; set; } = 100;
    }
}
