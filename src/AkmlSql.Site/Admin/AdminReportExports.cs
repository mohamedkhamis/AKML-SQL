using AkmlSql.Site.Analytics;
using AkmlSql.Site.Feedback;
using AkmlSql.Site.Telemetry;

namespace AkmlSql.Site.Admin;

/// <summary>All exports remain inside the existing admin guard and use no-store responses.</summary>
public static class AdminReportExports
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/people.csv", (HttpContext http, AnalyticsStore store, string? days, string? country, string? downloaded) =>
        {
            var window = store.ResolveWindow(AdminDashboardOptions.ResolveRange(days));
            var filter = new IndividualFilter(window.Range.Days, string.IsNullOrWhiteSpace(country) ? null : country,
                downloaded switch { "yes" => true, "no" => false, _ => null });
            var fileFilter = filter with { CountryCode = filter.CountryCode is null ? null :
                filter.CountryCode.Length == 2 && filter.CountryCode.All(char.IsAsciiLetter) ? filter.CountryCode : "filtered" };
            return AdminCsv.Download(http, IndividualsExport.FileName("people", fileFilter, window.Range.Days, window.Now, window), People(store, window, filter));
        });
        endpoints.MapGet("/admin/feedback.csv", (HttpContext http, FeedbackStore store, string? show, string? q) =>
        {
            bool? handled = show switch { "handled" => true, "all" => null, _ => false };
            return AdminCsv.Download(http, $"akml-feedback-{DateTimeOffset.UtcNow:yyyyMMdd}.csv", Feedback(store, handled, AdminPaging.Search(q)));
        });
        endpoints.MapGet("/admin/errors.csv", (HttpContext http, AnalyticsStore store, string? days, string? level, string? version, string? q) =>
        {
            var window = store.ResolveWindow(AdminDashboardOptions.ResolveRange(days));
            return AdminCsv.Download(http, $"akml-errors-{window.Range.Key}-{window.Now:yyyyMMdd}.csv",
                Errors(store, window, ClientErrorLevelNormalization.Normalize(level), AdminPaging.Search(version), AdminPaging.Search(q)));
        });
        endpoints.MapGet("/admin/insights.csv", (HttpContext http, AnalyticsStore store, string? days) =>
        {
            var report = store.GetInsights(store.ResolveWindow(AdminDashboardOptions.ResolveRange(days)));
            return AdminCsv.Download(http, $"akml-insights-{report.Window.Range.Key}-{report.Window.Now:yyyyMMdd}.csv", Insights(report));
        });
    }

    internal static IEnumerable<object?[]> People(AnalyticsStore store, ReportWindow window, IndividualFilter filter)
    {
        yield return [IndividualsExport.PersonalDataNotice];
        yield return ["# Window", window.Describe()];
        yield return ["# Filters", "country", filter.CountryCode ?? "All", "downloaded", filter.Downloaded?.ToString() ?? "Any"];
        yield return ["# Scope", "All matching consenting browsers; unattributed traffic excluded; latest known country in the window; all their window events"];
        yield return ["visitor_id", "first_seen_utc", "last_seen_utc", "country", "country_code", "ip_address", "network", "device", "os", "browser", "visits", "downloads", "downloaded", "returning"];
        foreach (var row in store.EnumerateIndividuals(filter, window))
            yield return [row.VisitorId, row.FirstSeen.ToString("u"), row.LastSeen.ToString("u"), row.Country, row.CountryCode,
                row.IpAddress, row.NetworkPrefix, row.Device, row.Os, row.Browser, row.VisitCount, row.DownloadCount,
                row.Downloaded ? "yes" : "no", row.IsReturning ? "yes" : "no"];
    }

    private static IEnumerable<object?[]> Feedback(FeedbackStore store, bool? handled, string? search)
    {
        yield return ["# CONTAINS PERSONAL DATA: feedback text and optional reply addresses. Handle accordingly."];
        yield return ["# Filters", "handled", handled?.ToString() ?? "All", "search", search ?? ""];
        yield return ["id", "received_utc", "category", "message", "email", "page", "country", "browser", "handled"];
        foreach (var item in store.Enumerate(handled, search))
            yield return [item.Id, item.ReceivedUtc.ToString("u"), item.Category.Label, item.Message, item.Email, item.Page, item.Country, item.Browser, item.Handled];
    }

    private static IEnumerable<object?[]> Errors(AnalyticsStore store, ReportWindow window, string? level, string? version, string? search)
    {
        yield return ["# SENSITIVE DIAGNOSTICS: free text and persistent installation identifiers. Handle accordingly."];
        yield return ["# Window", window.Describe()];
        yield return ["# Filters", "level", level ?? "All", "version", version ?? "All", "search", search ?? ""];
        yield return ["id", "event_utc", "level", "product_version", "host", "install_id", "message", "exception"];
        foreach (var item in store.EnumerateClientErrors(window, level, version, search))
            yield return [item.Id, item.Utc.ToString("u"), item.Level, item.ProductVersion, item.Host, item.InstallId, item.Message, item.Exception];
    }

    private static IEnumerable<object?[]> Insights(InsightsReport report)
    {
        yield return ["# Window", report.Window.Describe()];
        yield return ["# Scope", "Displayed aggregates; each conversion grouping is limited to the top 10. Visitor-day attribution is an estimate, not an install funnel."];
        yield return ["section", "key", "value"];
        yield return ["headline", "visits", report.Headline.Visits];
        yield return ["headline", "visitor_days", report.Headline.Visitors];
        yield return ["headline", "download_requests", report.Headline.Downloads];
        yield return ["headline", "downloader_days", report.Headline.Downloaders];
        foreach (var (section, rows) in new[] { ("country", report.ConversionByCountry), ("source", report.ConversionBySource), ("landing_page", report.ConversionByLandingPage) })
            foreach (var row in rows)
            {
                yield return [section + "_visitor_days", row.Label, row.Visitors];
                yield return [section + "_downloader_days", row.Label, row.Downloaders];
                yield return [section + "_conversion_percent", row.Label, row.ConversionPercent];
            }
        for (var hour = 0; hour < 24; hour++)
        {
            yield return ["visits_by_hour", hour, report.VisitsByHour[hour]];
            yield return ["downloads_by_hour", hour, report.DownloadsByHour[hour]];
            for (var day = 0; day < 7; day++) yield return ["visits_weekday_hour", $"{day}/{hour}", report.VisitHeatmap[day, hour]];
        }
        yield return ["loyalty", "new_browsers", report.Loyalty.NewVisitors];
        yield return ["loyalty", "returning_browsers", report.Loyalty.ReturningVisitors];
        yield return ["loyalty", "new_downloaders", report.Loyalty.NewDownloaders];
        yield return ["loyalty", "returning_downloaders", report.Loyalty.ReturningDownloaders];
        yield return ["loyalty", "attributed_visits", report.Loyalty.AttributedVisits];
        yield return ["loyalty", "all_visits", report.Loyalty.AllVisits];
    }
}
