using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to perform an action on history entries (get full SQL, toggle favorite, delete, export, diff).
    /// Sent Shell -> Engine as MessageType 42 (HistoryAction).
    /// </summary>
    [MessagePackObject]
    public class HistoryActionRequest
    {
        /// <summary>
        /// The action to perform.
        /// 0 = GetFullSql, 1 = ToggleFavorite, 2 = Delete, 3 = Export, 4 = GetDiff, 5 = DeleteAll,
        /// 6 = Rename, 7 = GetVersions, 8 = SetOpenStatus, 9 = SaveVersion, 10 = RemoveOlderThan.
        /// </summary>
        [Key(0)]
        public int Action { get; set; }

        /// <summary>
        /// Entry IDs to operate on.
        /// GetFullSql: single ID. GetDiff: exactly 2 IDs. Delete: one or more IDs.
        /// ToggleFavorite: single ID. Export/DeleteAll: may be empty (uses filter).
        /// </summary>
        [Key(1)]
        public long[] EntryIds { get; set; } = System.Array.Empty<long>();

        /// <summary>
        /// Export format (only used when Action = Export).
        /// Maps to <see cref="Core.Models.History.ExportFormat"/>: 0=Csv, 1=Json, 2=Sql.
        /// </summary>
        [Key(2)]
        public int? ExportFormat { get; set; }

        /// <summary>
        /// Output file path for export (only used when Action = Export).
        /// Must be an absolute path.
        /// </summary>
        [Key(3)]
        public string? ExportPath { get; set; }

        /// <summary>
        /// Optional search filter for Export and DeleteAll actions.
        /// When provided, the action operates on entries matching this filter
        /// rather than specific EntryIds.
        /// </summary>
        [Key(4)]
        public HistorySearchRequest? Filter { get; set; }

        /// <summary>
        /// New display name for a history entry (only used when Action = Rename).
        /// <para>
        /// Also reused (only) when Action = SaveVersion to carry the document's full path —
        /// i.e. <c>history.source</c> — that identifies which entry to snapshot against. NOT the
        /// tab title: an unsaved document has no meaningful title but always has a source path.
        /// </para>
        /// </summary>
        [Key(5)]
        public string? NewName { get; set; }

        /// <summary>
        /// Open/closed status to set (only used when Action = SetOpenStatus).
        /// </summary>
        [Key(6)]
        public bool? IsOpen { get; set; }

        /// <summary>
        /// SQL text for SaveVersion action — the version snapshot content.
        /// </summary>
        [Key(7)]
        public string? SqlText { get; set; }

        /// <summary>
        /// For RemoveOlderThan: keep favorited entries (default true). The cutoff is the executed_at
        /// timestamp of <see cref="EntryIds"/>[0], resolved server-side.
        /// </summary>
        [Key(8)]
        public bool? KeepFavorites { get; set; }
    }

    /// <summary>
    /// Constants for <see cref="HistoryActionRequest.Action"/>.
    /// </summary>
    public static class HistoryActions
    {
        public const int GetFullSql = 0;
        public const int ToggleFavorite = 1;
        public const int Delete = 2;
        public const int Export = 3;
        public const int GetDiff = 4;
        public const int DeleteAll = 5;
        public const int Rename = 6;
        public const int GetVersions = 7;
        public const int SetOpenStatus = 8;
        public const int SaveVersion = 9;
        public const int RemoveOlderThan = 10;
    }
}
