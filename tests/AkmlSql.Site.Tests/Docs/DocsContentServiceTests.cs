using AkmlSql.Site.Docs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Integration tests for <see cref="DocsContentService.Build"/> — the catalog→renderer
/// startup wiring: content/assets split (S3), duplicate-title-H1 drop (U3), and on-page
/// TOC capture (U15).
/// </summary>
public sealed class DocsContentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "akml-docs-service-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup; temp dir
        }
    }

    private void WriteContentFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void Build_ResolvesImagesAgainstAssetsRoot_DropsDuplicateH1_AndCapturesToc()
    {
        WriteContentFile("Content/docs/guide.md", """
            # Guide Title

            Intro paragraph.

            ## First Section

            ![Diagram](images/diagram.png)

            ## Second Section
            """);
        WriteContentFile("Content/docs-assets/images/diagram.png", "not-really-a-png");

        var service = DocsContentService.Build(new StubWebHostEnvironment(_root), new DocsOptions());

        var document = Assert.Single(service.Documents);
        Assert.Equal("Guide Title", document.Title);

        // U3: the source H1 equals the catalog title — the page renders its own <h1>.
        Assert.DoesNotContain("<h1", document.HtmlContent);

        // S3: the image existence check ran against Content/docs-assets/, not Content/docs/.
        Assert.Contains("src=\"/docs-assets/images/diagram.png\"", document.HtmlContent);

        // U15: both H2s captured with ids that match the emitted anchors.
        Assert.Equal(["First Section", "Second Section"], document.Toc.Select(h => h.Text).ToArray());
        Assert.All(document.Toc, h => Assert.Contains($"<h2 id=\"{h.Id}\">", document.HtmlContent));
    }

    [Fact]
    public void Build_MissingContentRoots_YieldsEmptyService()
    {
        // The content root exists but holds no .md files — the service must be empty, never
        // throw. (An absent root would fall back to AppContext.BaseDirectory, which in the
        // test host contains the real docs corpus via the project reference.)
        Directory.CreateDirectory(Path.Combine(_root, "Content", "docs"));

        var service = DocsContentService.Build(new StubWebHostEnvironment(_root), new DocsOptions());

        Assert.True(service.IsEmpty);
    }

    [Fact]
    public void Build_AppliesFreshnessBadges_FromWwwrootDocsMetadata()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        WriteContentFile("Content/docs/fresh.md", "# Fresh Guide\n\nBody.\n");
        WriteContentFile("Content/docs/stale.md", "# Stale Guide\n\nBody.\n");
        WriteContentFile("Content/docs/untouched.md", "# Untouched Guide\n\nBody.\n");
        WriteContentFile("wwwroot/docs-metadata.json", $$"""
            {
              "generatedAt": "2026-08-28T16:00:00Z",
              "docs": {
                "fresh.md": { "added": "{{today:yyyy-MM-dd}}", "updated": "{{today:yyyy-MM-dd}}" },
                "stale.md": { "added": "{{today.AddDays(-90):yyyy-MM-dd}}", "updated": "{{today.AddDays(-5):yyyy-MM-dd}}" }
              }
            }
            """);

        var service = DocsContentService.Build(
            new StubWebHostEnvironment(_root, Path.Combine(_root, "wwwroot")), new DocsOptions());

        // Added within the window -> New; added long ago but changed recently -> Updated.
        Assert.Equal(DocBadge.New, service.Documents.Single(d => d.SourcePath == "fresh.md").Badge);
        Assert.Equal(DocBadge.Updated, service.Documents.Single(d => d.SourcePath == "stale.md").Badge);
        // No metadata entry -> no badge.
        Assert.Equal(DocBadge.None, service.Documents.Single(d => d.SourcePath == "untouched.md").Badge);
        Assert.Equal(DocsOptions.DefaultBadgeWindowDays, service.BadgeWindowDays);
    }

    [Fact]
    public void Build_WithoutDocsMetadata_LeavesAllBadgesNone()
    {
        WriteContentFile("Content/docs/guide.md", "# Guide\n\nBody.\n");

        var service = DocsContentService.Build(new StubWebHostEnvironment(_root), new DocsOptions());

        Assert.Equal(DocBadge.None, Assert.Single(service.Documents).Badge);
    }

    [Fact]
    public void Build_MalformedDocsMetadata_Tolerated_NoBadges()
    {
        WriteContentFile("Content/docs/guide.md", "# Guide\n\nBody.\n");
        WriteContentFile("wwwroot/docs-metadata.json", "{ broken");

        var service = DocsContentService.Build(
            new StubWebHostEnvironment(_root, Path.Combine(_root, "wwwroot")), new DocsOptions());

        Assert.Equal(DocBadge.None, Assert.Single(service.Documents).Badge);
    }

    [Fact]
    public void Build_OrdersSections_PerSectionOrderConfig()
    {
        WriteContentFile("Content/docs/architecture.md", "# Architecture\n");
        WriteContentFile("Content/docs/topics/getting-started.md", "# Getting Started\n");
        var options = new DocsOptions
        {
            SectionTitles = new Dictionary<string, string> { [""] = "Developer Reference", ["topics"] = "User Guides" },
            SectionOrder = ["User Guides", "Developer Reference"],
        };

        var service = DocsContentService.Build(new StubWebHostEnvironment(_root), options);

        Assert.Equal(["User Guides", "Developer Reference"], service.Sections.Select(s => s.Name).ToArray());
    }

    /// <summary>Minimal IWebHostEnvironment stub pointing ContentRootPath at a temp tree.</summary>
    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath, string? webRootPath = null)
        {
            ContentRootPath = contentRootPath;
            if (webRootPath is not null)
            {
                WebRootPath = webRootPath;
                WebRootFileProvider = new PhysicalFileProvider(webRootPath);
            }
        }

        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "AkmlSql.Site.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
