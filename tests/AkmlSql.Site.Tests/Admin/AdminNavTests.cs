using AkmlSql.Site.Admin;
using Xunit;

namespace AkmlSql.Site.Tests.Admin;

/// <summary>
/// Spec 038 T018: the portal's section list and range propagation (US4).
/// <para>
/// The section list is also the input to the authorization enumeration test — every route living
/// under <c>/admin</c> is what makes <see cref="AdminBranchMiddleware"/> cover new sections
/// automatically, rather than each one needing to remember its own guard.
/// </para>
/// </summary>
public sealed class AdminNavTests
{
    [Fact]
    public void EverySection_LivesUnderAdmin_SoTheExistingGuardCoversIt()
    {
        Assert.NotEmpty(AdminNav.Sections);

        foreach (var section in AdminNav.Sections)
        {
            Assert.StartsWith("/admin", section.Route, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
            Assert.False(string.IsNullOrWhiteSpace(section.Description));
        }
    }

    [Fact]
    public void Sections_HaveUniqueRoutesAndTitles()
    {
        Assert.Equal(
            AdminNav.Sections.Count,
            AdminNav.Sections.Select(s => s.Route).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            AdminNav.Sections.Count,
            AdminNav.Sections.Select(s => s.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Sections_LeadWithDownloadsAndPeople_NotPageVisits()
    {
        // FR-030: page-visit reporting is retained but must not occupy the lead position.
        var titles = AdminNav.Sections.Select(s => s.Title).ToList();

        Assert.Equal("Overview", titles[0]);
        Assert.True(
            titles.IndexOf("Downloads") < titles.IndexOf("Pages"),
            "Downloads must appear before Pages in the portal navigation.");
        Assert.True(
            titles.IndexOf("People") < titles.IndexOf("Pages"),
            "People must appear before Pages in the portal navigation.");
    }

    [Fact]
    public void IsActive_ForOverview_MatchesOnlyTheExactPath()
    {
        var overview = AdminNav.Sections.Single(s => s.Route == "/admin");

        Assert.True(AdminNav.IsActive(overview, "/admin"));
        Assert.True(AdminNav.IsActive(overview, "/admin/"));

        // Without the exact-match rule the overview would claim every portal page.
        Assert.False(AdminNav.IsActive(overview, "/admin/people"));
        Assert.False(AdminNav.IsActive(overview, "/admin/settings"));
    }

    [Fact]
    public void IsActive_ForASection_MatchesItsChildRoutes()
    {
        var people = AdminNav.Sections.Single(s => s.Route == "/admin/people");

        Assert.True(AdminNav.IsActive(people, "/admin/people"));
        Assert.True(AdminNav.IsActive(people, "/admin/people/3f2a9c"));

        // Segment boundary, not prefix: /admin/peoplexyz is a different route.
        Assert.False(AdminNav.IsActive(people, "/admin/peoplexyz"));
        Assert.False(AdminNav.IsActive(people, "/admin/pages"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsActive_WithNoPath_IsFalse(string? path)
    {
        Assert.False(AdminNav.IsActive(AdminNav.Sections[0], path));
    }

    [Fact]
    public void WithRange_AppendsTheWindowToEveryLink()
    {
        Assert.Equal("/admin/people?days=7", AdminNav.WithRange("/admin/people", 7));
        Assert.Equal("/admin?days=30", AdminNav.WithRange("/admin", 30));
    }

    [Fact]
    public void WithRange_OnALinkThatAlreadyHasAQuery_UsesAmpersand()
    {
        Assert.Equal(
            "/admin/people?country=EG&days=90",
            AdminNav.WithRange("/admin/people?country=EG", 90));
    }

    [Fact]
    public void ActiveSection_ResolvesTheOwningSection()
    {
        Assert.Equal("/admin/people", AdminNav.ActiveSection("/admin/people/abc")?.Route);
        Assert.Equal("/admin", AdminNav.ActiveSection("/admin")?.Route);
        Assert.Null(AdminNav.ActiveSection("/download"));
    }
}
