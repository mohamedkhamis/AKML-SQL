using System;
using Xunit;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Tests.Config
{
    public class AppSettingsTests
    {
        // ── AppSettings ────────────────────────────────────────────────────────

        [Fact]
        public void AppSettings_Defaults()
        {
            var s = new AppSettings();
            Assert.Equal(1, s.ConfigVersion);
            Assert.True(s.AutoUpdateEnabled);
            Assert.False(s.TelemetryEnabled);
            Assert.Null(s.LastUpdateCheck);
            Assert.NotNull(s.InstallId);
            Assert.NotEmpty(s.InstallId);
            Assert.NotNull(s.InstalledTargets);
            Assert.Empty(s.InstalledTargets);
            Assert.NotNull(s.IntelliSense);
            Assert.NotNull(s.Cache);
            Assert.NotNull(s.Formatter);
            Assert.NotNull(s.Snippets);
            Assert.False(s.NativeIntelliSensePrompted);
            Assert.False(s.DisabledNativeIntelliSense);
        }

        [Fact]
        public void AppSettings_Mutations()
        {
            var now = DateTimeOffset.UtcNow;
            var s = new AppSettings
            {
                ConfigVersion = 2,
                AutoUpdateEnabled = false,
                TelemetryEnabled = true,
                LastUpdateCheck = now,
                InstallId = "test-id",
                InstalledTargets = [],
                IntelliSense = new IntelliSenseSettings(),
                Cache = new CacheSettings(),
                Formatter = new FormatterSettings(),
                Snippets = new SnippetSettings(),
                NativeIntelliSensePrompted = true,
                DisabledNativeIntelliSense = true
            };
            Assert.Equal(2, s.ConfigVersion);
            Assert.False(s.AutoUpdateEnabled);
            Assert.True(s.TelemetryEnabled);
            Assert.Equal(now, s.LastUpdateCheck);
            Assert.Equal("test-id", s.InstallId);
            Assert.NotNull(s.InstalledTargets);
            Assert.True(s.NativeIntelliSensePrompted);
            Assert.True(s.DisabledNativeIntelliSense);
        }

        // ── IntelliSenseSettings ───────────────────────────────────────────────

        [Fact]
        public void IntelliSenseSettings_Defaults()
        {
            var s = new IntelliSenseSettings();
            Assert.True(s.Enabled);
            Assert.True(s.AutoTrigger);
            Assert.Equal(100, s.TriggerDelayMs);
            Assert.True(s.AfterDot);
            Assert.Equal(50, s.MaxSuggestions);
            Assert.True(s.FuzzyMatch);
            Assert.True(s.ShowDataTypes);
            Assert.True(s.ShowNullability);
            Assert.True(s.ShowPkFk);
            Assert.True(s.AutoAlias);
            Assert.True(s.JoinAssist);
            Assert.Equal(KeywordCaseOption.Upper, s.KeywordCase);
            Assert.True(s.DisableNativeIntelliSense);
        }

        [Fact]
        public void IntelliSenseSettings_Mutations()
        {
            var s = new IntelliSenseSettings
            {
                Enabled = false,
                AutoTrigger = false,
                TriggerDelayMs = 250,
                AfterDot = false,
                MaxSuggestions = 25,
                FuzzyMatch = false,
                ShowDataTypes = false,
                ShowNullability = false,
                ShowPkFk = false,
                AutoAlias = false,
                JoinAssist = false,
                KeywordCase = KeywordCaseOption.Lower,
                DisableNativeIntelliSense = false
            };
            Assert.False(s.Enabled);
            Assert.Equal(250, s.TriggerDelayMs);
            Assert.Equal(25, s.MaxSuggestions);
            Assert.Equal(KeywordCaseOption.Lower, s.KeywordCase);
            Assert.False(s.DisableNativeIntelliSense);
        }

        // ── KeywordCaseOption enum ─────────────────────────────────────────────

        [Fact]
        public void KeywordCaseOption_AllValues()
        {
            Assert.Equal(0, (int)KeywordCaseOption.Upper);
            Assert.Equal(1, (int)KeywordCaseOption.Lower);
            Assert.Equal(2, (int)KeywordCaseOption.PascalCase);
            Assert.Equal(3, (int)KeywordCaseOption.AsIs);
        }

        // ── CacheSettings ─────────────────────────────────────────────────────

        [Fact]
        public void CacheSettings_Defaults()
        {
            var s = new CacheSettings();
            Assert.True(s.AutoRefresh);
            Assert.Equal(300, s.RefreshIntervalSeconds);
            Assert.True(s.DetectDdl);
            Assert.Equal(10, s.MaxDatabases);
            Assert.True(s.LazyLoadColumns);
            Assert.True(s.PersistToDisk);
            Assert.Equal(string.Empty, s.PersistPath);
        }

        [Fact]
        public void CacheSettings_Mutations()
        {
            var s = new CacheSettings
            {
                AutoRefresh = false,
                RefreshIntervalSeconds = 600,
                DetectDdl = false,
                MaxDatabases = 20,
                LazyLoadColumns = false,
                PersistToDisk = false,
                PersistPath = @"C:\cache"
            };
            Assert.False(s.AutoRefresh);
            Assert.Equal(600, s.RefreshIntervalSeconds);
            Assert.False(s.DetectDdl);
            Assert.Equal(20, s.MaxDatabases);
            Assert.False(s.LazyLoadColumns);
            Assert.False(s.PersistToDisk);
            Assert.Equal(@"C:\cache", s.PersistPath);
        }

        // ── InstalledTarget ────────────────────────────────────────────────────

        [Fact]
        public void InstalledTarget_Defaults()
        {
            var t = new InstalledTarget();
            Assert.Equal(string.Empty, t.IdeType);
            Assert.Equal(string.Empty, t.Version);
            Assert.Equal(string.Empty, t.Architecture);
            Assert.Equal(string.Empty, t.ExtensionsPath);
            Assert.Equal(default(DateTimeOffset), t.InstalledAt);
        }

        [Fact]
        public void InstalledTarget_Mutations()
        {
            var now = DateTimeOffset.UtcNow;
            var t = new InstalledTarget
            {
                IdeType = "SSMS22",
                Version = "22",
                Architecture = "x64",
                ExtensionsPath = @"C:\ext",
                InstalledAt = now
            };
            Assert.Equal("SSMS22", t.IdeType);
            Assert.Equal("22", t.Version);
            Assert.Equal("x64", t.Architecture);
            Assert.Equal(@"C:\ext", t.ExtensionsPath);
            Assert.Equal(now, t.InstalledAt);
        }

        // ── FormatterSettings ─────────────────────────────────────────────────

        [Fact]
        public void FormatterSettings_Defaults()
        {
            var s = new FormatterSettings();
            Assert.True(s.Enabled);
            Assert.Equal("Default", s.ActiveProfile);
            Assert.False(s.FormatOnPaste);
            Assert.False(s.FormatOnSave);
            Assert.False(s.FormatOnDelimiter);
            Assert.Equal("Ctrl+K, Y", s.ShortcutKey);
            Assert.True(s.ShowProfileInStatusBar);
            Assert.True(s.ConfirmBulkFormat);
            Assert.True(s.CreateBackups);
            Assert.True(s.RespectNoformat);
            Assert.True(s.HandleParseErrors);
            Assert.True(s.SemanticValidation);
        }

        [Fact]
        public void FormatterSettings_Mutations()
        {
            var s = new FormatterSettings
            {
                Enabled = false,
                ActiveProfile = "Custom",
                FormatOnPaste = true,
                FormatOnSave = true,
                FormatOnDelimiter = true,
                ShortcutKey = "Ctrl+F",
                ShowProfileInStatusBar = false,
                ConfirmBulkFormat = false,
                CreateBackups = false,
                RespectNoformat = false,
                HandleParseErrors = false,
                SemanticValidation = false
            };
            Assert.False(s.Enabled);
            Assert.Equal("Custom", s.ActiveProfile);
            Assert.True(s.FormatOnPaste);
            Assert.True(s.FormatOnSave);
            Assert.True(s.FormatOnDelimiter);
            Assert.Equal("Ctrl+F", s.ShortcutKey);
            Assert.False(s.ShowProfileInStatusBar);
            Assert.False(s.SemanticValidation);
        }

        // ── SnippetSettings ───────────────────────────────────────────────────

        [Fact]
        public void SnippetSettings_Defaults()
        {
            var s = new SnippetSettings();
            Assert.True(s.Enabled);
            Assert.True(s.ShowInCompletion);
            Assert.Equal("Tab", s.TriggerKey);
            Assert.True(s.FormatOnExpand);
            Assert.Equal(string.Empty, s.PersonalFolder);
            Assert.Equal(string.Empty, s.TeamFolder);
            Assert.True(s.ContextFilter);
            Assert.Equal("Ctrl+K, Ctrl+S", s.SurroundShortcut);
            Assert.True(s.TrackUsage);
        }

        [Fact]
        public void SnippetSettings_Mutations()
        {
            var s = new SnippetSettings
            {
                Enabled = false,
                ShowInCompletion = false,
                TriggerKey = "Space",
                FormatOnExpand = false,
                PersonalFolder = @"C:\snip",
                TeamFolder = @"\\srv\snip",
                ContextFilter = false,
                SurroundShortcut = "Ctrl+W",
                TrackUsage = false
            };
            Assert.False(s.Enabled);
            Assert.Equal("Space", s.TriggerKey);
            Assert.Equal(@"C:\snip", s.PersonalFolder);
            Assert.Equal(@"\\srv\snip", s.TeamFolder);
            Assert.False(s.TrackUsage);
        }
    }
}
