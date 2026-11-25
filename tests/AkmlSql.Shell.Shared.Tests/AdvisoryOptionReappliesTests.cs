using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 047 installed-product run of 2026-11-22 (F10): in the installed SSMS, Options › Code analysis › Advisory findings
    /// = Underline was saved (config showed "underline") but the open query window kept its margin dots with no underline,
    /// because the tagger re-read the style only when a new analysis arrived — after the next edit. Options OK now makes
    /// every open document ask for its tags again, with the 2 s settings cache dropped, so the saved style shows at once.
    /// </summary>
    [Collection("AnalysisController seams")]
    public sealed class AdvisoryOptionReappliesTests
    {
        [Fact]
        public void Options_OK_makes_an_open_document_redraw_its_findings()
        {
            var buffer = new FakeTextBuffer("select 1");
            using var controller = new AnalysisController(buffer, "session-f10");
            using var tagger = new DiagnosticTagger(buffer, controller);
            var raised = 0;
            tagger.TagsChanged += (_, e) =>
            {
                raised++;
                Assert.Equal(0, e.Span.Start.Position);
                Assert.Equal(buffer.CurrentSnapshot.Length, e.Span.Length);
            };

            ErrorListReporter.ReapplyAll();

            Assert.Equal(1, raised);
        }

        [Fact]
        public void A_closed_document_is_left_alone()
        {
            var buffer = new FakeTextBuffer("select 1");
            using var controller = new AnalysisController(buffer, "session-f10-closed");
            var tagger = new DiagnosticTagger(buffer, controller);
            var raised = 0;
            tagger.TagsChanged += (_, _) => raised++;
            tagger.Dispose();

            ErrorListReporter.ReapplyAll();

            Assert.Equal(0, raised);
        }
    }
}
