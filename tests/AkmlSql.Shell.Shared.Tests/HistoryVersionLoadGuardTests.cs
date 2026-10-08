#nullable enable
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T061, HIS-04) — the Versions pane keeps the answer for the entry now selected: a
    /// slower response for an entry selected earlier is dropped.
    /// </summary>
    public class HistoryVersionLoadGuardTests
    {
        [Fact]
        public void The_older_of_two_overlapping_loads_is_discarded()
        {
            var guard = new VersionLoadGuard();
            var first = guard.Begin();
            var second = guard.Begin();

            // The second load finishes first and is shown …
            Assert.True(guard.IsCurrent(second));
            // … then the first, slower one arrives and must not overwrite it.
            Assert.False(guard.IsCurrent(first));
        }

        [Fact]
        public void A_single_load_is_always_current()
        {
            var guard = new VersionLoadGuard();
            var token = guard.Begin();

            Assert.True(guard.IsCurrent(token));
            Assert.True(guard.IsCurrent(token));
        }
    }
}
