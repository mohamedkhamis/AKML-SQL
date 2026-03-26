using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing an AI-generated explanation of a SQL statement.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiExplainResponse
    {
        /// <summary>Whether the explanation was generated successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>High-level purpose of the SQL statement.</summary>
        [Key(1)]
        public string? Purpose { get; set; }

        /// <summary>Step-by-step breakdown of the SQL logic.</summary>
        [Key(2)]
        public string? StepByStep { get; set; }

        /// <summary>Key details such as joins, filters, and aggregations.</summary>
        [Key(3)]
        public string? KeyDetails { get; set; }

        /// <summary>Optional improvement suggestions.</summary>
        [Key(4)]
        public string? Suggestions { get; set; }

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
