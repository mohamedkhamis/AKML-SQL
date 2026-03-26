using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Analysis
{
    public class GlobalSuppression
    {
        [JsonPropertyName("rule")]
        public string Rule { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
