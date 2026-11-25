using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 047 (SSMS-001, plan-time amendment A1) — <c>codeAnalysis.showAdvisoryHints</c> keeps its documented meaning:
    /// when it is off, a finding with no automatic fix offers no lightbulb fix actions. Its margin marker, its Error List
    /// entry and the suppress / disable actions of the glyph menu stay.
    /// </summary>
    public sealed class LightbulbAdvisoryHintsTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]     // an automatic fix is always offered
        [InlineData(false, true, true)]     // advisory hints on: the advisory lightbulb shows
        [InlineData(false, false, false)]   // advisory hints off: no lightbulb for a finding without a fix
        public void Fix_actions_are_offered_unless_the_finding_is_advisory_and_hints_are_off(
            bool hasAutomaticFix, bool showAdvisoryHints, bool offered)
        {
            Assert.Equal(offered, LightbulbProvider.OffersFixActions(hasAutomaticFix, showAdvisoryHints));
        }
    }
}
