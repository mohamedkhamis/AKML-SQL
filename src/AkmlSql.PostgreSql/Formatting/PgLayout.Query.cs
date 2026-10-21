namespace AkmlSql.PostgreSql.Formatting;

internal sealed partial class PgLayout
{
    private enum QueryContext
    {
        Statement,
        Subquery,
        Cte,
        InsertSource,
    }

    // ═══ the body of a statement ═════════════════════════════════════════════════════════════════════

    private PgDoc StatementBody(int lo, int hi)
    {
        var first = T[lo];
        if (first.IsAny("SELECT", "WITH", "VALUES", "TABLE") || (first.Kind == PgFt.Open && IsQueryStart(lo)))
            return Query(lo, hi, QueryContext.Statement);
        if (first.Is("INSERT")) return Insert(lo, hi);
        if (first.Is("UPDATE")) return Update(lo, hi);
        if (first.Is("DELETE")) return Delete(lo, hi);
        if (first.Is("CREATE")) return Create(lo, hi);
        if (first.Is("MERGE")) return Merge(lo, hi);
        if (first.IsAny("EXPLAIN", "PREPARE", "DECLARE")) return Wrapper(lo, hi);
        return Fallback(lo, hi);
    }

    // ═══ queries ═════════════════════════════════════════════════════════════════════════════════════

    private PgDoc Query(int lo, int hi, QueryContext context)
    {
        if (lo >= hi) return PgDoc.Empty;
        if (T[lo].Is("WITH")) return WithQuery(lo, hi, context);

        var operators = FindAllTop(lo, hi, t => t.Kind == PgFt.Word && PgWords.SetOperators.Contains(t.Upper));
        if (operators.Count > 0) return SetOperation(lo, hi, operators, context);
        return SelectBlock(lo, hi, context);
    }

    private int? CollapseFor(QueryContext context) =>
        context is QueryContext.Statement or QueryContext.InsertSource && S.Dml.CollapseShortStatements
            ? S.Dml.CollapseStatementsShorterThan - 1
            : null;

    /// <summary>Queries joined by UNION / INTERSECT / EXCEPT: each operand, then the operator on a line of its own, at the statement's column.</summary>
    private PgDoc SetOperation(int lo, int hi, List<int> operators, QueryContext context)
    {
        var parts = new List<PgDoc>();
        var start = lo;
        for (var i = 0; i <= operators.Count; i++)
        {
            var end = i < operators.Count ? operators[i] : hi;
            parts.Add(Query(start, end, QueryContext.Subquery == context ? QueryContext.Subquery : QueryContext.Cte));
            if (i < operators.Count)
            {
                var op = operators[i];
                var last = op;
                if (op + 1 < hi && T[op + 1].IsAny("ALL", "DISTINCT")) last = op + 1;
                parts.Add(PgDoc.Line(" "));
                parts.Add(Phrase(op, last));
                parts.Add(PgDoc.Line(" "));
                start = last + 1;
            }
        }
        return PgDoc.Block(PgDoc.Concat(parts), CollapseFor(context));
    }

    // ═══ WITH ════════════════════════════════════════════════════════════════════════════════════════

    private sealed record Cte(int NameLo, int NameHi, int ColsOpen, int ColsClose, int As, int BodyOpen, int BodyClose);

    /// <summary>The common table expressions after WITH, and where the main statement starts.</summary>
    private (List<Cte> Ctes, int MainStart, int FirstName, bool Ok) ParseCtes(int lo, int hi)
    {
        var ctes = new List<Cte>();
        var i = lo + 1;
        if (i < hi && T[i].Is("RECURSIVE")) i++;
        var firstName = i;

        while (i < hi)
        {
            var nameLo = i;
            if (!(T[i].IsWord || T[i].Kind == PgFt.Quoted)) return (ctes, i, firstName, false);
            i++;
            var colsOpen = -1;
            var colsClose = -1;
            if (i < hi && T[i].Kind == PgFt.Open)
            {
                colsOpen = i;
                colsClose = T.MatchOf(i);
                if (colsClose < 0) return (ctes, i, firstName, false);
                i = colsClose + 1;
            }
            if (i >= hi || !T[i].Is("AS")) return (ctes, i, firstName, false);
            var asIndex = i++;
            if (i < hi && T[i].Is("NOT")) i++;
            if (i < hi && T[i].Is("MATERIALIZED")) i++;
            if (i >= hi || T[i].Kind != PgFt.Open) return (ctes, i, firstName, false);
            var bodyOpen = i;
            var bodyClose = T.MatchOf(i);
            if (bodyClose < 0) return (ctes, i, firstName, false);
            ctes.Add(new Cte(nameLo, nameLo + 1, colsOpen, colsClose, asIndex, bodyOpen, bodyClose));
            i = bodyClose + 1;

            // SEARCH ... / CYCLE ... clauses belong to this CTE; skip to the comma or the main statement.
            while (i < hi && T[i].Kind != PgFt.Comma && !IsMainStatementStart(i)) i++;
            if (i < hi && T[i].Kind == PgFt.Comma) { i++; continue; }
            break;
        }
        return (ctes, i, firstName, ctes.Count > 0 && i < hi);
    }

