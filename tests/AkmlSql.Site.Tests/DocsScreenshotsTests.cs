using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Site.Tests;

/// <summary>
/// The docs topic pages show the same SSMS screenshots as the Home and Features pages. The docs
/// copies live in <c>doc/images/screenshots/</c> — relative to the Markdown, so they render on
/// GitHub too, and the site publishes them under <c>/docs-assets/</c> where they open in the
/// screenshot viewer. A recapture of the site's screenshots must not leave the docs on the old
/// ones, and nothing else would report it: these tests do.
/// </summary>
public sealed class DocsScreenshotsTests
{
    // Markdown image: ![alt](path "optional title")
    private static readonly Regex MarkdownImage = new(@"!\[[^\]]*\]\((?<path>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);

    [Fact]
    public void DocsScreenshots_AreIdenticalToTheSitesDarkCaptures()
    {
        var root = RepositoryRoot();
        var docs = Path.Combine(root, "doc", "images", "screenshots");
        var site = Path.Combine(root, "src", "AkmlSql.Site", "wwwroot", "img", "screenshots", "dark");

        var files = Directory.GetFiles(docs, "*.png");
        Assert.NotEmpty(files);
        foreach (var docsFile in files)
        {
            var name = Path.GetFileName(docsFile);
            var siteFile = Path.Combine(site, name);
            Assert.True(File.Exists(siteFile), $"doc/images/screenshots/{name} has no site original at {siteFile}.");
            Assert.True(
                File.ReadAllBytes(docsFile).AsSpan().SequenceEqual(File.ReadAllBytes(siteFile)),
                $"doc/images/screenshots/{name} differs from the site's capture — copy " +
                $"src/AkmlSql.Site/wwwroot/img/screenshots/dark/{name} over it.");
        }
    }

    [Fact]
    public void EveryImageATopicPageShows_Exists()
    {
        var root = RepositoryRoot();
        var topics = Path.Combine(root, "doc", "topics");
        var missing = new List<string>();
        var count = 0;

        foreach (var page in Directory.GetFiles(topics, "*.md"))
        {
            foreach (Match m in MarkdownImage.Matches(File.ReadAllText(page)))
            {
                count++;
                var target = Path.GetFullPath(Path.Combine(topics, m.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(target))
                {
                    missing.Add($"{Path.GetFileName(page)}: {m.Groups["path"].Value}");
                }
            }
        }

        Assert.True(count > 0, "No topic page shows an image — the screenshots were removed from the docs.");
        Assert.True(missing.Count == 0, "Topic pages reference missing images (they would render broken):\n" + string.Join("\n", missing));
    }

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
