#nullable enable
using System;
using System.Globalization;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Single source of truth for absolute timestamp formatting across the SQL History tool window
    /// (query-list rows, version rows, and the preview header). Previously each of those used a
    /// different format (yyyy-MM-dd HH:mm:ss / MMM dd, HH:mm / yyyy-MM-dd HH:mm), which read
    /// inconsistently against SQL Prompt's uniform rows (report §3 rec #2). This helper renders one
    /// locale-aware absolute format everywhere: the culture's short-date pattern + 24-hour time.
    /// </summary>
    internal static class HistoryTimeFormat
    {
        public static string Absolute(DateTime local, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            return local.ToString(culture.DateTimeFormat.ShortDatePattern + " HH:mm", culture);
        }
    }
}
