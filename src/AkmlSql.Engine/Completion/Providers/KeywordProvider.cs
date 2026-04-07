using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides T-SQL keyword completions based on the current clause context.
/// Supports keyword casing preferences (UPPER by default).
/// </summary>
public class KeywordProvider(
    KeywordCasing casing = KeywordCasing.Upper,
    int sqlServerVersion = KeywordDictionary.SqlServer2022)
    : ICompletionProvider
{
    public string Name => "Keyword";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // T055: Suppress in comments and strings
        if (context.InComment || context.InString)
        {
            return false;
        }

        // Don't offer keywords in dot-qualified context (schema.object)
        if (context.PrecedingDot)
        {
            return false;
        }

        return true;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        var keywords = KeywordDictionary.GetKeywordsForClause(context.ClauseType);

        // If the clause-specific list is empty (e.g., Exec), fall back to nothing
        // The general keyword list is already handled by GetKeywordsForClause for Unknown
        if (keywords.Count == 0)
        {
            yield break;
        }

        // Deduplicate and yield
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            if (!seen.Add(keyword))
            {
                continue;
            }

            var displayText = ApplyCasing(keyword);
            // Single-word keywords (JOIN, WHERE) sort before compound ones
            // (CROSS JOIN, LEFT OUTER JOIN) so plain "JOIN" appears first.
            int priority = keyword.Contains(' ') ? 510 : 500;
            yield return new CompletionItem
            {
                DisplayText = displayText,
                InsertText = displayText,
                ObjectType = (int)CompletionObjectType.Keyword,
                SecondaryText = "Keyword",
                SortPriority = priority
            };
        }

        // Also include version-specific keywords if applicable
        if (sqlServerVersion >= KeywordDictionary.SqlServer2022 &&
            context.ClauseType is ClauseType.Select or ClauseType.Where or ClauseType.Unknown)
        {
            foreach (var keyword in KeywordDictionary.SqlServer2022Keywords)
            {
                if (!seen.Add(keyword))
                {
                    continue;
                }

                var displayText = ApplyCasing(keyword);
                yield return new CompletionItem
                {
                    DisplayText = displayText,
                    InsertText = displayText,
                    ObjectType = (int)CompletionObjectType.Keyword,
                    SecondaryText = "Keyword (2022+)",
                    SortPriority = 550
                };
            }
        }

        if (sqlServerVersion >= KeywordDictionary.SqlServer2025 &&
            context.ClauseType is ClauseType.Select or ClauseType.Where or ClauseType.Unknown)
        {
            foreach (var keyword in KeywordDictionary.SqlServer2025Keywords)
            {
                if (!seen.Add(keyword))
                {
                    continue;
                }

                var displayText = ApplyCasing(keyword);
                yield return new CompletionItem
                {
                    DisplayText = displayText,
                    InsertText = displayText,
                    ObjectType = (int)CompletionObjectType.Keyword,
                    SecondaryText = "Keyword (2025+)",
                    SortPriority = 560
                };
            }
        }
    }

    private string ApplyCasing(string keyword)
    {
        return casing switch
        {
            KeywordCasing.Upper => keyword.ToUpperInvariant(),
            KeywordCasing.Lower => keyword.ToLowerInvariant(),
            KeywordCasing.PascalCase => ToPascalCase(keyword),
            _ => keyword
        };
    }

    private static string ToPascalCase(string keyword)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            return keyword;
        }

        var parts = keyword.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) +
                            parts[i][1..].ToLowerInvariant();
            }
        }
        return string.Join(" ", parts);
    }
}

public enum KeywordCasing
{
    Upper,
    Lower,
    PascalCase
}
