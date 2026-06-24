using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T098: Suggests table aliases after a table reference in FROM/JOIN clauses.
/// Generates alias candidates from PascalCase first letters (e.g., "OrderDetails" → "od").
/// Checks for conflicts with existing aliases in the query.
/// </summary>
public class AliasProvider : ICompletionProvider
{
    public string Name => "Alias";

    // Spec 030 T035 / FR-015 — alias generation policy, pushed per request by CompletionEngine.
    /// <summary>Insert the <c>AS</c> keyword ("Orders AS o" vs "Orders o").</summary>
    public bool IncludeAs { get; set; } = true;
    /// <summary>User-defined object→alias overrides (case-insensitive by object name).</summary>
    public IReadOnlyDictionary<string, string> ObjectAliasMap { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Prefixes stripped from a table name before generating an alias (e.g. "tbl_").</summary>
    public IReadOnlyList<string> PrefixesToIgnore { get; set; } = System.Array.Empty<string>();

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Suggest alias when in FROM/JOIN after a table name (no dot, no partial text)
        if (context.PrecedingDot || context.InComment || context.InString)
        {
            return false;
        }

        if (context.ClauseType != ClauseType.From)
        {
            return false;
        }

        // Only suggest if the preceding token looks like it could be a table name
        if (context.PrecedingToken == null)
        {
            return false;
        }

        var tokenType = context.PrecedingToken.TokenType;
        return tokenType is Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Identifier or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.QuotedIdentifier;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (context.PrecedingToken == null)
        {
            yield break;
        }

        var tableName = context.PrecedingToken.Text.Trim('[', ']', '"');
        var existingAliases = new HashSet<string>(context.AvailableAliases.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var (display, insert) in BuildAliasItems(tableName, existingAliases))
        {
            yield return new CompletionItem
            {
                DisplayText = display,
                InsertText = insert,
                ObjectType = (int)CompletionObjectType.Alias,
                SecondaryText = $"Alias for {tableName}",
                SourceObject = tableName,
                SortPriority = 50 // High priority — aliases are contextually relevant
            };
        }
    }

    /// <summary>
    /// Spec 030 T035 / FR-015 — applies the alias policy: a custom <see cref="ObjectAliasMap"/>
    /// entry is offered first; otherwise candidates are generated from the table name after
    /// stripping any <see cref="PrefixesToIgnore"/>. <c>Insert</c> carries the <c>AS</c> keyword
    /// when <see cref="IncludeAs"/> is set. Aliases already in scope are skipped.
    /// </summary>
    public IReadOnlyList<(string Display, string Insert)> BuildAliasItems(string tableName, ISet<string> existingAliases)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(tableName))
            return result;

        var candidates = new List<string>(4);

        // 1. Custom object→alias override (offered first).
        if (ObjectAliasMap != null && ObjectAliasMap.TryGetValue(tableName, out var mapped)
            && !string.IsNullOrWhiteSpace(mapped))
        {
            candidates.Add(mapped);
        }

        // 2. Generated candidates from the prefix-stripped name.
        foreach (var c in GenerateAliasCandidates(StripIgnoredPrefixes(tableName)))
        {
            if (!candidates.Contains(c, StringComparer.OrdinalIgnoreCase))
                candidates.Add(c);
        }

        foreach (var candidate in candidates)
        {
            if (existingAliases != null && existingAliases.Contains(candidate))
                continue;
            result.Add((candidate, IncludeAs ? "AS " + candidate : candidate));
        }
        return result;
    }

    /// <summary>Returns <paramref name="tableName"/> with the first matching ignored prefix removed.</summary>
    internal string StripIgnoredPrefixes(string tableName)
    {
        if (PrefixesToIgnore == null) return tableName;
        foreach (var prefix in PrefixesToIgnore)
        {
            if (!string.IsNullOrEmpty(prefix) && tableName.Length > prefix.Length
                && tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return tableName.Substring(prefix.Length);
            }
        }
        return tableName;
    }

    /// <summary>
    /// Generates alias candidates from a table name:
    /// 1. PascalCase first letters: "OrderDetails" → "od"
    /// 2. First letter only: "OrderDetails" → "o"
    /// 3. First + last letter for short names: "Users" → "u"
    /// </summary>
    internal static List<string> GenerateAliasCandidates(string tableName)
    {
        var candidates = new List<string>(3);

        if (string.IsNullOrEmpty(tableName))
        {
            return candidates;
        }

        // Strategy 1: PascalCase extraction (e.g., "OrderDetails" → "od", "SalesOrderHeader" → "soh")
        var pascalAlias = ExtractPascalCaseAlias(tableName);
        if (!string.IsNullOrEmpty(pascalAlias) && pascalAlias.Length > 1)
        {
            candidates.Add(pascalAlias);
        }

        // Strategy 2: First letter (lowercase)
        var firstLetter = tableName.Substring(0, 1).ToLowerInvariant();
        if (!candidates.Contains(firstLetter, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(firstLetter);
        }

        // Strategy 3: First two letters for longer names
        if (tableName.Length >= 3)
        {
            var twoLetters = tableName.Substring(0, 2).ToLowerInvariant();
            if (!candidates.Contains(twoLetters, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(twoLetters);
            }
        }

        return candidates;
    }

    private static string ExtractPascalCaseAlias(string name)
    {
        var chars = new List<char>(8);

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i == 0 || char.IsUpper(c) || c == '_' || c == '-')
            {
                if (c == '_' || c == '-')
                {
                    // Take the character after the separator (underscore or hyphen)
                    if (i + 1 < name.Length)
                    {
                        chars.Add(char.ToLowerInvariant(name[i + 1]));
                        i++; // skip next
                    }
                }
                else
                {
                    chars.Add(char.ToLowerInvariant(c));
                }
            }
        }

        return new string(chars.ToArray());
    }
}
