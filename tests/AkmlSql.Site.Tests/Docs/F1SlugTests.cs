using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Spec 040 (T171, X-03, FR-062) — F1 in the SSMS extension opens <c>https://akml.khamis.work/docs/</c>
/// plus a topic (<c>slug#anchor</c>). The topics are hand-written in the shell source, so a renamed
/// document or heading would turn F1 into a 404 or a page that doesn't scroll to its section with
/// nothing to catch it. Following the <c>FooterDocLinksTests</c> pattern, this reads
/// the shell's help registrations and every Options page's <c>HelpTopic</c> as text and checks each
/// topic against the real docs pipeline: the slug must be a published document, and the anchor a
/// heading id that Markdig's AutoIdentifiers extension actually generates for that document.
/// </summary>
public sealed class F1SlugTests
{
    /// <summary><c>DocUrl("…")</c> arguments and <c>const string …Topic = "…"</c> values.</summary>
    private static readonly Regex RegistrationTopic = new(
        @"DocUrl\(""(?<topic>[^""]+)""\)|const\s+string\s+\w+Topic\s*=\s*""(?<topic>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary><c>public string HelpTopic => "…";</c> on an Options page.</summary>
    private static readonly Regex PageTopic = new(
        @"\bstring\s+HelpTopic\s*=>\s*""(?<topic>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary>An id Markdig gave a heading in the rendered HTML.</summary>
    private static readonly Regex HeadingId = new(
        @"<h[1-6] id=""(?<id>[^""]+)""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryOptionsPageHasAHelpTopic()
    {
        var pagesWithoutTopic = PageFiles()
            .Where(file => File.ReadAllText(file).Contains(": IPageBuilder", StringComparison.Ordinal))
            .Where(file => !PageTopic.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(pagesWithoutTopic.Count == 0,
            "Options pages with no HelpTopic: " + string.Join(", ", pagesWithoutTopic));
    }

    [Fact]
    public void EveryF1TopicNamesAPublishedDocument()
    {
        var topics = AllTopics();
        Assert.NotEmpty(topics); // the regexes must actually be finding topics

        var known = CorpusDocuments().Select(d => d.Slug).ToHashSet(StringComparer.Ordinal);
        var broken = topics
            .Where(t => !known.Contains(Split(t.Topic).Slug))
            .Select(t => $"{t.Topic} ({t.Source})")
            .ToList();

        Assert.True(broken.Count == 0,
            "F1 topics that name no published document (they would 404): " + string.Join(", ", broken)
            + ". Published slugs: " + string.Join(", ", known.OrderBy(s => s, StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryF1AnchorIsAHeadingIdOfItsDocument()
    {
        var documents = CorpusDocuments().ToDictionary(d => d.Slug, StringComparer.Ordinal);
        var docsRoot = Path.Combine(RepositoryRoot(), "doc");
        var renderer = new MarkdownRenderer(docsRoot);
        var idsBySlug = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var anchored = AllTopics().Where(t => Split(t.Topic).Anchor is not null).ToList();
        Assert.NotEmpty(anchored);

        var broken = new List<string>();
        foreach (var (topic, source) in anchored)
        {
            var (slug, anchor) = Split(topic);
            if (!documents.TryGetValue(slug, out var document))
            {
                continue; // reported by EveryF1TopicNamesAPublishedDocument
            }

            if (!idsBySlug.TryGetValue(slug, out var ids))
            {
                var markdown = File.ReadAllText(Path.Combine(docsRoot, document.SourcePath.Replace('/', Path.DirectorySeparatorChar)));
                var html = renderer.Render(markdown, document.SourcePath, document.Title).Html;
                ids = HeadingId.Matches(html).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
                idsBySlug[slug] = ids;
            }

            if (!ids.Contains(anchor!))
            {
                broken.Add($"{topic} ({source}); headings in {document.SourcePath}: {string.Join(", ", ids.OrderBy(i => i, StringComparer.Ordinal))}");
            }
        }

        Assert.True(broken.Count == 0,
            "F1 anchors with no matching heading (the page would open at the top): " + string.Join(" | ", broken));
    }

    /// <summary>The Options topic doc has one section per page, so each page's F1 lands on its own heading.</summary>
    [Fact]
    public void EveryOptionsPageHasItsOwnAnchor()
    {
        var pageTopics = PageFiles()
            .SelectMany(file => PageTopic.Matches(File.ReadAllText(file)).Select(m => m.Groups["topic"].Value))
            .ToList();

        Assert.All(pageTopics, topic => Assert.NotNull(Split(topic).Anchor));
        Assert.Equal(pageTopics.Count, pageTopics.Distinct(StringComparer.Ordinal).Count());
    }

    private static (string Slug, string? Anchor) Split(string topic)
    {
        var hash = topic.IndexOf('#');
        return hash < 0 ? (topic, null) : (topic[..hash], topic[(hash + 1)..]);
    }

    /// <summary>Every topic in the help registrations and on the Options pages, with the file it came from.</summary>
    private static List<(string Topic, string Source)> AllTopics()
    {
        var topics = new List<(string Topic, string Source)>();

        var registrations = Path.Combine(ShellSharedDirectory(), "Help", "F1HelpRegistrations.cs");
        topics.AddRange(RegistrationTopic.Matches(File.ReadAllText(registrations))
            .Select(m => (m.Groups["topic"].Value, Path.GetFileName(registrations))));

        foreach (var file in PageFiles())
        {
            topics.AddRange(PageTopic.Matches(File.ReadAllText(file))
                .Select(m => (m.Groups["topic"].Value, Path.GetFileName(file))));
        }

        return topics.Distinct().ToList();
    }

    private static IEnumerable<string> PageFiles() =>
        Directory.EnumerateFiles(Path.Combine(ShellSharedDirectory(), "Dialogs", "Pages"), "*.cs")
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>The documents the real docs pipeline publishes from <c>doc/</c> under the configured exclusions.</summary>
    private static IReadOnlyList<Document> CorpusDocuments() =>
        DocsCatalog.Scan(Path.Combine(RepositoryRoot(), "doc"), LoadDocsOptions());

    /// <summary>Reads the Docs section straight from appsettings.json, exclusions included.</summary>
    private static DocsOptions LoadDocsOptions()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AkmlSql.Site", "appsettings.json")));

        return document.RootElement.GetProperty(DocsOptions.SectionName).Deserialize<DocsOptions>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new DocsOptions();
    }

    private static string ShellSharedDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "AkmlSql.Shell.Shared");

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
