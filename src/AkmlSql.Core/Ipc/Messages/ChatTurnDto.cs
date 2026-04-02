using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// A single turn in an AI chat conversation.
    /// </summary>
    [MessagePackObject]
    public class ChatTurnDto
    {
        /// <summary>Role of the speaker: "user" or "assistant".</summary>
        [Key(0)]
        public string Role { get; set; } = string.Empty;

        /// <summary>Text content of this turn.</summary>
        [Key(1)]
        public string Content { get; set; } = string.Empty;
    }
}
