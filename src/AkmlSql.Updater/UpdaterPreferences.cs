using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace AkmlSql.Updater
{
    /// <summary>
    /// The two settings the updater and the installer care about, read from and written to
    /// <c>config.json</c> with <see cref="JsonNode"/> — this exe is trimmed, so the reflection-based
    /// AppSettings round trip is unavailable. Every other key in the file passes through untouched.
    /// </summary>
    internal sealed class UpdaterPreferences
    {
        internal const string AutoUpdateKey = "autoUpdateEnabled";
        internal const string ErrorReportsKey = "telemetryEnabled";
        internal const string LastCheckKey = "lastUpdateCheck";

        /// <summary>Defaults match <c>AppSettings</c>: both on when the file does not say.</summary>
        public bool AutoUpdateEnabled { get; private init; } = true;

        public bool ErrorReportsEnabled { get; private init; } = true;

        public DateTimeOffset? LastUpdateCheck { get; private init; }

        public static UpdaterPreferences Read(string configPath)
        {
            try
            {
                if (!File.Exists(configPath) || JsonNode.Parse(File.ReadAllText(configPath)) is not JsonObject config)
                {
                    return new UpdaterPreferences();
                }

                return new UpdaterPreferences
                {
                    AutoUpdateEnabled = ReadBool(config, AutoUpdateKey) ?? true,
                    ErrorReportsEnabled = ReadBool(config, ErrorReportsKey) ?? true,
                    LastUpdateCheck = config[LastCheckKey] is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                            ? parsed
                            : null,
                };
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not read {Path}; using defaults", configPath);
                return new UpdaterPreferences();
            }
        }

        /// <summary>
        /// Sets the given keys (and only those) in <paramref name="configPath"/>, creating the file
        /// when it does not exist. Written atomically: temp file + rename.
        /// </summary>
        public static void Write(string configPath, IReadOnlyDictionary<string, bool> values)
        {
            JsonObject config;
            if (File.Exists(configPath))
            {
                config = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                    ?? throw new InvalidDataException($"{configPath} is not a JSON object.");
            }
            else
            {
                config = new JsonObject { ["configVersion"] = 1 };
            }

            foreach (var (key, value) in values)
            {
                config[key] = value;
            }

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = configPath + ".tmp";
            File.WriteAllText(tempPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, configPath, overwrite: true);
        }

        /// <summary>Stamps <c>lastUpdateCheck</c> (the same key the IDE's 24-hour throttle reads).</summary>
        public static void StampLastCheck(string configPath, DateTimeOffset when)
        {
            try
            {
                var config = File.Exists(configPath)
                    ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject()
                    : new JsonObject();
                config[LastCheckKey] = when.ToString("O", CultureInfo.InvariantCulture);

                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = configPath + ".tmp";
                File.WriteAllText(tempPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempPath, configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update last check timestamp");
            }
        }

        /// <summary>
        /// Parses <c>--configure</c> arguments: <c>auto-update=on|off</c>, <c>error-reports=on|off</c>.
        /// Returns null (and the reason) for anything else, so a typo in the installer fails loudly
        /// in its log instead of silently writing nothing.
        /// </summary>
        public static Dictionary<string, bool>? ParseConfigureArgs(IEnumerable<string> args, out string? error)
        {
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var arg in args)
            {
                var parts = arg.TrimStart('-').Split('=', 2);
                var setting = parts[0].ToLowerInvariant() switch
                {
                    "auto-update" => AutoUpdateKey,
                    "error-reports" => ErrorReportsKey,
                    _ => null,
                };
                bool? on = parts.Length == 2
                    ? parts[1].ToLowerInvariant() switch
                    {
                        "on" or "true" or "1" => true,
                        "off" or "false" or "0" => false,
                        _ => null,
                    }
                    : null;

                if (setting is null || on is null)
                {
                    error = $"Unrecognised setting '{arg}'. Use auto-update=on|off and error-reports=on|off.";
                    return null;
                }

                values[setting] = on.Value;
            }

            if (values.Count == 0)
            {
                error = "Nothing to configure. Use auto-update=on|off and/or error-reports=on|off.";
                return null;
            }

            error = null;
            return values;
        }

        private static bool? ReadBool(JsonObject config, string key) =>
            config[key] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;
    }
}
