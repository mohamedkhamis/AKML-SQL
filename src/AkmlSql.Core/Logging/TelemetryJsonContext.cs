using System.Text.Json.Serialization;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// Source-generated serializer metadata for the telemetry payload. The updater (which also
    /// hosts this sink via <see cref="LoggerFactory"/>) publishes trimmed, where
    /// reflection-based System.Text.Json is disabled — every JSON call must route through a
    /// generated context.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(TelemetryBatch))]
    internal partial class TelemetryJsonContext : JsonSerializerContext
    {
    }
}
