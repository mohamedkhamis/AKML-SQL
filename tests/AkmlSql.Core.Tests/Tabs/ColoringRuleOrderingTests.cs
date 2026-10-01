using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    /// <summary>
    /// Spec 040 (T156, OPT-08, FR-054) — the Queries › Color grid's ↑/↓: moving swaps neighbours,
    /// moves past either end are ignored, and <see cref="ColoringRule.Order"/> always ends up
    /// 0..n-1 in list order (which repairs duplicate orders left by older configs).
    /// </summary>
    public class ColoringRuleOrderingTests
    {
        private static List<ColoringRule> Rules(params string[] patterns)
            => patterns.Select((p, i) => new ColoringRule { Pattern = p, Order = i }).ToList();

        private static string[] Patterns(IEnumerable<ColoringRule> rules) => rules.Select(r => r.Pattern).ToArray();

        private static int[] Orders(IEnumerable<ColoringRule> rules) => rules.Select(r => r.Order).ToArray();

        [Fact]
        public void MoveUp_SwapsWithThePreviousRule()
        {
            var rules = Rules("A", "B", "C");

            var index = ColoringRuleOrdering.Move(rules, 2, -1);

            Assert.Equal(1, index);
            Assert.Equal(new[] { "A", "C", "B" }, Patterns(rules));
            Assert.Equal(new[] { 0, 1, 2 }, Orders(rules));
        }

        [Fact]
        public void MoveDown_SwapsWithTheNextRule()
        {
            var rules = Rules("A", "B", "C");

            var index = ColoringRuleOrdering.Move(rules, 0, +1);

            Assert.Equal(1, index);
            Assert.Equal(new[] { "B", "A", "C" }, Patterns(rules));
            Assert.Equal(new[] { 0, 1, 2 }, Orders(rules));
        }

        [Fact]
        public void MoveUp_OnTheFirstRule_IsIgnored()
        {
            var rules = Rules("A", "B", "C");

            var index = ColoringRuleOrdering.Move(rules, 0, -1);

            Assert.Equal(0, index);
            Assert.Equal(new[] { "A", "B", "C" }, Patterns(rules));
        }

        [Fact]
        public void MoveDown_OnTheLastRule_IsIgnored()
        {
            var rules = Rules("A", "B", "C");

            var index = ColoringRuleOrdering.Move(rules, 2, +1);

            Assert.Equal(2, index);
            Assert.Equal(new[] { "A", "B", "C" }, Patterns(rules));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(3, -1)]
        [InlineData(1, 5)]
        [InlineData(1, -5)]
        public void Move_OutOfRange_ChangesNoPosition(int index, int delta)
        {
            var rules = Rules("A", "B", "C");

            var result = ColoringRuleOrdering.Move(rules, index, delta);

            Assert.Equal(index, result);
            Assert.Equal(new[] { "A", "B", "C" }, Patterns(rules));
        }

        [Fact]
        public void Move_RenumbersDuplicateOrdersFromOlderConfigs()
        {
            var rules = new List<ColoringRule>
            {
                new ColoringRule { Pattern = "A", Order = 0 },
                new ColoringRule { Pattern = "B", Order = 0 },
                new ColoringRule { Pattern = "C", Order = 7 },
            };

            ColoringRuleOrdering.Move(rules, 2, -1);

            Assert.Equal(new[] { "A", "C", "B" }, Patterns(rules));
            Assert.Equal(new[] { 0, 1, 2 }, Orders(rules));
        }

        [Fact]
        public void Move_ThatIsIgnored_StillRenumbers()
        {
            var rules = new List<ColoringRule>
            {
                new ColoringRule { Pattern = "A", Order = 4 },
                new ColoringRule { Pattern = "B", Order = 4 },
            };

            ColoringRuleOrdering.Move(rules, 0, -1);

            Assert.Equal(new[] { 0, 1 }, Orders(rules));
        }

        [Fact]
        public void InEvaluationOrder_SortsByOrder_KeepingTiesInListOrder()
        {
            var rules = new List<ColoringRule>
            {
                new ColoringRule { Pattern = "C", Order = 2 },
                new ColoringRule { Pattern = "A1", Order = 0 },
                new ColoringRule { Pattern = "A2", Order = 0 },
                null!,
                new ColoringRule { Pattern = "B", Order = 1 },
            };

            var sorted = ColoringRuleOrdering.InEvaluationOrder(rules);

            Assert.Equal(new[] { "A1", "A2", "B", "C" }, Patterns(sorted));
        }
    }
}
