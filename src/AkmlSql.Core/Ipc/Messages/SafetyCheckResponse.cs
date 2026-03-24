#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from the engine containing execution safety warnings.
    /// Sent Engine -> Shell as MessageType 155 (SafetyCheckResult).
    /// </summary>
    [MessagePackObject]
    public class SafetyCheckResponse
    {
        /// <summary>
        /// Whether the execution requires user confirmation before proceeding.
        /// <c>true</c> when one or more warnings were detected.
        /// </summary>
        [Key(0)]
        public bool RequiresConfirmation { get; set; }

        /// <summary>
        /// Array of safety warnings detected in the SQL text.
        /// Empty when no warnings were found.
        /// </summary>
        [Key(1)]
        public SafetyWarningDto[] Warnings { get; set; } = [];
    }
}
