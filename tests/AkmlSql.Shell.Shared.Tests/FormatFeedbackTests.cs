#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.StatusBar;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T092, STY-09, FR-034) — after Format Document or Format Selection the status bar
    /// says which style was used; when the style could not be loaded the user is warned once
    /// instead; a failed format says neither.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatFeedbackTests : AppDataIsolatedTest, IDisposable
    {
        private readonly List<string> _status = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public FormatFeedbackTests() : base("akmlsql-formatfeedback-test-")
        {
            StatusBarManager.TextSinkOverride = _status.Add;
            StatusBarManager.DelayOverride = (_, _) => { };
            StatusBarManager.ResetForTests();
            FormatFailureNotifier.ProfileFallbackNotifierOverride = _warnings.Add;
            FormatFailureNotifier.ResetProfileFallbackWarnings();
        }

        public override void Dispose()
        {
            StatusBarManager.TextSinkOverride = null;
            StatusBarManager.DelayOverride = null;
            StatusBarManager.ResetForTests();
            FormatFailureNotifier.ProfileFallbackNotifierOverride = null;
            FormatFailureNotifier.ResetProfileFallbackWarnings();
            base.Dispose();
        }

        [Fact]
        public void Success_names_the_style_in_the_status_bar()
        {
            FormatFeedback.Report("Khamis Style", success: true, fallbackWarning: null);

            Assert.Contains("Formatted with 'Khamis Style'", _status);
            Assert.Empty(_warnings);
        }

        [Fact]
        public void A_fallback_warns_once_and_does_not_claim_the_style()
        {
            const string warning = "Formatting style 'Gone' could not be loaded, so the built-in defaults were used instead.";

            FormatFeedback.Report("Gone", success: true, fallbackWarning: warning);
            FormatFeedback.Report("Gone", success: true, fallbackWarning: warning);

            Assert.Equal(new[] { warning }, _warnings);
            Assert.DoesNotContain(_status, t => t.StartsWith("Formatted with", StringComparison.Ordinal));
        }

        [Fact]
        public void A_failed_format_reports_neither()
        {
            FormatFeedback.Report("Khamis Style", success: false, fallbackWarning: "ignored");

            Assert.Empty(_status);
            Assert.Empty(_warnings);
        }

        [Fact]
        public void No_style_name_means_the_defaults()
        {
            FormatFeedback.Report(null, success: true, fallbackWarning: null);

            Assert.Contains("Formatted with the default style", _status);
        }
    }
}
