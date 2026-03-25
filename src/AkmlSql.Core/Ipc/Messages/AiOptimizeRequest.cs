#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to optimize a SQL statement using AI assistance.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiOptimizeRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The SQL text to optimize.</summary>
        [Key(1)]
        public string SelectedSql { get; set; } = string.Empty;
    }
}
