#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from the engine with statement boundary information.
    /// Sent Engine -> Shell as MessageType 165 (StatementBoundaryResult).
    /// </summary>
    [MessagePackObject]
    public class StatementBoundaryResponse
    {
        /// <summary>Whether the request was processed successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>
        /// The statement range containing the cursor position.
        /// <c>null</c> when the cursor is not inside any statement.
        /// </summary>
        [Key(1)]
        public StatementRangeDto? CurrentStatement { get; set; }

        /// <summary>
        /// All statement ranges in the document.
        /// Only populated when <see cref="StatementBoundaryRequest.AllStatements"/> is <c>true</c>.
        /// </summary>
        [Key(2)]
        public StatementRangeDto[]? AllStatements { get; set; }

        /// <summary>Error message if the request failed.</summary>
        [Key(3)]
        public string? Error { get; set; }
    }
}
