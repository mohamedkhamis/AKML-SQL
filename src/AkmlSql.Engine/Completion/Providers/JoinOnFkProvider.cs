using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Emits ready-made FK equality predicates for the <c>ON</c> clause of a JOIN.
/// Given <c>SELECT * FROM Customers c JOIN Orders o ON │</c>, iterates the foreign
/// keys between the join target (<c>Orders</c>) and every other table already in
/// scope, yielding one atomic <c>left.fk = right.pk</c> suggestion per FK direction.
/// <para>
/// Gated by the <c>JoinAssistEnabled</c> toggle in <see cref="CompletionEngine"/>.
/// </para>
/// </summary>
public class JoinOnFkProvider : ICompletionProvider
{
    public string Name => "JoinOnFk";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null)
            return false;

        // Activate only inside the ON clause of a JOIN — not in plain WHERE/HAVING
        // equality predicates, where the suggestions would be noise.
        if (context.ClauseType != ClauseType.JoinOn)
            return false;

        // Need at least two tables in scope (the JOIN target + at least one prior table)
        // for there to be a meaningful FK pair.
        if (context.AvailableAliases.Count < 2)
            return false;

        // Don't fire for dot-qualified identifiers (e.g. "a.") — ColumnProvider owns that.
        if (context.PrecedingDot)
            return false;

        return true;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null)
            yield break;

        // Iterate every (target, other) alias pair and yield FK equalities in both
        // directions. The user can filter by typing — we don't try to detect which
        // alias is "the join target" because the parser may not know the cursor's
        // position within the current JOIN clause without deeper AST introspection.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (targetAlias, targetFullName) in context.AvailableAliases)
        {
            var (targetSchema, targetTable) = SplitName(targetFullName);
            var fks = cache.GetForeignKeysForTable(targetSchema, targetTable);

            foreach (var fk in fks)
            {
                foreach (var (otherAlias, otherFullName) in context.AvailableAliases)
                {
                    // Skip self-references — a table joining to itself can use this
                    // provider too, but the user usually writes those by hand.
                    if (otherAlias.Equals(targetAlias, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var (otherSchema, otherTable) = SplitName(otherFullName);

                    // Does this FK describe a relationship between the two tables?
                    bool targetIsParent = fk.ParentSchema.Equals(targetSchema, StringComparison.OrdinalIgnoreCase)
                                       && fk.ParentTable.Equals(targetTable, StringComparison.OrdinalIgnoreCase)
                                       && fk.ReferencedSchema.Equals(otherSchema, StringComparison.OrdinalIgnoreCase)
                                       && fk.ReferencedTable.Equals(otherTable, StringComparison.OrdinalIgnoreCase);

                    bool targetIsChild = fk.ReferencedSchema.Equals(targetSchema, StringComparison.OrdinalIgnoreCase)
                                      && fk.ReferencedTable.Equals(targetTable, StringComparison.OrdinalIgnoreCase)
                                      && fk.ParentSchema.Equals(otherSchema, StringComparison.OrdinalIgnoreCase)
                                      && fk.ParentTable.Equals(otherTable, StringComparison.OrdinalIgnoreCase);

                    if (!targetIsParent && !targetIsChild)
                        continue;

                    // Emit with target on the left, prior table on the right — matches
                    // SQL Prompt's convention where the "new" join target is leftmost.
                    var targetColumns = targetIsParent ? fk.ParentColumns : fk.ReferencedColumns;
                    var otherColumns = targetIsParent ? fk.ReferencedColumns : fk.ParentColumns;

                    var predicate = BuildPredicate(targetAlias, targetColumns, otherAlias, otherColumns);

                    // Dedup — the same FK can be matched from both directions.
                    if (!seen.Add(predicate))
                        continue;

                    yield return new CompletionItem
                    {
                        DisplayText = predicate,
                        InsertText = predicate,
                        ObjectType = (int)CompletionObjectType.Keyword,
                        SecondaryText = $"FK · {fk.FkName}",
                        SourceObject = fk.FkName,
                        // Priority 5 — beats columns (100) and keywords (500) so the
                        // ready-made FK predicate appears at the very top of the list.
                        SortPriority = 5
                    };
                }
            }
        }
    }

    private static (string schema, string table) SplitName(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length >= 2
            ? (parts[0], parts[1])
            : ("dbo", parts[0]);
    }

    /// <summary>
    /// Build an equality predicate, handling multi-column FKs with <c>AND</c>.
    /// E.g. <c>o.CustomerId = c.Id</c> or
    /// <c>od.OrderId = o.Id AND od.ProductId = p.Id</c>.
    /// </summary>
    private static string BuildPredicate(
        string leftAlias,
        List<string> leftColumns,
        string rightAlias,
        List<string> rightColumns)
    {
        var count = Math.Min(leftColumns.Count, rightColumns.Count);
        if (count == 1)
            return $"{leftAlias}.{leftColumns[0]} = {rightAlias}.{rightColumns[0]}";

        var parts = new List<string>(count);
        for (int i = 0; i < count; i++)
            parts.Add($"{leftAlias}.{leftColumns[i]} = {rightAlias}.{rightColumns[i]}");
        return string.Join(" AND ", parts);
    }
}
