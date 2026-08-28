namespace AkmlSql.Site.Docs;

// Docs pipeline models for spec 034 US2 (T020). Field shapes per
// specs/034-blazor-product-site/data-model.md; behavior rules per
// specs/034-blazor-product-site/contracts/docs-content.md.

/// <summary>Configuration binding for the <c>Docs</c> section of appsettings.json.</summary>
public sealed class DocsOptions
{
    public const string SectionName = "Docs";

    /// <summary>Docs content folder, resolved against the app content root (default <c>Content/docs</c>).</summary>
    public string ContentRoot { get; set; } = "Content/docs";

    /// <summary>
    /// Docs image assets folder (S3: images only — no <c>.md</c>, no <c>.svg</c>), served at
    /// <see cref="DocsContentService.AssetsRequestPath"/>; resolved like <see cref="ContentRoot"/>.
    /// </summary>
    public string AssetsRoot { get; set; } = "Content/docs-assets";

    /// <summary>
    /// Exclusion list (contracts/docs-content.md): entries ending in <c>/</c> exclude a folder
    /// prefix; other entries (wildcards <c>*</c>/<c>?</c> supported) match file names at any depth.
    /// Also read by scripts/generate-docs-metadata.ps1 — the single source of truth for both.
    /// </summary>
    public List<string> Exclusions { get; set; } = [];

    /// <summary>Folder-key → display-name overrides; <c>""</c> maps top-level files (default "Guides").</summary>
    public Dictionary<string, string> SectionTitles { get; set; } = [];

    /// <summary>
    /// Section display names in pinned nav order; sections not listed sort ordinal-ignore-case
    /// after the pinned ones (empty → fully alphabetical, the pre-SectionOrder behavior).
    /// </summary>
    public List<string> SectionOrder { get; set; } = [];

    /// <summary>Default freshness window for the New/Updated badges when unconfigured.</summary>
    public const int DefaultBadgeWindowDays = 30;

    /// <summary>
    /// Days a doc counts as fresh: <see cref="DocBadge.New"/> when its docs-metadata.json
    /// <c>added</c> date is within the window, else <see cref="DocBadge.Updated"/> when its
    /// <c>updated</c> date is. Default <see cref="DefaultBadgeWindowDays"/>.
    /// </summary>
    public int BadgeWindowDays { get; set; } = DefaultBadgeWindowDays;
}

/// <summary>Freshness badge derived from docs-metadata.json dates and <c>Docs:BadgeWindowDays</c>.</summary>
public enum DocBadge
{
    /// <summary>No badge: no metadata, or both dates outside the freshness window.</summary>
    None,

    /// <summary>Recently added (green).</summary>
    New,

    /// <summary>Recently changed (red); only when <see cref="New"/> does not apply.</summary>
    Updated,
}

/// <summary>Git added/updated dates (yyyy-MM-dd) for one doc, from docs-metadata.json.</summary>
public readonly record struct DocDates(DateOnly Added, DateOnly Updated);

/// <summary>
/// A documentation entry discovered automatically from the docs content source.
/// Identity fields are set by <see cref="DocsCatalog"/>; the render outputs
/// (<see cref="HtmlContent"/>, <see cref="PlainText"/>, <see cref="Headings"/>) are filled
/// once at startup by <see cref="MarkdownRenderer"/>.
/// </summary>
public sealed class Document
{
    /// <summary>First H1 text; fallback: filename-derived title (kebab/snake → words, title-cased).</summary>
    public required string Title { get; init; }

    /// <summary>URL-safe slug from the relative path: lowercase, <c>[a-z0-9/-]</c> only; unique.</summary>
    public required string Slug { get; init; }

    /// <summary>Site route: <c>/docs/{Slug}</c>.</summary>
    public string Route => "/docs/" + Slug;

    /// <summary>Source <c>.md</c> path relative to the content root, forward slashes (diagnostics only).</summary>
    public required string SourcePath { get; init; }

    /// <summary>Display section name from the folder mapping; top-level files → "Guides".</summary>
    public required string Section { get; init; }

    /// <summary>Leading <c>NN-</c> filename prefix when present, else <see cref="int.MaxValue"/> (sorts last).</summary>
    public required int Order { get; init; }

    /// <summary>
    /// Freshness badge from docs-metadata.json (git dates) + <c>Docs:BadgeWindowDays</c>;
    /// set once at startup by <see cref="DocsContentService.Build"/>.
    /// </summary>
    public DocBadge Badge { get; set; } = DocBadge.None;

    /// <summary>Markdig-rendered, ColorCode-highlighted, sanitized HTML; cached at startup.</summary>
    public string HtmlContent { get; set; } = "";

    /// <summary>Whitespace-normalized plain text for the search index.</summary>
    public string PlainText { get; set; } = "";

    /// <summary>First-paragraph text; used as the page meta description (T031). Empty when the document has no paragraph.</summary>
    public string Summary { get; set; } = "";

    /// <summary>H2/H3 heading text for the search index.</summary>
    public IReadOnlyList<string> Headings { get; set; } = [];

    /// <summary>H2 (text, id) pairs for the on-page "On this page" TOC (U15); ids are the
    /// exact anchors Markdig's AutoIdentifier extension emitted into <see cref="HtmlContent"/>.</summary>
    public IReadOnlyList<HeadingAnchor> Toc { get; set; } = [];
}

/// <summary>A rendered H2 heading and its anchor id, for the on-page table of contents.</summary>
public sealed record HeadingAnchor(string Text, string Id);

/// <summary>A nav-tree node grouping documents under a display name.</summary>
public sealed class DocSection
{
    /// <summary>Display name (from the section mapping).</summary>
    public required string Name { get; init; }

    /// <summary>Folder key (<c>web</c>, <c>guides</c>, …).</summary>
    public required string Key { get; init; }

    /// <summary>Documents ordered per the contract (numeric prefix first, then ordinal-ignore-case title).</summary>
    public required IReadOnlyList<Document> Documents { get; init; }
}

/// <summary>One entry of the generated <c>search-index.json</c> (consumed by MiniSearch).</summary>
public sealed class SearchIndexEntry
{
    /// <summary>Document title.</summary>
    public required string Title { get; init; }

    /// <summary>Concatenated H2/H3 headings.</summary>
    public required string Headings { get; init; }

    /// <summary>Whitespace-normalized plain text body (truncated).</summary>
    public required string Body { get; init; }

    /// <summary>Document route (<c>/docs/{slug}</c>).</summary>
    public required string Url { get; init; }
}
