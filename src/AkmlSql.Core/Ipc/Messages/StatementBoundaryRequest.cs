using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to detect statement boundaries in SQL text.
    /// Sent Shell -> Engine as MessageType 65 (StatementBoundary).
    /// </summary>
    [MessagePackObject]
    public class StatementBoundaryRequest
    {
        /// <summary>Session identifier for the active editor.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full SQL text of the document.</summary>
        [Key(1)]
        public string SqlText { get; set; } = string.Empty;

        /// <summary>0-based character offset of the cursor position.</summary>
        [Key(2)]
        public int CursorOffset { get; set; }

        /// <summary>
        /// When <c>true</c>, returns all statement ranges in the document
        /// (used for next/previous statement navigation).
        /// When <c>false</c>, only returns the statement containing the cursor.
        /// </summary>
        [Key(3)]
        public bool AllStatements { get; set; }
    }
}
