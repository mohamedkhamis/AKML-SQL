using System.Text.Json.Serialization;

namespace AkmlSql.Engine.Snippets.Models;

public class SnippetMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("shortcode")]
    public string Shortcode { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("created")]
    public DateTime Created { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Custom";

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("context")]
    public string[] Context { get; set; } = ["global"];

    [JsonPropertyName("surroundsWith")]
    public bool SurroundsWith { get; set; }
}
