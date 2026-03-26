using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to send a message to the AI chat panel.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiChatRequest
    {
        /// <summary>Session identifier to look up the active connection and schema context.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The user's chat message.</summary>
        [Key(1)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Previous conversation turns for multi-turn context.</summary>
        [Key(2)]
        public List<ChatTurnDto> History { get; set; } = new();
    }
}
