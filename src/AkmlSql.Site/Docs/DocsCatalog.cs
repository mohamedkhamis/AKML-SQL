using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace AkmlSql.Site.Docs;

/// <summary>
/// Spec 034 T021 (US2): scans the docs content root and derives title/slug/section/order
/// per specs/034-blazor-product-site/contracts/docs-content.md:
/// <list type="bullet">
/// <item>Included: every <c>*.md</c> under the content root, minus the exclusion list.</item>
/// <item>Title: first <c># H1</c>; fallback: filename without extension, separators → spaces, title-cased.</item>
/// <item>Slug: relative path without extension, lowercase, non-<c>[a-z0-9/-]</c> runs → <c>-</c>; duplicates get <c>-2</c>, <c>-3</c>.</item>
/// <item>Section: first path segment via <see cref="DocsOptions.SectionTitles"/> (top-level → "Guides").</item>
/// <item>Order: leading <c>NN-</c> filename prefix forces position, else ordinal-ignore-case by title.</item>
/// </list>
/// Pure logic: the content root is a path parameter, so tests point at a fixture directory.
/// A missing/empty content root yields an empty catalog, never an exception (spec edge case).
/// </summary>
public static class DocsCatalog
{
    private static readonly Regex SlugInvalidRun = new("[^a-z0-9/-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OrderPrefixPattern = new(@"^(?<order>\d+)-", RegexOptions.Compiled);

    /// <summary>
    /// Discovers all included <c>.md</c> files under <paramref name="contentRootPath"/>.
    /// Returns documents in a deterministic order (ordinal by source path); section grouping
    /// and display ordering are applied by <see cref="BuildSections"/>.
    /// </summary>
    public static IReadOnlyList<Document> Scan(string contentRootPath, DocsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(contentRootPath))
        {
            return [];
        }

        // C8: "*.md" matching is case-sensitive on Linux — force case-insensitive so
        // "README.MD" is discovered on every OS. (RecurseSubdirectories defaults to false
        // in EnumerationOptions, so it is set explicitly.)
        var files = Directory
            .EnumerateFiles(contentRootPath, "*.md", new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var documents = new List<Document>(files.Count);
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(contentRootPath, file));
            if (IsExcluded(relativePath, options.Exclusions))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            var title = ExtractTitle(file) ?? FilenameTitle(fileName);
            var slug = Deduplicate(SlugFromRelativePath(relativePath), usedSlugs);
            var section = SectionName(relativePath, options.SectionTitles);
            var order = OrderPrefix(fileName);

            documents.Add(new Document
            {
                Title = title,
                Slug = slug,
                SourcePath = relativePath,
                Section = section,
                Order = order,
            });
        }

        return documents;
    }

    /// <summary>
    /// Groups documents into sections. Section order: names listed in <paramref name="sectionOrder"/>
    /// first (config order, ordinal-ignore-case match), unlisted sections ordinal-ignore-case after
    /// (null/empty → fully alphabetical, the original behavior). Documents within a section sort by
    /// the numeric filename prefix first, then ordinal-ignore-case by title.
    /// </summary>
    public static IReadOnlyList<DocSection> BuildSections(IEnumerable<Document> documents, IReadOnlyList<string>? sectionOrder = null)
    {
        var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sectionOrder is not null)
        {
            foreach (var name in sectionOrder)
            {
                if (!string.IsNullOrWhiteSpace(name) && !orderIndex.ContainsKey(name))
                {
                    orderIndex[name] = orderIndex.Count;
                }
            }
        }

        return documents
            .GroupBy(d => d.Section, StringComparer.Ordinal)
            .OrderBy(g => orderIndex.TryGetValue(g.Key, out var index) ? index : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DocSection
            {
                Name = g.Key,
                Key = Slugify(g.Key),
                Documents = g
                    .OrderBy(d => d.Order)
                    .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();
    }

    /// <summary>Forward-slash relative path, e.g. <c>web/m4-iis-installer.md</c>.</summary>
    internal static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static bool IsExcluded(string relativePath, IReadOnlyList<string> exclusions)
    {
        var fileName = Path.GetFileName(relativePath);
        foreach (var exclusion in exclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion))
            {
                continue;
            }

            var pattern = NormalizeRelativePath(exclusion.Trim());
            if (pattern.EndsWith('/'))
            {
                // Folder exclusion: matches the folder at the root or any depth segment.
                if (relativePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
                    || relativePath.Contains("/" + pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (WildcardMatch(pattern, fileName) || WildcardMatch(pattern, relativePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Case-insensitive full-string match with <c>*</c>/<c>?</c> wildcards.</summary>
    private static bool WildcardMatch(string pattern, string value)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// First top-level H1 per the Markdig AST (C9: the old line regex matched <c># …</c>
    /// lines inside fenced code blocks, mistaking code comments for the title). Closing ATX
    /// hashes and emphasis markup are handled by the parser. Null when absent (fallback
    /// title applies).
    /// </summary>
    private static string? ExtractTitle(string file)
    {
        var document = Markdig.Markdown.Parse(File.ReadAllText(file));
        foreach (var block in document)
        {
            if (block is HeadingBlock { Level: 1 } heading)
            {
                var title = MarkdownRenderer.InlineText(heading.Inline);
                if (title.Length > 0)
                {
                    return title;
                }
            }
        }

        return null;
    }

    /// <summary>Fallback title: separators → spaces, each word's first letter upper-cased ("m3-security" → "M3 Security").</summary>
    private static string FilenameTitle(string fileNameWithoutExtension)
    {
        var words = fileNameWithoutExtension
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>Relative path minus extension, slugified (lowercase, <c>[a-z0-9/-]</c> only).</summary>
    internal static string SlugFromRelativePath(string relativePath)
    {
        var withoutExtension = relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^3]
            : relativePath;
        return Slugify(withoutExtension);
    }

    /// <summary>
    /// URL-safe slug (S6): lowercase, restricted to <c>[a-z0-9/-]</c> — any run of other
    /// characters collapses to a single dash, with leading/trailing dashes trimmed. The old
    /// spaces/underscores-only mapping let <c>#</c>, <c>%</c>, <c>&amp;</c> etc. through, and
    /// a slug like <c>a#b</c> is unreachable (the fragment never reaches the server).
    /// Non-ASCII letters (e.g. <c>café</c>) are stripped to dashes rather than transliterated —
    /// transliteration is locale-dependent, so slugs stay ASCII-only (<c>café.md</c> → <c>caf</c>).
    /// </summary>
    private static string Slugify(string value) =>
        SlugInvalidRun.Replace(value.ToLowerInvariant(), "-").Trim('-');

    private static string Deduplicate(string slug, HashSet<string> usedSlugs)
    {
        var candidate = slug;
        for (var suffix = 2; !usedSlugs.Add(candidate); suffix++)
        {
            candidate = slug + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    /// <summary>First path segment mapped via SectionTitles; top-level files use the "" entry (default "Guides").</summary>
    private static string SectionName(string relativePath, IReadOnlyDictionary<string, string> sectionTitles)
    {
        var slashIndex = relativePath.IndexOf('/');
        var folderKey = slashIndex < 0 ? "" : relativePath[..slashIndex];

        if (sectionTitles.TryGetValue(folderKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }

        if (folderKey.Length == 0)
        {
            return "Guides";
        }

        return FilenameTitle(folderKey);
    }

    private static int OrderPrefix(string fileNameWithoutExtension)
    {
        var match = OrderPrefixPattern.Match(fileNameWithoutExtension);
        return match.Success && int.TryParse(match.Groups["order"].Value, out var order)
            ? order
            : int.MaxValue;
    }
}
