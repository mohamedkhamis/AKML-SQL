using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// MessagePack-serializable DTO representing a recoverable session for IPC transfer.
    /// Returned inside <see cref="SessionRestoreResponse"/> so the shell can display
    /// a recovery dialog listing available sessions.
    /// </summary>
    [MessagePackObject]
    public class RecoverableSessionDto
    {
        /// <summary>Unique session identifier (GUID string).</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>ISO 8601 UTC timestamp when the session was last captured.</summary>
        [Key(1)]
        public string CapturedAt { get; set; } = string.Empty;

        /// <summary>Number of tabs in the session snapshot.</summary>
        [Key(2)]
        public int TabCount { get; set; }

        /// <summary>Tab snapshots from the session, for restoration.</summary>
        [Key(3)]
        public TabSnapshotDto[] Tabs { get; set; } = [];
    }
}
