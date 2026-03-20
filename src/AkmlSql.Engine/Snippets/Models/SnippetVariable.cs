using System.Text.Json.Serialization;

namespace AkmlSql.Engine.Snippets.Models;

public class SnippetVariable
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("default")]
    public string Default { get; set; } = string.Empty;

    [JsonPropertyName("tooltip")]
    public string Tooltip { get; set; } = string.Empty;

    [JsonPropertyName("schemaAware")]
    public string? SchemaAware { get; set; }
}
