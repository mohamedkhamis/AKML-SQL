#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Snippets;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>Spec 040 (T018, OPT-01) — the snippet switches that used to be ignored.</summary>
    public class SnippetGateTests
    {
        private static AppSettings With(bool snippets, bool inCompletion, bool formatOnExpand = true)
        {
            var s = new AppSettings();
            s.Snippets.Enabled = snippets;
            s.Snippets.FormatOnExpand = formatOnExpand;
            s.IntelliSense.SnippetsInCompletion = inCompletion;
            return s;
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Snippets_are_offered_only_when_both_switches_are_on(bool snippets, bool inCompletion, bool expected)
            => Assert.Equal(expected, SnippetGate.ShouldOfferSnippets(With(snippets, inCompletion)));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Expansion_follows_enable_snippets(bool snippets)
            => Assert.Equal(snippets, SnippetGate.ExpansionEnabled(With(snippets, inCompletion: false)));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Format_on_expand_follows_its_setting(bool format)
            => Assert.Equal(format, SnippetGate.FormatOnExpand(With(true, true, format)));
    }
}
