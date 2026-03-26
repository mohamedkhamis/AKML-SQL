using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to retrieve recoverable sessions after a crash.
    /// Sent Shell → Engine on startup. No fields needed — the engine
    /// scans all persisted session files and returns those that were not
    /// cleanly shut down.
    /// </summary>
    [MessagePackObject]
    // ReSharper disable once UnusedMember.Global
    public class SessionRestoreRequest
    {
    }
}
