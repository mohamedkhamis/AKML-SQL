using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Spec 040 (OPT-08, FR-054) — the ↑/↓ reordering behind the Queries › Color grid. Rules are
    /// evaluated lowest <see cref="ColoringRule.Order"/> first and the first match wins, so the
    /// list position IS the priority: every operation renumbers <c>Order</c> to 0..n-1 from the
    /// list, which also repairs the duplicate orders older configs can carry.
    /// </summary>
    public static class ColoringRuleOrdering
    {
        /// <summary>
        /// Moves the rule at <paramref name="index"/> by <paramref name="delta"/> places (−1 = up,
        /// +1 = down) and renumbers <see cref="ColoringRule.Order"/>. A move past either end, or an
        /// index outside the list, changes no position (the renumbering still happens). Returns the
        /// rule's index afterwards.
        /// </summary>
        public static int Move(IList<ColoringRule> rules, int index, int delta)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var result = index;
            var target = index + delta;
            if (delta != 0 && index >= 0 && index < rules.Count && target >= 0 && target < rules.Count)
            {
                var rule = rules[index];
                rules.RemoveAt(index);
                rules.Insert(target, rule);
                result = target;
            }

            Renumber(rules);
            return result;
        }

        /// <summary>Sets each rule's <see cref="ColoringRule.Order"/> to its list index.</summary>
        public static void Renumber(IList<ColoringRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i] != null) rules[i].Order = i;
            }
        }

        /// <summary>
        /// The rules in evaluation order: by <see cref="ColoringRule.Order"/> ascending, rules with
        /// the same order kept in list order. Null entries are dropped. A new list; the input is not
        /// changed.
        /// </summary>
        public static List<ColoringRule> InEvaluationOrder(IEnumerable<ColoringRule>? rules)
            => rules == null
                ? new List<ColoringRule>()
                : rules.Where(r => r != null).OrderBy(r => r.Order).ToList();
    }
}