    private bool IsMainStatementStart(int i) =>
        T[i].IsAny("SELECT", "INSERT", "UPDATE", "DELETE", "VALUES", "TABLE", "MERGE") || (T[i].Kind == PgFt.Open && IsQueryStart(i));

    /// <summary>
    /// WITH name (columns) AS (query), ...: the name on the WITH line or its own (indented or not), the column list on the name line
    /// or its own, AS on the column line or its own, the body in the CTE parenthesis style; then the main statement at the WITH's column.
    /// </summary>
    private PgDoc WithQuery(int lo, int hi, QueryContext context)
    {
        var (ctes, mainStart, firstName, ok) = ParseCtes(lo, hi);
        if (!ok) return Fallback(lo, hi);

        var cte = S.Cte;
        var withWidth = T[lo].Text.Length;
        var recursive = T[lo + 1].Is("RECURSIVE");
        var parts = new List<PgDoc> { Kw(lo) };
        if (recursive)
        {
            parts.Add(PgDoc.Text(" "));
            parts.Add(Kw(lo + 1));
        }

        var names = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        for (var n = 0; n < ctes.Count; n++)
        {
            var c = ctes[n];
            var item = new List<PgDoc> { Emit(c.NameLo) };

            if (c.ColsOpen >= 0)
            {
                var columns = ColumnListParens(c.ColsOpen, c.ColsClose);
                if (cte.PlaceColumnsOnNewLine)
                {
                    var offset = cte.ColumnAlignment switch { "indented" => Tab, "rightAligned" => withWidth + 1, _ => 0 };
                    item.Add(PgDoc.ScopedLine(PgAnchor.Column, offset, " "));
                }
                else
                {
                    item.Add(PgDoc.Text(S.Parentheses.SpacesAround ? " " : string.Empty));
                }
                item.Add(columns);
            }

            if (cte.PlaceAsOnNewLine)
            {
                var offset = cte.AsAlignment switch { "indented" => Tab, "rightAligned" => Math.Max(0, withWidth - 2), _ => 0 };
                item.Add(PgDoc.ScopedLine(PgAnchor.Column, offset, " "));
            }
            else
            {
                item.Add(PgDoc.Text(" "));
            }
            item.Add(Kw(c.As));
            for (var k = c.As + 1; k < c.BodyOpen; k++)
            {
                item.Add(PgDoc.Text(" "));
                item.Add(Kw(k));
            }
            item.Add(PgDoc.Text(" "));
            var body = Query(c.BodyOpen + 1, c.BodyClose, QueryContext.Cte);
            item.Add(Parens(c.BodyOpen, c.BodyClose, new ParenSpec(cte.ParenthesisStyle, cte.IndentContents, S.Parentheses.SpacesInside), body));

            names.Add(PgDoc.Concat(item));
            var comma = c.BodyClose + 1 < hi && T[c.BodyClose + 1].Kind == PgFt.Comma ? c.BodyClose + 1 : -1;
            commas.Add(n < ctes.Count - 1 ? comma : -1);
            previousLast.Add(c.BodyClose);
        }

        var nameIndent = cte.PlaceNameOnNewLine ? (cte.IndentName ? Tab : 0) : 0;
        var list = ItemList(names, commas, previousLast, new ListSpec
        {
            FirstOnNewLine = cte.PlaceNameOnNewLine ? "always" : "never",
            Subsequent = "always",
            AlignToFirst = true,
            Indent = cte.PlaceNameOnNewLine && cte.IndentName,
            ItemColumnFromStatement = withWidth + 1,
        }, pad: PgDoc.Text(" "));
        parts.Add(list);

        var main = MainStatement(mainStart, hi, context);
        parts.Add(PgDoc.ScopedLine(PgAnchor.Column, 0, " "));
        parts.Add(main);
        return PgDoc.Scope(PgDoc.Block(PgDoc.Concat(parts)));
    }

