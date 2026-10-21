namespace AkmlSql.PostgreSql.Formatting;

internal sealed partial class PgLayout
{
    // ═══ VALUES ══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A standalone <c>VALUES (...), (...)</c> with the ORDER BY / LIMIT / OFFSET / FETCH that may follow it.</summary>
    private PgDoc ValuesBlock(int lo, int hi, QueryContext context)
    {
        var tailStart = FindTop(lo + 1, hi, t => t.Kind == PgFt.Word && t.Upper is "ORDER" or "LIMIT" or "OFFSET" or "FETCH");
        var clauses = new List<ClauseSpec> { new(lo, lo, c => ValueRows(lo + 1, tailStart, c)) };
        if (tailStart < hi) AppendTailClauses(clauses, tailStart, hi);
        return PgDoc.Block(Clauses(clauses), CollapseFor(context));
    }

    private void AppendTailClauses(List<ClauseSpec> clauses, int lo, int hi)
    {
        var starts = new List<(int Index, int Last)>();
        for (var i = lo; i < hi;)
        {
            i = FindTop(i, hi, t => t.Kind == PgFt.Word && t.Upper is "ORDER" or "LIMIT" or "OFFSET" or "FETCH");
            if (i >= hi) break;
            starts.Add((i, T[i].Is("ORDER") && i + 1 < hi ? i + 1 : i));
            i++;
        }
        for (var n = 0; n < starts.Count; n++)
        {
            var (index, last) = starts[n];
            var bodyLo = last + 1;
            var bodyHi = n + 1 < starts.Count ? starts[n + 1].Index : hi;
            clauses.Add(T[index].Is("ORDER")
                ? new ClauseSpec(index, last, c => DmlItems(bodyLo, bodyHi, c))
                : new ClauseSpec(index, last, c => GenericBody(bodyLo, bodyHi, c)));
        }
    }

    /// <summary>The rows after VALUES: each in the INSERT values parenthesis style, one to a line (aligned) per the list options.</summary>
    private ClauseResult ValueRows(int lo, int hi, ClauseColumns c)
    {
        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var (a, b, comma) in SplitCommas(lo, hi))
        {
            docs.Add(ValueRow(a, b));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
        }

