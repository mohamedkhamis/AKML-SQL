using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Analysis
{
    public class GlobalSuppression
    {
        [JsonPropertyName("rule")]
        public string Rule { get; set; } = string.Empty;

        /// <summary>
        /// Alias for <see cref="Rule"/>. The .casettings documentation has always shown this key
        /// ("globalSuppressions": [{ "ruleId": "NM002", ... }]), while the type only bound "rule",
        /// so a file written from the docs parsed without error and suppressed nothing. Both keys
        /// are accepted; whichever is present wins, and only "rule" is written back.
        /// </summary>
        [JsonPropertyName("ruleId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RuleId
        {
            get => null;    // never serialised — "rule" is the canonical key
            set { if (!string.IsNullOrWhiteSpace(value)) Rule = value!; }
        }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
