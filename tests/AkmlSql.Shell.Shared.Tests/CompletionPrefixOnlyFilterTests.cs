#nullable enable
using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T013, OPT-01) — with fuzzy matching off, the popup filters by prefix only, using
    /// the engine's FilterText so "alias.Column" items keep matching the typed column prefix.
    /// </summary>
    public class CompletionPrefixOnlyFilterTests
    {
        private static CompletionItemModel Qualified() =>
            new CompletionItemModel { DisplayText = "p.UnitPrice", FilterText = "UnitPrice" };

        [Theory]
        [InlineData("Unit")]
        [InlineData("unit")]
        [InlineData("p.Unit")]
        public void Prefix_only_matches_the_filter_text_or_the_display_text(string typed)
        {
            var item = Qualified();

            Assert.True(item.MatchesFilter(typed, prefixOnly: true));
            Assert.Equal(0, item.FilterScore(typed, prefixOnly: true));
        }

        [Fact]
        public void Prefix_only_rejects_substring_matches()
        {
            var item = new CompletionItemModel { DisplayText = "QuantityPerUnit" };

            Assert.False(item.MatchesFilter("unit", prefixOnly: true));
            Assert.Equal(int.MaxValue, item.FilterScore("unit", prefixOnly: true));
        }

        [Fact]
        public void Prefix_only_rejects_camel_case_matches()
        {
            var item = new CompletionItemModel { DisplayText = "ProductCategory" };

            Assert.False(item.MatchesFilter("PC", prefixOnly: true));
        }

        [Fact]
        public void Fuzzy_mode_keeps_todays_behaviour()
        {
            var substring = new CompletionItemModel { DisplayText = "QuantityPerUnit" };
            var camel = new CompletionItemModel { DisplayText = "ProductCategory" };

            Assert.True(substring.MatchesFilter("unit"));
            Assert.Equal(100, substring.FilterScore("unit"));
            Assert.True(camel.MatchesFilter("PC"));
            Assert.Equal(50, camel.FilterScore("PC"));
        }

        [Fact]
        public void Empty_filter_matches_everything_in_both_modes()
        {
            var item = Qualified();

            Assert.True(item.MatchesFilter(string.Empty, prefixOnly: true));
            Assert.True(item.MatchesFilter(string.Empty));
        }
    }
}
