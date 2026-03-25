#nullable enable
using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing an AI-optimized version of a SQL statement.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiOptimizeResponse
    {
        /// <summary>Whether the optimization succeeded.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The optimized SQL text.</summary>
        [Key(1)]
        public string? OptimizedSql { get; set; }

        /// <summary>Human-readable explanation of the optimizations applied.</summary>
        [Key(2)]
        public string? Explanation { get; set; }

        /// <summary>Line-level annotations highlighting safe and review-worthy changes.</summary>
        [Key(3)]
        public List<AnnotationDto>? Annotations { get; set; }

        /// <summary>Suggested indexes that could further improve performance.</summary>
        [Key(4)]
        public List<IndexSuggestionDto>? IndexSuggestions { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(5)]
        public string? ErrorMessage { get; set; }

        /// <summary>Number of tokens consumed by the AI request.</summary>
        [Key(6)]
        public int TokensUsed { get; set; }

        /// <summary>Round-trip latency in milliseconds.</summary>
        [Key(7)]
        public int LatencyMs { get; set; }
    }
}
