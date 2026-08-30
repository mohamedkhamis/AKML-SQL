using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.ColorCode;

namespace AkmlSql.Site.Docs;

/// <summary>Render outputs for one Markdown document.</summary>
/// <param name="Html">Sanitized, highlighted HTML, safe to emit as a <c>MarkupString</c>.</param>
/// <param name="PlainText">Whitespace-normalized plain text for the search index.</param>
/// <param name="Headings">H2/H3 heading text (H1 is the document title).</param>
/// <param name="Summary">First-paragraph text (T031 SEO meta description), empty when the document has none.</param>
/// <param name="Toc">H2 (text, id) pairs for the on-page table of contents (U15); ids are read
/// back from the AST after rendering, exactly as Markdig's AutoIdentifier extension emitted them.</param>
public sealed record RenderedDocument(string Html, string PlainText, IReadOnlyList<string> Headings, string Summary, IReadOnlyList<HeadingAnchor> Toc);

/// <summary>
/// Spec 034 T022 (US2): Markdown → HTML per specs/034-blazor-product-site/contracts/docs-content.md.
/// Markdig <c>UseAdvancedExtensions()</c>; fenced code highlighted server-side via
/// ColorCode.Universal with CSS classes (<see cref="HtmlFormatterType.Css"/>); relative
/// <c>.md</c> links rewritten to site routes (<c>./ipc-api.md</c> → <c>/docs/ipc-api</c>) while
/// links to excluded/unknown files stay untouched; relative images resolve to copied content
/// assets under <c>/docs-assets/</c>.
///
/// Sanitization (contract: "Output HTML is sanitized before insertion") is Markdig-native,
/// deliberately without an HTML-parser dependency (HtmlSanitizer 9.0.x hard-pins AngleSharp
/// 0.17.1, flagged by GHSA-pgww-w46g-26qg — an mXSS parser-differential bypass, the exact
/// failure mode a sanitizer must not have; it also trips NU1902):
/// <list type="number">
/// <item><see cref="MarkdownPipelineBuilderExtensions.DisableHtml"/> — raw HTML in the source
/// (scripts, event-handler attributes, raw anchors/images) never becomes live markup; it is
/// escaped to visible text. The doc corpus uses no rendering-intent raw HTML, and placeholder
/// tokens like <c>&lt;port&gt;</c> actually render MORE faithfully escaped.</item>
/// <item>Positive protocol allowlist at the AST level (S4) — link/image destinations must be
/// <c>http(s):</c>, <c>mailto:</c>, a <c>#fragment</c>, or a scheme-less relative path;
/// everything else (<c>javascript:</c>, <c>data:</c>, <c>file:</c>, protocol-relative
/// <c>//host/…</c>) is neutralized before rendering.</item>
/// </list>
/// Everything left is Markdig-generated from a fixed, safe tag vocabulary with escaped
/// attribute values. Markdig is non-throwing, so malformed files render what parses
/// (spec edge case).
/// </summary>
public sealed class MarkdownRenderer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly string _contentRootPath;
    private readonly string _assetsRootPath;
    private readonly IReadOnlyDictionary<string, string> _documentRoutes;
    private readonly MarkdownPipeline _pipeline;

    /// <param name="contentRootPath">Absolute (or resolvable) path of the docs content root (<c>.md</c> files).</param>
    /// <param name="documentRoutes">
    /// Map of normalized relative source path (lowercase, forward slashes, e.g. <c>web/m4.md</c>)
    /// to document slug, for inter-document link rewriting.
    /// </param>
    /// <param name="assetsRootPath">
    /// Absolute (or resolvable) path of the image assets root served at <c>/docs-assets</c> —
    /// used to test image assets for existence (S3: images live apart from the <c>.md</c> tree).
    /// Defaults to <paramref name="contentRootPath"/> when omitted.
    /// </param>
    public MarkdownRenderer(string contentRootPath, IReadOnlyDictionary<string, string>? documentRoutes = null, string? assetsRootPath = null)
    {
        _contentRootPath = Path.GetFullPath(contentRootPath);
        _assetsRootPath = Path.GetFullPath(assetsRootPath ?? contentRootPath);
        _documentRoutes = documentRoutes ?? new Dictionary<string, string>();
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseColorCode(HtmlFormatterType.Css)
            .DisableHtml()
            .Build();
    }

    /// <summary>
    /// Renders one document. <paramref name="sourceRelativePath"/> is the document's path
    /// relative to the content root (forward slashes) — the base for resolving relative links.
    /// <paramref name="documentTitle"/> is the catalog title; when the body's first block is an
    /// H1 with the same text it is dropped from the rendered HTML (U3: the page component
    /// renders its own <c>&lt;h1&gt;</c>, so the title would otherwise appear twice).
    /// </summary>
    public RenderedDocument Render(string markdown, string sourceRelativePath, string? documentTitle = null)
    {
        markdown ??= string.Empty;

        var document = Markdig.Markdown.Parse(markdown, _pipeline);

        DropDuplicateTitleHeading(document, documentTitle);

        var headings = ExtractHeadings(document);
        var summary = ExtractSummary(document);
        SanitizeAndRewriteLinks(document, sourceRelativePath);

        var html = Markdig.Markdown.ToHtml(document, _pipeline);
        html = AddHeadingPermalinks(html);
        // Mobile tables: wrap in a scroll container — a display:block table with
        // border-collapse does not scroll reliably in Chromium at narrow widths, so the
        // wrapper owns the scrolling and the table stays a real table.
        html = html.Replace("<table>", "<div class=\"table-scroll\"><table>", StringComparison.Ordinal)
                   .Replace("</table>", "</table></div>", StringComparison.Ordinal);
        // AutoIdentifier assigns heading ids during ToHtml — read them back AFTER rendering.
        var toc = ExtractToc(document);
        var plainText = WhitespaceRun.Replace(Markdig.Markdown.ToPlainText(markdown, _pipeline), " ").Trim();

        return new RenderedDocument(html, plainText, headings, summary, toc);
    }

    /// <summary>
    /// Matches an H2-H4 opening tag that AutoIdentifiers gave an id, capturing the level and id.
    /// </summary>
    private static readonly Regex HeadingWithId = new(
        @"<h(?<level>[2-4]) id=""(?<id>[^""]+)"">",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// DOC-004: appends a permalink anchor to every id-bearing H2-H4 so a reader can link
    /// directly to a section. Markdig's AutoIdentifier extension already emits the ids; only
    /// the affordance was missing.
    /// <para>
    /// The href is a bare fragment, which is safe here because the docs pages do not rely on
    /// <c>&lt;base href&gt;</c> resolution for it — the anchor is activated in-page and the
    /// browser scrolls without navigating. (The "On this page" nav in DocPage.razor uses the
    /// full route because it is rendered before the document is known to be current.)
    /// </para>
    /// The link is aria-hidden with a title, so screen readers are not read a "#" after every
    /// heading; CSS reveals it on hover/focus of the heading.
    /// </summary>
    internal static string AddHeadingPermalinks(string html) =>
        HeadingWithId.Replace(html, match =>
        {
            var level = match.Groups["level"].Value;
            var id = match.Groups["id"].Value;
            return $"<h{level} id=\"{id}\" class=\"doc-heading\">"
                 + $"<a class=\"doc-anchor\" href=\"#{id}\" title=\"Link to this section\" aria-hidden=\"true\" tabindex=\"-1\">#</a>";
        });

    /// <summary>
    /// U3: the doc page renders its own <c>&lt;h1&gt;@Title&lt;/h1&gt;</c>, so a leading source
    /// H1 whose text matches the document title is dropped from the rendered HTML.
    /// </summary>
    private static void DropDuplicateTitleHeading(MarkdownDocument document, string? documentTitle)
    {
        if (documentTitle is null || document.Count == 0 || document[0] is not HeadingBlock { Level: 1 } heading)
        {
            return;
        }

        if (string.Equals(InlineText(heading.Inline), documentTitle, StringComparison.Ordinal))
        {
            document.RemoveAt(0);
        }
    }

    /// <summary>
    /// U15: H2 (text, id) pairs for the on-page TOC. The ids are read back from the AST after
    /// rendering — Markdig's AutoIdentifier extension (via <c>UseAdvancedExtensions</c>) stores
    /// each heading's generated id on its <see cref="HeadingBlock"/> HtmlAttributes during
    /// <c>ToHtml</c>, so these match the emitted <c>&lt;h2 id="…"&gt;</c> anchors exactly.
    /// </summary>
    private static List<HeadingAnchor> ExtractToc(MarkdownDocument document)
    {
        var toc = new List<HeadingAnchor>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level != 2)
            {
                continue;
            }

            var id = heading.GetAttributes().Id;
            var text = InlineText(heading.Inline);
            if (id is { Length: > 0 } && text.Length > 0)
            {
                toc.Add(new HeadingAnchor(text, id));
            }
        }

        return toc;
    }

    /// <summary>
    /// T031 (SEO): first-paragraph text for the meta description, truncated to ~200 chars at a
    /// word boundary (meta descriptions past ~160 chars are truncated by search engines anyway).
    /// </summary>
    private static string ExtractSummary(MarkdownDocument document)
    {
        const int maxLength = 200;

        foreach (var paragraph in document.Descendants<ParagraphBlock>())
        {
            var text = InlineText(paragraph.Inline);
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            var cut = text.LastIndexOf(' ', maxLength);
            return (cut > 0 ? text[..cut] : text[..maxLength]).TrimEnd() + "…";
        }

        return "";
    }

    private static List<string> ExtractHeadings(MarkdownDocument document)
    {
        var headings = new List<string>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level is 2 or 3)
            {
                var text = InlineText(heading.Inline);
                if (text.Length > 0)
                {
                    headings.Add(text);
                }
            }
        }

        return headings;
    }

    /// <summary>Plain text of an inline container (literals + code spans). Shared with
    /// <see cref="DocsCatalog"/>, which reads document titles from the same AST shape (C9).</summary>
    internal static string InlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var inline in container.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content);
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private void SanitizeAndRewriteLinks(MarkdownDocument document, string sourceRelativePath)
    {
        var sourceFolder = FolderOf(sourceRelativePath);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (string.IsNullOrEmpty(link.Url))
            {
                continue;
            }

            if (!IsAllowedDestination(link.Url))
            {
                // Neutralize non-allowlisted destinations (javascript:/data:/file:/
                // protocol-relative/…) — Markdig passes URLs through unexamined. Rendered as
                // an anchor (or image) without the URL.
                link.Url = null;
                continue;
            }

            if (link.IsImage)
            {
                // T033: content images lazy-load (contracts/site-routes.md performance clause).
                link.GetAttributes().AddPropertyIfNotExist("loading", "lazy");
            }

            link.Url = link.IsImage
                ? RewriteImageUrl(link.Url, sourceFolder)
                : RewriteLinkUrl(link.Url, sourceFolder);
        }
    }

    /// <summary>Relative links to included docs become site routes; everything else is left as-is.</summary>
    private string RewriteLinkUrl(string url, string sourceFolder)
    {
        if (IsExternalOrAbsolute(url))
        {
            return url;
        }

        var hashIndex = url.IndexOf('#');
        var path = hashIndex < 0 ? url : url[..hashIndex];
        var fragment = hashIndex < 0 ? "" : url[hashIndex..];

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var resolved = ResolveRelative(sourceFolder, path);
        if (resolved is not null && _documentRoutes.TryGetValue(resolved.ToLowerInvariant(), out var slug))
        {
            return "/docs/" + slug + fragment;
        }

        // Excluded or unknown file: leave untouched per contract.
        return url;
    }

    /// <summary>Relative images resolve to copied content assets; missing/external images stay untouched.</summary>
    private string RewriteImageUrl(string url, string sourceFolder)
    {
        if (IsExternalOrAbsolute(url))
        {
            return url;
        }

        var resolved = ResolveRelative(sourceFolder, url);
        if (resolved is null)
        {
            return url;
        }

        // C7: compare against root + separator (not the bare root, which a sibling like
        // "docs2" also prefixes) — and with backslash segments a ".." can escape the root on
        // Windows before ResolveRelative ever sees it, so this guard is the real boundary.
        var fullPath = Path.GetFullPath(Path.Combine(_assetsRootPath, resolved.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(fullPath, _assetsRootPath) || !File.Exists(fullPath))
        {
            return url;
        }

        return DocsContentService.AssetsRequestPath + "/" + string.Join('/', resolved.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>True when <paramref name="fullPath"/> sits strictly under <paramref name="root"/>.
    /// Ordinal on case-sensitive file systems; OrdinalIgnoreCase only on Windows (C7).</summary>
    private static bool IsUnderRoot(string fullPath, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// S4: positive destination allowlist (replacing the old javascript:/vbscript:/data:
    /// denylist). Permitted: <c>http:</c>, <c>https:</c>, <c>mailto:</c>, <c>#fragment</c>,
    /// and scheme-less relative paths. Everything else is neutralized — including
    /// protocol-relative <c>//host/…</c>, which we treat as unsafe because the scheme is
    /// unknown at render time (a <c>//</c> image could phone home over whatever protocol the
    /// page was served on; the corpus uses none, for links or images).
    /// </summary>
    private static bool IsAllowedDestination(string url)
    {
        if (url.StartsWith('#'))
        {
            return true;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto";
        }

        // Relative path — but never one carrying a stray scheme-like colon.
        return !url.Contains(':', StringComparison.Ordinal);
    }

    private static bool IsExternalOrAbsolute(string url) =>
        url.StartsWith('#')
        || url.StartsWith('/')
        || url.Contains("://", StringComparison.Ordinal)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string FolderOf(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? "" : relativePath[..slashIndex];
    }

    /// <summary>
    /// Resolves <paramref name="path"/> against <paramref name="sourceFolder"/> (both
    /// content-root-relative, forward slashes), honoring <c>.</c>/<c>..</c>. Returns null when
    /// the result escapes the content root.
    /// </summary>
    private static string? ResolveRelative(string sourceFolder, string path)
    {
        var segments = new List<string>(
            sourceFolder.Length == 0 ? [] : sourceFolder.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case "..":
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }
}
