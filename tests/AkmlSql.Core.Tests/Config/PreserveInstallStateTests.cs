using System;
using System.Collections.Generic;
using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 040 (T009, OPT-03, FR-007) — "Restore all defaults" and Import keep the installation's
    /// identity and first-run state, and nothing else.
    /// </summary>
    public class PreserveInstallStateTests
    {
        private static AppSettings Source()
        {
            var from = new AppSettings
            {
                ConfigVersion = 7,
                InstallId = "install-123",
                LastUpdateCheck = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
                NativeIntelliSensePrompted = true,
                DisabledNativeIntelliSense = true,
                Theme = "dark",
            };
            from.InstalledTargets.Add(new InstalledTarget { IdeType = "SSMS22", Version = "22.0", Architecture = "x64" });
            from.CommandPalette.UsageCounts["format"] = 5;
            from.CommandPalette.RecentItems.Add("format");
            from.IntelliSense.MaxSuggestions = 99;
            return from;
        }

        [Fact]
        public void Copies_every_install_field()
        {
            var from = Source();
            var to = new AppSettings();

            ConfigManager.PreserveInstallState(from, to);

            Assert.Equal(7, to.ConfigVersion);
            Assert.Equal("install-123", to.InstallId);
            Assert.Equal(from.LastUpdateCheck, to.LastUpdateCheck);
            Assert.True(to.NativeIntelliSensePrompted);
            Assert.True(to.DisabledNativeIntelliSense);
            Assert.Equal("SSMS22", Assert.Single(to.InstalledTargets).IdeType);
            Assert.Equal(5, to.CommandPalette.UsageCounts["format"]);
            Assert.Equal(new List<string> { "format" }, to.CommandPalette.RecentItems);
        }

        [Fact]
        public void Copies_nothing_else()
        {
            var from = Source();
            var to = new AppSettings();
            var before = JsonSerializer.Serialize(to);

            ConfigManager.PreserveInstallState(from, to);

            Assert.Equal("light", to.Theme);
            Assert.Equal(new AppSettings().IntelliSense.MaxSuggestions, to.IntelliSense.MaxSuggestions);

            // Put the eight preserved fields back and the rest must be byte-identical.
            var fresh = JsonSerializer.Deserialize<AppSettings>(before)!;
            to.ConfigVersion = fresh.ConfigVersion;
            to.InstallId = fresh.InstallId;
            to.LastUpdateCheck = fresh.LastUpdateCheck;
            to.NativeIntelliSensePrompted = fresh.NativeIntelliSensePrompted;
            to.DisabledNativeIntelliSense = fresh.DisabledNativeIntelliSense;
            to.InstalledTargets = fresh.InstalledTargets;
            to.CommandPalette.UsageCounts = fresh.CommandPalette.UsageCounts;
            to.CommandPalette.RecentItems = fresh.CommandPalette.RecentItems;
            Assert.Equal(before, JsonSerializer.Serialize(to));
        }

        [Fact]
        public void Copies_are_independent_of_the_source()
        {
            var from = Source();
            var to = new AppSettings();

            ConfigManager.PreserveInstallState(from, to);
            from.CommandPalette.RecentItems.Add("later");
            from.InstalledTargets.Clear();

            Assert.Single(to.CommandPalette.RecentItems);
            Assert.Single(to.InstalledTargets);
        }
    }
}