        var spec = new ListSpec
        {
            FirstOnNewLine = "never",
            Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
            AlignToFirst = true,
            Indent = S.Lists.IndentListItems,
            ItemColumnFromStatement = c.ItemColumn,
        };
        var space = PgDoc.Text(S.Parentheses.SpacesAround ? " " : string.Empty);
        return new ClauseResult(ItemList(docs, commas, previousLast, spec, space), c.ItemColumn);
    }

    private PgDoc ValueRow(int lo, int hi)
    {
        if (hi - lo < 2 || T[lo].Kind != PgFt.Open || T.MatchOf(lo) != hi - 1) return Flow(lo, hi);
        var ins = S.Insert;
        var items = SplitCommas(lo + 1, hi - 1);
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
            Subsequent = ins.PlaceSubsequentValuesOnNewLines,
            AlignToFirst = false,
            Indent = false,
        });
        return Parens(lo, hi - 1, new ParenSpec(ins.ValuesParenthesisStyle, ins.ValuesIndentContents, S.Parentheses.SpacesInside), list);
    }

    // ═══ INSERT ══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INSERT INTO table [(columns)] source [ON CONFLICT ...] [RETURNING ...]: the table on the INSERT line (or the next), the column list
    /// in the INSERT columns parenthesis style, the source (VALUES rows or a query) on a line of its own, then ON CONFLICT and RETURNING as clauses.
    /// </summary>
    private PgDoc Insert(int lo, int hi)
    {
        if (lo + 2 >= hi || !T[lo + 1].Is("INTO")) return Fallback(lo, hi);

        var i = lo + 2;
        var colsOpen = -1;
        var colsClose = -1;
        while (i < hi)
        {
            var t = T[i];
            if (t.Kind == PgFt.Open)
            {
                if (!IsQueryStart(i + 1))
                {
                    colsOpen = i;
                    colsClose = T.MatchOf(i);
                }
                break;
            }
            if (t.IsAny("VALUES", "DEFAULT", "SELECT", "WITH", "TABLE", "OVERRIDING", "ON", "RETURNING")) break;
            i++;
        }
        var targetHi = i;
        if (colsOpen >= 0)
        {
            if (colsClose < 0) return Fallback(lo, hi);
            i = colsClose + 1;
        }

        var overridingLo = i;
        if (i < hi && T[i].Is("OVERRIDING"))
        {
            while (i < hi && !T[i].Is("VALUE")) i++;
            if (i < hi) i++;
        }
        var overridingHi = i;

        var tailStart = hi;
        for (var k = i; k < hi;)
        {
            k = FindTop(k, hi, t => t.Kind == PgFt.Word && (t.Upper == "RETURNING" || t.Upper == "ON"));
            if (k >= hi) break;
            if (T[k].Is("RETURNING") || (k + 1 < hi && T[k + 1].Is("CONFLICT"))) { tailStart = k; break; }
            k++;
        }
        var sourceLo = overridingHi;
        var sourceHi = tailStart;
        if (sourceLo >= sourceHi) return Fallback(lo, hi);

        var parts = new List<PgDoc>();
        parts.Add(Phrase(lo, lo + 1));
        var table = Flow(lo + 2, targetHi);
        parts.Add(S.Dml.PlaceInsertTableOnNewLine ? PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), table)) : PgDoc.Concat(PgDoc.Text(" "), table));

        if (colsOpen >= 0)
        {
            var ins = S.Insert;
            var items = SplitCommas(colsOpen + 1, colsClose);
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
                Subsequent = ins.PlaceSubsequentColumnsOnNewLines,
                AlignToFirst = false,
                Indent = false,
            });
            parts.Add(PgDoc.Text(S.Parentheses.SpacesAround ? " " : string.Empty));
            parts.Add(Parens(colsOpen, colsClose, new ParenSpec(ins.ColumnsParenthesisStyle, ins.ColumnsIndentContents, S.Parentheses.SpacesInside), list));
        }

        if (overridingHi > overridingLo)
        {
            parts.Add(PgDoc.Text(" "));
            parts.Add(Flow(overridingLo, overridingHi));
        }

        parts.Add(PgDoc.Line(" "));
        if (T[sourceLo].Is("DEFAULT")) parts.Add(Flow(sourceLo, sourceHi));
        else if (T[sourceLo].Is("VALUES")) parts.Add(PgDoc.Align(Concat2(Kw(sourceLo), ValueRows(sourceLo + 1, sourceHi, new ClauseColumns(0, T[sourceLo].Text.Length, T[sourceLo].Text.Length + 1)).Doc)));
        else parts.Add(Query(sourceLo, sourceHi, QueryContext.InsertSource));

        if (tailStart < hi)
        {
            parts.Add(PgDoc.Line(" "));
            parts.Add(InsertTail(tailStart, hi));
        }
        return PgDoc.Block(PgDoc.Concat(parts), S.Dml.CollapseShortStatements ? S.Dml.CollapseStatementsShorterThan - 1 : null);
    }

    private static PgDoc Concat2(PgDoc a, PgDoc b) => PgDoc.Concat(a, b);

    /// <summary>ON CONFLICT [target] DO NOTHING | DO UPDATE SET ... [WHERE ...], then RETURNING ..., as clauses of their own.</summary>
    private PgDoc InsertTail(int lo, int hi)
    {
        var clauses = new List<ClauseSpec>();
        var returning = FindTop(lo, hi, t => t.Kind == PgFt.Word && t.Upper == "RETURNING");
        var conflictHi = returning;

        if (T[lo].Is("ON") && lo + 1 < hi && T[lo + 1].Is("CONFLICT"))
        {
            var doIndex = FindTop(lo + 2, conflictHi, t => t.Kind == PgFt.Word && t.Upper == "DO");
            clauses.Add(new ClauseSpec(lo, lo + 1, c => GenericBody(lo + 2, doIndex, c)));
            if (doIndex < conflictHi)
            {
                if (doIndex + 1 < conflictHi && T[doIndex + 1].Is("NOTHING"))
                {
                    clauses.Add(new ClauseSpec(doIndex, doIndex + 1, _ => new ClauseResult(PgDoc.Empty, 0)));
                }
                else
                {
                    var set = FindTop(doIndex + 1, conflictHi, t => t.Kind == PgFt.Word && t.Upper == "SET");
                    var where = FindTop(set + 1, conflictHi, t => t.Kind == PgFt.Word && t.Upper == "WHERE");
                    if (set >= conflictHi)
                    {
                        clauses.Add(new ClauseSpec(doIndex, doIndex, c => GenericBody(doIndex + 1, conflictHi, c)));
                    }
                    else
                    {
                        clauses.Add(new ClauseSpec(doIndex, set, c => DmlItems(set + 1, where, c, firstOnNewLine: "never")));
                        if (where < conflictHi)
                            clauses.Add(new ClauseSpec(where, where, c => ConditionBody(where + 1, conflictHi, c, S.Dml.PlaceWhereConditionOnNewLine)));
                    }
                }
            }
        }
        else if (returning > lo)
        {
            clauses.Add(new ClauseSpec(lo, lo, c => GenericBody(lo + 1, returning, c)));
        }

        if (returning < hi) clauses.Add(new ClauseSpec(returning, returning, c => ReturningBody(returning + 1, hi, c)));
        return Clauses(clauses, alignAcross: false);
    }

    private ClauseResult ReturningBody(int lo, int hi, ClauseColumns c)
    {
        var (docs, commas, previousLast) = ListItems(lo, hi);
        var firstNew = FirstOnNewLine(S.Lists.PlaceFirstItemOnNewLine, docs.Count);
        return new ClauseResult(ItemList(docs, commas, previousLast, DmlList(c, S.Lists.PlaceFirstItemOnNewLine), ItemPad(c)), FirstItemColumn(c, firstNew));
    }

    // ═══ UPDATE ══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>UPDATE target / SET assignments / FROM ... / WHERE ... / RETURNING ... as clauses, aligned per the DML clause options.</summary>
    private PgDoc Update(int lo, int hi)
    {
        var set = FindTop(lo + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "SET");
        if (set >= hi) return Fallback(lo, hi);
        var from = FindTop(set + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "FROM");
        var where = FindTop(set + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "WHERE");
        var returning = FindTop(set + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "RETURNING");

        var ends = new[] { from, where, returning, hi }.Where(x => x > set).ToList();
        var setHi = ends.Min();

        var clauses = new List<ClauseSpec>
        {
            new(lo, lo, c => new ClauseResult(PgDoc.Concat(ItemPad(c), PgDoc.Align(Flow(lo + 1, set))), c.ItemColumn)),
            new(set, set, c => DmlItems(set + 1, setHi, c, firstOnNewLine: S.Lists.PlaceFirstItemOnNewLine)),
        };
        AddTrailingDmlClauses(clauses, from, where, returning, hi);
        return PgDoc.Block(Clauses(clauses), S.Dml.CollapseShortStatements ? S.Dml.CollapseStatementsShorterThan - 1 : null);
    }

    /// <summary>FROM / USING, WHERE and RETURNING, in whatever order they stand (they stand in this one).</summary>
    private void AddTrailingDmlClauses(List<ClauseSpec> clauses, int from, int where, int returning, int hi)
    {
        var starts = new List<(int Index, string Kind)>();
        if (from < hi) starts.Add((from, "FROM"));
        if (where < hi && where != from) starts.Add((where, "WHERE"));
        if (returning < hi) starts.Add((returning, "RETURNING"));
        starts.Sort((a, b) => a.Index.CompareTo(b.Index));

        for (var n = 0; n < starts.Count; n++)
        {
            var (index, kind) = starts[n];
            var bodyLo = index + 1;
            var bodyHi = n + 1 < starts.Count ? starts[n + 1].Index : hi;
            clauses.Add(kind switch
            {
                "FROM" => new ClauseSpec(index, index, c => FromBody(bodyLo, bodyHi, c)),
                "WHERE" => new ClauseSpec(index, index, c => ConditionBody(bodyLo, bodyHi, c, S.Dml.PlaceWhereConditionOnNewLine)),
                _ => new ClauseSpec(index, index, c => ReturningBody(bodyLo, bodyHi, c)),
            });
        }
    }

    // ═══ DELETE ══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>DELETE FROM target / USING ... / WHERE ... / RETURNING ... as clauses.</summary>
    private PgDoc Delete(int lo, int hi)
    {
        if (lo + 2 >= hi || !T[lo + 1].Is("FROM")) return Fallback(lo, hi);
        var using_ = FindTop(lo + 2, hi, t => t.Kind == PgFt.Word && t.Upper == "USING");
        var where = FindTop(lo + 2, hi, t => t.Kind == PgFt.Word && t.Upper == "WHERE");
        var returning = FindTop(lo + 2, hi, t => t.Kind == PgFt.Word && t.Upper == "RETURNING");
        var targetHi = new[] { using_, where, returning, hi }.Min();

        var clauses = new List<ClauseSpec>
        {
            new(lo, lo + 1, c => new ClauseResult(PgDoc.Concat(ItemPad(c), PgDoc.Align(Flow(lo + 2, targetHi))), c.ItemColumn)),
        };

        var starts = new List<(int Index, string Kind)>();
        if (using_ < hi) starts.Add((using_, "USING"));
        if (where < hi) starts.Add((where, "WHERE"));
        if (returning < hi) starts.Add((returning, "RETURNING"));
        starts.Sort((a, b) => a.Index.CompareTo(b.Index));
        for (var n = 0; n < starts.Count; n++)
        {
            var (index, kind) = starts[n];
            var bodyLo = index + 1;
            var bodyHi = n + 1 < starts.Count ? starts[n + 1].Index : hi;
            clauses.Add(kind switch
            {
                "USING" => new ClauseSpec(index, index, c => FromBody(bodyLo, bodyHi, c)),
                "WHERE" => new ClauseSpec(index, index, c => ConditionBody(bodyLo, bodyHi, c, S.Dml.PlaceWhereConditionOnNewLine)),
                _ => new ClauseSpec(index, index, c => ReturningBody(bodyLo, bodyHi, c)),
            });
        }
        return PgDoc.Block(Clauses(clauses), S.Dml.CollapseShortStatements ? S.Dml.CollapseStatementsShorterThan - 1 : null);
    }
}
