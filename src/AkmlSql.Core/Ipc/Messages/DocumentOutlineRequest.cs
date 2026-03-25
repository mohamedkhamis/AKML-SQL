#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to build a structural document outline tree from SQL text.
    /// Sent Shell -> Engine as MessageType 64 (DocumentOutline).
    /// </summary>
    [MessagePackObject]
    public class DocumentOutlineRequest
    {
        /// <summary>Session identifier for the active editor.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full SQL text of the document.</summary>
        [Key(1)]
        public string SqlText { get; set; } = string.Empty;
    }
}
