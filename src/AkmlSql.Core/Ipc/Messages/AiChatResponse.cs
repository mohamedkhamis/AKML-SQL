#nullable enable
using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from the AI chat panel containing a reply and optional code actions.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiChatResponse
    {
        /// <summary>Whether the chat response was generated successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The AI assistant's response text.</summary>
        [Key(1)]
        public string? Response { get; set; }

        /// <summary>Optional actionable code suggestions the user can apply.</summary>
        [Key(2)]
        public List<CodeActionDto>? CodeActions { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(3)]
        public string? ErrorMessage { get; set; }

        /// <summary>Number of tokens consumed by the AI request.</summary>
        [Key(4)]
        public int TokensUsed { get; set; }

        /// <summary>Round-trip latency in milliseconds.</summary>
        [Key(5)]
        public int LatencyMs { get; set; }
    }
}
