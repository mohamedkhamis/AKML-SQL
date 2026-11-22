using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Spec 047 (DOC-001…003, contracts/cli-docs-a11y.md §3) — the published documentation reads well where it is found: every
/// page has a summary a search result can show, no link points at a file the site does not publish, the troubleshooting
/// page has an entry for each web symptom a user meets, and the connecting page says where a remembered password is kept.
/// Before, configuration's summary was a fragment, it linked to the unpublished <c>deployment.md#web-edition</c>, and the
/// web symptoms had no entries.
/// <para>The checks read the real <c>doc/</c> tree with the site's own exclusions, rendered the way the site renders it.</para>
/// </summary>
public sealed class DocsQualityTests
{
    private static readonly Lazy<IReadOnlyList<Document>> Published = new(RenderPublished);

    private static IReadOnlyList<Document> RenderPublished()
    {
        var root = Path.Combine(RepositoryRoot(), "doc");
        var documents = DocsCatalog.Scan(root, LoadDocsOptions());
        var routes = documents.ToDictionary(d => d.SourcePath.ToLowerInvariant(), d => d.Slug, StringComparer.Ordinal);
        var renderer = new MarkdownRenderer(root, routes);
        foreach (var document in documents)
        {
            var markdown = File.ReadAllText(Path.Combine(root, document.SourcePath.Replace('/', Path.DirectorySeparatorChar)));
            var rendered = renderer.Render(markdown, document.SourcePath, document.Title, document.Route);
            document.HtmlContent = rendered.Html;
            document.PlainText = rendered.PlainText;
            document.Headings = rendered.Headings;
            document.Summary = rendered.Summary;
        }
        return documents;
    }

    private static Document Page(string slug) =>
        Published.Value.SingleOrDefault(d => d.Slug == slug) ?? throw new InvalidOperationException($"No published page '{slug}'.");

    [Fact]
    public void Every_published_page_has_a_summary_a_search_result_can_show()
    {
        var short_ = Published.Value
            .Where(d => d.Summary.Trim().Length < 50)
            .Select(d => $"{d.SourcePath}: \"{d.Summary}\"")
            .ToList();

        Assert.True(short_.Count == 0, "Summaries under 50 characters: " + string.Join(" | ", short_));
    }

    private static readonly Regex Href = new("href=\"(?<url>[^\"]*)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool Resolved(string url) =>
        url.StartsWith('/') || url.StartsWith('#')
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void No_link_points_at_a_file_the_site_does_not_publish()
    {
        // A relative link the renderer could not turn into a site route stays relative, and on the site it is a 404.
        var unresolved = Published.Value
            .SelectMany(d => Href.Matches(d.HtmlContent).Select(m => (d.SourcePath, Url: m.Groups["url"].Value)))
            .Where(l => !Resolved(l.Url))
            .Select(l => $"{l.SourcePath} -> {l.Url}")
            .ToList();

        Assert.True(unresolved.Count == 0, "Links the site cannot serve: " + string.Join(" | ", unresolved));
    }

    [Theory]
    [InlineData("Handshake failed")]
    [InlineData("No engine paired yet")]
    [InlineData("saved connections are gone")]
    [InlineData("asks for my password again")]
    [InlineData("Stored procedures are missing")]
    public void Troubleshooting_has_an_entry_for_each_web_symptom(string symptom)
    {
        var headings = Page("topics/troubleshooting").Headings;

        Assert.Contains(headings, h => h.Contains(symptom, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Connecting_says_where_a_remembered_password_is_kept()
    {
        var text = Page("topics/connecting").PlainText;

        Assert.Contains("Remember", text);
        Assert.Contains("engine machine", text);
        Assert.Contains("Connecting in the browser", Page("topics/connecting").Headings);
    }

    private static DocsOptions LoadDocsOptions()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AkmlSql.Site", "appsettings.json")));
        return document.RootElement.GetProperty(DocsOptions.SectionName).Deserialize<DocsOptions>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new DocsOptions();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AkmlSql.Site", "AkmlSql.Site.csproj")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
