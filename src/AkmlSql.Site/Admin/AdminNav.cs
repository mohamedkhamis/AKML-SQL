using System.Globalization;

namespace AkmlSql.Site.Admin;

/// <summary>One section of the admin portal.</summary>
/// <param name="Route">Absolute path, always under <c>/admin</c>.</param>
/// <param name="Title">Navigation label.</param>
/// <param name="Description">One line describing what the section answers.</param>
public sealed record AdminSection(string Route, string Title, string Description);

/// <summary>
/// The portal's section list — the single source of truth for navigation, and for the test that
/// proves every section is behind the admin cookie (spec 038 US4; contract admin-portal-surface §1/§3).
/// <para>
/// Sections are ordered by what the owner actually asks first: downloads and the people making them
/// lead, page-visit reporting follows. That ordering is the feature's whole point — the previous
/// dashboard led with page views, which the owner described as "fine but not important".
/// </para>
/// </summary>
public static class AdminNav
{
    /// <summary>Every portal section, in display order.</summary>
    public static readonly IReadOnlyList<AdminSection> Sections =
    [
        new("/admin", "Overview", "Headline downloads and audience at a glance."),
        new("/admin/downloads", "Downloads", "Installer downloads by country, version and day."),
        new("/admin/people", "People", "Individuals who visited and what they did."),
        new("/admin/pages", "Pages", "Page visits, entry and exit pages, referrers and 404s."),
        new("/admin/errors", "Errors", "Anonymous client error reports from the desktop product."),
        new("/admin/releases", "Releases", "What is advertised versus what is actually on disk."),
        new("/admin/settings", "Settings", "Release visibility and data retention."),
    ];

    /// <summary>
    /// True when <paramref name="currentPath"/> belongs to <paramref name="section"/>.
    /// <para>
    /// The overview matches only exactly — otherwise <c>/admin</c> would claim every page, since all
    /// of them start with it. Every other section matches on segment boundaries, so
    /// <c>/admin/people/abc123</c> correctly marks "People" active.
    /// </para>
    /// </summary>
    public static bool IsActive(AdminSection section, string? currentPath)
    {
        if (section is null || string.IsNullOrEmpty(currentPath))
        {
            return false;
        }

        var path = currentPath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        if (string.Equals(section.Route, "/admin", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(path, "/admin", StringComparison.OrdinalIgnoreCase);
        }

        return path.Equals(section.Route, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(section.Route + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A portal link carrying the active reporting window.
    /// <para>
    /// This one helper is how the selected range survives navigation (FR-032). The window travels in
    /// the query string rather than session state, which keeps every portal page static-SSR and makes
    /// a chosen range bookmarkable and shareable — the same reasoning
    /// <see cref="AdminDashboardOptions"/> already documents for the dashboard.
    /// </para>
    /// <para>
    /// <b>Every</b> navigation and export link must be built with this. A hard-coded
    /// <c>/admin/...</c> href silently drops the range and breaks FR-032.
    /// </para>
    /// </summary>
    public static string WithRange(string route, int days) =>
        string.IsNullOrEmpty(route)
            ? route
            : $"{route}{(route.Contains('?', StringComparison.Ordinal) ? '&' : '?')}days={days.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The section owning <paramref name="currentPath"/>, or null outside the portal.</summary>
    public static AdminSection? ActiveSection(string? currentPath) =>
        Sections.FirstOrDefault(section => IsActive(section, currentPath));
}
