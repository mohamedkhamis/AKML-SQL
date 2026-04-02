using System.Text.Json.Serialization;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Metadata block for a formatting profile (.akmlstyle file).
/// </summary>
public class ProfileMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("created")]
    public DateTime Created { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("basedOn")]
    public string? BasedOn { get; set; }

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// When true, the formatter skips semantic validation after formatting.
    /// Use in tests or internal pipelines where validation is handled externally.
    /// </summary>
    [JsonPropertyName("skipValidation")]
    public bool SkipValidation { get; set; }

    /// <summary>
    /// When true (default), the formatter runs a second pass to verify idempotency.
    /// Disable for bulk/background formatting where the extra parse is undesirable.
    /// </summary>
    [JsonPropertyName("enableIdempotencyCheck")]
    public bool EnableIdempotencyCheck { get; set; } = true;
}
