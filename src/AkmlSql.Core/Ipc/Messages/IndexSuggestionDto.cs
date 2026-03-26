using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// An AI-suggested index with CREATE script and impact estimates.
    /// </summary>
    [MessagePackObject]
    public class IndexSuggestionDto
    {
        /// <summary>Full CREATE INDEX statement ready to execute.</summary>
        [Key(0)]
        public string CreateScript { get; set; } = string.Empty;

        /// <summary>Fully qualified table name the index targets.</summary>
        [Key(1)]
        public string TableName { get; set; } = string.Empty;

        /// <summary>Key columns of the suggested index.</summary>
        [Key(2)]
        public List<string> Columns { get; set; } = new();

        /// <summary>Optional INCLUDE columns for covering index scenarios.</summary>
        [Key(3)]
        public List<string>? IncludeColumns { get; set; }

        /// <summary>Estimated performance improvement description (e.g. "~40% faster reads").</summary>
        [Key(4)]
        public string EstimatedImprovement { get; set; } = string.Empty;

        /// <summary>Estimated index size in kilobytes.</summary>
        [Key(5)]
        public int EstimatedSizeKb { get; set; }

        /// <summary>Impact on write operations: "Low", "Medium", or "High".</summary>
        [Key(6)]
        public string WriteImpact { get; set; } = string.Empty;
    }
}
