#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to save a session snapshot for crash recovery.
    /// Sent Shell → Engine on the auto-save timer tick and during clean shutdown.
    /// </summary>
    [MessagePackObject]
    public class SessionSaveRequest
    {
        /// <summary>Stable GUID identifying this SSMS/VS session.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Process ID of the SSMS/VS instance.</summary>
        [Key(1)]
        public int SsmsProcessId { get; set; }

        /// <summary>
        /// When <c>true</c>, this save was triggered by a clean shutdown.
        /// The engine marks the snapshot so it is excluded from crash recovery.
        /// </summary>
        [Key(2)]
        public bool IsNormalShutdown { get; set; }

        /// <summary>Snapshot of all open tabs at save time.</summary>
        [Key(3)]
        public TabSnapshotDto[] Tabs { get; set; } = [];
    }
}
