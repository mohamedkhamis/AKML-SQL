#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to check SQL text for execution safety warnings before running a query.
    /// Sent Shell -> Engine as MessageType 55 (SafetyCheck).
    /// </summary>
    [MessagePackObject]
    public class SafetyCheckRequest
    {
        /// <summary>The SQL text about to be executed.</summary>
        [Key(0)]
        public string SqlText { get; set; } = string.Empty;

        /// <summary>Server name the query will be executed against.</summary>
        [Key(1)]
        public string? Server { get; set; }

        /// <summary>Whether the target server is classified as a production server.</summary>
        [Key(2)]
        public bool IsProductionServer { get; set; }
    }
}
