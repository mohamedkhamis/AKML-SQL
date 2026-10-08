#nullable enable
using System;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// SSMS raises <c>DocumentClosing</c> up to three times for a single tab close: once when the
    /// close starts (before the "save changes?" prompt) and twice more as the tab goes away; with
    /// several tabs closing at once (Close All, shutdown) their events interleave.
    /// <see cref="ClosedTabStack"/> uses this to keep one Reopen Closed Tab entry per close.
    /// (SQL History waits for the tab to go instead — see <c>History.PendingCloses</c>.)
    /// </summary>
    internal static class RepeatedCloseFilter
    {
        /// <summary>
        /// How long after a close the same document and text still count as that close reported
        /// again. Long enough for the save prompt to be answered.
        /// </summary>
        internal static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Whether a close of <paramref name="documentName"/> with <paramref name="text"/> at
        /// <paramref name="atUtc"/> repeats the earlier close described by the <c>previous*</c> values.
        /// </summary>
        internal static bool IsSameClose(string? previousName, string? previousText, DateTime previousAtUtc,
            string? documentName, string? text, DateTime atUtc) =>
            string.Equals(previousName, documentName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previousText, text, StringComparison.Ordinal)
            && atUtc >= previousAtUtc
            && atUtc - previousAtUtc < Window;
    }
}
