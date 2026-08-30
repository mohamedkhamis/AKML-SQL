using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// DOC-001 guard: the footer hand-writes links into the docs corpus, so excluding a document
/// turns them into 404s with nothing to catch it — <c>/docs/architecture</c> broke exactly that
/// way when the contributor docs were unpublished. This asserts every hardcoded <c>/docs/…</c>
/// slug in the layout still resolves against the real corpus.
/// </summary>
public sealed class FooterDocLinksTests
{
    [Fact]
    public void EveryHardcodedDocLinkInTheLayoutResolvesToARealDocument()
    {
        var slugs = HardcodedDocSlugs();
        Assert.NotEmpty(slugs); // the regex must actually be finding links

        var known = RealCorpusSlugs();
        var broken = slugs.Where(slug => !known.Contains(slug)).ToList();

        Assert.True(
            broken.Count == 0,
            "MainLayout links to documents that are not published (they would 404): "
            + string.Join(", ", broken.Select(s => "/docs/" + s))
            + ". Published slugs: " + string.Join(", ", known.OrderBy(s => s)));
    }

    /// <summary><c>/docs/{slug}</c> hrefs written literally in MainLayout, excluding bare /docs.</summary>
    private static List<string> HardcodedDocSlugs()
    {
        var layout = Path.Combine(SiteProjectDirectory(), "Components", "Layout", "MainLayout.razor");

        return Regex.Matches(File.ReadAllText(layout), @"href=""/docs/(?<slug>[^""]+)""")
            .Select(m => m.Groups["slug"].Value.Trim('/'))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Slugs the real docs pipeline produces from the repository's <c>doc/</c> folder under the
    /// configured exclusions — the same catalog the running site serves.
    /// </summary>
    private static HashSet<string> RealCorpusSlugs()
    {
        var options = LoadDocsOptions();
        var docsRoot = Path.Combine(RepositoryRoot(), "doc");
        var documents = DocsCatalog.Scan(docsRoot, options);

        return new HashSet<string>(documents.Select(d => d.Slug), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads the Docs section straight from appsettings.json, exclusions included.</summary>
    private static DocsOptions LoadDocsOptions()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SiteProjectDirectory(), "appsettings.json")));

        var section = document.RootElement.GetProperty(DocsOptions.SectionName);

        // Deserialized rather than config-bound to keep the test project free of the configuration
        // packages; the "_note" key in the section is ignored as an unmapped property.
        return section.Deserialize<DocsOptions>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new DocsOptions();
    }

    private static string SiteProjectDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "AkmlSql.Site");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AkmlSql.Site", "AkmlSql.Site.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
