#nullable enable
using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing AI-generated SQL from a natural-language prompt.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiTextToSqlResponse
    {
        /// <summary>Whether the generation succeeded.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The generated SQL text.</summary>
        [Key(1)]
        public string? GeneratedSql { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }

        /// <summary>Number of tokens consumed by the AI request.</summary>
        [Key(3)]
        public int TokensUsed { get; set; }

        /// <summary>Round-trip latency in milliseconds.</summary>
        [Key(4)]
        public int LatencyMs { get; set; }

        /// <summary>
        /// Validation annotations for the generated SQL (e.g., objects not found in schema).
        /// Added by the post-generation SQL validation step (T063).
        /// </summary>
        [Key(5)]
        public List<AnnotationDto>? Annotations { get; set; }
    }
}
