namespace AkmlSql.PostgreSql.Completion;

/// <summary>A table reference found in a query, with where it is and whether a JOIN introduced it.</summary>
internal sealed record PgFromItem(PgTableRef Ref, int Index, bool IsJoin);

/// <summary>
/// Spec 042 (task T099, FR-064) - reads the tables, views, functions, derived tables and common table expressions a statement
/// refers to from its tokens. It is tolerant: the statement is being typed, so it ends where it ends and a missing piece is
/// skipped, not an error. This replaces the T-SQL alias and CTE extractors, which carry bracket and variable notions.
/// </summary>
internal static class PgTableRefExtractor
{
    // Words that cannot be a table alias: they continue or end the table reference.
    private static readonly HashSet<string> NotAlias = new(StringComparer.Ordinal)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "FETCH", "UNION", "INTERSECT", "EXCEPT", "JOIN", "INNER", "LEFT",
        "RIGHT", "FULL", "CROSS", "NATURAL", "ON", "USING", "SET", "RETURNING", "WINDOW", "FOR", "LATERAL", "SELECT", "FROM", "AS",
        "TABLESAMPLE", "WHEN", "THEN", "ONLY", "VALUES", "WITH", "INTO", "OUTER", "DO", "AND", "OR", "NOT", "IS", "IN", "NULL",
    };

    // Words that end the FROM list of a query.
    private static readonly HashSet<string> EndsFrom = new(StringComparer.Ordinal)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "FETCH", "UNION", "INTERSECT", "EXCEPT", "WINDOW", "RETURNING",
        "FOR", "SET", "VALUES", "SELECT", "ON", "WHEN",
    };

    // ── The FROM items of one query ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tables a query reads from or writes to, in order. <paramref name="start"/> and <paramref name="end"/> are token
    /// indices of the query's own tokens; text inside deeper parentheses belongs to other queries and is skipped.
    /// </summary>
    public static List<PgFromItem> FromItems(PgStatementTokens t, int start, int end)
    {
        var items = new List<PgFromItem>();
        var inFrom = false;          // after FROM or USING: commas separate items
        var verb = string.Empty;
        var i = start;

        while (i < end)
        {
            var tok = t[i];
            if (tok.IsOpen)
            {
                i = Math.Min(tok.Match, end - 1) + 1;
                continue;
            }

            if (tok.IsWord)
            {
                var up = tok.Upper;
                if (verb.Length == 0 && up is "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "MERGE")
                    verb = up;

                if (up == "FROM" && !IsDistinctFrom(t, i, start))
                {
                    if (verb == "DELETE" && !inFrom)
                    {
                        // DELETE FROM target [AS alias]
                        if (TryRef(t, i + 1, end, false, out var target, out var next)) { items.Add(new PgFromItem(target!, i + 1, false)); i = next; }
                        else i++;
                        continue;
                    }
                    inFrom = true;
                    i = AddRef(t, i + 1, end, items, false);
                    continue;
                }
                if (up == "JOIN")
                {
                    inFrom = true;
                    i = AddRef(t, i + 1, end, items, true);
                    continue;
                }
                if (up == "USING" && (verb is "DELETE" or "MERGE"))
                {
                    inFrom = true;
                    i = AddRef(t, i + 1, end, items, false);
                    continue;
                }
                if (up == "UPDATE" && (verb == "UPDATE") && items.Count == 0)
                {
                    i = AddRef(t, i + 1, end, items, false, allowFunction: false);
                    continue;
                }
                if (up == "INTO" && verb is "INSERT" or "MERGE")
                {
                    i = AddRef(t, i + 1, end, items, false, allowFunction: false);
                    continue;
                }
                if (EndsFrom.Contains(up) && up != "FROM") inFrom = false;
            }
            else if (inFrom && tok.IsComma)
            {
                i = AddRef(t, i + 1, end, items, false);
                continue;
            }
            i++;
        }
        return items;
    }

    // IS DISTINCT FROM / IS NOT DISTINCT FROM: the FROM belongs to the comparison. (SELECT DISTINCT FROM t is a real FROM.)
    private static bool IsDistinctFrom(PgStatementTokens t, int from, int start) =>
        from - 2 >= start && t[from - 1].IsWordEqual("DISTINCT") && (t[from - 2].IsWordEqual("IS") || t[from - 2].IsWordEqual("NOT"));

    private static int AddRef(PgStatementTokens t, int p, int end, List<PgFromItem> items, bool isJoin, bool allowFunction = true)
    {
        if (TryRef(t, p, end, allowFunction, out var reference, out var next))
        {
            items.Add(new PgFromItem(reference!, p, isJoin));
            return next;
        }
        return Math.Max(p, 0);
    }

    /// <summary>Parses one table reference at <paramref name="p"/>: <c>[ONLY] name [*] [[AS] alias [(cols)]]</c>, a subquery, a function call.</summary>
    private static bool TryRef(PgStatementTokens t, int p, int end, bool allowFunction, out PgTableRef? reference, out int next)
    {
        reference = null;
        next = p;
        if (p >= end) return false;

        while (p < end && t[p].IsWord && t[p].Upper is "ONLY" or "LATERAL")
            p++;
        if (p >= end) return false;

        var tok = t[p];

        // (subquery) alias, (VALUES ...) alias
        if (tok.IsOpen)
        {
            var close = Math.Min(tok.Match, end);
            var after = close + 1;
            if (!t.StartsSubquery(p))
            {
                // (a JOIN b ON ...) : a parenthesised join. Its tables are read as if they stood here.
                next = Math.Min(after, end);
                return false;
            }

            var isValues = t[p + 1].IsWordEqual("VALUES");
            var inner = isValues ? new List<PgFromItem>() : FromItems(t, p + 1, close);
            var stars = new List<PgTableRef>();
            var columns = isValues ? new List<string>() : SelectListNames(t, p + 1, close, inner.Select(i => i.Ref).ToList(), out stars);
            ReadAlias(t, after, end, out var alias, out var aliasColumns, out var afterAlias);
            reference = new PgTableRef(alias, null, null, isValues ? PgTableRefKind.Values : PgTableRefKind.Derived,
                aliasColumns.Count > 0 ? aliasColumns : columns, aliasColumns.Count > 0 ? new List<PgTableRef>() : stars);
            next = afterAlias;
            return true;
        }

        if (!tok.IsName) return false;
        if (tok.IsWord && NotAlias.Contains(tok.Upper) && tok.Upper != "ONLY") return false;

        // schema . name   (a leading database name is ignored)
        var parts = new List<PgName> { tok.Name };
        var q = p + 1;
        while (q + 1 < end && t[q].IsDot && t[q + 1].IsName)
        {
            parts.Add(t[q + 1].Name);
            q += 2;
        }
        var name = parts[^1];
        var schema = parts.Count >= 2 ? parts[^2] : (PgName?)null;

        // function in FROM: unnest(...) AS u(x), generate_series(1, 3) g
        if (allowFunction && q < end && t[q].IsOpen)
        {
            var closeCall = Math.Min(t[q].Match, end);
            var afterCall = closeCall + 1;
            ReadAlias(t, afterCall, end, out var fnAlias, out var fnColumns, out var afterFn);
            reference = new PgTableRef(fnAlias.Length > 0 ? fnAlias : name.Folded, schema?.Folded, name.Folded, PgTableRefKind.Function,
                fnColumns, Array.Empty<PgTableRef>(), name.Quoted);
            next = afterFn;
            return true;
        }

        if (q < end && t[q].IsStar) q++;          // name * : include inheritance children
        if (q < end && t[q].IsWordEqual("TABLESAMPLE"))
        {
            q++;
            while (q < end && !(t[q].IsOpen)) q++;
            if (q < end) q = Math.Min(t[q].Match, end) + 1;
            if (q < end && t[q].IsWordEqual("REPEATABLE") && q + 1 < end && t[q + 1].IsOpen)
                q = Math.Min(t[q + 1].Match, end) + 1;
        }

        ReadAlias(t, q, end, out var tableAlias, out var tableColumns, out var afterTable);
        reference = new PgTableRef(tableAlias, schema?.Folded, name.Folded, PgTableRefKind.Table, tableColumns, Array.Empty<PgTableRef>(), name.Quoted);
        next = afterTable;
        return true;
    }

    // [AS] alias [ ( col, ... ) ]
    private static void ReadAlias(PgStatementTokens t, int p, int end, out string alias, out List<string> columns, out int next)
    {
        alias = string.Empty;
        columns = new List<string>();
        next = Math.Min(p, end);
        if (p >= end) return;

        var q = p;
        var explicitAs = t[q].IsWordEqual("AS");
        if (explicitAs) q++;
        if (q >= end || !t[q].IsName) return;
        if (!explicitAs && t[q].IsWord && NotAlias.Contains(t[q].Upper)) return;
        if (t[q].IsWord && !explicitAs && IsClauseKeyword(t[q].Upper)) return;

        alias = t[q].Name.Folded;
        q++;
        if (q < end && t[q].IsOpen)
        {
            var close = Math.Min(t[q].Match, end);
            var itemStart = true;
            for (var c = q + 1; c < close; c++)
            {
                if (t[c].Depth != t[q].Depth + 1) continue;
                if (t[c].IsComma) { itemStart = true; continue; }
                if (itemStart && t[c].IsName) columns.Add(t[c].Name.Folded);
                itemStart = false;
            }
            q = close + 1;
        }
        next = Math.Min(q, end);
    }

    private static bool IsClauseKeyword(string upper) => EndsFrom.Contains(upper) || upper is "JOIN" or "INNER" or "LEFT" or "RIGHT" or "FULL" or "CROSS" or "NATURAL";

    // ── Select lists (the columns a subquery or CTE exposes) ────────────────────────────────────

    /// <summary>
    /// The output column names of the query whose tokens are [start, end): <c>expr AS name</c>, <c>t.name</c>, <c>name</c>,
    /// <c>fn(...)</c> (named after the function). A star is returned in <paramref name="stars"/> as the references it expands over.
    /// </summary>
    public static List<string> SelectListNames(PgStatementTokens t, int start, int end, IReadOnlyList<PgTableRef> scopeRefs, out List<PgTableRef> stars)
    {
        stars = new List<PgTableRef>();
        var names = new List<string>();
        if (start >= end) return names;

        var depth = t[start].Depth;
        var i = start;
        // WITH inside the body: its main query starts after the last CTE.
        while (i < end && !(t[i].IsWordEqual("SELECT") && t[i].Depth == depth)) i++;
        if (i >= end) return names;
        i++;
        if (i < end && t[i].IsWord && t[i].Upper is "DISTINCT" or "ALL")
        {
            i++;
            if (i < end && t[i].IsWordEqual("ON") && i + 1 < end && t[i + 1].IsOpen)
                i = Math.Min(t[i + 1].Match, end) + 1;
        }

        var itemStart = i;
        for (; i <= end; i++)
        {
            var atEnd = i == end;
            var stop = !atEnd && t[i].Depth == depth && t[i].IsWord && t[i].Upper is "FROM" or "INTO" or "WHERE" or "GROUP" or "HAVING" or "ORDER" or "LIMIT" or "UNION" or "INTERSECT" or "EXCEPT" or "WINDOW" or "RETURNING" or "FETCH" or "OFFSET" or "FOR";
            var comma = !atEnd && t[i].Depth == depth && t[i].IsComma;
            if (!atEnd && !stop && !comma) continue;

            ItemName(t, itemStart, i, depth, scopeRefs, names, stars);
            itemStart = i + 1;
            if (stop || atEnd) break;
        }
        return names;
    }

    private static void ItemName(PgStatementTokens t, int s, int e, int depth, IReadOnlyList<PgTableRef> scopeRefs, List<string> names, List<PgTableRef> stars)
    {
        if (s >= e) return;

        // `*` or `qualifier.*`
        var last = t[e - 1];
        if (last.IsStar && t[s].Depth == depth && (e - s == 1 || (e - s == 3 && t[s + 1].IsDot)))
        {
            if (e - s == 1) stars.AddRange(scopeRefs);
            else
            {
                var q = t[s].Name.Folded;
                var match = scopeRefs.FirstOrDefault(r => r.Qualifier == q);
                if (match != null) stars.Add(match);
            }
            return;
        }

        // expr AS alias
        if (e - s >= 3 && t[e - 2].IsWordEqual("AS") && t[e - 1].IsName)
        {
            names.Add(t[e - 1].Name.Folded);
            return;
        }

        // expr alias   (the last word is a name that is not part of a dotted path or an operator tail)
        if (e - s >= 2 && last.IsName && !(last.IsWord && (NotAlias.Contains(last.Upper) || last.Upper is "END" or "ASC" or "DESC" or "FIRST" or "LAST" or "TRUE" or "FALSE" or "NULL")))
        {
            var before = t[e - 2];
            var ok = before.IsClose || before.IsLiteral || before.IsName && !before.IsWordEqual("DISTINCT") && !before.IsWordEqual("NOT");
            if (ok && !(before.IsWord && before.Upper is "AND" or "OR" or "NOT" or "THEN" or "ELSE" or "WHEN" or "CASE" or "BETWEEN" or "IN" or "IS" or "LIKE" or "ILIKE" or "AS" or "DISTINCT"))
            {
                names.Add(last.Name.Folded);
                return;
            }
        }

        // name, qualifier.name, name::type
        if (t[s].IsName && !t[s].IsWordEqual("CASE") && !t[s].IsWordEqual("CAST") && !t[s].IsWordEqual("NOT"))
        {
            if (s + 1 >= e || t[s + 1].IsCast)
            {
                names.Add(t[s].Name.Folded);
                return;
            }
            var p = s;
            while (p + 2 < e + 0 && t[p + 1].IsDot && t[p + 2].IsName) p += 2;
            if (p > s && (p + 1 >= e || t[p + 1].IsCast))
            {
                names.Add(t[p].Name.Folded);
                return;
            }
            // fn(...)
            if (t[s + 1].IsOpen && t[s + 1].Match == e - 1)
            {
                names.Add(t[s].Name.Folded);
                return;
            }
        }
        if (t[s].IsWordEqual("CASE")) names.Add("case");
    }

    // ── Common table expressions ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The common table expressions defined by the <c>WITH</c> that starts at token <paramref name="withIndex"/>, with the index
    /// of the token that closes each body (so a caret before it can tell which are complete).
    /// </summary>
    public static List<(PgTableRef Cte, int BodyStart, int BodyEnd, bool Recursive)> Ctes(PgStatementTokens t, int withIndex, int end)
    {
        var result = new List<(PgTableRef, int, int, bool)>();
        if (withIndex >= end || !t[withIndex].IsWordEqual("WITH")) return result;

        var depth = t[withIndex].Depth;
        var i = withIndex + 1;
        var recursive = false;
        if (i < end && t[i].IsWordEqual("RECURSIVE")) { recursive = true; i++; }

        while (i < end)
        {
            if (!t[i].IsName || t[i].Depth != depth) break;
            var name = t[i].Name.Folded;
            i++;

            var declared = new List<string>();
            if (i < end && t[i].IsOpen && !t.StartsSubquery(i))
            {
                var close = Math.Min(t[i].Match, end);
                for (var c = i + 1; c < close; c++)
                    if (t[c].IsName) declared.Add(t[c].Name.Folded);
                i = close + 1;
            }

            if (i >= end || !t[i].IsWordEqual("AS")) break;
            i++;
            if (i < end && t[i].IsWordEqual("NOT")) i++;
            if (i < end && t[i].IsWordEqual("MATERIALIZED")) i++;
            if (i >= end || !t[i].IsOpen) break;

            var open = i;
            var closeBody = Math.Min(t[open].Match, end);
            var inner = FromItems(t, open + 1, closeBody);
            var innerRefs = inner.Select(x => x.Ref).ToList();
            List<string> columns;
            List<PgTableRef> stars;
            if (declared.Count > 0) { columns = declared; stars = new List<PgTableRef>(); }
            else columns = BodyColumns(t, open + 1, closeBody, innerRefs, out stars);

            result.Add((new PgTableRef(string.Empty, null, name, PgTableRefKind.Cte, columns, stars), open + 1, closeBody, recursive));

            i = closeBody + 1;
            if (i < end && t[i].IsComma && t[i].Depth == depth) i++;
            else break;
        }
        return result;
    }

    // SELECT ... bodies list their select list; INSERT/UPDATE/DELETE ... RETURNING bodies list their RETURNING list.
    private static List<string> BodyColumns(PgStatementTokens t, int start, int end, IReadOnlyList<PgTableRef> refs, out List<PgTableRef> stars)
    {
        stars = new List<PgTableRef>();
        if (start >= end) return new List<string>();
        var first = t[start];
        if (first.IsWord && first.Upper is "INSERT" or "UPDATE" or "DELETE")
        {
            var depth = first.Depth;
            for (var i = start; i < end; i++)
            {
                if (t[i].Depth == depth && t[i].IsWordEqual("RETURNING"))
                {
                    var wrapped = SelectListFromReturning(t, i + 1, end, depth, refs, out stars);
                    return wrapped;
                }
            }
            return new List<string>();
        }
        return SelectListNames(t, start, end, refs, out stars);
    }

    private static List<string> SelectListFromReturning(PgStatementTokens t, int start, int end, int depth, IReadOnlyList<PgTableRef> refs, out List<PgTableRef> stars)
    {
        stars = new List<PgTableRef>();
        var names = new List<string>();
        var itemStart = start;
        for (var i = start; i <= end; i++)
        {
            var atEnd = i == end;
            if (!atEnd && !(t[i].Depth == depth && t[i].IsComma)) continue;
            ItemName(t, itemStart, i, depth, refs, names, stars);
            itemStart = i + 1;
        }
        return names;
    }
}
