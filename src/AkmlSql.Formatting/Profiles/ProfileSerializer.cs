using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Source-generated JSON serializer context for <see cref="FormattingProfile"/>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FormattingProfile))]
internal partial class ProfileJsonContext : JsonSerializerContext;

/// <summary>
/// Serializes and deserializes <see cref="FormattingProfile"/> instances to/from JSON (.akmlstyle files).
/// </summary>
public static class ProfileSerializer
{
    /// <summary>
    /// Serializes a <see cref="FormattingProfile"/> to indented JSON.
    /// </summary>
    public static string Serialize(FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Metadata.Modified = DateTime.UtcNow;
        return JsonSerializer.Serialize(profile, ProfileJsonContext.Default.FormattingProfile);
    }

    /// <summary>
    /// Current schema version supported by this implementation.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Deserializes a <see cref="FormattingProfile"/> from JSON.
    /// Warns if the schema version is newer than supported.
    /// </summary>
    public static FormattingProfile Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var profile = JsonSerializer.Deserialize(json, ProfileJsonContext.Default.FormattingProfile)
               ?? throw new JsonException("Failed to deserialize formatting profile: result was null.");

        if (profile.Metadata.SchemaVersion > CurrentSchemaVersion)
        {
            // Forward compatibility: load with best-effort, unknown fields preserved via JsonExtensionData
            System.Diagnostics.Trace.TraceWarning(
                $"Profile '{profile.Metadata.Name}' has schema version {profile.Metadata.SchemaVersion} " +
                $"(current: {CurrentSchemaVersion}). Some options may not be recognized.");
        }

        return profile;
    }
}
