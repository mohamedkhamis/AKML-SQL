using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.PostgreSql.Completion.Providers;

/// <summary>
/// Spec 042 (task T100, FR-061) - join conditions from foreign keys: for the table just joined and each table before it, the
/// <c>a.col = b.col</c> predicate of every foreign key between them, and, where there is none, a predicate on columns of the same
/// name. Shown inside <c>ON</c> as the condition, and right after the joined table as <c>ON condition</c>. This ports the logic of
/// the SQL Server <c>FkHelpers</c> without brackets or dbo.
/// </summary>
internal sealed class PgJoinOnFkProvider : IPgCompletionProvider
{
    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (!input.Options.JoinAssist || c.AfterDot) return;
        if (c.JoinTarget == null || c.JoinLeft.Count == 0) return;

        var inOn = c.Clause == PgClause.JoinOn && !c.AfterOperand;
        var afterJoinedTable = c.Clause == PgClause.Join && c.AfterOperand;
        if (!inOn && !afterJoinedTable) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var right = c.JoinTarget;
        foreach (var left in c.JoinLeft)
        {
            foreach (var predicate in Predicates(input, left, right, c))
            {
                var text = afterJoinedTable ? "ON " + predicate.Text : predicate.Text;
                if (!seen.Add(text)) continue;
                items.Add(PgItems.Make(text, text, CompletionObjectType.Keyword, predicate.Source, predicate.Priority, source: predicate.Name, filter: text));
            }
        }
    }

    private sealed record Predicate(string Text, string Source, string Name, int Priority);

    private static IEnumerable<Predicate> Predicates(PgCompletionInput input, PgTableRef left, PgTableRef right, PgCursorContext c)
    {
        var catalog = input.Catalog;
        var leftObject = left.Name == null || left.Kind != PgTableRefKind.Table ? null : catalog.Resolve(left.Schema, left.Name);
        var rightObject = right.Name == null || right.Kind != PgTableRefKind.Table ? null : catalog.Resolve(right.Schema, right.Name);

        var viaForeignKey = false;
        if (leftObject != null && rightObject != null)
        {
            foreach (var fk in catalog.ForeignKeysOf(leftObject))
            {
                var leftIsParent = Same(fk.ParentSchema, fk.ParentTable, leftObject) && Same(fk.ReferencedSchema, fk.ReferencedTable, rightObject);
                var leftIsReferenced = Same(fk.ReferencedSchema, fk.ReferencedTable, leftObject) && Same(fk.ParentSchema, fk.ParentTable, rightObject);
                if (!leftIsParent && !leftIsReferenced) continue;

                viaForeignKey = true;
                if (leftIsParent)
                    yield return new Predicate(Build(left.Qualifier, fk.ParentColumns, right.Qualifier, fk.ReferencedColumns, c.PrefixQuoted), "FK · " + fk.FkName, fk.FkName, PgItems.JoinCondition);
                // a table that references itself (employees.reports_to) can be joined either way round
                if (leftIsReferenced)
                    yield return new Predicate(Build(left.Qualifier, fk.ReferencedColumns, right.Qualifier, fk.ParentColumns, c.PrefixQuoted), "FK · " + fk.FkName, fk.FkName, PgItems.JoinCondition);
            }
        }
        if (viaForeignKey) yield break;

        // No foreign key: columns that carry the same name (an id column first)
        var leftNames = catalog.ColumnsOf(left, c.VisibleCtes).Select(x => x.Name).ToList();
        var rightNames = catalog.ColumnsOf(right, c.VisibleCtes).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in leftNames.Where(rightNames.Contains))
        {
            var priority = name == "id" ? 7 : name.EndsWith("_id", StringComparison.Ordinal) ? 8 : 10;
            yield return new Predicate(Build(left.Qualifier, new List<string> { name }, right.Qualifier, new List<string> { name }, c.PrefixQuoted), "Name match", left.Qualifier + "↔" + right.Qualifier, priority);
        }
    }

    private static bool Same(string schema, string table, DatabaseObject o) =>
        string.Equals(schema, o.SchemaName, StringComparison.OrdinalIgnoreCase) && string.Equals(table, o.ObjectName, StringComparison.OrdinalIgnoreCase);

    private static string Build(string leftQualifier, IReadOnlyList<string> leftColumns, string rightQualifier, IReadOnlyList<string> rightColumns, bool quote)
    {
        var count = Math.Min(leftColumns.Count, rightColumns.Count);
        var parts = new List<string>(count);
        for (var i = 0; i < count; i++)
            parts.Add($"{PgCatalogView.Written(leftQualifier, quote)}.{PgCatalogView.Written(leftColumns[i], quote)} = {PgCatalogView.Written(rightQualifier, quote)}.{PgCatalogView.Written(rightColumns[i], quote)}");
        return string.Join(" AND ", parts);
    }
}
