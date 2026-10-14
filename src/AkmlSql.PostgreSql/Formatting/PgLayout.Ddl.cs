namespace AkmlSql.PostgreSql.Formatting;

internal sealed partial class PgLayout
{
    // ═══ statements the layout does not restructure ══════════════════════════════════════════════════

    /// <summary>
    /// Any other statement: its tokens spaced and cased by the shared rules, its line breaks and their indentation kept as written
    /// (relative to the statement's first line). Nothing is moved, so the result is stable.
    /// </summary>
    private PgDoc Fallback(int lo, int hi)
    {
        if (lo >= hi) return PgDoc.Empty;
        MarkStatementHead(lo, hi);
        MarkUtilityWords(lo, hi);
        var baseColumn = OriginalColumn(lo);

        // A statement written on one line is wrapped after its top-level commas when it is longer than the line; one written over several
        // lines keeps its own breaks.
        var wrap = true;
        for (var k = lo; k < hi && wrap; k++)
            if ((k > lo && (T[k].NewlinesBefore > 0 || T[k - 1].TrailingLine is not null)) || StartsOwnLine(k)) wrap = false;

        var parts = new List<PgDoc>();
        var depth = 0;
        for (var i = lo; i < hi; i++)
        {
            if (i > lo)
            {
                if (T[i - 1].TrailingLine is not null || T[i].NewlinesBefore > 0 || StartsOwnLine(i))
                {
                    var indent = Math.Max(0, OriginalColumn(i) - baseColumn);
                    parts.Add(PgDoc.Nest(indent, PgDoc.HardLine()));
                }
                else if (wrap && depth == 0 && T[i - 1].Kind == PgFt.Comma)
                {
                    parts.Add(PgDoc.Line(" "));
                }
                else if (NeedSpace(i - 1, i, lo))
                {
                    parts.Add(PgDoc.Text(" "));
                }
            }
            parts.Add(Single(i, lo, suppressTrailing: false));
            if (T[i].Kind is PgFt.Open or PgFt.OpenSq) depth++;
            else if (T[i].Kind is PgFt.Close or PgFt.CloseSq) depth--;
        }
        return wrap ? PgDoc.Group(PgDoc.Nest(Tab, PgDoc.Concat(parts))) : PgDoc.Concat(parts);
    }

    /// <summary>A statement's first word is a keyword; so are the words that make up the head of a CREATE / ALTER / DROP before the object's name.</summary>
    private void MarkStatementHead(int lo, int hi)
    {
        if (!T[lo].IsWord) return;
        _keywordTokens.Add(lo);
        if (!T[lo].IsAny("CREATE", "ALTER", "DROP", "COMMENT", "TRUNCATE", "GRANT", "REVOKE", "REFRESH", "REINDEX", "CLUSTER", "VACUUM", "LOCK")) return;
        for (var i = lo + 1; i < hi && T[i].IsWord && PgWords.DdlHeadWords.Contains(T[i].Upper); i++) _keywordTokens.Add(i);
    }

    /// <summary>
    /// In a statement the layout does not restructure, the words that are syntax there (<see cref="PgWords.UtilityWords"/>) are keywords
    /// at the top level of the statement - not inside parentheses, not after a dot or a cast.
    /// </summary>
    private void MarkUtilityWords(int lo, int hi)
    {
        if (!T[lo].IsAny("ALTER", "GRANT", "REVOKE", "MERGE", "SET", "RESET", "SHOW", "CREATE", "DROP", "COMMENT", "TRUNCATE", "VACUUM", "ANALYZE",
                "REINDEX", "REFRESH", "LOCK", "BEGIN", "COMMIT", "ROLLBACK", "START", "SAVEPOINT", "RELEASE", "LISTEN", "UNLISTEN", "NOTIFY",
                "PREPARE", "EXECUTE", "DEALLOCATE", "CLUSTER", "DO", "CALL", "DECLARE", "FETCH", "MOVE", "CLOSE", "COPY", "IMPORT", "SECURITY", "DISCARD"))
            return;

        for (var i = lo; i < hi;)
        {
            var end = ElementEnd(i, hi);
            if (end > i + 1) { i = end; continue; }          // a bracket group or CASE: names and expressions
            var t = T[i];
            if (t.IsWord && PgWords.UtilityWords.Contains(t.Upper)
                && !(i > lo && (T[i - 1].Kind == PgFt.Dot || T[i - 1].IsOp("::")))
                && !(i + 1 < hi && T[i + 1].Kind == PgFt.Dot))
                _keywordTokens.Add(i);
            i++;
        }
    }

