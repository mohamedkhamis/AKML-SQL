using System.Globalization;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// The Insights section: conversion, when people visit, and new versus returning visitors.
/// </summary>
public sealed partial class AnalyticsStore
{
    /// <summary>Everything the Insights section shows, for one window.</summary>
    public InsightsReport GetInsights(ReportWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_gate)
        {
            var headline = QueryHeadline(window);
            var (visitsByHour, downloadsByHour, heatmap) = QueryWhenPeopleVisit(window);

            return new InsightsReport
            {
                Window = window,
                Headline = headline,
                ConversionByCountry = QueryConversionBy(window, FirstTouchCountry),
                ConversionBySource = QueryConversionBy(window, FirstTouchSource),
                ConversionByLandingPage = QueryConversionBy(window, FirstTouchPage),
                VisitsByHour = visitsByHour,
                DownloadsByHour = downloadsByHour,
                VisitHeatmap = heatmap,
                Loyalty = QueryLoyalty(window),
            };
        }
    }

    // ------------------------------------------------------------------ conversion

    /// <summary>First-touch dimension expressions over the <c>first_touch</c> CTE.</summary>
    private const string FirstTouchCountry = "COALESCE(NULLIF(country, ''), '(unknown)')";

    /// <summary>
    /// Where a visitor-day came from: the UTM source if the link carried one, otherwise the referring
    /// site, otherwise "(direct)" -- typed in, bookmarked, or a referrer the browser withheld.
    /// </summary>
    private const string FirstTouchSource =
        "COALESCE(NULLIF(utm_source, ''), NULLIF(referrer_host, ''), '(direct)')";

    private const string FirstTouchPage = "path";

    /// <summary>
    /// Visitor-days and downloaders grouped by a property of each visitor-day's FIRST page view.
    /// <para>
    /// Attribution is first-touch within the day: the landing page, the source that brought them,
    /// the country they were in. The download is matched on the same per-day hash, which is the only
    /// link the anonymous data allows -- so a person who reads on Monday and downloads on Tuesday is
    /// a non-converting visitor-day plus a download with no visit behind it, and the overview reports
    /// those "downloads without a visit" separately rather than pretending to attribute them.
    /// </para>
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private IReadOnlyList<ConversionRow> QueryConversionBy(ReportWindow window, string dimension)
    {
        using var command = _connection.CreateCommand();
        // `dimension` is one of the fixed expressions above, never user input.
        command.CommandText =
            "WITH ranked AS (" +
            "  SELECT ip_hash, country, referrer_host, utm_source, path, " +
            "         ROW_NUMBER() OVER (PARTITION BY ip_hash ORDER BY utc, id) AS rn " +
            $"  FROM visits WHERE {InWindow} AND {HumanOnly}" +
            "), first_touch AS (SELECT * FROM ranked WHERE rn = 1), " +
            "dl AS (" +
            "  SELECT DISTINCT ip_hash FROM downloads " +
            $"  WHERE {InWindow} AND {RealDownloadOnly}" +
            ") " +
            $"SELECT {dimension} AS label, COUNT(*) AS visitors, " +
            "       SUM(CASE WHEN ip_hash IN (SELECT ip_hash FROM dl) THEN 1 ELSE 0 END) AS downloaders " +
            "FROM first_touch GROUP BY label " +
            "ORDER BY downloaders DESC, visitors DESC, label LIMIT $limit;";
        BindWindow(command, window);
        command.Parameters.AddWithValue("$limit", TopRowLimit);

        var rows = new List<ConversionRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ConversionRow(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        return rows;
    }

    // ------------------------------------------------------------------ when people visit

    /// <summary>
    /// Visits and downloads by local hour of day, and visits by local weekday × hour.
    /// <para>
    /// Counted per UTC minute in SQL and placed on the local clock here, so every event lands on the
    /// hour and weekday the owner would read off a wall clock -- summer time included. Doing it in
    /// SQL with a fixed offset would put every event an hour out for half the year.
    /// </para>
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private (long[] VisitsByHour, long[] DownloadsByHour, long[,] Heatmap) QueryWhenPeopleVisit(ReportWindow window)
    {
        var visitsByHour = new long[24];
        var downloadsByHour = new long[24];
        var heatmap = new long[7, 24];

        foreach (var (minute, count) in CountsByMinute("visits", HumanOnly, window))
        {
            var local = LocalMinute(minute, window.Zone);
            visitsByHour[local.Hour] += count;
            heatmap[(int)local.DayOfWeek, local.Hour] += count;
        }

        foreach (var (minute, count) in CountsByMinute("downloads", RealDownloadOnly, window))
        {
            downloadsByHour[LocalMinute(minute, window.Zone).Hour] += count;
        }

        return (visitsByHour, downloadsByHour, heatmap);
    }

    private List<(string Minute, long Count)> CountsByMinute(string table, string exclusion, ReportWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            $"SELECT substr(utc, 1, 16), COUNT(*) FROM {table} WHERE {InWindow} AND {exclusion} GROUP BY 1;";
        BindWindow(command, window);

        var rows = new List<(string, long)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    // ------------------------------------------------------------------ new vs returning

    /// <summary>
    /// New versus returning visitors, and how often each group downloads.
    /// <para>
    /// Only people who accepted the cookie can be followed from one day to the next -- the anonymous
    /// identifier is a per-day hash, by design -- so this covers that group alone and says how large
    /// it is. "New" means their first-ever visit falls inside the window; "returning" means they had
    /// been here before it opened.
    /// </para>
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private LoyaltySummary QueryLoyalty(ReportWindow window)
    {
        long newVisitors = 0, newDownloaders = 0, returningVisitors = 0, returningDownloaders = 0;

        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "WITH active AS (" +
                $"  SELECT visitor_id FROM visits WHERE {InWindow} AND visitor_id IS NOT NULL AND {PeopleHumanOnly} " +
                $"  UNION SELECT visitor_id FROM downloads WHERE {InWindow} AND visitor_id IS NOT NULL" +
                "), first_seen AS (" +
                "  SELECT visitor_id, MIN(utc) AS first_utc FROM (" +
                $"    SELECT visitor_id, utc FROM visits WHERE visitor_id IS NOT NULL AND {PeopleHumanOnly} " +
                "    UNION ALL SELECT visitor_id, utc FROM downloads WHERE visitor_id IS NOT NULL" +
                "  ) GROUP BY visitor_id" +
                "), downloaded AS (" +
                "  SELECT DISTINCT visitor_id FROM downloads " +
                $"  WHERE {InWindow} AND visitor_id IS NOT NULL AND {RealDownloadOnly}" +
                ") " +
                "SELECT CASE WHEN f.first_utc >= $from THEN 'new' ELSE 'returning' END AS kind, " +
                "       COUNT(*), " +
                "       SUM(CASE WHEN a.visitor_id IN (SELECT visitor_id FROM downloaded) THEN 1 ELSE 0 END) " +
                "FROM active a JOIN first_seen f ON f.visitor_id = a.visitor_id GROUP BY kind;";
            BindWindow(command, window);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var people = reader.GetInt64(1);
                var downloaders = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                if (reader.GetString(0) == "new")
                {
                    (newVisitors, newDownloaders) = (people, downloaders);
                }
                else
                {
                    (returningVisitors, returningDownloaders) = (people, downloaders);
                }
            }
        }

        // How much of the traffic the split can see: human page views from people who accepted
        // the cookie, over all human page views.
        var attributedVisits = Count("visits", $"visitor_id IS NOT NULL AND {HumanOnly}", window);
        var allVisits = Count("visits", HumanOnly, window);

        return new LoyaltySummary(
            newVisitors, newDownloaders, returningVisitors, returningDownloaders,
            attributedVisits, allVisits);
    }
}

