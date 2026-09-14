using AkmlSql.Site.Admin;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Admin;

/// <summary>
/// Spec 038 T090 (US4): every portal surface is behind the admin cookie (FR-033, SC-011).
/// <para>
/// The list is driven from <see cref="AdminNav.Sections"/> wherever possible, so a section added
/// later without a guard fails this test rather than shipping an open page. That matters more than
/// usual here: these pages now display full IP addresses and persistent identifiers.
/// </para>
/// </summary>
public sealed class PortalAuthorizationTests
{
    private static bool RequiresChallenge(string pathAndQuery, string method = "GET")
    {
        var http = new DefaultHttpContext();

        // Path and QueryString are separate in ASP.NET Core. Folding the query into Path would make
        // "/admin/login?error=1" a single opaque segment, so the guard's StartsWithSegments check
        // would not recognise it -- and the test would be asserting something the server never sees.
        var split = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        http.Request.Path = split < 0 ? pathAndQuery : pathAndQuery[..split];
        http.Request.QueryString = split < 0 ? QueryString.Empty : new QueryString(pathAndQuery[split..]);
        http.Request.Method = method;

        return AdminBranchMiddleware.RequiresChallenge(http);
    }

    [Fact]
    public void EverySectionInTheNavigation_IsGuarded()
    {
        foreach (var section in AdminNav.Sections)
        {
            Assert.True(
                RequiresChallenge(section.Route),
                $"{section.Route} ({section.Title}) is reachable without signing in.");
        }
    }

    [Theory]
    // Pages not in the nav list (child routes).
    [InlineData("/admin/people/3f2a9cdeadbeef01")]
    // Exports -- these carry addresses and identifiers.
    [InlineData("/admin/metrics.csv")]
    [InlineData("/admin/downloads.csv")]
    [InlineData("/admin/people.csv")]
    [InlineData("/admin/pages.csv")]
    // Query strings must not provide a way around the guard.
    [InlineData("/admin/people.csv?days=365&country=EG")]
    [InlineData("/admin/settings?saved=1")]
    public void EveryOtherPortalSurface_IsGuarded(string path)
    {
        Assert.True(RequiresChallenge(path), $"{path} is reachable without signing in.");
    }

    [Theory]
    [InlineData("/admin/settings", "POST")]
    [InlineData("/admin/people/abc/delete", "POST")]
    [InlineData("/admin/logout", "POST")]
    public void EveryPortalMutation_IsGuarded(string path, string method)
    {
        Assert.True(RequiresChallenge(path, method), $"{method} {path} is reachable without signing in.");
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin/login?error=1")]
    public void OnlyTheSignInPageIsOpen(string path)
    {
        Assert.False(RequiresChallenge(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/download")]
    [InlineData("/privacy")]
    [InlineData("/docs")]
    [InlineData("/dl/AKMLSQLSetup-1.26.0910.2248.exe")]
    [InlineData("/consent")]
    [InlineData("/privacy/forget")]
    public void PublicSurfaces_AreNotGuarded(string path)
    {
        // The mirror image: the consent and withdrawal endpoints must stay reachable, or a visitor
        // could not exercise a data right without an admin account.
        Assert.False(RequiresChallenge(path));
    }

    [Fact]
    public void APathThatMerelyStartsWithAdmin_IsNotTreatedAsThePortal()
    {
        // Segment boundaries, not prefixes: /administrator is a different route, and treating it as
        // portal would be a (harmless) false positive -- but /adminx must not bypass the guard by
        // accident either way.
        Assert.False(RequiresChallenge("/administrator"));
        Assert.False(RequiresChallenge("/adminx"));
    }
}
