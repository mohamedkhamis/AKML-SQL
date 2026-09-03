#nullable enable
using System;
using System.Diagnostics;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// The manual "Check for updates" flow (spec 036 US5 / FR-042): always launches the updater
    /// directly — never through the 24-hour throttle in <see cref="UpdateLauncher.LaunchIfDue"/> —
    /// and classifies the outcome so the command can report all three results: up to date,
    /// update available, check failed.
    ///
    /// Distinguishing "check failed" from "up to date": the updater stamps LastUpdateCheck on
    /// every completed check (update found, none found, or an unreadable manifest), while a
    /// failed check (offline, blocked host) leaves it untouched. A stamp that did not advance
    /// therefore means the check failed — and a stale result file must not be acted on.
    /// </summary>
    internal static class UpdateCheckFlow
    {
        internal enum ManualCheckOutcome
        {
            UpToDate,
            UpdateAvailable,
            CheckFailed
        }

        internal static ManualCheckOutcome RunManualCheck(
            Func<TimeSpan, Process?> launchAndWait,
            Func<DateTimeOffset?> readLastCheck,
            Func<UpdateResult?> readResult,
            TimeSpan timeout)
        {
            var before = readLastCheck();

            Process? process;
            try
            {
                process = launchAndWait(timeout);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Manual update check: updater failed to launch");
                return ManualCheckOutcome.CheckFailed;
            }

            if (process == null)
            {
                Log.Warning("Manual update check: updater not found");
                return ManualCheckOutcome.CheckFailed;
            }

            var after = readLastCheck();
            return Classify(before, after, readResult());
        }

        internal static ManualCheckOutcome Classify(
            DateTimeOffset? lastCheckBefore, DateTimeOffset? lastCheckAfter, UpdateResult? result)
        {
            var checkCompleted = lastCheckAfter.HasValue
                && (!lastCheckBefore.HasValue || lastCheckAfter.Value > lastCheckBefore.Value);
            if (!checkCompleted)
            {
                return ManualCheckOutcome.CheckFailed;
            }

            return result is { Available: true }
                ? ManualCheckOutcome.UpdateAvailable
                : ManualCheckOutcome.UpToDate;
        }
    }
}