    private PgDoc MainStatement(int lo, int hi, QueryContext context)
    {
        var first = T[lo];
        if (first.Is("INSERT")) return Insert(lo, hi);
        if (first.Is("UPDATE")) return Update(lo, hi);
        if (first.Is("DELETE")) return Delete(lo, hi);
        if (first.Is("MERGE")) return Fallback(lo, hi);
        return Query(lo, hi, context == QueryContext.Statement ? QueryContext.Statement : QueryContext.Subquery);
    }

    /// <summary>"(a, b, c)" column lists: on one line when they fit, one column per line aligned otherwise.</summary>
    private PgDoc ColumnListParens(int open, int close)
    {
        var items = SplitCommas(open + 1, close);
        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var (a, b, comma) in items)
        {
            docs.Add(Flow(a, b, hoistLastTrailing: comma >= 0 && b > a && T[b - 1].TrailingLine is not null));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
        }
        var list = ItemList(docs, commas, previousLast, new ListSpec
        {
            FirstOnNewLine = "never",
            Subsequent = "ifLongerThanMaxLineLength",
            AlignToFirst = true,
            Indent = false,
        });
        return Parens(open, close, GlobalParens(), list);
    }

    // ═══ a SELECT block ══════════════════════════════════════════════════════════════════════════════

    /// <summary>One clause of a block: its keyword tokens and how to print what follows.</summary>
    private sealed record ClauseSpec(int KeywordFirst, int KeywordLast, Func<ClauseColumns, ClauseResult> Body);

    /// <summary>Where a clause sits, in columns from the statement's column.</summary>
    private readonly record struct ClauseColumns(int KeywordColumn, int KeywordWidth, int ItemColumn);

    /// <summary>A clause's body and the column its first item landed in (for aligning later clauses to it).</summary>
    private readonly record struct ClauseResult(PgDoc Doc, int FirstItemColumn);

    private int TabStop(int column) => S.Lists.AlignItemsToTabStops ? (column + Tab - 1) / Tab * Tab : column;

    private PgDoc SelectBlock(int lo, int hi, QueryContext context)
    {
        // A parenthesised query operand: (query) followed by ORDER BY / LIMIT ... that apply to it.
        if (T[lo].Kind == PgFt.Open)
        {
            var close = T.MatchOf(lo);
            if (close > lo && close < hi && IsQueryStart(lo + 1))
            {
                var inner = Subquery(lo, close, suppressTrailing: false);
                if (close + 1 >= hi) return inner;
                var tail = TailClauses(close + 1, hi);
                return PgDoc.Block(PgDoc.Concat(inner, PgDoc.Line(" "), tail), CollapseFor(context));
            }
        }

        if (T[lo].Is("VALUES")) return ValuesBlock(lo, hi, context);
        if (!T[lo].Is("SELECT")) return Fallback(lo, hi);

        var clauses = BlockClauses(lo, hi);
        return PgDoc.Block(Clauses(clauses), CollapseFor(context));
    }

    /// <summary>The clauses of a SELECT block: SELECT, INTO, FROM, WHERE, GROUP BY, HAVING, WINDOW, ORDER BY, LIMIT, OFFSET, FETCH, FOR.</summary>
    private List<ClauseSpec> BlockClauses(int lo, int hi)
    {
        var starts = new List<(int Index, int KeywordLast, string Kind)> { (lo, lo, "SELECT") };
        for (var i = lo + 1; i < hi;)
        {
            i = FindTop(i, hi, t => t.Kind == PgFt.Word && t.Upper is "INTO" or "FROM" or "WHERE" or "GROUP" or "HAVING" or "WINDOW" or "ORDER"
                or "LIMIT" or "OFFSET" or "FETCH" or "FOR");
            if (i >= hi) break;
            var word = T[i].Upper;
            if (word is "GROUP" or "ORDER")
            {
                if (i + 1 < hi && T[i + 1].Is("BY")) starts.Add((i, i + 1, word));
            }
            else if (word == "FOR")
            {
                if (i + 1 < hi && T[i + 1].IsAny("UPDATE", "NO", "SHARE", "KEY")) starts.Add((i, i, word));
            }
            else
            {
                starts.Add((i, i, word));
            }
            i++;
        }

        var clauses = new List<ClauseSpec>();
        for (var n = 0; n < starts.Count; n++)
        {
            var (index, last, kind) = starts[n];
            var bodyLo = last + 1;
            var bodyHi = n + 1 < starts.Count ? starts[n + 1].Index : hi;
            clauses.Add(kind switch
            {
                "SELECT" => new ClauseSpec(index, last, c => SelectBody(index, bodyLo, bodyHi, c)),
                "FROM" => new ClauseSpec(index, last, c => FromBody(bodyLo, bodyHi, c)),
                "WHERE" => new ClauseSpec(index, last, c => ConditionBody(bodyLo, bodyHi, c, S.Dml.PlaceWhereConditionOnNewLine)),
                "HAVING" => new ClauseSpec(index, last, c => ConditionBody(bodyLo, bodyHi, c, "never")),
                "GROUP" or "ORDER" => new ClauseSpec(index, last, c => DmlItems(bodyLo, bodyHi, c)),
                "WINDOW" => new ClauseSpec(index, last, c => DmlItems(bodyLo, bodyHi, c, firstOnNewLine: "never")),
                _ => new ClauseSpec(index, last, c => GenericBody(bodyLo, bodyHi, c)),
            });
        }
        return clauses;
    }

    /// <summary>ORDER BY / LIMIT / OFFSET / FETCH / FOR that follow a parenthesised query.</summary>
    private PgDoc TailClauses(int lo, int hi)
    {
        var clauses = new List<ClauseSpec>();
        var starts = new List<(int Index, int KeywordLast)>();
        for (var i = lo; i < hi;)
        {
            i = FindTop(i, hi, t => t.Kind == PgFt.Word && t.Upper is "ORDER" or "LIMIT" or "OFFSET" or "FETCH" or "FOR");
            if (i >= hi) break;
            starts.Add((i, T[i].Is("ORDER") && i + 1 < hi ? i + 1 : i));
            i++;
        }
        if (starts.Count == 0) return Flow(lo, hi);
        for (var n = 0; n < starts.Count; n++)
        {
            var (index, last) = starts[n];
            var bodyLo = last + 1;
            var bodyHi = n + 1 < starts.Count ? starts[n + 1].Index : hi;
            clauses.Add(T[index].Is("ORDER")
                ? new ClauseSpec(index, last, c => DmlItems(bodyLo, bodyHi, c))
                : new ClauseSpec(index, last, c => GenericBody(bodyLo, bodyHi, c)));
        }
        return Clauses(clauses);
    }

    /// <summary>
    /// Lays the clauses of a block out per the DML clause options: left-aligned, right-aligned (a "river"), or to the first list item;
    /// clause indentation; and "align items across clauses" (every first item starts in one column). Columns are from the statement's.
    /// </summary>
    private PgDoc Clauses(List<ClauseSpec> clauses, bool alignAcross = true)
    {
        if (clauses.Count == 0) return PgDoc.Empty;
        var widths = clauses.Select(c => PhraseWidth(c.KeywordFirst, c.KeywordLast)).ToArray();
        var maxWidth = widths.Max();
        var indent = S.Dml.ClauseIndentation;
        var alignment = S.Dml.ClauseAlignment;

        int KeywordColumn(int i, int? firstItemColumn) => alignment switch
        {
            "rightAligned" => maxWidth - widths[i] + (i > 0 ? indent : 0),
            "toFirstListItem" when i > 0 && firstItemColumn is int f => f + indent,
            _ => i > 0 ? indent : 0,
        };

        int? across = null;
        if (alignAcross && S.Lists.AlignItemsAcrossClauses && clauses.Count > 1 && alignment != "toFirstListItem")
            across = TabStop(Enumerable.Range(0, clauses.Count).Max(i => KeywordColumn(i, null) + widths[i] + 1));

        var parts = new List<PgDoc>();
        int? firstItemColumn = null;
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var keywordColumn = KeywordColumn(i, firstItemColumn);
            var itemColumn = across ?? TabStop(keywordColumn + widths[i] + 1);
            var result = clause.Body(new ClauseColumns(keywordColumn, widths[i], itemColumn));

            var doc = PgDoc.Align(PgDoc.Concat(Phrase(clause.KeywordFirst, clause.KeywordLast), result.Doc));
            if (i == 0)
            {
                // The first keyword sits at the statement's column; a right-aligned block pushes it right.
                parts.Add(keywordColumn > 0 ? PgDoc.Concat(PgDoc.Text(new string(' ', keywordColumn)), doc) : doc);
                firstItemColumn = result.FirstItemColumn;
            }
            else
            {
                parts.Add(PgDoc.Nest(keywordColumn, PgDoc.Concat(PgDoc.Line(" "), doc)));
            }
        }
        return PgDoc.Concat(parts);
    }

    // ═══ clause bodies ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>The space (or padding to the item column) between a clause keyword and its first item.</summary>
    private static PgDoc ItemPad(ClauseColumns c) =>
        PgDoc.IfBreak(PgDoc.Text(new string(' ', Math.Max(1, c.ItemColumn - c.KeywordColumn - c.KeywordWidth))), PgDoc.Text(" "));

    private ListSpec DmlList(ClauseColumns c, string firstOnNewLine) => new()
    {
        FirstOnNewLine = firstOnNewLine,
        Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
        AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
        Indent = S.Lists.IndentListItems,
        ItemColumnFromStatement = c.ItemColumn,
    };

    private bool FirstOnNewLine(string policy, int count) => policy switch
    {
        "always" => true,
        "ifSubsequentItems" or "ifMultiple" or "ifMultipleItems" or "ifSubsequentValues" => count > 1,
        _ => false,
    };

    private int FirstItemColumn(ClauseColumns c, bool firstNew) => firstNew ? c.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0) : c.ItemColumn;

    private (List<PgDoc> Docs, List<int> Commas, List<int> PreviousLast) ListItems(int lo, int hi)
    {
        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var (a, b, comma) in SplitCommas(lo, hi))
        {
            docs.Add(Flow(a, b, hoistLastTrailing: comma >= 0 && b > a && T[b - 1].TrailingLine is not null));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
        }
        return (docs, commas, previousLast);
    }

    private ClauseResult SelectBody(int keyword, int lo, int hi, ClauseColumns c)
    {
        // SELECT [ALL | DISTINCT [ON (...)]] items
        var headerEnd = lo;
        if (lo < hi && T[lo].Is("DISTINCT"))
        {
            headerEnd = lo + 1;
            if (headerEnd < hi && T[headerEnd].Is("ON") && headerEnd + 1 < hi && T[headerEnd + 1].Kind == PgFt.Open)
                headerEnd = T.MatchOf(headerEnd + 1) + 1;
        }
        else if (lo < hi && T[lo].Is("ALL"))
        {
            headerEnd = lo + 1;
        }

        var (docs, commas, previousLast) = ListItems(headerEnd, hi);
        var firstNew = FirstOnNewLine(S.Lists.PlaceFirstItemOnNewLine, docs.Count);
        var spec = DmlList(c, S.Lists.PlaceFirstItemOnNewLine);

        if (headerEnd == lo) return new ClauseResult(ItemList(docs, commas, previousLast, spec, ItemPad(c)), FirstItemColumn(c, firstNew));

        // DISTINCT sits on the keyword's line (or the next, when the style says so); the items keep the clause's item column.
        var header = Flow(lo, headerEnd);
        var dml = S.Dml;
        var newLineBefore = dml.PlaceDistinctAndTopOnNewLine;
        var newLineAfter = dml.NewLineAfterDistinctAndTop;
        var itemSpec = new ListSpec
        {
            FirstOnNewLine = newLineAfter ? "always" : "never",
            Subsequent = spec.Subsequent,
            AlignToFirst = false,
            Indent = false,
            ItemColumnFromStatement = c.ItemColumn,
        };
        var itemOffset = c.ItemColumn - c.KeywordColumn;
        var items = ItemList(docs, commas, previousLast, itemSpec, PgDoc.Text(" "));
        var body = PgDoc.Concat(
            newLineBefore ? PgDoc.Nest(itemOffset, PgDoc.Concat(PgDoc.Line(" "), header)) : PgDoc.Concat(ItemPad(c), header),
            PgDoc.Nest(newLineAfter ? itemOffset : itemOffset, items));
        return new ClauseResult(body, c.ItemColumn);
    }

    private ClauseResult DmlItems(int lo, int hi, ClauseColumns c, string? firstOnNewLine = null)
    {
        var (docs, commas, previousLast) = ListItems(lo, hi);
        var policy = firstOnNewLine ?? S.Dml.PlaceGroupByAndOrderByOnNewLine;
        var firstNew = FirstOnNewLine(policy, docs.Count);
        var doc = ItemList(docs, commas, previousLast, DmlList(c, policy), ItemPad(c));
        return new ClauseResult(doc, FirstItemColumn(c, firstNew));
    }

    private ClauseResult GenericBody(int lo, int hi, ClauseColumns c) =>
        new(PgDoc.Concat(ItemPad(c), PgDoc.Align(Flow(lo, hi))), c.ItemColumn);

    private ClauseResult ConditionBody(int lo, int hi, ClauseColumns c, string placement)
    {
        var multiple = ConditionOperators(lo, hi).Count > 0;
        var newLine = placement switch { "always" => true, "ifMultiple" => multiple, _ => false };
        var firstOffset = newLine ? (S.Lists.IndentListItems ? Tab : 0) : c.ItemColumn - c.KeywordColumn;
        var condition = PgDoc.Align(Condition(lo, hi, new ConditionContext(c.KeywordWidth, firstOffset)));
        var doc = newLine
            ? PgDoc.Nest(S.Lists.IndentListItems ? Tab : 0, PgDoc.Concat(PgDoc.Line(" "), condition))
            : PgDoc.Concat(ItemPad(c), condition);
        return new ClauseResult(doc, c.KeywordColumn + firstOffset);
    }

    // ═══ FROM and joins ══════════════════════════════════════════════════════════════════════════════

    private int CountTables(int lo, int hi)
    {
        var count = 0;
        foreach (var (a, b, _) in SplitCommas(lo, hi))
        {
            count++;
            count += FindAllTop(a, b, t => t.Kind == PgFt.Word && t.Upper == "JOIN").Count;
        }
        return count;
    }

    private ClauseResult FromBody(int lo, int hi, ClauseColumns c)
    {
        var tables = CountTables(lo, hi);
        var policy = S.Dml.PlaceFromTableOnNewLine;
        var firstNew = policy switch { "always" => true, "ifMultiple" => tables > 1, _ => false };
        var firstColumn = firstNew ? c.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0) : c.ItemColumn;
        var itemStart = S.Lists.AlignSubsequentItemsWithFirstItem ? firstColumn : c.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0);

        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        var n = 0;
        foreach (var (a, b, comma) in SplitCommas(lo, hi))
        {
            var start = n == 0 ? firstColumn : itemStart;
            docs.Add(FromItem(a, b, c, firstColumn, start, hoist: comma >= 0 && b > a && T[b - 1].TrailingLine is not null));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
            n++;
        }

        var spec = new ListSpec
        {
            FirstOnNewLine = firstNew ? "always" : "never",
            Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
            AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
            Indent = S.Lists.IndentListItems,
            ItemColumnFromStatement = c.ItemColumn,
        };
        return new ClauseResult(ItemList(docs, commas, previousLast, spec, ItemPad(c)), firstColumn);
    }

    private sealed record JoinPart(int PhraseFirst, int JoinIndex, int TableLo, int TableHi, int On, int ConditionLo, int ConditionHi);

    /// <summary>One item of the FROM list: a table (or subquery or function) followed by its joins.</summary>
    private PgDoc FromItem(int lo, int hi, ClauseColumns from, int firstTableColumn, int itemStart, bool hoist)
    {
        var joins = FindAllTop(lo, hi, t => t.Kind == PgFt.Word && t.Upper == "JOIN");
        if (joins.Count == 0) return TableReference(lo, hi, hoist);

        var parts = new List<JoinPart>();
        var phraseStarts = new List<int>();
        foreach (var join in joins)
        {
            var first = join;
            while (first - 1 >= lo && T[first - 1].IsAny("NATURAL", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS")) first--;
            phraseStarts.Add(first);
        }

        for (var j = 0; j < joins.Count; j++)
        {
            var end = j + 1 < joins.Count ? phraseStarts[j + 1] : hi;
            var tableLo = joins[j] + 1;
            var on = FindTop(tableLo, end, t => t.Kind == PgFt.Word && t.Upper is "ON" or "USING");
            var tableHi = on < end ? on : end;
            parts.Add(new JoinPart(phraseStarts[j], joins[j], tableLo, tableHi, on < end ? on : -1, on < end ? on + 1 : end, end));
        }

        var docs = new List<PgDoc> { TableReference(lo, phraseStarts[0], hoist: false) };
        var j2 = S.Join;
        foreach (var part in parts)
        {
            var phraseWidth = PhraseWidth(part.PhraseFirst, part.JoinIndex);
            var firstWordWidth = T[part.PhraseFirst].Text.Length;
            var joinColumn = j2.JoinAlignment switch
            {
                "rightAlignedToFrom" => Math.Max(0, from.KeywordColumn + from.KeywordWidth - phraseWidth),
                "toTable" => firstTableColumn,
                "indented" => from.KeywordColumn + Tab,
                _ => from.KeywordColumn,
            };
            var joinOffset = joinColumn - itemStart;

            var joinParts = new List<PgDoc>();
            joinParts.Add(j2.PlaceJoinOnNewLine
                ? PgDoc.Nest(joinOffset, PgDoc.Line(" "))
                : PgDoc.Text(" "));
            // Everything of this join is aligned at the JOIN keyword's column from here on.
            var inner = new List<PgDoc> { Phrase(part.PhraseFirst, part.JoinIndex) };
            var tableColumnFromJoin = j2.PlaceJoinTableOnNewLine ? (j2.IndentJoinTable ? Tab : 0) : phraseWidth + 1;
            inner.Add(j2.PlaceJoinTableOnNewLine
                ? PgDoc.Nest(j2.IndentJoinTable ? Tab : 0, PgDoc.Line(" "))
                : PgDoc.Text(" "));
            inner.Add(PgDoc.Align(TableReference(part.TableLo, part.TableHi, hoist: false)));

            if (part.On >= 0)
            {
                var onOffset = j2.OnAlignment switch
                {
                    "rightAlignedToJoin" => Math.Max(0, phraseWidth - 2),
                    "rightAlignedToInner" => Math.Max(0, firstWordWidth - 2),
                    "toTable" => tableColumnFromJoin,
                    "indented" => Tab,
                    _ => 0,
                };
                inner.Add(j2.PlaceOnOnNewLine ? PgDoc.Nest(onOffset, PgDoc.Line(" ")) : PgDoc.Text(" "));
                var onWidth = T[part.On].Text.Length;
                var onColumnFromJoin = j2.PlaceOnOnNewLine ? onOffset : phraseWidth + 1 + 0;
                var conditionOffset = j2.ConditionAlignment switch
                {
                    "toInner" => 0,
                    "toTable" => tableColumnFromJoin,
                    "indented" => onColumnFromJoin + Tab,
                    _ => onColumnFromJoin,
                };
                var firstOffset = j2.PlaceConditionOnNewLine ? conditionOffset - onColumnFromJoin : onWidth + 1;
                var condition = PgDoc.Align(Condition(part.ConditionLo, part.ConditionHi, new ConditionContext(onWidth, firstOffset)));
                inner.Add(PgDoc.Align(PgDoc.Concat(
                    Kw(part.On),
                    j2.PlaceConditionOnNewLine
                        ? PgDoc.Nest(conditionOffset - onColumnFromJoin, PgDoc.Concat(PgDoc.Line(" "), condition))
                        : PgDoc.Concat(PgDoc.Text(" "), condition))));
            }
            else if (part.ConditionHi > part.ConditionLo)
            {
                inner.Add(PgDoc.Text(" "));
                inner.Add(Flow(part.ConditionLo, part.ConditionHi));
            }

            joinParts.Add(PgDoc.Align(PgDoc.Concat(inner)));
            docs.Add(j2.EmptyLineBetweenJoins && j2.PlaceJoinOnNewLine
                ? PgDoc.Concat(PgDoc.HardLine(1), PgDoc.Concat(joinParts.Skip(1)))   // an empty line before the JOIN
                : PgDoc.Concat(joinParts));
        }
        return PgDoc.Align(PgDoc.Concat(docs));
    }

    /// <summary>A table reference: a name with an alias, a parenthesised subquery with its alias, a function call, LATERAL ...</summary>
    private PgDoc TableReference(int lo, int hi, bool hoist) => Flow(lo, hi, hoist);
}
