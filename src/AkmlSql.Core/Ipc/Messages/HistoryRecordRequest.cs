using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to record a SQL execution in the history database.
    /// Sent Shell → Engine as MessageType 40 (HistoryRecord).
    /// </summary>
    [MessagePackObject]
    public class HistoryRecordRequest
    {
        /// <summary>The SQL text that was executed.</summary>
        [Key(0)]
        public string SqlText { get; set; } = string.Empty;

        /// <summary>Whether the SQL text was already truncated before sending.</summary>
        [Key(1)]
        public bool Truncated { get; set; }

        /// <summary>Server name the query was executed against.</summary>
        [Key(2)]
        public string? Server { get; set; }

        /// <summary>Database name the query was executed against.</summary>
        [Key(3)]
        public string? Database { get; set; }

        /// <summary>Username (login) that executed the query.</summary>
        [Key(4)]
        public string? Username { get; set; }

        /// <summary>Total execution duration in milliseconds.</summary>
        [Key(5)]
        public long DurationMs { get; set; }

        /// <summary>Number of rows affected or returned.</summary>
        [Key(6)]
        public long RowCount { get; set; }

        /// <summary>Execution outcome as integer (maps to <see cref="Core.Models.History.ExecutionStatus"/>).</summary>
        [Key(7)]
        public int Status { get; set; }

        /// <summary>Error message if execution failed; null on success.</summary>
        [Key(8)]
        public string? ErrorMessage { get; set; }

        /// <summary>Source file path or identifier.</summary>
        [Key(9)]
        public string? Source { get; set; }

        /// <summary>Title of the editor tab/window where the query was executed.</summary>
        [Key(10)]
        public string? TabTitle { get; set; }
    }
}
