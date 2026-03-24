#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response confirming a SQL execution was recorded in the history database.
    /// Sent Engine → Shell as MessageType 140 (HistoryRecordResult).
    /// </summary>
    [MessagePackObject]
    public class HistoryRecordResponse
    {
        /// <summary>Whether the record was successfully inserted.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The auto-generated ID of the new history entry.</summary>
        [Key(1)]
        public long EntryId { get; set; }

        /// <summary>Error message if recording failed; null on success.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }
}
