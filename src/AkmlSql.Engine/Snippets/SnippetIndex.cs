using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Snippets;

/// <summary>Spec 042 (FR-066): the dialects a snippet can belong to.</summary>
public static class SnippetDialects
{
    public const string SqlServer = "sqlserver";
    public const string PostgreSql = "postgresql";

    public static string For(AkmlSql.Core.DatabaseKind kind) => kind == AkmlSql.Core.DatabaseKind.PostgreSql ? PostgreSql : SqlServer;

    public static bool Is(Snippet snippet, string dialect) =>
        string.Equals(string.IsNullOrEmpty(snippet.Metadata.Dialect) ? SqlServer : snippet.Metadata.Dialect, dialect, StringComparison.OrdinalIgnoreCase);
}

public class SnippetIndex
{
    private readonly Dictionary<string, (Snippet Snippet, SnippetSourceType Source)> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(Snippet Snippet, SnippetSourceType Source)>> _byShortcode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Snippet>> _byCategory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _filePathById = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;

    /// <summary>
    /// Gets the file path for a snippet by its ID, or null if not tracked.
    /// </summary>
    public string? GetFilePath(string snippetId)
    {
        return _filePathById.GetValueOrDefault(snippetId);
    }

    public void Rebuild(List<(Snippet Snippet, SnippetSourceType Source, string? FilePath)> allSnippets)
    {
        _byId.Clear();
        _byShortcode.Clear();
        _byCategory.Clear();
        _filePathById.Clear();

        foreach (var (snippet, source, filePath) in allSnippets)
        {
            var id = snippet.Metadata.Id;
            _byId[id] = (snippet, source);
            if (filePath != null)
                _filePathById[id] = filePath;

            var shortcode = snippet.Metadata.Shortcode.ToLowerInvariant();
            if (!_byShortcode.TryGetValue(shortcode, out var shortcodeList))
            {
                shortcodeList = [];
                _byShortcode[shortcode] = shortcodeList;
            }
            shortcodeList.Add((snippet, source));

            var category = snippet.Metadata.Category;
            if (!_byCategory.TryGetValue(category, out var categoryList))
            {
                categoryList = [];
                _byCategory[category] = categoryList;
            }
            categoryList.Add(snippet);
        }

        // Sort shortcode lists once after building all entries (by source priority, lower = higher)
        foreach (var list in _byShortcode.Values)
            list.Sort((a, b) => ((int)a.Source).CompareTo((int)b.Source));
    }

    public Snippet? GetByShortcode(string shortcode, string dialect = SnippetDialects.SqlServer)
    {
        if (_byShortcode.TryGetValue(shortcode.ToLowerInvariant(), out var list))
            foreach (var (snippet, _) in list)
                if (SnippetDialects.Is(snippet, dialect)) return snippet; // highest priority first
        return null;
    }

    public (Snippet Snippet, SnippetSourceType Source)? GetById(string id)
    {
        return _byId.TryGetValue(id, out var entry) ? entry : null;
    }

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> GetByContext(string? clauseType, bool hasSelection, string dialect = SnippetDialects.SqlServer)
    {
        foreach (var (snippet, source) in _byId.Values)
        {
            if (!SnippetDialects.Is(snippet, dialect)) continue;
            // Surround-with snippets only when selection exists
            if (snippet.Metadata.SurroundsWith && !hasSelection)
                continue;
            if (!snippet.Metadata.SurroundsWith && hasSelection)
                continue; // When selection exists, only show surround-with

            if (string.IsNullOrEmpty(clauseType) || clauseType == "Unknown")
            {
                // Show snippets with "global" or "batch_start" context
                if (snippet.Metadata.Context.Any(c =>
                    c.Equals("global", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("batch_start", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return (snippet, source);
                }
            }
            else
            {
                var contextKey = $"after_{clauseType.ToLowerInvariant()}";
                if (snippet.Metadata.Context.Any(c =>
                    c.Equals(contextKey, StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("global", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return (snippet, source);
                }
            }
        }
    }

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> Search(string query, string dialect = SnippetDialects.SqlServer)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var entry in _byId.Values)
                if (SnippetDialects.Is(entry.Snippet, dialect)) yield return entry;
            yield break;
        }

        var lowerQuery = query.ToLowerInvariant();
        foreach (var (snippet, source) in _byId.Values)
        {
            if (!SnippetDialects.Is(snippet, dialect)) continue;
            if (snippet.Metadata.Shortcode.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                snippet.Metadata.Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                snippet.Metadata.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                snippet.Metadata.Tags.Any(t => t.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)) ||
                snippet.Body.Any(line => line.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)))
            {
                yield return (snippet, source);
            }
        }
    }

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> GetAll(string dialect = SnippetDialects.SqlServer)
    {
        return _byId.Values.Where(e => SnippetDialects.Is(e.Snippet, dialect));
    }
}
