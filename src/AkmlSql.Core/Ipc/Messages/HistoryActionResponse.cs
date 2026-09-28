using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from a history action request.
    /// Sent Engine -> Shell as MessageType 142 (HistoryActionResult).
    /// </summary>
    [MessagePackObject]
    public class HistoryActionResponse
    {
        /// <summary>Whether the action executed successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Full SQL text (returned by GetFullSql action).</summary>
        [Key(1)]
        public string? FullSqlText { get; set; }

        /// <summary>Left-side SQL text for diff comparison (returned by GetDiff action).</summary>
        [Key(2)]
        public string? DiffLeftSql { get; set; }

        /// <summary>Right-side SQL text for diff comparison (returned by GetDiff action).</summary>
        [Key(3)]
        public string? DiffRightSql { get; set; }

        /// <summary>Path of the exported file (returned by Export action).</summary>
        [Key(4)]
        public string? ExportPath { get; set; }

        /// <summary>Error message if the action failed; null on success.</summary>
        [Key(5)]
        public string? Error { get; set; }

        /// <summary>
        /// Version snapshots for a history entry (returned by GetVersions action).
        /// Each item contains: Id, SqlText, SavedAt.
        /// </summary>
        [Key(6)]
        public HistoryVersionDto[]? Versions { get; set; }

        /// <summary>Number of entries removed (returned by RemoveOlderThan / delete actions).</summary>
        [Key(7)]
        public int DeletedCount { get; set; }

        /// <summary>Spec 040 (HIS-04): the favourite state after ToggleFavorite.</summary>
        [Key(8)]
        public bool? IsFavorite { get; set; }

        /// <summary>Spec 040 (HIS-08): distinct server names (GetFilterValues).</summary>
        [Key(9)]
        public string[]? Servers { get; set; }

        /// <summary>Spec 040 (HIS-08): distinct database names (GetFilterValues).</summary>
        [Key(10)]
        public string[]? Databases { get; set; }

        /// <summary>
        /// Spec 040 (HIS-02/HIS-14): from ReconcileOpen — the grouped rows that were open when their
        /// shell exited or crashed, newest first. The restore-on-start service reads these.
        /// </summary>
        [Key(11)]
        public long[]? RestorableEntryIds { get; set; }
    }

    /// <summary>
    /// Lightweight DTO for a history version snapshot.
    /// </summary>
    [MessagePackObject]
    public class HistoryVersionDto
    {
        [Key(0)]
        public long Id { get; set; }

        [Key(1)]
        public string SqlText { get; set; } = string.Empty;

        [Key(2)]
        public string SavedAt { get; set; } = string.Empty;
    }
}
