using AkmlSql.Site.Analytics;

namespace AkmlSql.Site.Consent;

/// <summary>
/// The two public POST endpoints behind the consent bar and the privacy page (spec 038 T046;
/// contract consent-and-identity §4).
/// </summary>
public static class ConsentEndpoints
{
    /// <summary>Logger category for the consent audit trail.</summary>
    public const string AuditLoggerName = "AkmlSql.Site.Consent";

    /// <summary>Registers <c>POST /consent</c> and <c>POST /privacy/forget</c>.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // IFormCollection so the antiforgery middleware enforces the token, exactly as the admin
        // login POST does.
        endpoints.MapPost("/consent", (HttpContext http, IFormCollection form, ConsentRateLimit limit) =>
            limit.TryAcquire(HttpRequestFacts.ClientIp(http))
                ? HandleConsent(http, form, DateTimeOffset.UtcNow)
                : Results.StatusCode(StatusCodes.Status429TooManyRequests));

        endpoints.MapPost("/privacy/forget", (
            HttpContext http,
            IFormCollection form,
            AnalyticsStore store,
            ConsentRateLimit limit,
            ILoggerFactory loggerFactory) =>
            limit.TryAcquire(HttpRequestFacts.ClientIp(http))
                ? HandleForget(http, store, loggerFactory, DateTimeOffset.UtcNow)
                : Results.StatusCode(StatusCodes.Status429TooManyRequests));
    }

    /// <summary>
    /// Records the visitor's choice and returns them to where they were.
    /// <para>
    /// Handler body factored out for tests.
    /// </para>
    /// </summary>
    public static IResult HandleConsent(HttpContext http, IFormCollection form, DateTimeOffset now)
    {
        var choice = form["choice"].ToString();

        switch (ConsentStates.Parse(choice))
        {
            case ConsentState.Granted:
                ConsentCookies.Grant(http.Response, now);
                break;

            case ConsentState.Denied:
                // Both "Decline" and the bar's dismiss control post this: an explicit dismissal is
                // recorded as a refusal, so the visitor is not asked again (contract C2.6).
                ConsentCookies.Deny(http.Response, now);
                break;

            default:
                // An unrecognised choice changes nothing. Silence is never consent.
                break;
        }

        return Results.Redirect(SafeReturnUrl(form["returnUrl"].ToString()));
    }

    /// <summary>
    /// Withdraws consent and deletes everything held about this visitor.
    /// <para>
    /// Unauthenticated by design: the identifier being deleted is the visitor's own, presented by
    /// their own cookie. With no identity cookie present it succeeds idempotently and reports that
    /// nothing was stored — never an error, and never a probe that reveals whether an id exists
    /// (contract C4.4).
    /// </para>
    /// </summary>
    public static IResult HandleForget(
        HttpContext http,
        AnalyticsStore store,
        ILoggerFactory loggerFactory,
        DateTimeOffset now)
    {
        var visitorId = ConsentCookies.ReadVisitorId(http.Request);

        // Withdrawal happens regardless: consent is revoked even if there was nothing stored.
        ConsentCookies.Deny(http.Response, now);

        var deleted = 0;
        if (visitorId is not null)
        {
            try
            {
                deleted = store.DeleteVisitor(visitorId);
            }
            catch (Exception ex)
            {
                // The visitor's withdrawal has already taken effect via the cookie; a storage
                // failure must not present as an error page to someone exercising a data right.
                loggerFactory.CreateLogger(AuditLoggerName)
                    .LogError(ex, "Failed to delete rows for a withdrawing visitor.");
            }
        }

        loggerFactory.CreateLogger(AuditLoggerName)
            .LogInformation("Consent withdrawn; {Rows} row(s) deleted.", deleted);

        return Results.Redirect($"/privacy?forgotten={deleted}");
    }

    /// <summary>
    /// The redirect target, reduced to a safe local path.
    /// <para>
    /// This endpoint takes its redirect target from a form rendered on <b>every page</b>, so an open
    /// redirect here would be a real vulnerability — a link to the site's own domain that bounces
    /// the visitor anywhere. Anything that is not a single-slash-rooted local path collapses to "/".
    /// </para>
    /// </summary>
    public static string SafeReturnUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "/";
        }

        // Must start with exactly one slash: "//evil.example" and "/\evil.example" are
        // protocol-relative URLs, not local paths.
        if (candidate[0] != '/' || (candidate.Length > 1 && (candidate[1] == '/' || candidate[1] == '\\')))
        {
            return "/";
        }

        // A scheme or an authority anywhere in the value means it is not a local path.
        if (candidate.Contains(':', StringComparison.Ordinal))
        {
            return "/";
        }

        return candidate;
    }
}
