#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// A streaming chunk pushed from the engine to the shell during an AI operation.
    /// Sent Engine -> Shell as a notification (RequestId 0).
    /// </summary>
    [MessagePackObject]
    public class AiStreamChunkMessage
    {
        /// <summary>The request ID of the originating AI request this chunk belongs to.</summary>
        [Key(0)]
        public int RequestId { get; set; }

        /// <summary>The text fragment for this chunk.</summary>
        [Key(1)]
        public string Text { get; set; } = string.Empty;

        /// <summary>Whether this is the final chunk in the stream.</summary>
        [Key(2)]
        public bool IsComplete { get; set; }

        /// <summary>Reason the stream finished (e.g. "stop", "length", "cancelled"). Null while streaming.</summary>
        [Key(3)]
        public string? FinishReason { get; set; }

        /// <summary>Cumulative token count at this point in the stream.</summary>
        [Key(4)]
        public int TokensUsed { get; set; }
    }
}
