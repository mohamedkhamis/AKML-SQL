using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 040 (T152, OPT-08, data-model §1.3) — the tab-colour environments migration run by
    /// <see cref="ConfigManager.Load(string)"/>: stock rules become PRODUCTION / STAGING / DEV /
    /// AZURE with every rule linked; a label repeated with another colour becomes "Label (2)"; a
    /// second load changes nothing; no rules seeds the four defaults. Rule labels and colours are
    /// never touched (Safety and the History badge key on them). These tests read temp files
    /// through the explicit-path overload, so they never touch the real AppData folder.
    /// </summary>
    public class TabEnvironmentsMigrationTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { /* leave it for OS %TEMP% cleanup */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        private string WriteTempConfig(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), "akmlsql-tabenv-test-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, json);
            _tempFiles.Add(path);
            return path;
        }

        // The four stock rules exactly as configs written before spec 040 carry them (no
        // "environments" key, no "environment" on the rules).
        private const string StockRulesConfig = @"{
  ""tabs"": {
    ""coloringEnabled"": true,
    ""coloringRules"": [
      { ""order"": 0, ""pattern"": ""*PROD*,*LIVE*"", ""matchTarget"": ""serverName"", ""color"": ""#FF4444"", ""label"": ""PRODUCTION"" },
      { ""order"": 1, ""pattern"": ""*STG*,*UAT*,*STAGING*"", ""matchTarget"": ""serverName"", ""color"": ""#FFB800"", ""label"": ""STAGING"" },
      { ""order"": 2, ""pattern"": ""*DEV*,*LOCAL*,localhost,(local)"", ""matchTarget"": ""serverName"", ""color"": ""#44BB44"", ""label"": ""DEV"" },
      { ""order"": 3, ""pattern"": ""*.database.windows.net"", ""matchTarget"": ""serverName"", ""color"": ""#4488FF"", ""label"": ""AZURE"" }
    ]
  }
}";

        private static void AssertDefaultEnvironments(IReadOnlyList<TabEnvironment> environments)
        {
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, environments.Select(e => e.Name));
            Assert.Equal(new[] { "#FF4444", "#FFB800", "#44BB44", "#4488FF" }, environments.Select(e => e.Color));
        }

        [Fact]
        public void StockRules_BecomeTheFourDefaultEnvironments_WithEveryRuleLinked()
        {
            var settings = ConfigManager.Load(WriteTempConfig(StockRulesConfig));

            AssertDefaultEnvironments(settings.Tabs.Environments);
            Assert.Equal(4, settings.Tabs.ColoringRules.Count);
            foreach (var rule in settings.Tabs.ColoringRules)
                Assert.Equal(rule.Label, rule.Environment);
        }

        [Fact]
        public void Migration_LeavesRuleLabelsColorsAndPatternsUnchanged()
        {
            var settings = ConfigManager.Load(WriteTempConfig(StockRulesConfig));

            var rules = settings.Tabs.ColoringRules;
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, rules.Select(r => r.Label));
            Assert.Equal(new[] { "#FF4444", "#FFB800", "#44BB44", "#4488FF" }, rules.Select(r => r.Color));
            Assert.Equal(new[] { 0, 1, 2, 3 }, rules.Select(r => r.Order));
            Assert.Equal("*PROD*,*LIVE*", rules[0].Pattern);
            Assert.All(rules, r => Assert.Equal(EnvironmentMatcher.MatchTargetServerName, r.MatchTarget));
        }

        [Fact]
        public void NewAppSettings_MigratesToTheFourDefaults()
        {
            // The in-code defaults are the stock rules, so a fresh object migrates the same way.
            var settings = new AppSettings();
            Assert.Empty(settings.Tabs.Environments);

            TabEnvironmentMigration.Apply(settings.Tabs);

            AssertDefaultEnvironments(settings.Tabs.Environments);
            Assert.All(settings.Tabs.ColoringRules, r => Assert.Equal(r.Label, r.Environment));
        }

        [Fact]
        public void DuplicateLabelWithAnotherColor_BecomesLabelTwo()
        {
            const string json = @"{
  ""tabs"": {
    ""coloringRules"": [
      { ""order"": 0, ""pattern"": ""*PROD*"", ""color"": ""#FF4444"", ""label"": ""PRODUCTION"" },
      { ""order"": 1, ""pattern"": ""*PRD*"", ""color"": ""#CC0000"", ""label"": ""PRODUCTION"" },
      { ""order"": 2, ""pattern"": ""*LIVE*"", ""color"": ""#ff4444"", ""label"": ""production"" },
      { ""order"": 3, ""pattern"": ""*P2*"", ""color"": ""#CC0000"", ""label"": ""PRODUCTION"" }
    ]
  }
}";
            var settings = ConfigManager.Load(WriteTempConfig(json));

            var environments = settings.Tabs.Environments;
            Assert.Equal(new[] { "PRODUCTION", "PRODUCTION (2)" }, environments.Select(e => e.Name));
            Assert.Equal(new[] { "#FF4444", "#CC0000" }, environments.Select(e => e.Color));

            var rules = settings.Tabs.ColoringRules;
            Assert.Equal("PRODUCTION", rules[0].Environment);
            Assert.Equal("PRODUCTION (2)", rules[1].Environment);
            // Same pair ignoring case → the same environment.
            Assert.Equal("PRODUCTION", rules[2].Environment);
            // The repeated (label, colour) pair finds the suffixed environment it created.
            Assert.Equal("PRODUCTION (2)", rules[3].Environment);
            // The rules' own labels are untouched.
            Assert.Equal("PRODUCTION", rules[1].Label);
        }

        [Fact]
        public void EnvironmentsAreBuiltInRuleOrder_NotListOrder()
        {
            const string json = @"{
  ""tabs"": {
    ""coloringRules"": [
      { ""order"": 5, ""pattern"": ""*QA*"", ""color"": ""#9B59B6"", ""label"": ""QA"" },
      { ""order"": 1, ""pattern"": ""*PROD*"", ""color"": ""#FF4444"", ""label"": ""PRODUCTION"" }
    ]
  }
}";
            var settings = ConfigManager.Load(WriteTempConfig(json));

            Assert.Equal(new[] { "PRODUCTION", "QA" }, settings.Tabs.Environments.Select(e => e.Name));
        }

        [Fact]
        public void SecondLoad_IsIdempotent()
        {
            const string json = @"{
  ""tabs"": {
    ""coloringRules"": [
      { ""order"": 0, ""pattern"": ""*PROD*"", ""color"": ""#FF4444"", ""label"": ""PRODUCTION"" },
      { ""order"": 1, ""pattern"": ""*PRD*"", ""color"": ""#CC0000"", ""label"": ""PRODUCTION"" },
      { ""order"": 2, ""pattern"": ""*DEV*"", ""color"": ""#44BB44"", ""label"": ""DEV"" }
    ]
  }
}";
            var first = ConfigManager.Load(WriteTempConfig(json));

            // Running the migration again on the result changes nothing.
            var environmentsBefore = Snapshot(first.Tabs.Environments);
            var rulesBefore = Snapshot(first.Tabs.ColoringRules);
            TabEnvironmentMigration.Apply(first.Tabs);
            Assert.Equal(environmentsBefore, Snapshot(first.Tabs.Environments));
            Assert.Equal(rulesBefore, Snapshot(first.Tabs.ColoringRules));

            // And a load of the migrated config gives the same environments and links.
            var second = ConfigManager.Load(WriteTempConfig(System.Text.Json.JsonSerializer.Serialize(first)));
            Assert.Equal(environmentsBefore, Snapshot(second.Tabs.Environments));
            Assert.Equal(rulesBefore, Snapshot(second.Tabs.ColoringRules));
        }

        [Fact]
        public void NoRules_SeedsTheFourDefaults()
        {
            var settings = ConfigManager.Load(WriteTempConfig(@"{ ""tabs"": { ""coloringRules"": [] } }"));

            Assert.Empty(settings.Tabs.ColoringRules);
            AssertDefaultEnvironments(settings.Tabs.Environments);
        }

        [Fact]
        public void ExistingEnvironments_AreKeptAsTheyAre()
        {
            const string json = @"{
  ""tabs"": {
    ""environments"": [
      { ""name"": ""Production"", ""color"": ""#E91E63"" },
      { ""name"": ""Test"", ""color"": ""#00BCD4"" }
    ],
    ""coloringRules"": [
      { ""order"": 0, ""pattern"": ""*PROD*"", ""color"": ""#E91E63"", ""label"": ""Production"", ""environment"": ""Production"" }
    ]
  }
}";
            var settings = ConfigManager.Load(WriteTempConfig(json));

            Assert.Equal(new[] { "Production", "Test" }, settings.Tabs.Environments.Select(e => e.Name));
            Assert.Equal("Production", settings.Tabs.ColoringRules[0].Environment);
        }

        [Fact]
        public void UnlinkedRule_WithEnvironmentsPresent_IsLinkedToItsPair()
        {
            // A rule added by hand to a migrated config: it joins the environment of its own
            // (label, colour) pair, created when no environment has that pair.
            const string json = @"{
  ""tabs"": {
    ""environments"": [ { ""name"": ""PRODUCTION"", ""color"": ""#FF4444"" } ],
    ""coloringRules"": [
      { ""order"": 0, ""pattern"": ""*PROD*"", ""color"": ""#FF4444"", ""label"": ""PRODUCTION"" },
      { ""order"": 1, ""pattern"": ""*QA*"", ""color"": ""#9B59B6"", ""label"": ""QA"", ""environment"": ""Gone"" }
    ]
  }
}";
            var settings = ConfigManager.Load(WriteTempConfig(json));

            Assert.Equal(new[] { "PRODUCTION", "QA" }, settings.Tabs.Environments.Select(e => e.Name));
            Assert.Equal("#9B59B6", settings.Tabs.Environments[1].Color);
            Assert.Equal("PRODUCTION", settings.Tabs.ColoringRules[0].Environment);
            Assert.Equal("QA", settings.Tabs.ColoringRules[1].Environment);
        }

        [Fact]
        public void MissingFile_DefaultsAreMigrated_AndNothingIsWritten()
        {
            var path = Path.Combine(Path.GetTempPath(), "akmlsql-tabenv-missing-" + Guid.NewGuid() + ".json");

            var settings = ConfigManager.Load(path);

            AssertDefaultEnvironments(settings.Tabs.Environments);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void LoadWithPath_NeverRewritesTheFile()
        {
            var path = WriteTempConfig(StockRulesConfig);

            ConfigManager.Load(path);

            Assert.Equal(StockRulesConfig, File.ReadAllText(path));
        }

        [Fact]
        public void WriteLabelsFromEnvironments_CopiesNameAndColorOntoTheRules()
        {
            var environments = new List<TabEnvironment>
            {
                new TabEnvironment { Name = "Production", Color = "#E91E63" },
            };
            var rules = new List<ColoringRule>
            {
                new ColoringRule { Pattern = "*PROD*", Environment = "production", Label = "PRODUCTION", Color = "#FF4444" },
                new ColoringRule { Pattern = "*X*", Environment = "Missing", Label = "KEEP", Color = "#123456" },
            };

            TabEnvironmentMigration.WriteLabelsFromEnvironments(rules, environments);

            Assert.Equal("Production", rules[0].Environment);
            Assert.Equal("Production", rules[0].Label);
            Assert.Equal("#E91E63", rules[0].Color);
            // A rule whose environment doesn't exist keeps its own label and colour.
            Assert.Equal("KEEP", rules[1].Label);
            Assert.Equal("#123456", rules[1].Color);
        }

        private static List<string> Snapshot(IEnumerable<TabEnvironment> environments)
            => environments.Select(e => e.Name + "|" + e.Color).ToList();

        private static List<string> Snapshot(IEnumerable<ColoringRule> rules)
            => rules.Select(r => string.Join("|", r.Order, r.Pattern, r.MatchTarget, r.DatabaseName, r.Label, r.Color, r.Environment)).ToList();
    }

    /// <summary>
    /// Spec 040 (T152) — the same migration through the AppData overload
    /// <see cref="ConfigManager.Load()"/>, against an ISOLATED throwaway AppData root (the
    /// <c>AKML_APP_DATA_ROOT</c> override, serialised with the other real-AppData classes).
    /// </summary>
    [Collection("AkmlSql real AppData")]
    public class TabEnvironmentsMigrationAppDataTests : IDisposable
    {
        private const string AppDataRootEnvVar = "AKML_APP_DATA_ROOT";
        private readonly string? _priorRoot;
        private readonly string _tempRoot;

        public TabEnvironmentsMigrationAppDataTests()
        {
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            _tempRoot = Path.Combine(Path.GetTempPath(), "akmlsql-tabenv-appdata-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);
            try
            {
                if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException) { /* leave the temp tree for OS %TEMP% cleanup */ }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void FirstRun_WritesTheDefaultEnvironments()
        {
            var settings = ConfigManager.Load();

            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, settings.Tabs.Environments.Select(e => e.Name));
            Assert.Contains("\"environments\"", File.ReadAllText(Constants.ConfigFilePath));
        }

        [Fact]
        public void OlderConfig_Migrates_AndASaveAndReloadIsIdempotent()
        {
            // A pre-spec-040 config on disk: the stock rules without environments.
            var old = new AppSettings();
            ConfigManager.Save(old);
            Assert.DoesNotContain("PRODUCTION (2)", File.ReadAllText(Constants.ConfigFilePath));

            var first = ConfigManager.Load();
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, first.Tabs.Environments.Select(e => e.Name));
            Assert.All(first.Tabs.ColoringRules, r => Assert.Equal(r.Label, r.Environment));

            ConfigManager.Save(first);
            var second = ConfigManager.Load();
            Assert.Equal(first.Tabs.Environments.Select(e => e.Name + e.Color), second.Tabs.Environments.Select(e => e.Name + e.Color));
            Assert.Equal(first.Tabs.ColoringRules.Select(r => r.Environment), second.Tabs.ColoringRules.Select(r => r.Environment));
        }
    }
}
