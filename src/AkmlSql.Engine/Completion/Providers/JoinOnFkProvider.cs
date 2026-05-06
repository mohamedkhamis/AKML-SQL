using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

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

        // Canonical alias order = AvailableAliases insertion order ≈ FROM-clause
        // order. We use this to dedup `A.x = B.y` vs `B.y = A.x` (same SQL meaning,
        // different strings) and to put the existing/leftmost FROM participant on
        // the left of each predicate — matching the user's expectation that the
        // first FROM alias (e.g. Cte1) appears first.
        var aliasOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int aliasIdx = 0;
        foreach (var key in context.AvailableAliases.Keys)
            aliasOrder[key] = aliasIdx++;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: table-to-table FK predicates. For each canonical pair, look up
        // FKs on either side and emit one predicate per matching FK.
        foreach (var (leftAlias, leftFullName) in context.AvailableAliases)
        {
            foreach (var (rightAlias, rightFullName) in context.AvailableAliases)
            {
                if (aliasOrder[leftAlias] >= aliasOrder[rightAlias]) continue;

                // Skip CTE participants — covered by passes 2/3 below where CTE
                // column projections are taken into account.
                if (context.AvailableCtes.ContainsKey(leftAlias) ||
                    context.AvailableCtes.ContainsKey(rightAlias))
                    continue;

                var (leftSchema,  leftTable)  = FkHelpers.SplitName(leftFullName);
                var (rightSchema, rightTable) = FkHelpers.SplitName(rightFullName);

                foreach (var fk in cache.GetForeignKeysForTable(leftSchema, leftTable))
                {
                    bool leftIsFkParent = fk.ParentSchema.Equals(leftSchema,  StringComparison.OrdinalIgnoreCase)
                                       && fk.ParentTable .Equals(leftTable,  StringComparison.OrdinalIgnoreCase)
                                       && fk.ReferencedSchema.Equals(rightSchema, StringComparison.OrdinalIgnoreCase)
                                       && fk.ReferencedTable .Equals(rightTable, StringComparison.OrdinalIgnoreCase);
                    bool leftIsFkRef    = fk.ReferencedSchema.Equals(leftSchema,  StringComparison.OrdinalIgnoreCase)
                                       && fk.ReferencedTable .Equals(leftTable,  StringComparison.OrdinalIgnoreCase)
                                       && fk.ParentSchema.Equals(rightSchema, StringComparison.OrdinalIgnoreCase)
                                       && fk.ParentTable .Equals(rightTable, StringComparison.OrdinalIgnoreCase);
                    if (!leftIsFkParent && !leftIsFkRef) continue;

                    var leftCols  = leftIsFkParent ? fk.ParentColumns     : fk.ReferencedColumns;
                    var rightCols = leftIsFkParent ? fk.ReferencedColumns : fk.ParentColumns;

                    var predicate = FkHelpers.BuildFkPredicate(leftAlias, leftCols, rightAlias, rightCols);
                    if (!seen.Add(predicate)) continue;

                    yield return new CompletionItem
                    {
                        DisplayText   = predicate,
                        InsertText    = predicate,
                        ObjectType    = (int)CompletionObjectType.Keyword,
                        SecondaryText = $"FK · {fk.FkName}",
                        SourceObject  = fk.FkName,
                        SortPriority  = 5
                    };
                }
            }
        }

        // FK-via-CTE-sources pass. For each canonical pair, walk through to
        // their source tables and emit FK-based predicates using CTE column
        // names. Canonical order ensures one direction per pair.
        foreach (var (leftAlias, _) in context.AvailableAliases)
        {
            foreach (var (rightAlias, _) in context.AvailableAliases)
            {
                if (aliasOrder[leftAlias] >= aliasOrder[rightAlias]) continue;

                var leftSources  = ResolveSourceTables(leftAlias, context);
                var rightSources = ResolveSourceTables(rightAlias, context);

                // At least one side must be a CTE — otherwise the FK loop above
                // already covered it (and there's nothing extra to gain).
                if (!context.AvailableCtes.ContainsKey(leftAlias) &&
                    !context.AvailableCtes.ContainsKey(rightAlias))
                    continue;

                var leftCols  = ResolveJoinableColumnNames(leftAlias,  string.Empty, context, cache);
                var rightCols = ResolveJoinableColumnNames(rightAlias, string.Empty, context, cache);
                var leftSet  = new HashSet<string>(leftCols,  StringComparer.OrdinalIgnoreCase);
                var rightSet = new HashSet<string>(rightCols, StringComparer.OrdinalIgnoreCase);

                foreach (var (lSchema, lTable) in leftSources)
                {
                    foreach (var fk in cache.GetForeignKeysForTable(lSchema, lTable))
                    {
                        // Identify whether this FK connects leftTable to one of right's source tables.
                        bool fkParentMatchesLeft =
                            fk.ParentSchema.Equals(lSchema,  StringComparison.OrdinalIgnoreCase) &&
                            fk.ParentTable.Equals (lTable,  StringComparison.OrdinalIgnoreCase);
                        bool fkRefMatchesLeft =
                            fk.ReferencedSchema.Equals(lSchema, StringComparison.OrdinalIgnoreCase) &&
                            fk.ReferencedTable.Equals (lTable, StringComparison.OrdinalIgnoreCase);

                        foreach (var (rSchema, rTable) in rightSources)
                        {
                            bool fkOtherMatchesRight =
                                (fkParentMatchesLeft &&
                                 fk.ReferencedSchema.Equals(rSchema, StringComparison.OrdinalIgnoreCase) &&
                                 fk.ReferencedTable.Equals (rTable, StringComparison.OrdinalIgnoreCase))
                                ||
                                (fkRefMatchesLeft &&
                                 fk.ParentSchema.Equals(rSchema, StringComparison.OrdinalIgnoreCase) &&
                                 fk.ParentTable.Equals (rTable, StringComparison.OrdinalIgnoreCase));
                            if (!fkOtherMatchesRight) continue;

                            var leftFkColumns  = fkParentMatchesLeft ? fk.ParentColumns     : fk.ReferencedColumns;
                            var rightFkColumns = fkParentMatchesLeft ? fk.ReferencedColumns : fk.ParentColumns;
                            if (leftFkColumns.Count != rightFkColumns.Count || leftFkColumns.Count == 0)
                                continue;

                            // For each FK column on either side, check it's present
                            // in the CTE projection (the CTE may have dropped it).
                            // Build the multi-column AND-joined predicate when all
                            // FK columns survived the projections on both sides.
                            var parts = new List<string>(leftFkColumns.Count);
                            for (int idx = 0; idx < leftFkColumns.Count; idx++)
                            {
                                if (!leftSet.Contains(leftFkColumns[idx])) { parts.Clear(); break; }
                                if (!rightSet.Contains(rightFkColumns[idx])) { parts.Clear(); break; }
                                parts.Add($"{leftAlias}.{leftFkColumns[idx]} = {rightAlias}.{rightFkColumns[idx]}");
                            }
                            if (parts.Count == 0) continue;

                            var predicate = string.Join(" AND ", parts);
                            if (!seen.Add(predicate)) continue;

                            yield return new CompletionItem
                            {
                                DisplayText   = predicate,
                                InsertText    = predicate,
                                ObjectType    = (int)CompletionObjectType.Keyword,
                                SecondaryText = $"FK · {fk.FkName} (via CTE source)",
                                SourceObject  = fk.FkName,
                                // Priority 6 — between table-FK (5) and CTE name-match (7+).
                                SortPriority  = 6
                            };
                        }
                    }
                }
            }
        }

        // Name-match pass for CTE participants. Fires only when at least one
        // side is a CTE — for table-to-table pairs the FK loop above is
        // authoritative and we don't want to suggest noisy unrelated name
        // collisions. Canonical order ensures one direction per pair.
        foreach (var (leftAlias, leftFullName) in context.AvailableAliases)
        {
            foreach (var (rightAlias, rightFullName) in context.AvailableAliases)
            {
                if (aliasOrder[leftAlias] >= aliasOrder[rightAlias]) continue;

                bool leftIsCte  = context.AvailableCtes.ContainsKey(leftAlias);
                bool rightIsCte = context.AvailableCtes.ContainsKey(rightAlias);
                if (!leftIsCte && !rightIsCte)
                    continue;

                var leftCols  = ResolveJoinableColumnNames(leftAlias, leftFullName, context, cache);
                var rightCols = ResolveJoinableColumnNames(rightAlias, rightFullName, context, cache);
                if (leftCols.Count == 0 || rightCols.Count == 0)
                    continue;

                var rightLookup = new HashSet<string>(rightCols, StringComparer.OrdinalIgnoreCase);
                foreach (var col in leftCols)
                {
                    if (!rightLookup.Contains(col)) continue;
                    var predicate = $"{leftAlias}.{col} = {rightAlias}.{col}";
                    if (!seen.Add(predicate)) continue;

                    // Id columns rank highest (most likely the intended join key),
                    // *Id-suffixed columns next, other name matches last. Still
                    // beats bare columns (100) so suggestions surface up top.
                    int priority =
                        col.Equals("Id", StringComparison.OrdinalIgnoreCase)              ? 7 :
                        col.EndsWith("Id", StringComparison.OrdinalIgnoreCase)            ? 8 :
                                                                                            10;

                    yield return new CompletionItem
                    {
                        DisplayText   = predicate,
                        InsertText    = predicate,
                        ObjectType    = (int)CompletionObjectType.Keyword,
                        SecondaryText = "Name match · CTE",
                        SourceObject  = $"{leftAlias}↔{rightAlias}",
                        SortPriority  = priority
                    };
                }
            }
        }
    }

    /// <summary>
    /// Returns the column-name list for a join participant: the CTE projection
    /// when the alias is a CTE in scope, the schema-cache columns when it's a
    /// real table. Empty when neither resolves (no metadata available).
    /// </summary>
    private static List<string> ResolveJoinableColumnNames(
        string alias, string fullName, CursorContext context, DatabaseCache cache)
    {
        if (context.AvailableCtes.TryGetValue(alias, out var cteCols))
            return cteCols;

        if (string.IsNullOrEmpty(fullName) &&
            context.AvailableAliases.TryGetValue(alias, out var resolved))
            fullName = resolved;

        var (schema, table) = FkHelpers.SplitName(fullName);
        var dbObj = cache.FindObject(schema, table);
        if (dbObj == null || !dbObj.ColumnsLoaded)
            return new List<string>();

        var result = new List<string>(dbObj.Columns.Count);
        foreach (var c in dbObj.Columns)
            result.Add(c.ColumnName);
        return result;
    }

    /// <summary>
    /// For a join participant, returns the set of underlying tables to consult
    /// for FK lookups. CTEs contribute their tracked source tables; real tables
    /// contribute themselves.
    /// </summary>
    private static List<(string Schema, string Table)> ResolveSourceTables(
        string alias, CursorContext context)
    {
        if (context.AvailableCteSources.TryGetValue(alias, out var sources))
            return sources;

        if (context.AvailableAliases.TryGetValue(alias, out var fullName))
        {
            var (schema, table) = FkHelpers.SplitName(fullName);
            return new List<(string, string)> { (schema, table) };
        }

        return new List<(string, string)>();
    }
}
