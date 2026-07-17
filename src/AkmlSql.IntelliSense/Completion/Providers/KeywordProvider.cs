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
        // Spec 030 (user-reported): after an `IS` token the only valid continuations are NULL / NOT NULL.
        // Offer THOSE — not the full "IS NULL" / "IS NOT NULL" predicate keywords, which would duplicate
        // the already-typed IS (e.g. `WHERE x IS ` + committing `IS NOT NULL` → `IS IS NOT NULL`). The full
        // predicates still appear before IS is typed (filtered by the partial), so `IS NULL` is reachable.
        if (IsPrecedingTokenIs(context))
        {
            yield return KeywordItem("NOT NULL", 490);
            yield return KeywordItem("NULL", 491);
            yield break;
        }

        // CREATE TABLE column-definition context: data types rank first (100), then
        // constraint keywords (150). Handled separately to support different priorities
        // for the two sub-groups within the same clause context.
        if (context.ClauseType == ClauseType.CreateTableColumnDef)
        {
            var seenColDef = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var keyword in KeywordDictionary.AfterCreateTableColumnDef)
            {
                if (!seenColDef.Add(keyword)) continue;
                bool isDataType = KeywordDictionary.DataTypeSet.Contains(keyword);
                int priority = isDataType ? 100 : 150;
                var displayText = ApplyCasing(keyword);
                yield return new CompletionItem
                {
                    DisplayText   = displayText,
                    InsertText    = displayText,
                    ObjectType    = (int)CompletionObjectType.Keyword,
                    SecondaryText = isDataType ? "Data Type" : "Keyword",
                    SortPriority  = priority
                };
            }
            yield break;
        }

        // Spec 032 B3: join qualifiers get a qualifier-specific set (the dictionary's
        // JoinQualifier fallback is just ["JOIN"]). PrecedingToken IS the qualifier.
        var keywords = context.ClauseType == ClauseType.JoinQualifier
            ? JoinQualifierKeywords(context)
            : KeywordDictionary.GetKeywordsForClause(context.ClauseType);

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

        // Spec 032 D: built-in scalar functions in expression positions. The ~130-entry
        // catalog was referenced only by GetAllKeywords — no provider ever emitted it
        // per-clause, so `WHERE OrderDate >= |` / `SET Price = |` / `VALUES (|` offered no
        // built-ins at all. Ranked below clause keywords; columns (10–30) stay on top.
        if (ExpressionClauseTypes.Contains(context.ClauseType))
        {
            foreach (var fn in KeywordDictionary.ScalarFunctions)
            {
                if (!seen.Add(fn))
                {
                    continue;
                }

                yield return new CompletionItem
                {
                    DisplayText = fn,
                    InsertText = fn,
                    ObjectType = (int)CompletionObjectType.Function,
                    SecondaryText = "Built-in Function",
                    // Two tiers: the everyday functions stay above the 50-item cap even in
                    // busy contexts (but BELOW compound keywords at 510 — "IS NULL" etc. must
                    // survive the cap too); the long tail is reachable by typing a prefix.
                    SortPriority = CommonFunctions.Contains(fn) ? 512 : 520
                };
            }
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

    // Spec 032 D: the everyday built-ins users reach for constantly — ranked above the
    // long tail so they survive the suggestion cap in busy expression contexts.
    private static readonly HashSet<string> CommonFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Keep this list TIGHT (~20): it must fit under the 50-item suggestion cap after
        // columns + clause keywords + snippets; the long tail stays prefix-reachable at 520.
        "GETDATE", "DATEADD", "DATEDIFF", "YEAR", "MONTH", "DAY",
        "ISNULL", "COALESCE", "NULLIF", "IIF",
        "LEN", "UPPER", "LOWER", "TRIM", "SUBSTRING", "REPLACE", "CHARINDEX", "CONCAT",
        "ROUND", "ABS", "NEWID",
    };

    // Spec 032 D: positions where an expression is being written — built-ins are valid here.
    private static readonly HashSet<ClauseType> ExpressionClauseTypes =
    [
        ClauseType.Select,
        ClauseType.Where,
        ClauseType.Having,
        ClauseType.UpdateSet,
        ClauseType.InsertValues,
        ClauseType.OrderBy,
        ClauseType.GroupBy,
        ClauseType.JoinOn,
        ClauseType.CaseStart,
        ClauseType.CaseWhen,
        ClauseType.CaseThen,
        ClauseType.CaseElse
    ];

    /// <summary>Spec 032 B3 — the keyword set for a join-qualifier position, per qualifier.</summary>
    private static IReadOnlyList<string> JoinQualifierKeywords(CursorContext context)
    {
        return context.PrecedingToken?.TokenType switch
        {
            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Inner => ["JOIN"],
            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Left
                or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Right
                or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Full => ["JOIN", "OUTER JOIN"],
            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Cross => ["JOIN", "APPLY"],
            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Outer => ["JOIN"],
            _ => ["JOIN"]
        };
    }

    /// <summary>True when the token immediately before the cursor is the <c>IS</c> predicate keyword.</summary>
    private static bool IsPrecedingTokenIs(CursorContext context)
        => context.PrecedingToken != null
           && string.Equals(context.PrecedingToken.Text?.Trim(), "is", StringComparison.OrdinalIgnoreCase);

    private CompletionItem KeywordItem(string keyword, int sortPriority)
    {
        var displayText = ApplyCasing(keyword);
        return new CompletionItem
        {
            DisplayText  = displayText,
            InsertText   = displayText,
            ObjectType   = (int)CompletionObjectType.Keyword,
            SecondaryText = "Keyword",
            SortPriority = sortPriority
        };
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