    private bool StartsOwnLine(int i) => T[i].Leading.Count > 0 && T[i].Leading[^1].EndsLine;

    /// <summary>The column of token <paramref name="i"/> on its line in the original text, with tabs counted as the style's tab size.</summary>
    private int OriginalColumn(int i)
    {
        var start = T[i].Start;
        var lineStart = start;
        while (lineStart > 0 && _source[lineStart - 1] != '\n') lineStart--;
        var column = 0;
        for (var k = lineStart; k < start; k++) column += _source[k] == '\t' ? Tab : 1;
        return column;
    }

    // ═══ CREATE ══════════════════════════════════════════════════════════════════════════════════════

    private PgDoc Create(int lo, int hi)
    {
        // CREATE [OR REPLACE] [GLOBAL|LOCAL] [TEMP|TEMPORARY|UNLOGGED] [UNIQUE] [RECURSIVE] [MATERIALIZED] TABLE|INDEX|VIEW
        var i = lo + 1;
        while (i < hi && T[i].IsAny("OR", "REPLACE", "GLOBAL", "LOCAL", "TEMP", "TEMPORARY", "UNLOGGED", "UNIQUE", "RECURSIVE", "MATERIALIZED")) i++;
        if (i >= hi) return Fallback(lo, hi);
        if (T[i].IsAny("TABLE", "INDEX", "VIEW"))
        {
            MarkStatementHead(lo, hi);
            // IF NOT EXISTS / CONCURRENTLY after the object kind
            for (var k = i + 1; k < hi && T[k].IsAny("IF", "NOT", "EXISTS", "CONCURRENTLY"); k++) _keywordTokens.Add(k);
        }
        if (T[i].Is("TABLE")) return CreateTable(lo, i, hi);
        if (T[i].Is("INDEX")) return CreateIndex(lo, hi);
        if (T[i].Is("VIEW")) return CreateView(lo, i, hi);
        return Fallback(lo, hi);
    }

    // ── CREATE TABLE ─────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ConstraintStarters = new(StringComparer.Ordinal)
    {
        "CONSTRAINT", "NOT", "NULL", "DEFAULT", "PRIMARY", "UNIQUE", "CHECK", "REFERENCES", "GENERATED", "COLLATE", "EXCLUDE", "FOREIGN",
    };

    private static readonly HashSet<string> TableConstraintStarters = new(StringComparer.Ordinal)
    {
        "CONSTRAINT", "PRIMARY", "UNIQUE", "CHECK", "FOREIGN", "EXCLUDE", "LIKE",
    };

