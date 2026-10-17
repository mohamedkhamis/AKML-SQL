using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.PostgreSql.Completion.Providers;

/// <summary>
/// Spec 042 (task T100, FR-061, scenario US3-2) - columns after an alias or a dot, and in the select list, <c>WHERE</c>,
/// <c>GROUP BY</c>, <c>HAVING</c>, <c>ORDER BY</c>, join conditions, <c>SET</c>, <c>RETURNING</c>, the column list of an INSERT
/// or an index. With one table in the query the names are bare; with several they carry the alias so they are not ambiguous.
/// </summary>
internal sealed class PgColumnProvider : IPgCompletionProvider
{
    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;

        if (c.AfterDot)
        {
            AfterDot(input, items);
            return;
        }
        if (c.AfterOperand) return;

        var last = c.PrecedingWords.Count > 0 ? c.PrecedingWords[^1] : string.Empty;

        switch (c.Clause)
        {
            case PgClause.InsertColumns:
            case PgClause.OnConflictTarget:
                AddAll(input, items, TargetRefs(c), qualify: false);
                break;

            case PgClause.DdlColumnList:
                DdlColumns(input, items);
                break;

            case PgClause.JoinUsing:
                CommonColumns(input, items);
                break;

            case PgClause.Set:
                if (last is "SET" or ",") AddAll(input, items, TargetRefs(c), qualify: false);
                else AddAll(input, items, ReadRefs(c, includeTarget: true), qualify: ReadRefs(c, includeTarget: true).Count > 1);
                break;

            case PgClause.Returning:
                {
                    var refs = ReadRefs(c, includeTarget: true);
                    AddAll(input, items, refs, qualify: refs.Count > 1);
                    break;
                }

            case PgClause.MergeWhen when last != "AND":
                break;

            case PgClause.MergeWhen:
            case PgClause.JoinOn:
            case PgClause.MergeAction:
                {
                    var refs = ReadRefs(c, includeTarget: false);
                    AddAll(input, items, refs, qualify: true);
                    break;
                }

            case PgClause.SelectList:
            case PgClause.Where:
            case PgClause.GroupBy:
            case PgClause.Having:
            case PgClause.OrderBy:
            case PgClause.Window:
                {
                    var refs = ReadRefs(c, includeTarget: false);
                    AddAll(input, items, refs, qualify: refs.Count > 1);
                    break;
                }

            case PgClause.Other:
                // ALTER TABLE t DROP COLUMN | / ALTER COLUMN | / RENAME COLUMN |
                if (c.Verb == "ALTER" && last == "COLUMN" && c.ListOwner != null)
                    AddAll(input, items, new[] { c.ListOwner }, qualify: false);
                break;
        }
    }

    // ── What the statement reads ────────────────────────────────────────────────────────────────

    private static List<PgTableRef> TargetRefs(PgCursorContext c)
    {
        var owner = c.ListOwner ?? c.Target;
        return owner == null ? new List<PgTableRef>() : new List<PgTableRef> { owner };
    }

    private static List<PgTableRef> ReadRefs(PgCursorContext c, bool includeTarget)
    {
        var refs = new List<PgTableRef>(c.VisibleTables);
        if (includeTarget && c.Target != null && !refs.Any(r => ReferenceEquals(r, c.Target) || (r.Qualifier == c.Target.Qualifier && r.Schema == c.Target.Schema)))
            refs.Insert(0, c.Target);
        return refs;
    }

    // ── After a dot ─────────────────────────────────────────────────────────────────────────────

    private static void AfterDot(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        PgTableRef? reference = null;

        if (c.DotChain.Count == 1)
        {
            var q = c.DotChain[0].Folded;
            if (c.ExcludedAvailable && q == "excluded") reference = c.Target;
            reference ??= c.VisibleTables.FirstOrDefault(t => t.Qualifier == q);
            reference ??= c.VisibleCtes.FirstOrDefault(t => t.Name == q);
            if (reference == null)
            {
                // a table named but not in FROM yet, or a table of the search path
                var found = input.Catalog.Resolve(null, q);
                if (found != null) reference = new PgTableRef(string.Empty, found.SchemaName, found.ObjectName, PgTableRefKind.Table, Array.Empty<string>(), Array.Empty<PgTableRef>());
            }
        }
        else if (c.DotChain.Count == 2)
        {
            reference = new PgTableRef(string.Empty, c.DotChain[0].Folded, c.DotChain[1].Folded, PgTableRefKind.Table, Array.Empty<string>(), Array.Empty<PgTableRef>());
            // a quoted schema or table keeps its case
            if (c.DotChain[0].Quoted || c.DotChain[1].Quoted)
                reference = new PgTableRef(string.Empty, c.DotChain[0].Text, c.DotChain[1].Text, PgTableRefKind.Table, Array.Empty<string>(), Array.Empty<PgTableRef>());
        }

        if (reference == null) return;
        AddAll(input, items, new[] { reference }, qualify: false);
    }

    // ── Column lists in DDL and USING ───────────────────────────────────────────────────────────

    private static void DdlColumns(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.ListOwner != null)
        {
            AddAll(input, items, new[] { c.ListOwner }, qualify: false);
            return;
        }

        // PRIMARY KEY ( / UNIQUE ( / FOREIGN KEY ( inside CREATE TABLE: the columns declared so far
        foreach (var name in c.DeclaredColumns)
            items.Add(PgItems.Make(name, PgCatalogView.Written(name, c.PrefixQuoted), CompletionObjectType.Column, "column", PgItems.Column, filter: name));
    }

    private static void CommonColumns(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.JoinTarget == null) return;
        var ctes = c.VisibleCtes;
        var right = input.Catalog.ColumnsOf(c.JoinTarget, ctes);
        var left = c.JoinLeft.SelectMany(t => input.Catalog.ColumnsOf(t, ctes)).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var col in right.Where(x => left.Contains(x.Name)))
            items.Add(ColumnItem(input, col, qualifier: null, source: c.JoinTarget.Name ?? string.Empty));
    }

    // ── The items ───────────────────────────────────────────────────────────────────────────────

    private static void AddAll(PgCompletionInput input, List<CompletionItem> items, IReadOnlyList<PgTableRef> refs, bool qualify)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inner = input.Context.InnerTableCount;
        var index = -1;
        foreach (var r in refs)
        {
            index++;
            // columns of an enclosing query are legal in a subquery, but the subquery's own come first
            var outer = refs.Count == input.Context.VisibleTables.Count && input.Context.VisibleTables.Count > inner && index >= inner;
            foreach (var col in input.Catalog.ColumnsOf(r, input.Context.VisibleCtes))
            {
                var qualifier = qualify ? r.Qualifier : null;
                if (!seen.Add((qualifier ?? string.Empty) + "\u0001" + col.Name)) continue;
                var item = ColumnItem(input, col, qualifier, r.Name ?? r.Qualifier);
                if (outer) item.SortPriority += 2;
                items.Add(item);
            }
        }
    }

    private static CompletionItem ColumnItem(PgCompletionInput input, PgColumnInfo col, string? qualifier, string source)
    {
        var c = input.Context;
        var written = PgCatalogView.Written(col.Name, c.PrefixQuoted);
        var display = qualifier == null ? col.Name : qualifier + "." + col.Name;
        var insert = qualifier == null ? written : PgCatalogView.Written(qualifier, c.PrefixQuoted) + "." + written;

        var parts = new List<string>();
        if (input.Options.ShowDataTypes && col.Type.Length > 0) parts.Add(col.Type);
        if (input.Options.ShowKeyIndicators && col.IsPrimaryKey) parts.Add("PK");
        if (input.Options.ShowNullability && col.Type.Length > 0 && !col.IsNullable) parts.Add("NOT NULL");
        var secondary = parts.Count > 0 ? string.Join(" · ", parts) : "column";

        return PgItems.Make(display, insert, CompletionObjectType.Column, secondary, PgItems.Column, source, filter: col.Name);
    }
}
