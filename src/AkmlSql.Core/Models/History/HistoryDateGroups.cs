using System;
using System.Globalization;

namespace AkmlSql.Core.Models.History
{
    /// <summary>Spec 040 (HIS-08) — the date headings SQL History groups its rows under, newest first.</summary>
    public enum HistoryDateGroup
    {
        Today,
        Yesterday,
        ThisWeek,
        LastWeek,
        ThisMonth,
        Older,
    }

    /// <summary>
    /// Spec 040 (T126, HIS-08) — SQL Prompt's history groups: Today, Yesterday, This week, Last week,
    /// This month, Older. A time that fits several goes in the first that matches, in that order
    /// (yesterday is Yesterday, even when it is also this week). Weeks start on the culture's first
    /// day of the week. <see cref="HistoryDateBucket"/> stays as it is for the web edition.
    /// </summary>
    public static class HistoryDateGroups
    {
        /// <summary>Groups <paramref name="when"/> relative to <paramref name="now"/>, weeks from the current culture.</summary>
        public static HistoryDateGroup For(DateTime now, DateTime when) =>
            For(now, when, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);

        /// <summary>
        /// Groups <paramref name="when"/> relative to <paramref name="now"/>. UTC times are converted
        /// to local time first; a time in the future counts as Today.
        /// </summary>
        public static HistoryDateGroup For(DateTime now, DateTime when, DayOfWeek firstDayOfWeek)
        {
            var today = Local(now).Date;
            var day = Local(when).Date;

            if (day >= today) return HistoryDateGroup.Today;
            if (day == today.AddDays(-1)) return HistoryDateGroup.Yesterday;

            var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
            if (day >= weekStart) return HistoryDateGroup.ThisWeek;
            if (day >= weekStart.AddDays(-7)) return HistoryDateGroup.LastWeek;
            if (day.Year == today.Year && day.Month == today.Month) return HistoryDateGroup.ThisMonth;
            return HistoryDateGroup.Older;
        }

        public static string Label(HistoryDateGroup group)
        {
            switch (group)
            {
                case HistoryDateGroup.Today: return "Today";
                case HistoryDateGroup.Yesterday: return "Yesterday";
                case HistoryDateGroup.ThisWeek: return "This week";
                case HistoryDateGroup.LastWeek: return "Last week";
                case HistoryDateGroup.ThisMonth: return "This month";
                default: return "Older";
            }
        }

        private static DateTime Local(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
    }
}
