#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// The anonymous install identifier sent with telemetry batches: the same
    /// <c>installId</c> GUID the config has carried since first run — every process on the
    /// install (shell, engine, updater) already shares it. Read/written as a targeted JSON
    /// edit — the same pattern the updater uses for <c>lastUpdateCheck</c> — because this runs
    /// inside <see cref="LoggerFactory.Initialize"/>, before <c>ConfigManager</c> may log, and
    /// in the trimmed updater where reflection-based serialization is unavailable.
    /// </summary>
    internal static class TelemetryIdentity
    {
        public static string GetOrCreateInstallId() => GetOrCreateInstallId(Constants.ConfigFilePath);

        /// <summary>Path-parameterized core, directly testable.</summary>
        internal static string GetOrCreateInstallId(string configPath)
        {
            try
            {
                JsonObject config;
                if (File.Exists(configPath))
                {
                    config = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject();
                    if (config["installId"]?.GetValue<string>() is { Length: > 0 } existing)
                    {
                        return existing;
                    }
                }
                else
                {
                    config = new JsonObject();
                }

                // Older configs predate installId: generate the anonymous GUID once and persist
                // it under the canonical key, exactly as a fresh AppSettings default would.
                var created = Guid.NewGuid().ToString();
                config["installId"] = created;

                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Atomic write: temp file + rename (same pattern as every other JSON write).
                var tempPath = configPath + ".tmp";
                File.WriteAllText(tempPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
#if NETSTANDARD2_0
                if (File.Exists(configPath))
                {
                    File.Replace(tempPath, configPath, null);
                }
                else
                {
                    File.Move(tempPath, configPath);
                }
#else
                File.Move(tempPath, configPath, overwrite: true);
#endif
                return created;
            }
            catch
            {
                // Telemetry must never disturb the host: if the id cannot be read or persisted
                // (locked config, read-only profile), report under an anonymous session id for
                // this run instead of failing logger initialization.
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
