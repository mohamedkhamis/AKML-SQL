#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to fix a failing SQL statement using AI assistance.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiFixRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The SQL text that produced an error.</summary>
        [Key(1)]
        public string FailingSql { get; set; } = string.Empty;

        /// <summary>The error message returned by SQL Server.</summary>
        [Key(2)]
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>The SQL Server error number (e.g. 208 for invalid object name).</summary>
        [Key(3)]
        public int ErrorNumber { get; set; }
    }
}
