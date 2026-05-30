using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Reads and writes the AKML SQL configuration file (<c>%AppData%\AKML SQL\config.json</c>).
    /// Writes are performed atomically via a temp-file + rename pattern to prevent partial-write corruption.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Required for .NET 10 trimmed apps where reflection-based serialization is disabled
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        /// <summary>
        /// Loads <see cref="AppSettings"/> from disk. Creates and saves a default configuration
        /// file if none exists. Returns default settings on any read or parse failure.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                var path = Constants.ConfigFilePath;
                if (!File.Exists(path))
                {
                    Log.Information("No config file found at {Path}, creating defaults", path);
                    var defaults = new AppSettings();
                    Save(defaults);
                    return defaults;
                }

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load config, using defaults");
                return new AppSettings();
            }
        }

        /// <summary>
        /// Spec 026 (M4 closure) C2. Loads <see cref="AppSettings"/> from an explicit path. The
        /// web-edition engine service is launched with <c>--config %CommonAppData%\AKML SQL Web\config.json</c>
        /// and must read exactly that file. Unlike <see cref="Load()"/> this does NOT create or write a
        /// default file when the path is missing — the web service must never silently fall back to (or
        /// materialise) the per-user IDE-plugin config. Returns defaults on any read/parse failure; a
        /// null or blank path defers to <see cref="Load()"/>.
        /// </summary>
        public static AppSettings Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Load();
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warning("Config file not found at {Path}, using defaults", path);
                    return new AppSettings();
                }

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load config from {Path}, using defaults", path);
                return new AppSettings();
            }
        }

        /// <summary>
        /// Persists <paramref name="settings"/> to disk atomically.
        /// On .NET Standard 2.0 uses <c>File.Replace</c>; on .NET 10+ uses <c>File.Move(overwrite:true)</c>.
        /// Silently logs and swallows I/O exceptions so callers never receive a save-related exception.
        /// </summary>
        public static void Save(AppSettings settings)
        {
            try
            {
                var path = Constants.ConfigFilePath;
                var directory = Path.GetDirectoryName(path);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, SerializerOptions);

                // Atomic write: write to temp file then rename
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
                // File.Replace is atomic on NTFS (avoids TOCTOU race between Delete + Move)
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
#else
                File.Move(tempPath, path, overwrite: true);
#endif
                Log.Debug("Config saved to {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save config");
            }
        }
    }
}
