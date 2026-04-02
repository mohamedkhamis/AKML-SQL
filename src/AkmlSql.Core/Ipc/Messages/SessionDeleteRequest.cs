using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to delete a persisted session snapshot after successful restoration or dismissal.
    /// Sent Shell → Engine.
    /// </summary>
    [MessagePackObject]
    public class SessionDeleteRequest
    {
        /// <summary>The session identifier to delete.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;
    }
}
