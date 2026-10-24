namespace AkmlSql.PostgreSql.Reference;

/// <summary>
/// The one rule for a link into the PostgreSQL manual, shared by the analysis rules, the reference validator and the reference
/// generator (which compiles this file in, rather than referencing the library). Only a chapter-level section (an SGML
/// <c>sect1</c>) is a page of its own: <c>docs/18/sql-syntax-lexical.html</c>. A nested section is an anchor on its page,
/// written in upper case as the manual writes it: <c>docs/18/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS</c>.
/// </summary>
internal static class PgManual
{
    private const string Root = "https://www.postgresql.org/docs/";

    /// <summary>The link to <paramref name="page"/>, or to <paramref name="section"/> on it when the section is nested.</summary>
    public static string Url(string version, string page, string? section = null) =>
        section is null || section == page
            ? Root + version + "/" + page + ".html"
            : Root + version + "/" + page + ".html#" + section.ToUpperInvariant();

    /// <summary>The link for <c>"page"</c> or <c>"page#section"</c>.</summary>
    public static string Url(string version, string pageAndSection)
    {
        var hash = pageAndSection.IndexOf('#');
        // The section argument is passed explicitly: Url(version, pageAndSection) would bind to this overload again.
        return hash < 0 ? Url(version, pageAndSection, null) : Url(version, pageAndSection[..hash], pageAndSection[(hash + 1)..]);
    }

    /// <summary>True when <paramref name="url"/> is the manual link for <paramref name="sectionId"/>: its own page, or its anchor on a page.</summary>
    public static bool IsLinkFor(string url, string version, string sectionId)
    {
        var prefix = Root + version + "/";
        if (url == prefix + sectionId + ".html") return true;

        var anchor = ".html#" + sectionId.ToUpperInvariant();
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(anchor, StringComparison.Ordinal)) return false;
        var page = url[prefix.Length..^anchor.Length];
        return page.Length > 0 && page.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }
}
