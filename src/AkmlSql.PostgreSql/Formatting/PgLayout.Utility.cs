namespace AkmlSql.PostgreSql.Formatting;

internal sealed partial class PgLayout
{
    // ═══ statements that wrap another statement ═════════════════════════════════════════════════════

    private static readonly string[] InnerStarters = { "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "VALUES", "MERGE" };

    /// <summary>
    /// EXPLAIN [options] statement, PREPARE name [(types)] AS statement, DECLARE name CURSOR ... FOR statement: the head as written, with its
    /// option words as keywords, then the statement laid out like any other, on a line of its own.
    /// </summary>
    private PgDoc Wrapper(int lo, int hi)
    {
        var first = T[lo];
        var inner = -1;
        if (first.Is("EXPLAIN")) inner = FindTop(lo + 1, hi, t => t.Kind == PgFt.Word && Array.IndexOf(InnerStarters, t.Upper) >= 0);
        else if (first.Is("PREPARE")) inner = AfterTopLevel(lo + 1, hi, "AS");
        else if (first.Is("DECLARE")) inner = AfterTopLevel(lo + 1, hi, "FOR");
        if (inner < 0 || inner >= hi || !(T[inner].IsAny(InnerStarters) || IsQueryStart(inner))) return Fallback(lo, hi);

        if (first.Is("EXPLAIN")) MarkExplainOptions(lo + 1, inner);
        var head = Fallback(lo, inner);
        var body = StatementBody(inner, hi);
        return PgDoc.Block(PgDoc.Concat(head, PgDoc.Line(" "), body));
    }

    /// <summary>The token after the first top-level <paramref name="word"/>, or -1.</summary>
    private int AfterTopLevel(int from, int hi, string word)
    {
        var at = FindTop(from, hi, t => t.Kind == PgFt.Word && t.Upper == word);
        return at < hi ? at + 1 : -1;
    }

    private void MarkExplainOptions(int lo, int hi)
    {
        for (var i = lo; i < hi; i++)
        {
            if (!T[i].IsWord) continue;
            var inParens = T[lo].Kind == PgFt.Open;
            if (!inParens || PgWords.ExplainOptions.Contains(T[i].Upper)
                || (i > lo && T[i - 1].Is("FORMAT") && T[i].IsAny("TEXT", "XML", "JSON", "YAML")))
                _keywordTokens.Add(i);
        }
    }

    // ═══ MERGE ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// MERGE INTO target USING source ON condition, then each WHEN [NOT] MATCHED [AND condition] THEN action on a line of its own with the
    /// action one level in: UPDATE SET (a list), INSERT (columns) VALUES (...), DELETE, DO NOTHING.
    /// </summary>
    private PgDoc Merge(int lo, int hi)
    {
        if (lo + 1 >= hi || !T[lo + 1].Is("INTO")) return Fallback(lo, hi);
        var usingIndex = FindTop(lo + 2, hi, t => t.Kind == PgFt.Word && t.Upper == "USING");
        var onIndex = FindTop(usingIndex + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "ON");
        var whens = FindAllTop(onIndex + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "WHEN");
        if (usingIndex >= hi || onIndex >= hi || whens.Count == 0) return Fallback(lo, hi);

        foreach (var w in new[] { lo, lo + 1, usingIndex, onIndex }) _keywordTokens.Add(w);
        var parts = new List<PgDoc>
        {
            Phrase(lo, lo + 1), PgDoc.Text(" "), Flow(lo + 2, usingIndex),
            PgDoc.Line(" "), Kw(usingIndex), PgDoc.Text(" "), PgDoc.Align(Flow(usingIndex + 1, onIndex)),
            PgDoc.Line(" "), Kw(onIndex), PgDoc.Text(" "), PgDoc.Align(Condition(onIndex + 1, whens[0], new ConditionContext(2, 3))),
        };

        for (var n = 0; n < whens.Count; n++)
        {
            var when = whens[n];
            var end = n + 1 < whens.Count ? whens[n + 1] : hi;
            var then = FindTop(when + 1, end, t => t.Kind == PgFt.Word && t.Upper == "THEN");
            if (then >= end) return Fallback(lo, hi);

            for (var k = when; k <= then; k++)
                if (T[k].IsWord && T[k].IsAny("WHEN", "NOT", "MATCHED", "BY", "SOURCE", "TARGET", "THEN", "AND")) _keywordTokens.Add(k);

            // WHEN [NOT] MATCHED [BY SOURCE | BY TARGET] [AND condition] THEN
            var and = FindTop(when + 1, then, t => t.Kind == PgFt.Word && t.Upper == "AND");
            PgDoc head = and < then
                ? PgDoc.Concat(Flow(when, and), PgDoc.Text(" "), Kw(and), PgDoc.Text(" "), PgDoc.Align(Condition(and + 1, then, new ConditionContext(3, 4))))
                : Flow(when, then);
            parts.Add(PgDoc.Line(" "));
            parts.Add(PgDoc.Concat(head, PgDoc.Text(" "), Kw(then)));
            parts.Add(PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), MergeAction(then + 1, end))));
        }
        return PgDoc.Block(PgDoc.Concat(parts));
    }

    private PgDoc MergeAction(int lo, int hi)
    {
        if (lo >= hi) return PgDoc.Empty;
        var first = T[lo];
        if (first.Is("UPDATE") && lo + 1 < hi && T[lo + 1].Is("SET"))
        {
            _keywordTokens.Add(lo);
            _keywordTokens.Add(lo + 1);
            var clauses = new List<ClauseSpec> { new(lo, lo + 1, c => DmlItems(lo + 2, hi, c, firstOnNewLine: "never")) };
            return Clauses(clauses, alignAcross: false);
        }
        if (first.Is("INSERT"))
        {
            _keywordTokens.Add(lo);
            var values = FindTop(lo + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "VALUES");
            if (values < hi) _keywordTokens.Add(values);
            return Flow(lo, hi);
        }
        foreach (var k in new[] { lo, lo + 1 })
            if (k < hi && T[k].IsAny("DELETE", "DO", "NOTHING")) _keywordTokens.Add(k);
        return Flow(lo, hi);
    }
}
