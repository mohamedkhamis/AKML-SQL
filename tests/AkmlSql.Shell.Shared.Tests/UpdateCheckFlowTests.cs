#nullable enable
using System;
using System.Diagnostics;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 US5 / FR-042: the manual "Check for updates" command must run the check
    /// immediately (never through the 24-hour throttle) and must report all three outcomes —
    /// up to date, update available, check failed. A failed automatic check stays invisible
    /// (FR-041); the manual one distinguishes failure from "up to date" by whether the updater
    /// stamped a new LastUpdateCheck — a completed check always stamps, a failed one does not.
    /// </summary>
    public class UpdateCheckFlowTests
    {
        [Fact]
        public void RunManualCheck_launches_the_updater_even_with_a_recent_LastUpdateCheck()
        {
            var recent = DateTimeOffset.UtcNow.AddMinutes(-5);
            var launched = false;
            var checks = new[] { recent, recent.AddMinutes(5) }; // before, after — the check completed
            var read = 0;

            var outcome = UpdateCheckFlow.RunManualCheck(
                launchAndWait: _ => { launched = true; return new Process(); },
                readLastCheck: () => checks[Math.Min(read++, checks.Length - 1)],
                readResult: () => null,
                timeout: TimeSpan.FromSeconds(15));

            Assert.True(launched, "the manual check must launch the updater regardless of the last check time");
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.UpToDate, outcome);
        }

        [Fact]
        public void RunManualCheck_returns_CheckFailed_when_the_updater_is_missing()
        {
            var launched = false;

            var outcome = UpdateCheckFlow.RunManualCheck(
                launchAndWait: _ => { launched = true; return null; },
                readLastCheck: () => null,
                readResult: () => null,
                timeout: TimeSpan.FromSeconds(15));

            Assert.True(launched);
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.CheckFailed, outcome);
        }

        [Fact]
        public void RunManualCheck_returns_CheckFailed_when_no_new_check_was_stamped()
        {
            // A failed check (offline, blocked host) never stamps LastUpdateCheck — the manual
            // command must report failure, not "up to date", and must not act on a stale offer.
            var recent = DateTimeOffset.UtcNow.AddMinutes(-5);
            var stale = new UpdateResult { Available = true, Version = "1.26.0903.0900" };

            var outcome = UpdateCheckFlow.RunManualCheck(
                launchAndWait: _ => new Process(),
                readLastCheck: () => recent, // unchanged before and after
                readResult: () => stale,
                timeout: TimeSpan.FromSeconds(15));

            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.CheckFailed, outcome);
        }

        [Fact]
        public void RunManualCheck_returns_UpdateAvailable_when_the_completed_check_found_one()
        {
            DateTimeOffset? before = null;
            var after = DateTimeOffset.UtcNow;
            var read = 0;

            var outcome = UpdateCheckFlow.RunManualCheck(
                launchAndWait: _ => new Process(),
                readLastCheck: () => read++ == 0 ? before : after,
                readResult: () => new UpdateResult { Available = true, Version = "1.26.0903.0900" },
                timeout: TimeSpan.FromSeconds(15));

            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.UpdateAvailable, outcome);
        }

        [Fact]
        public void Classify_requires_a_strictly_newer_stamp()
        {
            var t = DateTimeOffset.UtcNow;

            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.CheckFailed,
                UpdateCheckFlow.Classify(t, t, null));                    // unchanged stamp
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.CheckFailed,
                UpdateCheckFlow.Classify(t, t.AddMinutes(-1), null));     // clock skew backwards
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.CheckFailed,
                UpdateCheckFlow.Classify(null, null, null));              // never checked
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.UpToDate,
                UpdateCheckFlow.Classify(null, t, null));                 // first check, no update
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.UpToDate,
                UpdateCheckFlow.Classify(t, t.AddMinutes(1), new UpdateResult()));
            Assert.Equal(UpdateCheckFlow.ManualCheckOutcome.UpdateAvailable,
                UpdateCheckFlow.Classify(t, t.AddMinutes(1),
                    new UpdateResult { Available = true, Version = "9.9.9" }));
        }
    }
}
