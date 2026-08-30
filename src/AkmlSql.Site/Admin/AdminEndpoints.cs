using System.Globalization;
using System.Security.Claims;
using AkmlSql.Site.Analytics;
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

    /// <summary>Registers <c>POST /admin/login</c> and <c>POST /admin/logout</c>.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/login", HandleLogin);
        // Cast to Delegate: HandleLogout matches the RequestDelegate shape, which would discard
        // the IResult (analyzer ASP0016) instead of writing the redirect to the response.
        endpoints.MapPost("/admin/logout", (Delegate)HandleLogout);
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

    private static async Task<IResult> HandleLogout(HttpContext http, ILoggerFactory loggerFactory)
    {
        await http.SignOutAsync(AdminAuth.Scheme);
        loggerFactory.CreateLogger(AuditLoggerName)
            .LogInformation("Admin signed out from {ClientIp}.", HttpRequestFacts.ClientIp(http) ?? "");
        return Results.Redirect("/admin/login");
    }
}