/// <summary>Read model for the Insights section.</summary>
public sealed class InsightsReport
{
    public required ReportWindow Window { get; init; }

    /// <summary>Headline figures for the window, the same ones the overview shows.</summary>
    public required HeadlineMetrics Headline { get; init; }

    /// <summary>Conversion by the country of each visitor-day's first page view.</summary>
    public required IReadOnlyList<ConversionRow> ConversionByCountry { get; init; }

    /// <summary>Conversion by traffic source (UTM source, else referrer, else direct).</summary>
    public required IReadOnlyList<ConversionRow> ConversionBySource { get; init; }

    /// <summary>Conversion by landing page.</summary>
    public required IReadOnlyList<ConversionRow> ConversionByLandingPage { get; init; }

    /// <summary>Human page views by local hour, index 0–23.</summary>
    public required long[] VisitsByHour { get; init; }

    /// <summary>Downloads by local hour, index 0–23.</summary>
    public required long[] DownloadsByHour { get; init; }

    /// <summary>Human page views by local weekday (<see cref="DayOfWeek"/> order, Sunday = 0) × hour.</summary>
    public required long[,] VisitHeatmap { get; init; }

    public required LoyaltySummary Loyalty { get; init; }

    /// <summary>True when the window holds too little data for the hour/weekday split to mean much.</summary>
    public bool HasTimingData => VisitsByHour.Sum() > 0;
}

/// <summary>Visitor-days and how many of them downloaded, for one value of a dimension.</summary>
public sealed record ConversionRow(string Label, long Visitors, long Downloaders)
{
    public double ConversionPercent => Visitors == 0 ? 0 : Math.Round(Downloaders * 100.0 / Visitors, 1);
}

/// <summary>New versus returning visitors among those who accepted the cookie.</summary>
public sealed record LoyaltySummary(
    long NewVisitors,
    long NewDownloaders,
    long ReturningVisitors,
    long ReturningDownloaders,
    long AttributedVisits,
    long AllVisits)
{
    public long Visitors => NewVisitors + ReturningVisitors;

    public double NewConversionPercent =>
        NewVisitors == 0 ? 0 : Math.Round(NewDownloaders * 100.0 / NewVisitors, 1);

    public double ReturningConversionPercent =>
        ReturningVisitors == 0 ? 0 : Math.Round(ReturningDownloaders * 100.0 / ReturningVisitors, 1);

    /// <summary>
    /// Share of human page views the split can see, 0–100. Low because most visitors decline or
    /// ignore the cookie -- which is their right -- so the page states it next to every figure.
    /// </summary>
    public double CoveragePercent =>
        AllVisits == 0 ? 0 : Math.Round(AttributedVisits * 100.0 / AllVisits, 1);
}
