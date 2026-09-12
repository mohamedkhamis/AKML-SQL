using AkmlSql.Site.Consent;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Consent;

/// <summary>Spec 038 T052 (US5): cookie mechanics.</summary>
public sealed class ConsentCookiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> SetCookieHeaders(HttpResponse response) =>
        response.Headers.SetCookie.Select(h => h ?? "").ToList();

    private static string? HeaderFor(HttpResponse response, string name) =>
        SetCookieHeaders(response).FirstOrDefault(h => h.StartsWith(name + "=", StringComparison.Ordinal));

    [Fact]
    public void Grant_SetsBothCookies_WithASafeConfiguration()
    {
        var http = new DefaultHttpContext();

        var visitorId = ConsentCookies.Grant(http.Response, Now);

        var consent = HeaderFor(http.Response, ConsentCookies.ConsentCookieName);
        var vid = HeaderFor(http.Response, ConsentCookies.VisitorCookieName);

        Assert.NotNull(consent);
        Assert.NotNull(vid);
        Assert.Contains("granted", consent!, StringComparison.Ordinal);
        Assert.Contains(visitorId, vid!, StringComparison.Ordinal);

        foreach (var header in new[] { consent!, vid! })
        {
            Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Grant_IssuesAVisitorIdInTheCanonicalShape()
    {
        var http = new DefaultHttpContext();

        var visitorId = ConsentCookies.Grant(http.Response, Now);

        Assert.Equal(32, visitorId.Length);
        Assert.All(visitorId, c => Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void Deny_RemembersTheRefusalWithoutIssuingAnIdentifier()
    {
        var http = new DefaultHttpContext();

        ConsentCookies.Deny(http.Response, Now);

        var consent = HeaderFor(http.Response, ConsentCookies.ConsentCookieName);
        Assert.NotNull(consent);
        Assert.Contains("denied", consent!, StringComparison.Ordinal);

        // Contract C1.1: the whole reason there are two cookies. Remembering a refusal must not
        // require issuing the very identifier being refused.
        var vid = HeaderFor(http.Response, ConsentCookies.VisitorCookieName);
        Assert.NotNull(vid);
        Assert.Contains("expires=Thu, 01 Jan 1970", vid!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("granted", ConsentState.Granted)]
    [InlineData("denied", ConsentState.Denied)]
    [InlineData("", ConsentState.Unknown)]
    [InlineData("true", ConsentState.Unknown)]
    [InlineData("GRANTED", ConsentState.Unknown)]   // exact match only; no fuzzy acceptance
    [InlineData("yes", ConsentState.Unknown)]
    public void Read_MapsOnlyExactValues_AndTreatsEverythingElseAsUnanswered(string cookie, ConsentState expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{ConsentCookies.ConsentCookieName}={cookie}";

        Assert.Equal(expected, ConsentCookies.Read(http.Request));
    }

    [Fact]
    public void Read_WithNoCookie_IsUnknown()
    {
        Assert.Equal(ConsentState.Unknown, ConsentCookies.Read(new DefaultHttpContext().Request));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("<script>")]
    [InlineData("short")]
    [InlineData("ABCDEF01234567890ABCDEF012345678")]   // uppercase is not the canonical shape
    [InlineData("g1234567890123456789012345678901")]   // 'g' is not hex
    public void ReadVisitorId_RejectsAnythingNotInTheCanonicalShape(string cookie)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{ConsentCookies.VisitorCookieName}={cookie}";

        // The value is client-supplied. Accepting it verbatim would let someone choose an id and
        // read or pollute another visitor's row.
        Assert.Null(ConsentCookies.ReadVisitorId(http.Request));
    }

    [Fact]
    public void ReadVisitorId_AcceptsAWellFormedId()
    {
        var id = Guid.NewGuid().ToString("N");
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{ConsentCookies.VisitorCookieName}={id}";

        Assert.Equal(id, ConsentCookies.ReadVisitorId(http.Request));
    }

    [Fact]
    public void CookieLifetime_DoesNotOutliveTheDataItPointsAt()
    {
        // Contract C1.3: a cookie that survives past the retention period is a dangling identifier.
        Assert.Equal(AkmlSql.Site.Settings.SiteSettings.DefaultRetentionDays, ConsentCookies.LifetimeDays);
    }
}
