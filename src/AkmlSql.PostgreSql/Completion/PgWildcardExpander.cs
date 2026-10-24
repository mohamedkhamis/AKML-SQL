using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using Serilog;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T103, FR-067) - expands <c>SELECT *</c> (or <c>alias.*</c>, or <c>RETURNING *</c>) into the column list. The
/// names come back already written the way PostgreSQL wants them - double-quoted only where the name needs it - so the caller
/// pastes them as they are, on the contract of the SQL Server wildcard handler.
/// </summary>
public sealed class PgWildcardExpander
{
    public WildcardExpansionResponse Handle(string? text, int cursorOffset, string? qualifier, DatabaseCache? cache)
    {
        try
        {
            text ??= string.Empty;
            if (cache == null) return Fail("The schema is not loaded yet.");

            var star = FindStar(text, cursorOffset);
            if (star < 0) return Fail("Place the caret on the * of a select list.");

            // the statement as it stands with the caret at the star
            var context = PgCursorContextAnalyzer.Analyze(text, star);
            if (context.Suppressed) return Fail("The caret is inside a string or a comment.");
            if (context.CallName != null) return Fail("A * inside a function call (count(*)) is not a column wildcard.");
            if (context.Clause is not (PgClause.SelectList or PgClause.Returning)) return Fail("The * is not in a select list.");
            if (context.AfterOperand) return Fail("The * multiplies; it is not a column wildcard.");

            // Spec 043 follow-up (review): the qualifier written before the star wins over the one the caller sends. The web
            // editor sends a quoted qualifier unquoted ('"Ord".*' as Ord, which SQL Server matches without regard to case),
            // and folding that would look for ord; the text still says it was quoted.
            var wanted = QualifierBefore(text, star) ?? Fold(qualifier);
            var view = new PgCatalogView(cache, new PgCompletionOptions());

            var refs = new List<PgTableRef>(context.VisibleTables);
            if (context.Clause == PgClause.Returning && context.Target != null && !refs.Contains(context.Target)) refs.Insert(0, context.Target);
            if (wanted != null) refs = refs.Where(r => r.Qualifier == wanted).ToList();

            var groups = new List<WildcardTableGroup>();
            foreach (var r in refs)
            {
                var columns = view.ColumnsOf(r, context.VisibleCtes);
                if (columns.Count == 0) continue;
                groups.Add(new WildcardTableGroup
                {
                    TableName = TableName(r, view),
                    Qualifier = PgCatalogView.Written(r.Qualifier, forceQuote: false),
                    Columns = columns.Select(c => new WildcardColumn { ColumnName = PgCatalogView.Written(c.Name, forceQuote: false), TypeDisplay = c.Type }).ToArray(),
                });
            }

            return groups.Count == 0
                ? Fail(wanted != null ? $"'{wanted}' is not a table or alias of this query." : "No table of this query has known columns.")
                : new WildcardExpansionResponse { Success = true, Tables = groups.ToArray() };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgreSQL wildcard expansion failed");
            return Fail("The wildcard could not be expanded.");
        }
    }

    // the table as the database knows it (schema.name), or what the query calls it when it is not a table of the database
    private static string TableName(PgTableRef r, PgCatalogView view)
    {
        if (r.Name == null) return r.Qualifier;
        var resolved = r.Kind == PgTableRefKind.Table ? view.Resolve(r.Schema, r.Name) : null;
        if (resolved != null) return resolved.SchemaName + "." + resolved.ObjectName;
        return (r.Schema != null ? r.Schema + "." : string.Empty) + r.Name;
    }

    private static WildcardExpansionResponse Fail(string message) => new() { Success = false, ErrorMessage = message };

    // the '*' at or just before the caret
    private static int FindStar(string text, int caret)
    {
        if (caret > 0 && caret <= text.Length && text[caret - 1] == '*') return caret - 1;
        if (caret >= 0 && caret < text.Length && text[caret] == '*') return caret;
        return -1;
    }

    // alias.* : the word before the dot
    private static string? QualifierBefore(string text, int star)
    {
        if (star < 2 || text[star - 1] != '.') return null;
        var end = star - 1;
        if (text[end - 1] == '"')
        {
            // Spec 043 follow-up (review): the opening quote is the first one back that is not half of a "" (a quote inside the
            // name); the nearest quote back was that half for '"a""b".*'.
            for (var open = end - 2; open >= 0; open--)
            {
                if (text[open] != '"') continue;
                if (open > 0 && text[open - 1] == '"') { open--; continue; }
                return open == end - 2 ? null : text.Substring(open + 1, end - open - 2).Replace("\"\"", "\"");
            }
            return null;
        }
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_' || text[start - 1] == '$')) start--;
        return start == end ? null : text.Substring(start, end - start).ToLowerInvariant();
    }

    private static string? Fold(string? qualifier)
    {
        if (string.IsNullOrWhiteSpace(qualifier)) return null;
        var q = qualifier!.Trim();
        return q.Length >= 2 && q[0] == '"' && q[^1] == '"' ? q.Substring(1, q.Length - 2).Replace("\"\"", "\"") : q.ToLowerInvariant();
    }
}
