using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing recoverable sessions after a crash.
    /// Sent Engine → Shell.
    /// </summary>
    [MessagePackObject]
    public class SessionRestoreResponse
    {
        /// <summary>Whether any recoverable (non-clean-shutdown) sessions were found.</summary>
        [Key(0)]
        public bool HasRecoverableSessions { get; set; }

        /// <summary>List of recoverable sessions, ordered newest-first.</summary>
        [Key(1)]
        public RecoverableSessionDto[] Sessions { get; set; } = [];

        /// <summary>Error message if the restore scan failed.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }
}
