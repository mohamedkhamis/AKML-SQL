#nullable enable
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
        /// 0 = GetFullSql, 1 = ToggleFavorite, 2 = Delete, 3 = Export, 4 = GetDiff, 5 = DeleteAll.
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
    }
}
