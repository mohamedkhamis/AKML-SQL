namespace AkmlSql.Site.Consent;

/// <summary>
/// All cookie mechanics for the consent flow, in one place (spec 038 T044; contract
/// consent-and-identity §1).
/// <para>
/// <b>Two cookies, deliberately independent.</b> The consent cookie remembers the choice; the
/// identity cookie is issued only when that choice is "granted". Storing the refusal inside the
/// identity cookie would mean issuing the very thing the visitor refused — so a visitor who declines
/// is remembered, never asked again, and carries no identifier (FR-046, contract C1.1).
/// </para>
/// </summary>
public static class ConsentCookies
{
    /// <summary>Records granted/denied. Set for every visitor who answers, including those who decline.</summary>
    public const string ConsentCookieName = "akml.consent";

    /// <summary>The persistent individual id. Issued ONLY with consent.</summary>
    public const string VisitorCookieName = "akml.vid";

    /// <summary>
    /// Lifetime of both cookies, in days. Matches the default identifiable retention period on
    /// purpose: a cookie that outlives the data it points at is a dangling identifier (contract C1.3).
    /// </summary>
    public const int LifetimeDays = 365;

    /// <summary>
    /// Options shared by both cookies.
    /// <para>
    /// <c>HttpOnly</c> because no client script reads either value — the banner is a form POST, and
    /// the site CSP forbids inline script anyway, so there is nothing to gain by exposing them to
    /// the document and a real XSS cost if one ever existed (contract C1.2).
    /// </para>
    /// </summary>
    public static CookieOptions Build(DateTimeOffset now) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = now.AddDays(LifetimeDays),
        IsEssential = false,
    };

    /// <summary>The visitor's recorded choice; <see cref="ConsentState.Unknown"/> when absent or malformed.</summary>
    public static ConsentState Read(HttpRequest request) =>
        request is null ? ConsentState.Unknown : ConsentStates.Parse(request.Cookies[ConsentCookieName]);

    /// <summary>
    /// The persistent visitor id, or null when absent or malformed.
    /// <para>
    /// The value is client-supplied, so it is validated rather than trusted: exactly 32 lowercase
    /// hex characters, the shape <c>Guid.ToString("N")</c> produces. Anything else is treated as
    /// absent, which makes that visitor a NEW individual rather than letting an attacker choose an
    /// id and read — or pollute — someone else's row.
    /// </para>
    /// </summary>
    public static string? ReadVisitorId(HttpRequest request)
    {
        var raw = request?.Cookies[VisitorCookieName];
        if (raw is null || raw.Length != 32)
        {
            return null;
        }

        foreach (var c in raw)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return null;
            }
        }

        return raw;
    }

    /// <summary>Issues a fresh visitor id in the canonical shape.</summary>
    public static string NewVisitorId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Records consent and issues the identity cookie.
    /// </summary>
    /// <returns>The visitor id now in force.</returns>
    public static string Grant(HttpResponse response, DateTimeOffset now, string? existingVisitorId = null)
    {
        var options = Build(now);
        response.Cookies.Append(ConsentCookieName, ConsentStates.Granted, options);

        var visitorId = existingVisitorId ?? NewVisitorId();
        response.Cookies.Append(VisitorCookieName, visitorId, options);
        return visitorId;
    }

    /// <summary>
    /// Records refusal and removes the identity cookie.
    /// <para>
    /// Deleting <c>akml.vid</c> is what makes withdrawal real rather than cosmetic: after this the
    /// visitor cannot be re-linked to their previous rows, and FR-022b forbids reconstructing the
    /// link by address or user-agent.
    /// </para>
    /// </summary>
    public static void Deny(HttpResponse response, DateTimeOffset now)
    {
        response.Cookies.Append(ConsentCookieName, ConsentStates.Denied, Build(now));
        response.Cookies.Delete(VisitorCookieName, new CookieOptions
        {
            Path = "/",
            Secure = true,
            SameSite = SameSiteMode.Lax,
        });
    }
}
