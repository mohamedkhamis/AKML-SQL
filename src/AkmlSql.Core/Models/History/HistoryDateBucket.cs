using System;
using System.Globalization;

namespace AkmlSql.Core.Models.History
{
    /// <summary>
    /// Pure date-bucketing shared by the web History page and the desktop History tool window.
    /// Classifies a timestamp into one of four contiguous buckets that cover every date:
    /// <see cref="Today"/> (the same calendar day), <see cref="ThisWeek"/> (the preceding six days),
    /// <see cref="TwoMonths"/> (7 to 59 days back), and <see cref="Older"/> (60+ days, or an
    /// unparseable value). The labels mirror the Red Gate SQL Prompt history reference; there is no
    /// separate "This Month" bucket by design.
    /// </summary>
    public static class HistoryDateBucket
    {
        public const string Today = "Today";
        public const string ThisWeek = "This Week";
        public const string TwoMonths = "Two Months Ago";
        public const string Older = "Older";

        /// <summary>
        /// Classifies an ISO-8601 timestamp relative to <paramref name="now"/>. Parses with
        /// <see cref="DateTimeStyles.RoundtripKind"/>, converts to local time, and compares the local
        /// date against <paramref name="now"/>'s date. An unparseable value buckets as <see cref="Older"/>.
        /// </summary>
        public static string Of(string? executedAtIso, DateTime now)
        {
            if (DateTime.TryParse(executedAtIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
            {
                return Of(dt.ToLocalTime(), now);
            }
            return Older;
        }

        /// <summary>
        /// Classifies an already-local timestamp relative to <paramref name="now"/> using the same
        /// today / prior-six-days / 7–59-days / 60+-days thresholds.
        /// </summary>
        public static string Of(DateTime executedAtLocal, DateTime now)
        {
            var d = executedAtLocal.Date;
            var today = now.Date;
            if (d == today) return Today;
            if (d > today.AddDays(-7)) return ThisWeek;
            if (d > today.AddDays(-60)) return TwoMonths;
            return Older;
        }
    }
}
