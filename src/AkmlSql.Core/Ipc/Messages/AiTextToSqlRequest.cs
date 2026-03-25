#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to generate SQL from a natural-language prompt.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiTextToSqlRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Natural-language description of the desired query.</summary>
        [Key(1)]
        public string Prompt { get; set; } = string.Empty;
    }
}
