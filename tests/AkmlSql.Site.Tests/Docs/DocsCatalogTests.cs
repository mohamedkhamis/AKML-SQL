using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Spec 034 T017 (US2): docs discovery against a fixture folder of fake .md files.
/// Behavior pinned by specs/034-blazor-product-site/contracts/docs-content.md:
/// glob discovery, exclusion list, H1 title + filename fallback, slug rules/dedup,
/// section mapping, ordering, empty source.
/// </summary>
public sealed class DocsCatalogTests : IDisposable
{
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup; temp dir
            }
        }
    }

    private string CreateContentRoot(params (string RelativePath, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-docs-catalog-tests", Guid.NewGuid().ToString("N"));
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        _tempRoots.Add(root);
        return root;
    }

    /// <summary>The production exclusion list from src/AkmlSql.Site/appsettings.json.</summary>
    private static DocsOptions ProductionOptions() => new()
    {
        Exclusions =
        [
            "_Prompt-Gap/",
            "Phase-One/",
            "superpowers/",
            "WEB/",
            "progress.md",
            "bugs.md",
            "manual-test-plan.md",
            "codebase-audit-*.md",
        ],
        SectionTitles = new Dictionary<string, string> { [""] = "Guides" },
    };

    /// <summary>Options with no exclusions, for slug/section tests that use excluded-looking paths.</summary>
    private static DocsOptions NoExclusions() => new()
    {
        Exclusions = [],
        SectionTitles = new Dictionary<string, string> { [""] = "Guides" },
    };

    [Fact]
    public void Scan_DiscoversTopLevelAndNestedMarkdownFiles()
    {
        var root = CreateContentRoot(
            ("architecture.md", "# Architecture Overview\n"),
            ("web/m4-iis-installer.md", "# M4 IIS Installer\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, d => d.SourcePath == "architecture.md");
        Assert.Contains(documents, d => d.SourcePath == "web/m4-iis-installer.md");
    }

    [Fact]
    public void Scan_IgnoresNonMarkdownFiles()
    {
        var root = CreateContentRoot(
            ("architecture.md", "# Architecture Overview\n"),
            ("images/diagram.png", "not-really-a-png"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Single(documents);
    }

    [Fact]
    public void Scan_HonorsExclusionList_FoldersAndFilesAtAnyDepth()
    {
        var root = CreateContentRoot(
            ("keep.md", "# Keep\n"),
            ("progress.md", "# Progress\n"),
            ("bugs.md", "# Bugs\n"),
            ("manual-test-plan.md", "# Manual Test Plan\n"),
            ("codebase-audit-2026-05-05.md", "# Audit\n"),
            ("_Prompt-Gap/notes.md", "# Notes\n"),
            ("Phase-One/plan.md", "# Plan\n"),
            ("superpowers/runbook.md", "# Runbook\n"),
            ("WEB/m4-iis-installer.md", "# M4\n"),
            ("nested/progress.md", "# Nested Progress\n"));

        var documents = DocsCatalog.Scan(root, ProductionOptions());

        var document = Assert.Single(documents);
        Assert.Equal("keep.md", document.SourcePath);
    }

    [Fact]
    public void Scan_ExtractsFirstH1AsTitle()
    {
        var root = CreateContentRoot(
            ("architecture.md", "Intro paragraph without heading.\n\n# Architecture Overview\n\nBody.\n## Later\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("Architecture Overview", Assert.Single(documents).Title);
    }

    [Fact]
    public void Scan_FallsBackToFilenameTitle_WhenNoH1()
    {
        var root = CreateContentRoot(
            ("m3-security.md", "Body only, no heading.\n"),
            ("my_doc.md", "Also no heading.\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("M3 Security", documents.Single(d => d.SourcePath == "m3-security.md").Title);
        Assert.Equal("My Doc", documents.Single(d => d.SourcePath == "my_doc.md").Title);
    }

    [Fact]
    public void Slug_IsRelativePathWithoutExtension_Lowercase_SeparatorsToDashes()
    {
        var root = CreateContentRoot(
            ("WEB/M4-iis-installer.md", "# M4\n"),
            ("my_doc file.md", "# My Doc File\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        var nested = documents.Single(d => d.SourcePath == "WEB/M4-iis-installer.md");
        Assert.Equal("web/m4-iis-installer", nested.Slug);
        Assert.Equal("/docs/web/m4-iis-installer", nested.Route);
        Assert.Equal("my-doc-file", documents.Single(d => d.SourcePath == "my_doc file.md").Slug);
    }

    [Fact]
    public void Slug_DuplicatesGetNumericSuffixes()
    {
        var root = CreateContentRoot(
            ("my-doc.md", "# A\n"),
            ("my_doc.md", "# B\n"),
            ("my doc.md", "# C\n"));

        var slugs = DocsCatalog.Scan(root, NoExclusions()).Select(d => d.Slug).ToList();

        Assert.Equal(3, slugs.Distinct().Count());
        Assert.Contains("my-doc", slugs);
        Assert.Contains("my-doc-2", slugs);
        Assert.Contains("my-doc-3", slugs);
    }

    [Fact]
    public void Section_TopLevelFilesMapToGuides_SubfoldersTitleCased()
    {
        var root = CreateContentRoot(
            ("architecture.md", "# Architecture\n"),
            ("web/m4-iis-installer.md", "# M4\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("Guides", documents.Single(d => d.SourcePath == "architecture.md").Section);
        Assert.Equal("Web", documents.Single(d => d.SourcePath == "web/m4-iis-installer.md").Section);
    }

    [Fact]
    public void Section_HonorsSectionTitlesOverride()
    {
        var root = CreateContentRoot(("web/m4-iis-installer.md", "# M4\n"));
        var options = NoExclusions();
        options.SectionTitles["web"] = "Web Edition";

        var documents = DocsCatalog.Scan(root, options);

        Assert.Equal("Web Edition", Assert.Single(documents).Section);
    }

    [Fact]
    public void Ordering_LeadingNumericPrefixForcesPosition_ThenTitleOrdinalIgnoreCase()
    {
        var root = CreateContentRoot(
            ("zebra.md", "# Zebra\n"),
            ("01-intro.md", "# Intro\n"),
            ("apple.md", "# apple\n"),
            ("banana.md", "# Banana\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());
        var sections = DocsCatalog.BuildSections(documents);

        var section = Assert.Single(sections);
        Assert.Equal("Guides", section.Name);
        Assert.Equal(
            ["Intro", "apple", "Banana", "Zebra"],
            section.Documents.Select(d => d.Title).ToArray());
        Assert.Equal(1, section.Documents[0].Order);
    }

    [Fact]
    public void Sections_AreOrderedByNameOrdinalIgnoreCase()
    {
        var root = CreateContentRoot(
            ("web/a.md", "# A\n"),
            ("top.md", "# Top\n"));

        var sections = DocsCatalog.BuildSections(DocsCatalog.Scan(root, NoExclusions()));

        Assert.Equal(["Guides", "Web"], sections.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Scan_MissingContentRoot_ReturnsEmptyCatalog()
    {
        var missing = Path.Combine(Path.GetTempPath(), "akml-docs-catalog-tests", Guid.NewGuid().ToString("N"));

        var documents = DocsCatalog.Scan(missing, ProductionOptions());

        Assert.Empty(documents);
    }

    [Fact]
    public void Scan_EmptyContentRoot_ReturnsEmptyCatalog()
    {
        var root = CreateContentRoot();

        Assert.Empty(DocsCatalog.Scan(root, ProductionOptions()));
    }

    [Fact]
    public void Slug_RestrictsToUrlSafeCharacters()
    {
        // S6: '#', '%', '&' etc. made docs unreachable ('/docs/a#b' never reaches the server).
        // Non-ASCII letters are stripped to dashes, not transliterated (locale-dependent).
        var root = CreateContentRoot(
            ("a#b.md", "# A Hash B\n"),
            ("100%.md", "# Full\n"),
            ("c&d.md", "# C And D\n"),
            ("café.md", "# Café\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("a-b", documents.Single(d => d.SourcePath == "a#b.md").Slug);
        Assert.Equal("100", documents.Single(d => d.SourcePath == "100%.md").Slug);
        Assert.Equal("c-d", documents.Single(d => d.SourcePath == "c&d.md").Slug);
        Assert.Equal("caf", documents.Single(d => d.SourcePath == "café.md").Slug);
    }

    [Fact]
    public void Slug_ThreeWayDedup_ThirdFileGetsDash3()
    {
        // Ordinal scan order: "my doc.md" → my-doc; "my-doc-2.md" → my-doc-2;
        // "my-doc.md" → my-doc (taken) → my-doc-2 (taken) → my-doc-3.
        var root = CreateContentRoot(
            ("my doc.md", "# A\n"),
            ("my-doc.md", "# B\n"),
            ("my-doc-2.md", "# C\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("my-doc", documents.Single(d => d.SourcePath == "my doc.md").Slug);
        Assert.Equal("my-doc-2", documents.Single(d => d.SourcePath == "my-doc-2.md").Slug);
        Assert.Equal("my-doc-3", documents.Single(d => d.SourcePath == "my-doc.md").Slug);
    }

    [Fact]
    public void Scan_Title_IgnoresHashLinesInsideFencedCodeBlocks()
    {
        // C9: the old line regex matched "# comment" inside code fences; the title now comes
        // from the Markdig AST (first top-level H1).
        var root = CreateContentRoot(
            ("guide.md", "```\n# not a title\n```\n\n# Real Title\n\nBody.\n"),
            ("code-only.md", "```\n# not a title\n```\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        Assert.Equal("Real Title", documents.Single(d => d.SourcePath == "guide.md").Title);
        Assert.Equal("Code Only", documents.Single(d => d.SourcePath == "code-only.md").Title); // filename fallback
    }

    [Fact]
    public void Scan_DiscoversUppercaseMarkdownExtension()
    {
        // C8: "*.md" matching is case-sensitive on Linux — pinned here via EnumerationOptions.
        var root = CreateContentRoot(("UPPER.MD", "# Upper\n"));

        var documents = DocsCatalog.Scan(root, NoExclusions());

        var document = Assert.Single(documents);
        Assert.Equal("Upper", document.Title);
        Assert.Equal("upper", document.Slug);
    }

    [Fact]
    public void Section_ProductionConfig_TopLevelDeveloperReference_TopicsUserGuides()
    {
        // appsettings.json Docs:SectionTitles — top-level "" renamed to "Developer Reference",
        // the topics/ folder mapped to "User Guides".
        var root = CreateContentRoot(
            ("architecture.md", "# Architecture\n"),
            ("topics/getting-started.md", "# Getting Started\n"));
        var options = NoExclusions();
        options.SectionTitles[""] = "Developer Reference";
        options.SectionTitles["topics"] = "User Guides";

        var documents = DocsCatalog.Scan(root, options);

        Assert.Equal("Developer Reference", documents.Single(d => d.SourcePath == "architecture.md").Section);
        Assert.Equal("User Guides", documents.Single(d => d.SourcePath == "topics/getting-started.md").Section);
    }

    [Fact]
    public void Sections_SectionOrder_PinsListedFirst_UnlistedAlphabeticalAfter()
    {
        var root = CreateContentRoot(
            ("architecture.md", "# Architecture\n"),
            ("topics/getting-started.md", "# Getting Started\n"),
            ("web/m4.md", "# M4\n"));
        var options = NoExclusions();
        options.SectionTitles[""] = "Developer Reference";
        options.SectionTitles["topics"] = "User Guides";
        var documents = DocsCatalog.Scan(root, options);

        var sections = DocsCatalog.BuildSections(documents, ["User Guides", "Developer Reference"]);

        // Alphabetical would put "Developer Reference" first; the config pins "User Guides"
        // before it, and the unlisted "Web" section sorts after the pinned ones.
        Assert.Equal(["User Guides", "Developer Reference", "Web"], sections.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Sections_NoSectionOrder_FallsBackToAlphabetical()
    {
        var root = CreateContentRoot(
            ("topics/getting-started.md", "# Getting Started\n"),
            ("architecture.md", "# Architecture\n"));
        var options = NoExclusions();
        options.SectionTitles[""] = "Developer Reference";
        options.SectionTitles["topics"] = "User Guides";
        var documents = DocsCatalog.Scan(root, options);

        var sections = DocsCatalog.BuildSections(documents);

        Assert.Equal(["Developer Reference", "User Guides"], sections.Select(s => s.Name).ToArray());
    }
}
