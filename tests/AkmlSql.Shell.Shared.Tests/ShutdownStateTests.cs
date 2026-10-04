#nullable enable
using System;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (HIS-02) — a close request the user cancels (Cancel at "save changes?") used to
    /// leave History in "shutting down" for the rest of the session: later closes left queries
    /// open, never-run tabs offered for restore, the autosave stopped.
    /// </summary>
    public sealed class ShutdownStateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void A_close_request_counts_as_shutting_down()
        {
            var state = new ShutdownState();
            Assert.False(state.IsShuttingDown(T0));

            state.CloseQueried(T0);

            Assert.True(state.IsShuttingDown(T0.AddSeconds(5)));
        }

        [Fact]
        public void A_cancelled_close_lapses_when_SSMS_shows_it_is_still_running()
        {
            var state = new ShutdownState();
            state.CloseQueried(T0);

            state.StillRunning();

            Assert.False(state.IsShuttingDown(T0.AddSeconds(5)));
        }

        [Fact]
        public void A_close_request_lapses_on_its_own_after_a_minute()
        {
            var state = new ShutdownState();
            state.CloseQueried(T0);

            Assert.False(state.IsShuttingDown(T0 + ShutdownState.QueryWindow));
        }

        [Fact]
        public void A_shutdown_that_has_begun_is_final()
        {
            var state = new ShutdownState();
            state.CloseQueried(T0);
            state.Begin();

            state.StillRunning();

            Assert.True(state.IsShuttingDown(T0.AddHours(1)));
        }
    }
}
