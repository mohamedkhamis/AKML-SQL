using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Analysis
{
    public class CaSettings
    {
        [JsonPropertyName("metadata")]
        public CaSettingsMetadata Metadata { get; set; } = new();

        [JsonPropertyName("rules")]
        public Dictionary<string, RuleConfig> Rules { get; set; } = new();

        [JsonPropertyName("globalSuppressions")]
        public List<GlobalSuppression> GlobalSuppressions { get; set; } = [];
    }

    public class CaSettingsMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }
}
