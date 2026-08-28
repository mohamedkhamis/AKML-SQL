using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Spec 034 T018 (US2): Markdown rendering pipeline per contracts/docs-content.md —
/// Markdig advanced extensions, ColorCode.Universal server-side highlighting (CSS classes),
/// relative-link rewriting to site routes, image asset resolution, HTML sanitization,
/// malformed-input tolerance.
/// </summary>
public sealed class MarkdownRendererTests : IDisposable
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

    /// <summary>
    /// Creates a renderer whose content root contains <c>images/diagram.png</c> and whose
    /// route map knows <c>ipc-api.md</c> and <c>web/m4-iis-installer.md</c>.
    /// </summary>
    private MarkdownRenderer CreateRenderer()
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-docs-renderer-tests", Guid.NewGuid().ToString("N"));
        var imagePath = Path.Combine(root, "images", "diagram.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47]); // PNG magic, content irrelevant
        _tempRoots.Add(root);

        var routes = new Dictionary<string, string>
        {
            ["ipc-api.md"] = "ipc-api",
            ["web/m4-iis-installer.md"] = "web/m4-iis-installer",
        };
        return new MarkdownRenderer(root, routes);
    }

    [Fact]
    public void Renders_Headings_Lists_Tables_AndExternalLinks()
    {
        var renderer = CreateRenderer();
        var markdown = """
            # Doc Title

            ## Configuration

            - first
            - second

            | Setting | Value |
            |---------|-------|
            | a       | 1     |

            See [the spec](https://example.com/spec).
            """;

        var result = renderer.Render(markdown, "configuration.md");

        Assert.Contains("<h2", result.Html);
        Assert.Contains("Configuration", result.Html);
        Assert.Contains("<li>first</li>", result.Html);
        Assert.Contains("<div class=\"table-scroll\"><table>", result.Html);
        Assert.Contains("</table></div>", result.Html);
        Assert.Contains("href=\"https://example.com/spec\"", result.Html);
    }

    [Fact]
    public void SqlFence_GetsServerSideColorCodeHighlighting()
    {
        var renderer = CreateRenderer();
        var markdown = "```sql\nSELECT Id FROM Users -- note\n```";

        var result = renderer.Render(markdown, "formatting.md");

        Assert.Contains("<span class=\"keyword\">SELECT</span>", result.Html);
        Assert.Contains("<span class=\"comment\">-- note</span>", result.Html);
    }

    [Fact]
    public void CSharpFence_GetsServerSideColorCodeHighlighting()
    {
        var renderer = CreateRenderer();
        var markdown = "```csharp\npublic class Foo { public string Bar() => \"baz\"; }\n```";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("<span class=\"keyword\">public</span>", result.Html);
        Assert.Contains("<span class=\"string\">", result.Html);
    }

    [Fact]
    public void RelativeMarkdownLink_RewrittenToSiteRoute()
    {
        var renderer = CreateRenderer();
        var markdown = "See the [IPC API](./ipc-api.md) for details.";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("href=\"/docs/ipc-api\"", result.Html);
        Assert.DoesNotContain("ipc-api.md", result.Html);
    }

    [Fact]
    public void RelativeMarkdownLink_PreservesFragment()
    {
        var renderer = CreateRenderer();
        var markdown = "See [authentication](./ipc-api.md#authentication).";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("href=\"/docs/ipc-api#authentication\"", result.Html);
    }

    [Fact]
    public void RelativeMarkdownLink_ResolvesAgainstSourceFolder()
    {
        var renderer = CreateRenderer();
        var markdown = "Back to the [IPC API](../ipc-api.md) and [sibling](./m4-iis-installer.md).";

        var result = renderer.Render(markdown, "web/quickstart-m4.md");

        Assert.Contains("href=\"/docs/ipc-api\"", result.Html);
        Assert.Contains("href=\"/docs/web/m4-iis-installer\"", result.Html);
    }

    [Fact]
    public void LinkToExcludedOrUnknownFile_IsLeftAsIs()
    {
        var renderer = CreateRenderer();
        var markdown = "Internal: [progress](./progress.md) and [missing](./no-such-doc.md).";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("href=\"./progress.md\"", result.Html);
        Assert.Contains("href=\"./no-such-doc.md\"", result.Html);
    }

    [Fact]
    public void RelativeImage_ResolvesToContentAssetPath_WhenAssetExists()
    {
        var renderer = CreateRenderer();
        var markdown = "![Architecture diagram](images/diagram.png)";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("src=\"/docs-assets/images/diagram.png\"", result.Html);
    }

    [Fact]
    public void RelativeImage_IsLeftAsIs_WhenAssetMissing()
    {
        var renderer = CreateRenderer();
        var markdown = "![Missing](images/missing.png)";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("src=\"images/missing.png\"", result.Html);
    }

    [Fact]
    public void Sanitization_RawHtmlNeverBecomesLiveMarkup()
    {
        var renderer = CreateRenderer();
        var markdown = """
            # Safe Title

            <script>alert('xss')</script>
            <a href="https://example.com" onclick="steal()">click</a>
            <img src="https://example.com/i.png" onerror="steal()" alt="i" />
            """;

        var result = renderer.Render(markdown, "architecture.md");

        // Raw HTML is escaped to visible inert text: no live script, anchor, or image markup.
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a href=\"https://example.com\"", result.Html);
        Assert.DoesNotContain("<img src=\"https://example.com/i.png\"", result.Html);
        Assert.Contains("&lt;script&gt;", result.Html);
        Assert.Contains("Safe Title", result.Html);
    }

    [Fact]
    public void Sanitization_StripsJavascriptUrls()
    {
        var renderer = CreateRenderer();
        var markdown = "[evil](javascript:alert(1))";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evil", result.Html);
    }

    [Fact]
    public void MalformedMarkdown_RendersWhatParses_WithoutThrowing()
    {
        var renderer = CreateRenderer();
        var markdown = "# Title\n\n```sql\nSELECT * FROM [unclosed\n\n| broken | table\n|---\n\n<di";

        var result = renderer.Render(markdown, "broken.md");

        Assert.Contains("Title", result.Html);
    }

    [Fact]
    public void Extracts_Headings_AndWhitespaceNormalizedPlainText()
    {
        var renderer = CreateRenderer();
        var markdown = "# Doc Title\n\n## Startup Sequence\n\nSome   body\n\ntext here.\n\n### Sub Detail\n\n```sql\nSELECT 1\n```\n";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("Startup Sequence", result.Headings);
        Assert.Contains("Sub Detail", result.Headings);
        Assert.DoesNotContain("Doc Title", result.Headings); // H1 is the title, not a search heading
        Assert.Contains("Some body text here.", result.PlainText);
        Assert.DoesNotContain("  ", result.PlainText);
    }

    [Fact]
    public void Summary_IsFirstParagraphText_ForMetaDescription()
    {
        var renderer = CreateRenderer();
        var markdown = "# Doc Title\n\nThe first paragraph explains the doc.\n\n## Details\n\nLater body text.\n";

        var result = renderer.Render(markdown, "architecture.md");

        // T031: meta description source — first paragraph only, not the H1 or later body.
        Assert.Equal("The first paragraph explains the doc.", result.Summary);
    }

    [Fact]
    public void Summary_IsEmpty_WhenDocumentHasNoParagraph()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render("# Only a Title\n\n## And a heading\n", "architecture.md");

        Assert.Equal("", result.Summary);
    }

    [Fact]
    public void ContentImages_LazyLoad()
    {
        var renderer = CreateRenderer();
        var markdown = "![Architecture diagram](images/diagram.png)";

        var result = renderer.Render(markdown, "architecture.md");

        // T033: performance clause — content images carry loading="lazy".
        Assert.Contains("loading=\"lazy\"", result.Html);
    }

    [Fact]
    public void Sanitization_NeutralizesFileProtocolUrls()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render("[evil](file:///C:/Windows/win.ini)", "architecture.md");

        // S4: positive allowlist — file: is not on it (the old denylist missed it).
        Assert.DoesNotContain("file:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evil", result.Html);
    }

    [Fact]
    public void Sanitization_NeutralizesProtocolRelativeUrls_ForLinksAndImages()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render(
            "[link](//evil.example.com/page) and ![img](//evil.example.com/pixel.png)",
            "architecture.md");

        // S4: protocol-relative destinations carry no scheme to check — neutralized for
        // both links and images (documented choice on IsAllowedDestination).
        Assert.DoesNotContain("evil.example.com", result.Html);
        Assert.Contains("link", result.Html);
    }

    [Fact]
    public void Sanitization_AllowsHttpsMailtoFragmentAndRelativeLinks()
    {
        var renderer = CreateRenderer();
        var markdown = "[web](https://example.com) [mail](mailto:docs@example.com) [jump](#details) [doc](./ipc-api.md)";

        var result = renderer.Render(markdown, "architecture.md");

        Assert.Contains("href=\"https://example.com\"", result.Html);
        Assert.Contains("href=\"mailto:docs@example.com\"", result.Html);
        Assert.Contains("href=\"#details\"", result.Html);
        Assert.Contains("href=\"/docs/ipc-api\"", result.Html);
    }

    [Fact]
    public void RelativeImage_ResolvesAgainstSeparateAssetsRoot()
    {
        // S3: images live in Content/docs-assets/ apart from the .md tree; the existence
        // check must run against the assets root, not the content root.
        var parent = Path.Combine(Path.GetTempPath(), "akml-docs-renderer-tests", Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(parent, "docs");
        var assetsImage = Path.Combine(parent, "docs-assets", "images", "diagram.png");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(assetsImage)!);
        File.WriteAllBytes(assetsImage, [0x89, 0x50, 0x4E, 0x47]);
        _tempRoots.Add(parent);

        var renderer = new MarkdownRenderer(contentRoot, assetsRootPath: Path.Combine(parent, "docs-assets"));

        var result = renderer.Render("![Architecture diagram](images/diagram.png)", "architecture.md");

        Assert.Contains("src=\"/docs-assets/images/diagram.png\"", result.Html);
    }

    [Fact]
    public void RelativeImage_BackslashDotDotEscape_ToSiblingPrefixFolder_IsNotRewritten()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // backslash is not a directory separator elsewhere
        }

        // C7: root "…/docs" and sibling "…/docs2" share the "…/docs" prefix. The old
        // StartsWith(root) guard accepted the escape; the fixed guard requires root + '\'.
        var parent = Path.Combine(Path.GetTempPath(), "akml-docs-renderer-tests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "docs");
        var sibling = Path.Combine(parent, "docs2");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        File.WriteAllBytes(Path.Combine(sibling, "secret.png"), [0x89, 0x50, 0x4E, 0x47]);
        _tempRoots.Add(parent);

        var renderer = new MarkdownRenderer(root);

        var result = renderer.Render("![x](..\\docs2\\secret.png)", "architecture.md");

        Assert.DoesNotContain("/docs-assets/", result.Html);
    }

    [Fact]
    public void Render_DropsLeadingH1_WhenItMatchesTheDocumentTitle()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render("# Doc Title\n\n## Details\n\nBody.\n", "architecture.md", documentTitle: "Doc Title");

        // U3: the page renders its own <h1> — the duplicate source H1 is dropped.
        Assert.DoesNotContain("<h1", result.Html);
        Assert.Contains("<h2", result.Html);
        Assert.Contains("Details", result.Html);
    }

    [Fact]
    public void Render_KeepsLeadingH1_WhenItDoesNotMatchTheDocumentTitle()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render("# Body Heading\n\nText.\n", "architecture.md", documentTitle: "Different Title");

        Assert.Contains("<h1", result.Html);
        Assert.Contains("Body Heading", result.Html);
    }

    [Fact]
    public void Render_KeepsLeadingH1_WhenNoTitleProvided()
    {
        var renderer = CreateRenderer();

        var result = renderer.Render("# Doc Title\n\nBody.\n", "architecture.md");

        Assert.Contains("<h1", result.Html);
    }

    [Fact]
    public void Render_CapturesH2Anchors_WithExactMarkdigIds()
    {
        var renderer = CreateRenderer();
        var markdown = "# Doc Title\n\n## Getting Started\n\n## Advanced Setup\n\n### Nested H3 Not In Toc\n";

        var result = renderer.Render(markdown, "architecture.md", documentTitle: "Doc Title");

        // U15: H2s only, in order; each captured id must match the emitted <h2 id="…">.
        Assert.Equal(["Getting Started", "Advanced Setup"], result.Toc.Select(h => h.Text).ToArray());
        Assert.All(result.Toc, h => Assert.Contains($"<h2 id=\"{h.Id}\">", result.Html));
        Assert.DoesNotContain(result.Toc, h => h.Text.Contains("Nested"));
    }
}
