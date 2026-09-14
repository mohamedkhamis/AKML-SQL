using System.Globalization;
using System.Security.Claims;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AkmlSql.Site.Admin;

/// <summary>
/// POST endpoints for the admin portal (GETs are Razor pages). The login POST takes an
/// <see cref="IFormCollection"/> parameter so the antiforgery middleware enforces the token the
/// login page renders — form-action is additionally pinned to 'self' by the site CSP.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Logger category for the sign-in audit trail (SEC-003).</summary>
    public const string AuditLoggerName = "AkmlSql.Site.Admin.Login";

    /// <summary>Registers the admin POST endpoints and the CSV export.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/login", HandleLogin);

        // Spec 038 T035 (US2): the release-visibility / retention save. IFormCollection so the
        // antiforgery middleware enforces the token the settings page renders, exactly as the login
        // POST does. It lives under /admin, so AdminBranchMiddleware already guards it -- there is
        // deliberately no second authorization path to get wrong (contract A2.1).
        endpoints.MapPost("/admin/settings", HandleSettings);

        // Spec 038 T073 (US3): delete every record for one individual (FR-040). Antiforgery via
        // IFormCollection, and guarded by AdminBranchMiddleware like everything under /admin.
        endpoints.MapPost("/admin/people/{visitorId}/delete", (
            HttpContext http,
            string visitorId,
            IFormCollection form,
            AnalyticsStore store,
            ILoggerFactory loggerFactory) =>
        {
            var deleted = store.DeleteVisitor(visitorId);

            // The row count is the audit fact; the identifier is NOT logged. Writing a persistent
            // visitor id into the application log would put it somewhere the consent gate does not
            // reach — logs are read by anyone with server access and get shipped to support
            // channels (contract C7.1). The owner initiated this deletion, so they already know
            // whose record it was.
            loggerFactory.CreateLogger(AuditLoggerName).LogInformation(
                "Admin deleted {Rows} row(s) for one individual.", deleted);

            var days = AdminDashboardOptions.NormalizeDays(
                int.TryParse(form["days"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null);
            return Results.Redirect($"/admin/people?days={days}&deleted={deleted}");
        });

        // Spec 038 T072 (US3): per-view exports. All under /admin, so one guard covers them; all
        // no-store, matching the existing metrics.csv.
        endpoints.MapGet("/admin/downloads.csv", (HttpContext http, AnalyticsStore store, string? days) =>
        {
            var window = NormalizeDaysQuery(days);
            var now = DateTimeOffset.UtcNow;
            // Two sections in one file: country breakdown, then release breakdown, separated by a
            // blank line so a spreadsheet import treats them as distinct blocks.
            var csv = IndividualsExport.CountriesToCsv(store.GetDownloadsByCountry(window, now), window, now)
                      + "\n"
                      + IndividualsExport.VersionsToCsv(store.GetDownloadsByVersion(window, now), window, now);

            http.Response.Headers.CacheControl = "no-store";
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(csv),
                "text/csv",
                IndividualsExport.FileName("downloads", null, window, now));
        });

        endpoints.MapGet("/admin/people.csv", (
            HttpContext http, AnalyticsStore store, string? days, string? country, string? downloaded) =>
        {
            var window = NormalizeDaysQuery(days);
            var now = DateTimeOffset.UtcNow;

            // The export must describe the SAME set the page is showing, filters included -- an
            // unfiltered dump beside a filtered view is how the two get confused (contract M6.1).
            var filter = new IndividualFilter(
                window,
                string.IsNullOrWhiteSpace(country) ? null : country,
                downloaded switch { "yes" => true, "no" => false, _ => null },
                Page: 0,
                PageSize: 500);

            var csv = IndividualsExport.IndividualsToCsv(
                store.GetIndividuals(filter, now), store.GetCoverage(window, now), filter, now);

            http.Response.Headers.CacheControl = "no-store";
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(csv),
                "text/csv",
                IndividualsExport.FileName("people", filter, window, now));
        });

        endpoints.MapGet("/admin/pages.csv", (HttpContext http, AnalyticsStore store, string? days) =>
        {
            var window = NormalizeDaysQuery(days);
            var summary = store.GetSummary(window);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(MetricsExport.ToCsv(summary)),
                "text/csv",
                MetricsExport.FileName(summary, DateTimeOffset.UtcNow));
        });
        // Cast to Delegate: HandleLogout matches the RequestDelegate shape, which would discard
        // the IResult (analyzer ASP0016) instead of writing the redirect to the response.
        endpoints.MapPost("/admin/logout", (Delegate)HandleLogout);

        // ADM-007: CSV export. Sits under /admin so AdminBranchMiddleware guards it with the same
        // cookie as the dashboard -- no separate authorization path to get wrong.
        // `days` is a string for the same reason as on the dashboard: an unparseable int binds to
        // an error response instead of falling back to the default window.
        endpoints.MapGet("/admin/metrics.csv", (HttpContext http, AnalyticsStore store, string? days) =>
        {
            var requested = int.TryParse(days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
            var summary = store.GetSummary(AdminDashboardOptions.NormalizeDays(requested));
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(MetricsExport.ToCsv(summary)),
                "text/csv",
                MetricsExport.FileName(summary, DateTimeOffset.UtcNow));
        });
    }

    private static async Task<IResult> HandleLogin(
        HttpContext http,
        IFormCollection form,
        IOptions<AdminOptions> options,
        AdminLoginThrottle throttle,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(AuditLoggerName);
        var ip = HttpRequestFacts.ClientIp(http) ?? "";

        // SEC-002: a locked-out IP is rejected immediately. The previous version awaited the
        // back-off inside the request, which let attackers park connections for free.
        //
        // The rejection is a redirect carrying Retry-After rather than a bare 429: this endpoint
        // backs a browser form, and the login page can explain the wait in the site's own chrome.
        // A bare 429 has no body, so UseStatusCodePagesWithReExecute would render the "page not
        // found" page at it — actively misleading. The security property is unchanged either way;
        // the attempt is refused without doing any work.
        var retryAfter = throttle.GetRetryAfter(ip);
        if (retryAfter > TimeSpan.Zero)
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            logger.LogWarning(
                "Admin sign-in throttled for {ClientIp}; {Failures} prior failures, retry after {RetryAfterSeconds}s.",
                ip, throttle.GetFailureCount(ip), seconds);

            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return Results.Redirect("/admin/login?error=throttled&retry=" + seconds.ToString(CultureInfo.InvariantCulture));
        }

        var password = form["password"].ToString();
        if (options.Value.IsConfigured && AdminAuth.Verify(password, options.Value.PasswordHash))
        {
            throttle.Reset(ip);
            logger.LogInformation("Admin sign-in succeeded for {ClientIp}.", ip);

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], AdminAuth.Scheme);
            await http.SignInAsync(AdminAuth.Scheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/admin");
        }

        var failures = throttle.RecordFailure(ip);
        logger.LogWarning(
            "Admin sign-in failed for {ClientIp}; {Failures} failure(s) in the current window. Portal configured: {Configured}.",
            ip, failures, options.Value.IsConfigured);

        return Results.Redirect("/admin/login?error=1");
    }

    /// <summary>
    /// Validates and saves the owner's settings.
    /// <para>
    /// Out-of-range values are rejected with a message naming the bound, never silently clamped:
    /// clamping leaves the owner believing they saved something they did not (contract A5.3).
    /// </para>
    /// </summary>
    private static IResult HandleSettings(
        HttpContext http,
        IFormCollection form,
        SiteSettingsStore settings,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(AuditLoggerName);

        var candidate = settings.Current with
        {
            Visibility = ReleaseVisibility.Parse(form["visibility"].ToString()),
            VisibilityCount = ParseIntOr(form["visibilityCount"].ToString(), settings.Current.VisibilityCount),
            IdentifiableRetentionDays = ParseIntOr(form["retentionDays"].ToString(), settings.Current.IdentifiableRetentionDays),
        };

        var previous = settings.Current;
        var errors = settings.Save(candidate, http.User.Identity?.Name ?? "admin");

        if (errors.Count > 0)
        {
            logger.LogInformation(
                "Admin settings rejected for {ClientIp}: {Errors}",
                HttpRequestFacts.ClientIp(http) ?? "", string.Join(" ", errors));

            return Results.Redirect("/admin/settings?error=" + Uri.EscapeDataString(string.Join("|", errors)));
        }

        // FR-035: what changed, when, and from which session.
        logger.LogInformation(
            "Admin settings changed by {User} from {ClientIp}: visibility {OldVisibility}/{OldCount} -> {NewVisibility}/{NewCount}, retention {OldDays} -> {NewDays} days.",
            http.User.Identity?.Name ?? "admin",
            HttpRequestFacts.ClientIp(http) ?? "",
            previous.Visibility, previous.VisibilityCount,
            candidate.Visibility, candidate.VisibilityCount,
            previous.IdentifiableRetentionDays, candidate.IdentifiableRetentionDays);

        return Results.Redirect("/admin/settings?saved=1");
    }

    /// <summary>
    /// Normalises a `days` query value. Bound as a string deliberately: an unparseable int binds to
    /// an error response instead of falling back to the default window.
    /// </summary>
    private static int NormalizeDaysQuery(string? days) =>
        AdminDashboardOptions.NormalizeDays(
            int.TryParse(days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null);

    /// <summary>Form input is user input: an unparseable number keeps the current value rather than erroring.</summary>
    private static int ParseIntOr(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static async Task<IResult> HandleLogout(HttpContext http, ILoggerFactory loggerFactory)
    {
        await http.SignOutAsync(AdminAuth.Scheme);
        loggerFactory.CreateLogger(AuditLoggerName)
            .LogInformation("Admin signed out from {ClientIp}.", HttpRequestFacts.ClientIp(http) ?? "");
        return Results.Redirect("/admin/login");
    }
}
