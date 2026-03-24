#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// A single safety warning detected by the engine during pre-execution analysis.
    /// Carried inside <see cref="SafetyCheckResponse.Warnings"/>.
    /// </summary>
    [MessagePackObject]
    public class SafetyWarningDto
    {
        /// <summary>
        /// The warning category, mapped to <see cref="Core.Models.Safety.SafetyWarningType"/> (int).
        /// </summary>
        [Key(0)]
        public int WarningType { get; set; }

        /// <summary>Human-readable warning message displayed to the user.</summary>
        [Key(1)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The database object name associated with the warning (e.g. the table name for DROP TABLE).
        /// Used for type-to-confirm dialogs. <c>null</c> when not applicable.
        /// </summary>
        [Key(2)]
        public string? ObjectName { get; set; }

        /// <summary>
        /// Severity level: 0 = Info, 1 = Warning, 2 = Error.
        /// </summary>
        [Key(3)]
        public int Severity { get; set; }
    }
}
