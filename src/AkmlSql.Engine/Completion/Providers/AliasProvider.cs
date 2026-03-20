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

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Suggest alias when in FROM/JOIN after a table name (no dot, no partial text)
        if (context.PrecedingDot || context.InComment || context.InString)
            return false;

        if (context.ClauseType != ClauseType.From)
            return false;

        // Only suggest if the preceding token looks like it could be a table name
        if (context.PrecedingToken == null)
            return false;

        var tokenType = context.PrecedingToken.TokenType;
        return tokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Identifier
            || tokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.QuotedIdentifier;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (context.PrecedingToken == null)
            yield break;

        var tableName = context.PrecedingToken.Text.Trim('[', ']', '"');
        var candidates = GenerateAliasCandidates(tableName);
        var existingAliases = new HashSet<string>(context.AvailableAliases.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (existingAliases.Contains(candidate))
                continue;

            yield return new CompletionItem
            {
                DisplayText = candidate,
                InsertText = candidate,
                ObjectType = (int)CompletionObjectType.Alias,
                SecondaryText = $"Alias for {tableName}",
                SourceObject = tableName,
                SortPriority = 50 // High priority — aliases are contextually relevant
            };
        }
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
            return candidates;

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
            if (i == 0 || char.IsUpper(c) || c == '_')
            {
                if (c == '_')
                {
                    // Take the character after underscore
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
