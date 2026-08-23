using System;
using System.IO;
using Xunit;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Tests for ConfigManager.Load() and Save().
    /// Each test runs against an ISOLATED throwaway AppData root (via the <c>AKML_APP_DATA_ROOT</c>
    /// override) so it never touches — or destroys — the real <c>%AppData%\AKML SQL</c> directory.
    /// That directory holds product-owned state (sqlhistory.db, logs) that a running SSMS/VS/engine
    /// process keeps locked; the previous fixture read/wrote the real path and
    /// <see cref="Save_CreatesDirectoryIfMissing"/> recursively deleted it, so it threw
    /// <c>IOException: sqlhistory.db is being used by another process</c> (and wiped real history)
    /// on any machine where the product was running.
    /// The <c>[Collection]</c> serialises the process-global env-var override against the other
    /// classes that resolve Constants AppData paths (<c>SqlCredentialStoreTests</c>,
    /// <c>ConstantsTests</c>) so it can never leak into a parallel test.
    /// </summary>
    [Collection("AkmlSql real AppData")]
    public class ConfigManagerTests : IDisposable
    {
        private const string AppDataRootEnvVar = "AKML_APP_DATA_ROOT";
        private readonly string? _priorRoot;
        private readonly string _tempRoot;
        private readonly string _configPath;

        public ConfigManagerTests()
        {
            // Redirect AKML's AppData resolution to a throwaway temp tree BEFORE reading any
            // Constants path, so ConfigManager.Save/Load operate on isolated files we fully own.
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            _tempRoot = Path.Combine(Path.GetTempPath(), "akmlsql-config-test-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _tempRoot);
            _configPath = Constants.ConfigFilePath;   // now resolves under _tempRoot\AKML SQL
        }

        public void Dispose()
        {
            // Restore the environment FIRST so a leaked override can never poison a later test.
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);

            // Best-effort teardown of the throwaway tree. The write-fail test leaves a read-only
            // .tmp, so clear attributes before the recursive delete.
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch (IOException) { /* leave the temp tree for OS %TEMP% cleanup */ }
            catch (UnauthorizedAccessException) { }
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
