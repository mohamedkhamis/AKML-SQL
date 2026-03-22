using System;
using System.IO;
using Xunit;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Tests for ConfigManager.Load() and Save().
    /// Reads/writes the real %AppData%\AKML SQL\config.json;
    /// the constructor backs up any pre-existing file and Dispose() restores it.
    /// </summary>
    public class ConfigManagerTests : IDisposable
    {
        private readonly string _configPath = Constants.ConfigFilePath;
        private readonly string _backupPath;
        private readonly bool _hadOriginal;

        public ConfigManagerTests()
        {
            // Store the backup in %TEMP% so tests that delete the config directory don't lose it
            _backupPath = Path.Combine(Path.GetTempPath(), "akmlsql-config-test-" + Guid.NewGuid() + ".bak");
            _hadOriginal = File.Exists(_configPath);
            if (_hadOriginal)
                File.Copy(_configPath, _backupPath, overwrite: true);
        }

        public void Dispose()
        {
            // Clean up any leftover .tmp file (maybe read-only from the save-fail test)
            var tmpPath = _configPath + ".tmp";
            if (File.Exists(tmpPath))
            {
                File.SetAttributes(tmpPath, FileAttributes.Normal);
                File.Delete(tmpPath);
            }

            if (_hadOriginal)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.Copy(_backupPath, _configPath, overwrite: true);
                File.Delete(_backupPath);
            }
            else if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }

        // ── Load ──────────────────────────────────────────────────────────────

        [Fact]
        public void Load_WhenFileAbsent_CreatesDefaultsAndSavesFile()
        {
            if (File.Exists(_configPath)) File.Delete(_configPath);

            var s = ConfigManager.Load();

            Assert.NotNull(s);
            Assert.Equal(1, s.ConfigVersion);
            Assert.True(s.AutoUpdateEnabled);
            Assert.True(File.Exists(_configPath));  // Save() was called internally
        }

        [Fact]
        public void Load_WhenFilePresent_ReturnsDeserializedSettings()
        {
            var expected = new AppSettings { AutoUpdateEnabled = false, TelemetryEnabled = true, InstallId = "known" };
            ConfigManager.Save(expected);

            var actual = ConfigManager.Load();

            Assert.NotNull(actual);
            Assert.False(actual.AutoUpdateEnabled);
            Assert.True(actual.TelemetryEnabled);
            Assert.Equal("known", actual.InstallId);
        }

        [Fact]
        public void Load_WhenJsonIsMalformed_ReturnsDefaults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, "{ INVALID }");

            var s = ConfigManager.Load();

            Assert.NotNull(s);
            Assert.Equal(1, s.ConfigVersion);   // default
        }

        [Fact]
        public void Load_WhenJsonIsNull_ReturnsNewSettings()
        {
            // JsonSerializer.Deserialize<AppSettings>("null") returns null for a class
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, "null");

            var s = ConfigManager.Load();

            Assert.NotNull(s);
        }

        // ── Save ──────────────────────────────────────────────────────────────

        [Fact]
        public void Save_WritesValidJsonToDisk()
        {
            var original = new AppSettings { InstallId = "save-test", TelemetryEnabled = true };
            ConfigManager.Save(original);

            Assert.True(File.Exists(_configPath));
            var json = File.ReadAllText(_configPath);
            Assert.Contains("save-test", json);
        }

        [Fact]
        public void Save_CreatesDirectoryIfMissing()
        {
            var dir = Path.GetDirectoryName(_configPath)!;
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            ConfigManager.Save(new AppSettings());

            Assert.True(File.Exists(_configPath));
        }

        [Fact]
        public void Save_WhenWriteFails_DoesNotThrow()
        {
            // Pre-create a read-only .tmp file so File.WriteAllText(tempPath) throws
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            var tmpPath = _configPath + ".tmp";
            File.WriteAllText(tmpPath, "x");
            File.SetAttributes(tmpPath, FileAttributes.ReadOnly);

            // ConfigManager.Save catches the exception internally — must not propagate
            ConfigManager.Save(new AppSettings());
        }

        // ── Round-trip ────────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_SaveThenLoad_PreservesAllSettings()
        {
            var original = new AppSettings
            {
                AutoUpdateEnabled = false,
                TelemetryEnabled = true,
                InstallId = "rt-test",
                NativeIntelliSensePrompted = true,
                DisabledNativeIntelliSense = true,
                Formatter =
                {
                    ActiveProfile = "MyProfile"
                },
                Snippets =
                {
                    TriggerKey = "Space"
                },
                IntelliSense =
                {
                    KeywordCase = KeywordCaseOption.Lower
                }
            };

            ConfigManager.Save(original);
            var loaded = ConfigManager.Load();

            Assert.False(loaded.AutoUpdateEnabled);
            Assert.True(loaded.TelemetryEnabled);
            Assert.Equal("rt-test", loaded.InstallId);
            Assert.True(loaded.NativeIntelliSensePrompted);
            Assert.True(loaded.DisabledNativeIntelliSense);
            Assert.Equal("MyProfile", loaded.Formatter.ActiveProfile);
            Assert.Equal("Space", loaded.Snippets.TriggerKey);
            Assert.Equal(KeywordCaseOption.Lower, loaded.IntelliSense.KeywordCase);
        }
    }
}
