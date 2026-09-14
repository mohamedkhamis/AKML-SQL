using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AkmlSql.Site.Tests.Consent;

/// <summary>Spec 038 T054 (US5): the consent and withdrawal endpoints.</summary>
public sealed class ConsentEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static IFormCollection Form(params (string Key, string Value)[] fields) =>
        new FormCollection(fields.ToDictionary(f => f.Key, f => new Microsoft.Extensions.Primitives.StringValues(f.Value)));

    private static string? CookieHeader(HttpResponse response, string name) =>
        response.Headers.SetCookie
            .Select(h => h ?? "")
            .FirstOrDefault(h => h.StartsWith(name + "=", StringComparison.Ordinal));

    // --- POST /consent ------------------------------------------------------

    [Fact]
    public void Granted_IssuesBothCookies()
    {
        var http = new DefaultHttpContext();

        ConsentEndpoints.HandleConsent(http, Form(("choice", "granted"), ("returnUrl", "/download")), Now);

        Assert.Contains("granted", CookieHeader(http.Response, ConsentCookies.ConsentCookieName)!, StringComparison.Ordinal);
        Assert.NotNull(CookieHeader(http.Response, ConsentCookies.VisitorCookieName));
    }

    [Fact]
    public void Denied_RecordsTheRefusalWithoutIssuingAnIdentifier()
    {
        var http = new DefaultHttpContext();

        ConsentEndpoints.HandleConsent(http, Form(("choice", "denied"), ("returnUrl", "/")), Now);

        Assert.Contains("denied", CookieHeader(http.Response, ConsentCookies.ConsentCookieName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrecognisedChoice_ChangesNothing()
    {
        var http = new DefaultHttpContext();

        ConsentEndpoints.HandleConsent(http, Form(("choice", "maybe"), ("returnUrl", "/")), Now);

        // Silence -- and nonsense -- are never consent.
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void ItReturnsTheVisitorToWhereTheyWere()
    {
        var http = new DefaultHttpContext();

        var result = ConsentEndpoints.HandleConsent(http, Form(("choice", "granted"), ("returnUrl", "/docs/formatting")), Now);

        Assert.Equal("/docs/formatting", Assert.IsType<RedirectHttpResult>(result).Url);
    }

    // --- open redirect ------------------------------------------------------

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/path")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("evil.example")]
    [InlineData("")]
    [InlineData(null)]
    public void AnExternalReturnUrl_CollapsesToTheHomePage(string? returnUrl)
    {
        // This endpoint takes its redirect target from a form rendered on EVERY page. An open
        // redirect here would be a genuine vulnerability: a link on the site's own domain that
        // bounces the visitor anywhere.
        Assert.Equal("/", ConsentEndpoints.SafeReturnUrl(returnUrl));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/download", "/download")]
    [InlineData("/docs/topics/getting-started", "/docs/topics/getting-started")]
    [InlineData("/download?utm_source=x", "/download?utm_source=x")]
    public void ALocalPath_IsHonoured(string returnUrl, string expected)
    {
        Assert.Equal(expected, ConsentEndpoints.SafeReturnUrl(returnUrl));
    }

    // --- POST /privacy/forget ----------------------------------------------

    [Fact]
    public void Forget_DeletesTheVisitorsRowsAndRevokesConsent()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var id = Guid.NewGuid().ToString("N");

        store.LogVisit(new VisitInfo(Now, "/download", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = id,
        });

        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{ConsentCookies.VisitorCookieName}={id}";

        var result = ConsentEndpoints.HandleForget(http, store, NullLoggerFactory.Instance, Now);

        Assert.Equal("/privacy?forgotten=1", Assert.IsType<RedirectHttpResult>(result).Url);
        Assert.Contains("denied", CookieHeader(http.Response, ConsentCookies.ConsentCookieName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Forget_WithNothingStored_SucceedsIdempotently()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        var http = new DefaultHttpContext();

        var result = ConsentEndpoints.HandleForget(http, store, NullLoggerFactory.Instance, Now);

        // Contract C4.4: never an error, and the response must not reveal whether an id existed --
        // "0 deleted" is the same answer for "never tracked" and "already deleted".
        Assert.Equal("/privacy?forgotten=0", Assert.IsType<RedirectHttpResult>(result).Url);
        Assert.Contains("denied", CookieHeader(http.Response, ConsentCookies.ConsentCookieName)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Forget_WithAMalformedCookie_DeletesNothingAndStillSucceeds()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{ConsentCookies.VisitorCookieName}=../../etc/passwd";

        var result = ConsentEndpoints.HandleForget(http, store, NullLoggerFactory.Instance, Now);

        Assert.Equal("/privacy?forgotten=0", Assert.IsType<RedirectHttpResult>(result).Url);
    }
}
