using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing AI-generated index improvement suggestions.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiIndexAnalysisResponse
    {
        /// <summary>Whether the analysis succeeded.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>List of suggested indexes with CREATE scripts and impact estimates.</summary>
        [Key(1)]
        public List<IndexSuggestionDto>? Suggestions { get; set; }

        /// <summary>Human-readable summary of the index analysis.</summary>
        [Key(2)]
        public string? Summary { get; set; }

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
