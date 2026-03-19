using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Core.Config
{
    public static class ConfigManager
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

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

        public static void Save(AppSettings settings)
        {
            try
            {
                var path = Constants.ConfigFilePath;
                var directory = Path.GetDirectoryName(path);
                if (directory != null)
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(settings, SerializerOptions);

                // Atomic write: write to temp file then rename
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
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
