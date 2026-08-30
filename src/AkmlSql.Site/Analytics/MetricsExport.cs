using System.Globalization;
using System.Text;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// ADM-007: CSV export of the dashboard figures. Metrics were previously viewable only in the
/// browser, so any longitudinal look meant querying the SQLite file on the server by hand.
/// <para>
/// One long-format file (section, key, value) rather than several wide ones: it is a single
/// download, it opens straight into a pivot table, and adding a metric never changes the shape.
/// </para>
/// </summary>
public static class MetricsExport
{
    /// <summary>Builds the CSV body for a summary.</summary>
    public static string ToCsv(AnalyticsSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var builder = new StringBuilder();
        builder.Append("section,key,value\n");

        void Row(string section, string key, long value) =>
            builder.Append(Escape(section)).Append(',')
                   .Append(Escape(key)).Append(',')
                   .Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

        Row("totals", "window_days", summary.Days);
        Row("totals", "visits_today", summary.VisitsToday);
        Row("totals", "unique_visitors_today", summary.UniqueVisitorsToday);
        Row("totals", "visits_7d", summary.VisitsLast7Days);
        Row("totals", "visits_window", summary.VisitsWindow);
        Row("totals", "automated_visits_window", summary.AutomatedVisitsWindow);
        Row("totals", "downloads_total", summary.DownloadsTotal);
        Row("totals", "downloads_7d", summary.DownloadsLast7Days);
        Row("totals", "downloads_window", summary.DownloadsWindow);
        Row("totals", "sessions", summary.Sessions);
        // Rates are carried at a fixed scale so the whole file stays integer-valued and a
        // spreadsheet cannot reinterpret one column's decimal separator by locale.
        Row("totals", "bounce_rate_percent_x10", (long)Math.Round(summary.BounceRatePercent * 10));
        Row("totals", "pages_per_session_x100", (long)Math.Round(summary.PagesPerSession * 100));
        Row("totals", "avg_session_seconds", (long)Math.Round(summary.AverageSessionSeconds));

        foreach (var day in summary.DailyVisits)
        {
            Row("daily_visits", Day(day.Day), day.Count);
        }

        foreach (var day in summary.DailyUniqueVisitors)
        {
            Row("daily_unique_visitors", Day(day.Day), day.Count);
        }

        foreach (var day in summary.DailyDownloads)
        {
            Row("daily_downloads", Day(day.Day), day.Count);
        }

        foreach (var row in summary.TopPages)
        {
            Row("top_pages", row.Key, row.Count);
        }

        foreach (var row in summary.TopReferrers)
        {
            Row("top_referrers", row.Key, row.Count);
        }

        foreach (var row in summary.BrowserMix)
        {
            Row("browsers", row.Key, row.Count);
        }

        foreach (var row in summary.DownloadsByFile)
        {
            Row("downloads_by_file", row.Key, row.Count);
        }

        foreach (var row in summary.TopNotFound)
        {
            Row("not_found", row.Key, row.Count);
        }

        // Enrichment dimensions, each in its own section so the long-format file pivots cleanly.
        var sections = new (string Section, IReadOnlyList<CountRow> Rows)[]
        {
            ("countries", summary.Countries),
            ("cities", summary.Cities),
            ("devices", summary.Devices),
            ("operating_systems", summary.OperatingSystems),
            ("languages", summary.Languages),
            ("campaigns", summary.Campaigns),
            ("referrer_urls", summary.ReferrerUrls),
            ("entry_pages", summary.EntryPages),
            ("exit_pages", summary.ExitPages),
            ("slowest_pages_mean_ms", summary.SlowestPages),
        };

        foreach (var (section, rows) in sections)
        {
            foreach (var row in rows)
            {
                Row(section, row.Key, row.Count);
            }
        }

        return builder.ToString();
    }

    /// <summary>Suggested download file name, stamped with the window and the UTC date.</summary>
    public static string FileName(AnalyticsSummary summary, DateTimeOffset now) =>
        $"akml-site-metrics-{summary.Days}d-{now.UtcDateTime:yyyy-MM-dd}.csv";

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// RFC 4180 quoting. Page paths and referrer hosts come from request data, so a comma or a
    /// quote in one must not be able to shift a value into the next column.
    /// </summary>
    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var needsQuotes = value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal);

        return needsQuotes ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : value;
    }
}
