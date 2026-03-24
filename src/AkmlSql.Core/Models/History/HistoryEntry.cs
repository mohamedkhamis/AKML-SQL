#nullable enable
using System;

namespace AkmlSql.Core.Models.History
{
    /// <summary>
    /// Represents a single SQL execution history entry, stored in the SQLite history database.
    /// </summary>
    public class HistoryEntry
    {
        /// <summary>Auto-incremented primary key.</summary>
        public long Id { get; set; }

        /// <summary>The SQL text that was executed.</summary>
        public string SqlText { get; set; } = string.Empty;

        /// <summary>Whether the SQL text was truncated to fit the 1 MB storage limit.</summary>
        public bool SqlTextTruncated { get; set; }

        /// <summary>Server name the query was executed against.</summary>
        public string? Server { get; set; }

        /// <summary>Database name the query was executed against.</summary>
        public string? Database { get; set; }

        /// <summary>Username (login) that executed the query.</summary>
        public string? Username { get; set; }

        /// <summary>When the execution started, in UTC.</summary>
        public DateTime ExecutedAt { get; set; }

        /// <summary>Total execution duration in milliseconds.</summary>
        public long DurationMs { get; set; }

        /// <summary>Number of rows affected or returned.</summary>
        public long RowCount { get; set; }

        /// <summary>Outcome of the execution.</summary>
        public ExecutionStatus Status { get; set; }

        /// <summary>Error message if execution failed; null on success.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Source file path or identifier (e.g. script file name).</summary>
        public string? Source { get; set; }

        /// <summary>Title of the editor tab/window where the query was executed.</summary>
        public string? TabTitle { get; set; }

        /// <summary>SHA-256 hash of the whitespace-normalized, case-folded SQL text for deduplication.</summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>Whether this entry has been marked as a favorite (protected from retention purge).</summary>
        public bool IsFavorite { get; set; }
    }
}
