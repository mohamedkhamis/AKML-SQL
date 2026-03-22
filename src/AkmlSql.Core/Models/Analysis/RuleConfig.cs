using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Analysis
{
    public class RuleConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>"error", "warning", "information", "hint", "ignore"</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;
    }
}
