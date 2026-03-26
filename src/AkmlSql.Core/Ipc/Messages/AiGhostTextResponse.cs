using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing an inline ghost-text prediction.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiGhostTextResponse
    {
        /// <summary>Whether a prediction was generated successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The predicted text to display as ghost text after the cursor.</summary>
        [Key(1)]
        public string? PredictedText { get; set; }

        /// <summary>The cursor offset this prediction was generated for (for staleness detection).</summary>
        [Key(2)]
        public int CursorOffset { get; set; }

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
