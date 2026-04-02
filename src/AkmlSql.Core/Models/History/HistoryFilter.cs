using System;

namespace AkmlSql.Core.Models.History
{
    /// <summary>
    /// Represents filter criteria for searching execution history.
    /// Used by the shell to build search requests and by the engine to construct
    /// parameterized SQLite queries.
    /// </summary>
    public class HistoryFilter
    {
        /// <summary>Full-text search query applied against SQL text via FTS5 MATCH.</summary>
        public string? SearchText { get; set; }

        /// <summary>Filter to entries executed against a specific server.</summary>
        public string? Server { get; set; }

        /// <summary>Filter to entries executed against a specific database.</summary>
        public string? Database { get; set; }

        /// <summary>Filter to entries with a specific execution status (maps to <see cref="ExecutionStatus"/>).</summary>
        public int? Status { get; set; }

        /// <summary>Include only entries executed at or after this UTC date/time.</summary>
        public DateTime? DateFrom { get; set; }

        /// <summary>Include only entries executed at or before this UTC date/time.</summary>
        public DateTime? DateTo { get; set; }

        /// <summary>When true, include only entries marked as favorites.</summary>
        public bool FavoritesOnly { get; set; }

        /// <summary>
        /// When true, group results by content_hash and return only the most recent
        /// execution of each unique query, with an execution count.
        /// </summary>
        public bool Deduplicate { get; set; }

        /// <summary>Number of entries to skip for pagination.</summary>
        public int Offset { get; set; }

        /// <summary>Maximum number of entries to return. Defaults to 100.</summary>
        public int Limit { get; set; } = 100;
    }
}
