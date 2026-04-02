using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing history search results with pagination metadata.
    /// Sent Engine -> Shell as MessageType 141 (HistorySearchResult).
    /// </summary>
    [MessagePackObject]
    public class HistorySearchResponse
    {
        /// <summary>Whether the search executed successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Matching history entries (page of results).</summary>
        [Key(1)]
        public HistoryEntryDto[] Entries { get; set; } = System.Array.Empty<HistoryEntryDto>();

        /// <summary>
        /// Total number of matching entries across all pages (before OFFSET/LIMIT).
        /// Used by the UI for pagination display and "Load More" logic.
        /// </summary>
        [Key(2)]
        public int TotalCount { get; set; }

        /// <summary>Error message if the search failed; null on success.</summary>
        [Key(3)]
        public string? Error { get; set; }
    }
}
