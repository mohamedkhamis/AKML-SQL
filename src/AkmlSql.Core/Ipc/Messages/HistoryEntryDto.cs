using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Lightweight DTO for transferring history entry data over IPC.
    /// SQL text is truncated to 500 characters for preview purposes.
    /// </summary>
    [MessagePackObject]
    public class HistoryEntryDto
    {
        /// <summary>Auto-incremented primary key from the history table.</summary>
        [Key(0)]
        public long Id { get; set; }

        /// <summary>First 500 characters of the SQL text for preview display.</summary>
        [Key(1)]
        public string SqlText { get; set; } = string.Empty;

        /// <summary>Server name the query was executed against.</summary>
        [Key(2)]
        public string? Server { get; set; }

        /// <summary>Database name the query was executed against.</summary>
        [Key(3)]
        public string? Database { get; set; }

        /// <summary>Username (login) that executed the query.</summary>
        [Key(4)]
        public string? Username { get; set; }

        /// <summary>When the execution started, in ISO 8601 UTC format.</summary>
        [Key(5)]
        public string ExecutedAt { get; set; } = string.Empty;

        /// <summary>Total execution duration in milliseconds.</summary>
        [Key(6)]
        public long DurationMs { get; set; }

        /// <summary>Number of rows affected or returned.</summary>
        [Key(7)]
        public long RowCount { get; set; }

        /// <summary>Execution outcome as integer (maps to <see cref="Core.Models.History.ExecutionStatus"/>).</summary>
        [Key(8)]
        public int Status { get; set; }

        /// <summary>Error message if execution failed; null on success.</summary>
        [Key(9)]
        public string? ErrorMessage { get; set; }

        /// <summary>Source file path or identifier.</summary>
        [Key(10)]
        public string? Source { get; set; }

        /// <summary>Title of the editor tab/window where the query was executed.</summary>
        [Key(11)]
        public string? TabTitle { get; set; }

        /// <summary>Whether this entry has been marked as a favorite.</summary>
        [Key(12)]
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Number of executions with the same content hash. Greater than 1 when deduplicated.
        /// </summary>
        [Key(13)]
        public int ExecutionCount { get; set; } = 1;

        /// <summary>SHA-256 content hash for deduplication detection.</summary>
        [Key(14)]
        public string? ContentHash { get; set; }
    }
}
