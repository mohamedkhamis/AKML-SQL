using System;
using System.IO;
using System.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// "Disable rule {id} globally" must persist through config.json ruleOverrides — the store
    /// the engine actually reads. The old implementation wrote %AppData%\AKML SQL\.casettings,
    /// which CaSettingsLoader never loads (it only searches upward from the document folder),
    /// so the lightbulb action silently did nothing across restarts.
    /// Runs against an isolated AKML_APP_DATA_ROOT so the real user config is never touched.
    /// </summary>
    public sealed class DisableRuleFixActionTests : IDisposable
    {
        private const string AppDataRootEnvVar = "AKML_APP_DATA_ROOT";
        private readonly string _priorRoot;
        private readonly string _tempRoot;

        public DisableRuleFixActionTests()
        {
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            _tempRoot = Path.Combine(Path.GetTempPath(), "akmlsql-disablerule-test-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void Invoke_persists_the_disable_into_config_ruleOverrides()
        {
            new DisableRuleGloballyFixAction("PE002").Invoke(CancellationToken.None);

            var settings = ConfigManager.Load();
            Assert.True(settings.CodeAnalysis.RuleOverrides.TryGetValue("PE002", out var o));
            Assert.False(o.Enabled);
        }

        [Fact]
        public void Invoke_preserves_an_existing_severity_override()
        {
            var pre = ConfigManager.Load();
            pre.CodeAnalysis.RuleOverrides["ST001"] = new RuleOverride { Enabled = true, Severity = "error" };
            ConfigManager.Save(pre);

            new DisableRuleGloballyFixAction("ST001").Invoke(CancellationToken.None);

            var post = ConfigManager.Load();
            Assert.False(post.CodeAnalysis.RuleOverrides["ST001"].Enabled);
            Assert.Equal("error", post.CodeAnalysis.RuleOverrides["ST001"].Severity);
        }
    }
}
