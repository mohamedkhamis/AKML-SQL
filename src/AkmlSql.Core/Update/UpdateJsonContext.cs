using System.Text.Json.Serialization;

namespace AkmlSql.Core.Update
{
    /// <summary>
    /// Source-generated serializer metadata for the update-channel types. The updater publishes
    /// trimmed (<c>PublishTrimmed</c>), where reflection-based System.Text.Json is disabled —
    /// the shipped builds' JSON calls threw on that path (spec 036 research R10: no build ever
    /// checked successfully). Both serializer consumers on the update path route through this
    /// context so the single-file updater works.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateManifest))]
    [JsonSerializable(typeof(UpdateResult))]
    public partial class UpdateJsonContext : JsonSerializerContext
    {
    }
}
