using System.Text.Json.Serialization;

namespace AkmlSql.Engine.Snippets.Models;

public class Snippet
{
    [JsonPropertyName("metadata")]
    public SnippetMetadata Metadata { get; set; } = new();

    [JsonPropertyName("variables")]
    public SnippetVariable[] Variables { get; set; } = [];

    [JsonPropertyName("body")]
    public string[] Body { get; set; } = [];
}
