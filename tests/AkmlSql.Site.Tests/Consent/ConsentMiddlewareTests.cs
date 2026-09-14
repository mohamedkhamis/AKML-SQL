using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Consent;

/// <summary>Spec 038 T053 (US5): consent resolution, once per request, before anything reads it.</summary>
public sealed class ConsentMiddlewareTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static DefaultHttpContext WithCookies(params string[] cookies)
    {
        var http = new DefaultHttpContext();
        if (cookies.Length > 0)
        {
            http.Request.Headers.Cookie = string.Join("; ", cookies);
        }

        return http;
    }

    [Fact]
    public void NoCookies_ResolvesUnknownAndIssuesNothing()
    {
        var http = WithCookies();

        ConsentMiddleware.Resolve(http, Now);

        Assert.Equal(ConsentState.Unknown, ConsentMiddleware.StateOf(http));
        Assert.Null(ConsentMiddleware.VisitorIdOf(http));
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Denied_ResolvesDeniedAndIssuesNothing()
    {
        var http = WithCookies($"{ConsentCookies.ConsentCookieName}=denied");

        ConsentMiddleware.Resolve(http, Now);

        Assert.Equal(ConsentState.Denied, ConsentMiddleware.StateOf(http));
        Assert.Null(ConsentMiddleware.VisitorIdOf(http));

        // A visitor who declined must not be handed an identifier on every subsequent request.
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Granted_WithAValidId_ReusesIt()
    {
        var id = Guid.NewGuid().ToString("N");
        var http = WithCookies(
            $"{ConsentCookies.ConsentCookieName}=granted",
            $"{ConsentCookies.VisitorCookieName}={id}");

        ConsentMiddleware.Resolve(http, Now);

        Assert.Equal(ConsentState.Granted, ConsentMiddleware.StateOf(http));
        Assert.Equal(id, ConsentMiddleware.VisitorIdOf(http));
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Granted_WithNoId_IssuesAFreshOne()
    {
        var http = WithCookies($"{ConsentCookies.ConsentCookieName}=granted");

        ConsentMiddleware.Resolve(http, Now);

        var issued = ConsentMiddleware.VisitorIdOf(http);
        Assert.NotNull(issued);
        Assert.Equal(32, issued!.Length);
        Assert.True(http.Response.Headers.SetCookie.Count > 0);
    }

    [Fact]
    public void Granted_WithAMalformedId_IssuesAFreshOne_AndDoesNotReuseTheBadValue()
    {
        const string Malformed = "not-a-valid-id";
        var http = WithCookies(
            $"{ConsentCookies.ConsentCookieName}=granted",
            $"{ConsentCookies.VisitorCookieName}={Malformed}");

        ConsentMiddleware.Resolve(http, Now);

        var issued = ConsentMiddleware.VisitorIdOf(http);
        Assert.NotNull(issued);
        Assert.NotEqual(Malformed, issued);
        Assert.Equal(32, issued!.Length);
    }

    [Fact]
    public void AVisitorWhoseCookieWasCleared_BecomesANewIndividual_NotTheOldOne()
    {
        // FR-022b. Re-linking by address or user-agent would reconstruct exactly what a visitor
        // clearing their cookies asked to break, so the middleware must not attempt it.
        var first = WithCookies($"{ConsentCookies.ConsentCookieName}=granted");
        ConsentMiddleware.Resolve(first, Now);
        var firstId = ConsentMiddleware.VisitorIdOf(first);

        var second = WithCookies($"{ConsentCookies.ConsentCookieName}=granted");
        second.Connection.RemoteIpAddress = first.Connection.RemoteIpAddress;
        second.Request.Headers.UserAgent = first.Request.Headers.UserAgent;
        ConsentMiddleware.Resolve(second, Now);

        Assert.NotEqual(firstId, ConsentMiddleware.VisitorIdOf(second));
    }

    [Fact]
    public void Resolution_NeverConsultsTheGeoDatabase()
    {
        // FR-043a / contract C2.4: consent is asked of every visitor regardless of country, so a
        // missing or stale geo database must not be able to affect whether it is asked for. The
        // strongest form of that is structural: Resolve takes no GeoLookup and cannot reach one.
        var parameters = typeof(ConsentMiddleware)
            .GetMethod(nameof(ConsentMiddleware.Resolve))!
            .GetParameters();

        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(GeoLookup));

        var fields = typeof(ConsentMiddleware)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(GeoLookup));
    }

    [Theory]
    [InlineData("EG")]
    [InlineData("GB")]
    [InlineData(null)]
    public void ResolutionIsIdenticalWhateverTheCountry(string? countryCode)
    {
        // The behavioural companion to the structural test above: same inputs, same outcome,
        // whether the visitor resolves to an EU country, a non-EU country, or nowhere at all.
        var http = WithCookies();
        http.Items["country"] = countryCode; // stand-in for any geo enrichment

        ConsentMiddleware.Resolve(http, Now);

        Assert.Equal(ConsentState.Unknown, ConsentMiddleware.StateOf(http));
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void StateOf_WithNoResolution_DefaultsToUnknown()
    {
        // Safe default: an unresolved request writes nothing identifiable.
        Assert.Equal(ConsentState.Unknown, ConsentMiddleware.StateOf(new DefaultHttpContext()));
        Assert.Equal(ConsentState.Unknown, ConsentMiddleware.StateOf(null));
        Assert.Null(ConsentMiddleware.VisitorIdOf(null));
    }
}
