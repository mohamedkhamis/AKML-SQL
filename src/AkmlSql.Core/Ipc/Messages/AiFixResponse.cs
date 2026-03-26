using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing an AI-generated fix for a failing SQL statement.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiFixResponse
    {
        /// <summary>Whether a fix was generated successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The corrected SQL text.</summary>
        [Key(1)]
        public string? FixedSql { get; set; }

        /// <summary>Human-readable explanation of what was changed and why.</summary>
        [Key(2)]
        public string? Explanation { get; set; }

        /// <summary>Line-level annotations highlighting safe and review-worthy changes.</summary>
        [Key(3)]
        public List<AnnotationDto>? Annotations { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(4)]
        public string? ErrorMessage { get; set; }

        /// <summary>Number of tokens consumed by the AI request.</summary>
        [Key(5)]
        public int TokensUsed { get; set; }

        /// <summary>Round-trip latency in milliseconds.</summary>
        [Key(6)]
        public int LatencyMs { get; set; }
    }
}
