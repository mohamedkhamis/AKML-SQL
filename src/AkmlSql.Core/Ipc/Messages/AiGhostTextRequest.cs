using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request for inline ghost-text (autocomplete) prediction at the current cursor position.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiGhostTextRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full document text at the time of the request.</summary>
        [Key(1)]
        public string DocumentText { get; set; } = string.Empty;

        /// <summary>Zero-based character offset of the cursor within <see cref="DocumentText"/>.</summary>
        [Key(2)]
        public int CursorOffset { get; set; }

        /// <summary>Text immediately preceding the cursor (for context windowing).</summary>
        [Key(3)]
        public string PrecedingText { get; set; } = string.Empty;
    }
}
