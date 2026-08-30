using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AkmlSql.Site.Docs;

/// <summary>
/// Spec 034 T023 (US2): builds the docs pipeline ONCE at startup and caches the results —
/// nav sections, rendered HTML per slug, and the <c>search-index.json</c> payload — per
/// specs/034-blazor-product-site/contracts/docs-content.md ("All rendering happens once at
/// startup into an in-memory cache; no per-request parsing"). Registered as a singleton in
/// the Program.cs composition root, same pattern as <c>ReleasesManifest</c>.
/// </summary>
public sealed class DocsContentService
{
    /// <summary>Request path under which docs content assets (images) are served.</summary>
    public const string AssetsRequestPath = "/docs-assets";

    /// <summary>
    /// Plain-text body truncation for search index entries (data-model.md).
    /// <para>
    /// PERF-002: was 20,000, which produced a 168 KB index that every docs page downloaded. Titles
    /// and headings — the fields that actually decide a match — are indexed in full and are
    /// unaffected; this only bounds how deep into a long document a body-text match can be found.
    /// </para>
    /// </summary>
    private const int SearchBodyMaxLength = 4_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, Document> _documentsBySlug;

    private DocsContentService(IReadOnlyList<Document> documents, string searchIndexJson, IReadOnlyList<string>? sectionOrder, int badgeWindowDays)
    {
        Documents = documents;
        Sections = DocsCatalog.BuildSections(documents, sectionOrder);
        SearchIndexJson = searchIndexJson;
        BadgeWindowDays = badgeWindowDays;
        _documentsBySlug = documents.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All documents (catalog scan order). Empty when the content source is empty.</summary>
    public IReadOnlyList<Document> Documents { get; }

    /// <summary>Nav-tree sections with ordered documents.</summary>
    public IReadOnlyList<DocSection> Sections { get; }

    /// <summary>Serialized search-index.json payload (contract schema: generatedAt + documents).</summary>
    public string SearchIndexJson { get; }

    /// <summary>Freshness window behind the New/Updated badges (for the docs-index legend).</summary>
    public int BadgeWindowDays { get; }

    /// <summary>True when the docs content source yielded no documents (empty-state UI, never an error).</summary>
    public bool IsEmpty => Documents.Count == 0;

    /// <summary>Finds a document by slug (case-insensitive); null when unknown.</summary>
    public Document? FindBySlug(string slug) =>
        slug is not null && _documentsBySlug.TryGetValue(slug, out var document) ? document : null;

    /// <summary>
    /// FR-007 title filter (works with JS disabled — plain GET form, filtered server-side).
    /// Returns sections whose documents match <paramref name="titleFilter"/> (ordinal-ignore-case
    /// substring on the title); empty sections and an empty/blank filter pass through unfiltered.
    /// </summary>
    public IReadOnlyList<DocSection> FilterSections(string? titleFilter)
    {
        if (string.IsNullOrWhiteSpace(titleFilter))
        {
            return Sections;
        }

        return Sections
            .Select(s => new DocSection
            {
                Name = s.Name,
                Key = s.Key,
                Documents = s.Documents
                    .Where(d => d.Title.Contains(titleFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            })
            .Where(s => s.Documents.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Builds a service from already-rendered documents (used by tests and by <see cref="Build"/>).
    /// </summary>
    public static DocsContentService Create(
        IEnumerable<Document> documents,
        DateTimeOffset? generatedAt = null,
        IReadOnlyList<string>? sectionOrder = null,
        int badgeWindowDays = DocsOptions.DefaultBadgeWindowDays)
    {
        var list = documents.ToList();
        return new DocsContentService(list, SerializeSearchIndex(list, generatedAt ?? DateTimeOffset.UtcNow), sectionOrder, badgeWindowDays);
    }

    /// <summary>
    /// Resolves the docs content root. Published: <c>Content/docs</c> sits next to the app
    /// content root. <c>dotnet run</c> from the project directory: the csproj glob (T003) copies
    /// docs into the build output, so fall back to the assembly's folder.
    /// </summary>
    public static string ResolveContentRootPath(IWebHostEnvironment environment, DocsOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var fromContentRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.ContentRoot));
        if (Directory.Exists(fromContentRoot))
        {
            return fromContentRoot;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.ContentRoot));
    }

    /// <summary>
    /// Resolves the docs image assets root (served at <see cref="AssetsRequestPath"/>) with the
    /// same content-root-then-assembly-folder fallback as <see cref="ResolveContentRootPath"/>.
    /// </summary>
    public static string ResolveAssetsRootPath(IWebHostEnvironment environment, DocsOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var fromContentRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.AssetsRoot));
        if (Directory.Exists(fromContentRoot))
        {
            return fromContentRoot;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.AssetsRoot));
    }

    /// <summary>
    /// Scans the configured docs content root, renders every document, applies the freshness
    /// badges from <c>wwwroot/docs-metadata.json</c> (missing/malformed → no badges), and
    /// produces the search index. A missing/empty content root yields an empty service, never
    /// an exception.
    /// </summary>
    public static DocsContentService Build(IWebHostEnvironment environment, DocsOptions options)
    {
        var contentRootPath = ResolveContentRootPath(environment, options);
        var assetsRootPath = ResolveAssetsRootPath(environment, options);
        var documents = DocsCatalog.Scan(contentRootPath, options);

        var routes = documents.ToDictionary(d => d.SourcePath.ToLowerInvariant(), d => d.Slug, StringComparer.Ordinal);
        var renderer = new MarkdownRenderer(contentRootPath, routes, assetsRootPath);

        var metadata = DocsMetadata.Load(environment);
        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var document in documents)
        {
            var markdown = File.ReadAllText(Path.Combine(contentRootPath, document.SourcePath.Replace('/', Path.DirectorySeparatorChar)));
            var rendered = renderer.Render(markdown, document.SourcePath, document.Title);
            document.HtmlContent = rendered.Html;
            document.PlainText = rendered.PlainText;
            document.Headings = rendered.Headings;
            document.Summary = rendered.Summary;
            document.Toc = rendered.Toc;
            if (metadata.TryGet(document.SourcePath, out var dates))
            {
                document.Dates = dates;
                document.Badge = DocsMetadata.ComputeBadge(dates, today, options.BadgeWindowDays);
            }
            else
            {
                document.Dates = null;
                document.Badge = DocBadge.None;
            }
        }

        return Create(documents, sectionOrder: options.SectionOrder, badgeWindowDays: options.BadgeWindowDays);
    }

    private static string SerializeSearchIndex(IReadOnlyList<Document> documents, DateTimeOffset generatedAt)
    {
        var payload = new SearchIndexPayload(
            GeneratedAt: generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            Documents: documents.Select(d => new SearchIndexEntry
            {
                Title = d.Title,
                Headings = string.Join("; ", d.Headings),
                Body = d.PlainText.Length <= SearchBodyMaxLength ? d.PlainText : d.PlainText[..SearchBodyMaxLength],
                Url = d.Route,
            }).ToList());

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>Serialized shape of search-index.json (camelCase via <see cref="JsonSerializerDefaults.Web"/>).</summary>
    private sealed record SearchIndexPayload(string GeneratedAt, IReadOnlyList<SearchIndexEntry> Documents);
}
