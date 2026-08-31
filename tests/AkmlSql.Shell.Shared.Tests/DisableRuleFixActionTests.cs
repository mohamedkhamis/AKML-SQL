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
    [Collection("AkmlSql AppData isolation")]
    public sealed class DisableRuleFixActionTests : AppDataIsolatedTest
    {
        public DisableRuleFixActionTests() : base("akmlsql-disablerule-test-") { }

        // The buffer is only used to re-run analysis so the squiggles clear immediately; the
        // action null-guards it, and constructing a real ITextBuffer would require standing up the
        // editor's MEF composition for no gain here. What is under test is the config write.
        private const Microsoft.VisualStudio.Text.ITextBuffer NoBuffer = null;

        [Fact]
        public void Invoke_persists_the_disable_into_config_ruleOverrides()
        {
            new DisableRuleGloballyFixAction(NoBuffer, "PE002").Invoke(CancellationToken.None);

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

            new DisableRuleGloballyFixAction(NoBuffer, "ST001").Invoke(CancellationToken.None);

            var post = ConfigManager.Load();
            Assert.False(post.CodeAnalysis.RuleOverrides["ST001"].Enabled);
            Assert.Equal("error", post.CodeAnalysis.RuleOverrides["ST001"].Severity);
        }
    }

    /// <summary>
    /// The suppression scopes offered by the lightbulb and the warning-glyph menu.
    ///
    /// <para>
    /// The directive strings are pinned here because they are a contract with a component this
    /// project cannot reference: the shell targets net472 and the parser
    /// (<c>AkmlSql.Analysis.SuppressionParser</c>) targets net10.0, so nothing makes the compiler
    /// notice if the two drift. They already had: the shell emitted <c>-- noqa:</c> while every
    /// document and the Options page advertised <c>-- akml-disable</c>, which the parser did not
    /// understand at all. The matching engine-side assertions live in
    /// <c>AkmlSql.Engine.Tests.Analysis.SuppressionScopeEndToEndTests</c>.
    /// </para>
    /// </summary>
    public sealed class SuppressionScopeActionTests
    {
        [Fact]
        public void Line_directive_is_the_documented_akml_disable_line_form()
        {
            Assert.Equal(" -- akml-disable-line PE001", SuppressionActions.LineDirective("PE001"));
        }

        [Fact]
        public void Script_directive_is_a_disable_with_no_matching_enable()
        {
            // No "-- akml-enable" is emitted: that is exactly what makes it run to end of file.
            Assert.Equal("-- akml-disable PE001", SuppressionActions.ScriptDirective("PE001"));
            Assert.DoesNotContain("akml-enable", SuppressionActions.ScriptDirective("PE001"));
        }

        [Fact]
        public void The_four_scopes_are_offered_narrowest_first()
        {
            // Menu wording is user-facing and is what tells someone how far a click will reach,
            // so it is pinned rather than left to drift.
            Assert.Equal("Suppress PE001 on this line",
                new SuppressLineFixAction(null, 1, "PE001").DisplayText);
            Assert.Equal("Disable PE001 in this script",
                new SuppressScriptFixAction(null, "PE001").DisplayText);
            Assert.Equal("Disable PE001 for this session",
                new DisableRuleForSessionFixAction(null, "PE001").DisplayText);
            Assert.Equal("Disable PE001 everywhere",
                new DisableRuleGloballyFixAction(null, "PE001").DisplayText);
        }
    }
}
