#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to analyze a SQL statement for index improvement suggestions.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiIndexAnalysisRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The SQL text to analyze for indexing opportunities.</summary>
        [Key(1)]
        public string SelectedSql { get; set; } = string.Empty;

        /// <summary>Optional XML execution plan to provide additional context for analysis.</summary>
        [Key(2)]
        public string? ExecutionPlanXml { get; set; }
    }
}
