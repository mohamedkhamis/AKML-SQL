using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Snippets;

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
        return _filePathById.TryGetValue(snippetId, out var path) ? path : null;
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
            // Sort by source priority (lower = higher priority)
            shortcodeList.Sort((a, b) => ((int)a.Source).CompareTo((int)b.Source));

            var category = snippet.Metadata.Category;
            if (!_byCategory.TryGetValue(category, out var categoryList))
            {
                categoryList = [];
                _byCategory[category] = categoryList;
            }
            categoryList.Add(snippet);
        }
    }

    public Snippet? GetByShortcode(string shortcode)
    {
        if (_byShortcode.TryGetValue(shortcode.ToLowerInvariant(), out var list) && list.Count > 0)
            return list[0].Snippet; // Highest priority
        return null;
    }

    public (Snippet Snippet, SnippetSourceType Source)? GetById(string id)
    {
        return _byId.TryGetValue(id, out var entry) ? entry : null;
    }

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> GetByContext(string? clauseType, bool hasSelection)
    {
        foreach (var (snippet, source) in _byId.Values)
        {
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

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var entry in _byId.Values)
                yield return entry;
            yield break;
        }

        var lowerQuery = query.ToLowerInvariant();
        foreach (var (snippet, source) in _byId.Values)
        {
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

    public IEnumerable<(Snippet Snippet, SnippetSourceType Source)> GetAll() => _byId.Values;
}