    /// <summary>
    /// CREATE TABLE name ( elements ) options: the parenthesis always breaks, one element to a line; with alignment on, column names,
    /// data types and column constraints line up in columns. Options after the parenthesis follow on its line.
    /// </summary>
    private PgDoc CreateTable(int lo, int tableIndex, int hi)
    {
        var open = FindTop(tableIndex + 1, hi, t => t.Kind == PgFt.Open);
        if (open >= hi) return Fallback(lo, hi);
        var close = T.MatchOf(open);
        if (close < 0 || IsQueryStart(open + 1)) return Fallback(lo, hi);

        // CREATE TABLE name OF type / PARTITION OF parent: not an element list in the parenthesis.
        for (var k = tableIndex + 1; k < open; k++)
            if (T[k].IsAny("OF", "AS")) return Fallback(lo, hi);

        var head = Flow(lo, open);
        var elements = SplitCommas(open + 1, close);

        // Split each element into name / type / constraints for alignment.
        var rows = new List<(PgDoc? Name, PgDoc? Type, PgDoc? Constraints, PgDoc Whole, int Comma, int Last)>();
        foreach (var (a, b, comma) in elements)
        {
            var last = b > a ? b - 1 : -1;
            var hoist = comma >= 0 && last >= 0 && T[last].TrailingLine is not null;
            if (b <= a || TableConstraintStarters.Contains(T[a].Upper) || !(T[a].IsWord || T[a].Kind == PgFt.Quoted))
            {
                MarkConstraintWords(a, b);
                rows.Add((null, null, null, TableConstraintDoc(a, b, hoist), comma, last));
                continue;
            }

            var typeLo = a + 1;
            var restLo = FindTop(typeLo, b, t => t.Kind == PgFt.Word && ConstraintStarters.Contains(t.Upper));
            MarkConstraintWords(restLo, b);
            var name = Emit(a);
            var type = typeLo < restLo ? ColumnType(typeLo, restLo) : PgDoc.Empty;
            var rest = restLo < b ? Flow(restLo, b, hoistLastTrailing: hoist) : PgDoc.Empty;
            // A column whose type ends in a trailing comment (no constraints) hoists it on the type.
            rows.Add((name, type, rest, PgDoc.Empty, comma, last));
        }

        var align = S.Ddl.AlignDataTypesAndConstraints;
        var nameWidth = align ? rows.Where(r => r.Name is not null).Select(r => PgDoc.FlatWidth(r.Name!)).DefaultIfEmpty(0).Max() : 0;
        var typeWidth = align ? rows.Where(r => r.Name is not null && r.Constraints is not null && r.Constraints != PgDoc.Empty).Select(r => PgDoc.FlatWidth(r.Type!)).DefaultIfEmpty(0).Max() : 0;
        var constraintsOnNewLines = S.Ddl.PlaceConstraintsOnNewLines;

        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var row in rows)
        {
            if (row.Name is null)
            {
                docs.Add(row.Whole);
            }
            else
            {
                var parts = new List<PgDoc> { row.Name };
                var hasType = row.Type != PgDoc.Empty;
                var hasRest = row.Constraints != PgDoc.Empty;
                if (hasType)
                {
                    parts.Add(PgDoc.Text(new string(' ', align ? Math.Max(1, nameWidth - PgDoc.FlatWidth(row.Name) + 1) : 1)));
                    parts.Add(row.Type!);
                }
                if (hasRest)
                {
                    if (constraintsOnNewLines)
                    {
                        parts.Add(PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), row.Constraints!)));
                    }
                    else
                    {
                        var pad = align && hasType ? Math.Max(1, typeWidth - PgDoc.FlatWidth(row.Type!) + 1) : 1;
                        parts.Add(PgDoc.Text(new string(' ', pad)));
                        parts.Add(row.Constraints!);
                    }
                }
                docs.Add(constraintsOnNewLines ? PgDoc.Block(PgDoc.Concat(parts)) : PgDoc.Concat(parts));
            }
            commas.Add(row.Comma);
            previousLast.Add(row.Last);
        }

        var list = ItemList(docs, commas, previousLast, new ListSpec
        {
            FirstOnNewLine = "never",
            Subsequent = "always",
            AlignToFirst = false,
            Indent = false,
        });
        var parens = Parens(open, close, DdlParens(), list, force: true);

        var parts2 = new List<PgDoc> { head, PgDoc.Text(S.Parentheses.SpacesAround ? " " : string.Empty), parens };
        if (close + 1 < hi)
        {
            parts2.Add(PgDoc.Text(" "));
            parts2.Add(Flow(close + 1, hi));
        }
        return PgDoc.Align(PgDoc.Concat(parts2));
    }

    /// <summary>A data type: its words cased by the data type option, modifiers and array brackets as they are.</summary>
    private PgDoc ColumnType(int lo, int hi)
    {
        var parts = new List<PgDoc>();
        var previous = -1;
        for (var i = lo; i < hi;)
        {
            if (T[i].IsWord && T[i].Kind == PgFt.Word && (IsKnownType(i) || TypePhraseStartsAt(i)))
            {
                var length = 1;
                foreach (var phrase in PgWords.MultiWordTypes)
                    if (MatchesPhrase(i, phrase)) { length = Math.Max(length, phrase.Length); }
                for (var k = 0; k < length && i + k < hi; k++)
                {
                    if (previous >= 0 || k > 0) parts.Add(PgDoc.Text(" "));
                    parts.Add(Ty(i + k));
                }
                previous = i + length - 1;
                i += length;
                continue;
            }

            var end = ElementEnd(i, hi);
            if (previous >= 0 && NeedSpace(previous, i, lo)) parts.Add(PgDoc.Text(" "));
            parts.Add(Element(i, end, lo, suppressTrailing: false));
            previous = end - 1;
            i = end;
        }
        return PgDoc.Concat(parts);
    }

    /// <summary>The constraint words at the top level of an element (not inside its parentheses, which hold names and expressions).</summary>
    private void MarkConstraintWords(int lo, int hi)
    {
        for (var i = lo; i < hi;)
        {
            var end = ElementEnd(i, hi);
            if (end == i + 1 && T[i].IsWord && PgWords.ConstraintWords.Contains(T[i].Upper)) _keywordTokens.Add(i);
            i = end;
        }
    }

    private PgDoc TableConstraintDoc(int lo, int hi, bool hoist) => hi <= lo ? PgDoc.Empty : Flow(lo, hi, hoist);

    // ── CREATE INDEX ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>CREATE [UNIQUE] INDEX name, then ON / INCLUDE / WITH / TABLESPACE / WHERE as lines of their own, indented when the DDL style asks.</summary>
    private PgDoc CreateIndex(int lo, int hi)
    {
        var starts = new List<int>();
        for (var i = lo + 1; i < hi;)
        {
            i = FindTop(i, hi, t => t.Kind == PgFt.Word && t.Upper is "ON" or "INCLUDE" or "WITH" or "TABLESPACE" or "WHERE" or "NULLS");
            if (i >= hi) break;
            // "ON" only counts as the clause once (the table); INCLUDE must be followed by a parenthesis, NULLS by NOT DISTINCT.
            if (T[i].Is("INCLUDE") && !(i + 1 < hi && T[i + 1].Kind == PgFt.Open)) { i++; continue; }
            if (T[i].Is("NULLS") && !(i + 1 < hi && T[i + 1].IsAny("NOT", "DISTINCT"))) { i++; continue; }
            if (T[i].Is("WITH") && !(i + 1 < hi && T[i + 1].Kind == PgFt.Open)) { i++; continue; }
            starts.Add(i);
            i++;
        }
        if (starts.Count == 0) return Fallback(lo, hi);

        foreach (var start in starts)
        {
            _keywordTokens.Add(start);
            if (T[start].Is("NULLS"))
                for (var k = start + 1; k < hi && T[k].IsAny("NOT", "DISTINCT"); k++) _keywordTokens.Add(k);
        }
        var parts = new List<PgDoc> { Flow(lo, starts[0]) };
        var indent = S.Ddl.IndentClauses ? Tab : 0;
        for (var n = 0; n < starts.Count; n++)
        {
            var end = n + 1 < starts.Count ? starts[n + 1] : hi;
            PgDoc clause = T[starts[n]].Is("WHERE")
                ? PgDoc.Concat(Kw(starts[n]), PgDoc.Text(" "), PgDoc.Align(Condition(starts[n] + 1, end, new ConditionContext(5, 6))))
                : Flow(starts[n], end);
            parts.Add(PgDoc.Nest(indent, PgDoc.Concat(PgDoc.Line(" "), clause)));
        }
        return PgDoc.Block(PgDoc.Concat(parts));
    }

    // ── CREATE VIEW ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>CREATE VIEW name [(columns)] AS query: the header, AS and the query each at the statement's column.</summary>
    private PgDoc CreateView(int lo, int viewIndex, int hi)
    {
        var asIndex = FindTop(viewIndex + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "AS");
        if (asIndex >= hi - 1 || !IsQueryStart(asIndex + 1)) return Fallback(lo, hi);

        var head = Flow(lo, asIndex);
        var query = Query(asIndex + 1, hi, QueryContext.Statement);
        return PgDoc.Block(PgDoc.Concat(head, PgDoc.Line(" "), Kw(asIndex), PgDoc.Line(" "), query));
    }
}
