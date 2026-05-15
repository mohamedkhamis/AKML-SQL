using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Spec 020 (US1, FR-030) — one-time theme migration on first launch with the new
    /// <c>ThemeTokens</c> / <c>ThemeRegistry</c> token system. Writes a marker file at
    /// <c>%AppData%/AKML SQL/themeMigration.v1.json</c> on first run; subsequent runs short-circuit.
    ///
    /// <para>
    /// If the user has any legacy colour customisations in config (currently no such field exists,
    /// but the hook is here so the spec can extend it without re-shipping infrastructure), those
    /// customisations take precedence over the new defaults and <see cref="PendingNoticeAvailable"/>
    /// becomes <c>true</c>. Consumers (Options dialog, status bar) may surface a one-time notice;
    /// when they do, they call <see cref="AcknowledgeNotice"/> to suppress it on later opens.
    /// </para>
    ///
    /// <para>
    /// Marker write uses the temp-file + rename pattern (CLAUDE.md "Atomic Config Writes" rule).
    /// Failures are non-fatal — the migration is best-effort and must never block extension startup.
    /// </para>
    /// </summary>
    public sealed class ThemeMigrationManager
    {
        private static readonly Lazy<ThemeMigrationManager> _lazy =
            new Lazy<ThemeMigrationManager>(() => new ThemeMigrationManager());

        public static ThemeMigrationManager Instance => _lazy.Value;

        private ThemeMigrationManager() { }

        /// <summary>
        /// <c>true</c> after <see cref="RunIfNeeded"/> detected legacy customisations on first
        /// launch. Consumers should display a one-time notice and then call
        /// <see cref="AcknowledgeNotice"/>.
        /// </summary>
        public bool PendingNoticeAvailable { get; private set; }

        /// <summary>
        /// Idempotent. On first call (marker file absent), writes the marker; on subsequent
        /// calls it short-circuits. Never throws — every failure is caught and logged.
        /// </summary>
        public void RunIfNeeded()
        {
            try
            {
                var configDir = GetConfigDir();
                Directory.CreateDirectory(configDir);

                var markerPath = Path.Combine(configDir, "themeMigration.v1.json");
                if (File.Exists(markerPath))
                {
                    return;
                }

                var hadOverrides = DetectLegacyOverrides(configDir);

                var record = new MigrationRecord
                {
                    MigratedAt = DateTimeOffset.UtcNow,
                    HadLegacyOverrides = hadOverrides,
                    SchemaVersion = 1,
                };

                WriteAtomic(markerPath, JsonSerializer.Serialize(record, JsonOptions));

                if (hadOverrides)
                {
                    PendingNoticeAvailable = true;
                }
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "Theme migration marker write failed (non-fatal)"); } catch { /* logger may not be initialized */ }
            }
        }

        /// <summary>
        /// Marks the pending notice as displayed. Subsequent gets of
        /// <see cref="PendingNoticeAvailable"/> return <c>false</c>.
        /// </summary>
        public void AcknowledgeNotice()
        {
            PendingNoticeAvailable = false;
        }

        // -------------------------------------------------------------------

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static string GetConfigDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AKML SQL");
        }

        /// <summary>
        /// Detects whether the user's existing config carries any legacy colour-override fields.
        /// Currently always returns <c>false</c> — there is no <c>legacyColorOverrides</c> section
        /// in <c>AppSettings</c> yet. This method is the extension point: when a future spec adds
        /// such a section (or detects user-customised palette via some other means), add the
        /// detection logic here. Keeping the hook present means consumers don't have to be
        /// updated when detection is wired up.
        /// </summary>
        private static bool DetectLegacyOverrides(string configDir)
        {
            // Probe config.json for an "legacyColorOverrides" key without fully deserialising,
            // so the contract stays decoupled from AppSettings shape.
            try
            {
                var configPath = Path.Combine(configDir, "config.json");
                if (!File.Exists(configPath)) return false;

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("legacyColorOverrides", out var el)
                       && el.ValueKind == JsonValueKind.Object
                       && el.EnumerateObject().MoveNext();
            }
            catch
            {
                return false;
            }
        }

        private static void WriteAtomic(string finalPath, string content)
        {
            var tmpPath = finalPath + ".tmp";
            try { File.Delete(tmpPath); } catch { /* tmp didn't exist */ }
            File.WriteAllText(tmpPath, content);
            File.Move(tmpPath, finalPath);
        }

        private sealed class MigrationRecord
        {
            [JsonPropertyName("migratedAt")]
            public DateTimeOffset MigratedAt { get; set; }

            [JsonPropertyName("hadLegacyOverrides")]
            public bool HadLegacyOverrides { get; set; }

            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; }
        }
    }
}
