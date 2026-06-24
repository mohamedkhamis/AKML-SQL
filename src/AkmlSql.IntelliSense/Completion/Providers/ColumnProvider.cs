using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides column completions in two scenarios:
/// 1. <b>Dot-qualified</b>: after typing <c>alias.</c> or <c>table.</c> — yields columns from that table.
/// 2. <b>Expression-position</b>: in WHERE/JOIN ON/SELECT list/GROUP BY/HAVING/ORDER BY/UPDATE SET
///    contexts — yields columns from ALL tables already in scope (resolved via FROM/JOIN clauses).
///    When multiple tables are in scope the column is qualified with its alias to avoid ambiguity.
/// Ranking: PK columns first (priority 10), FK columns second (priority 20), then by ordinal (priority 30).
/// </summary>
public class ColumnProvider : ICompletionProvider
{
    public string Name => "Column";

    /// <summary>
    /// Spec 030 R6 / T032 / FR-012 — suggestion scope for bare column completions. Default
    /// <see cref="ColumnSuggestionScope.ReferencedOnly"/>: columns only from FROM-referenced
    /// tables. <see cref="ColumnSuggestionScope.All"/>: when no table is referenced yet (e.g. a
    /// bare <c>SELECT |</c>), suggest columns from every column-loaded user table. Pushed per
    /// request by <see cref="CompletionEngine"/>.
    /// </summary>
    public ColumnSuggestionScope ColumnScopeMode { get; set; } = ColumnSuggestionScope.ReferencedOnly;

    /// <summary>
    /// Spec 030 T036 / FR-016 — limits the <see cref="ColumnSuggestionScope.All"/> column list
    /// to schemas in this set (case-insensitive). Empty = all schemas in scope. Mirrors the same
    /// property on <see cref="ObjectProvider"/>; pushed per request by <see cref="CompletionEngine"/>.
    /// </summary>
    public ISet<string> ScopeSchemas { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clause contexts where bare column names are valid completions
    /// (i.e. expression positions inside a query referencing in-scope tables).
    /// </summary>
    private static readonly HashSet<ClauseType> ExpressionClauses =
    [
        ClauseType.Select,
        ClauseType.Where,
        ClauseType.JoinOn,
        ClauseType.GroupBy,
        ClauseType.Having,
        ClauseType.OrderBy,
        ClauseType.UpdateSet,
        ClauseType.InsertColumns,
        ClauseType.AlterTableColumn
    ];

    /// <summary>
    /// Clauses where SQL Server rejects with "Msg 209: Ambiguous column name" when
    /// a `SELECT col, *` form makes a bare reference unresolvable. In these clauses
    /// the column provider emits BOTH the bare column AND the `table.column`
    /// qualified form even in single-table queries so the user can pick the
    /// disambiguated variant. WHERE / SELECT-list / JOIN ON / UPDATE SET stay
    /// bare-only in single-table queries because the engine resolves them without
    /// ambiguity issues.
    /// </summary>
    private static readonly HashSet<ClauseType> AmbiguityProneClauses =
    [
        ClauseType.OrderBy,
        ClauseType.GroupBy,
        ClauseType.Having
    ];

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null)
        {
            return false;
        }

        // ── Path 1: dot-qualified completion ("alias." / "table." / "cte.") ──
        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            if (context.AvailableAliases.ContainsKey(context.DotPrefix))
                return true;

            if (context.AvailableCtes.ContainsKey(context.DotPrefix))
                return true;

            if (context.AvailableTempTables.ContainsKey(context.DotPrefix))
                return true;

            if (cache.FindObject("dbo", context.DotPrefix) != null)
                return true;

            foreach (var schema in cache.Schemas.Values)
            {
                if (cache.FindObject(schema.SchemaName, context.DotPrefix) != null)
                    return true;
            }

            return false;
        }

        // ── Path 2: bare column name in an expression-position clause ──
        // The cursor is in WHERE/JOIN ON/SELECT/etc. AND there is at least one table already
        // referenced in the FROM/JOIN clause — OR ColumnScope=All (FR-012), which suggests
        // columns from all tables even before a FROM clause exists.
        return ExpressionClauses.Contains(context.ClauseType)
               && (context.AvailableAliases.Count > 0 || ColumnScopeMode == ColumnSuggestionScope.All);
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null)
        {
            yield break;
        }

        // ── Path 1: dot-qualified columns from a single table ──
        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            foreach (var item in GetDotQualifiedColumns(context, cache))
                yield return item;
            yield break;
        }

        // ── ColumnScope=All (FR-012): no FROM table referenced yet → suggest columns from every
        // column-loaded user table so the user can pick a column before writing FROM. ──
        if (ExpressionClauses.Contains(context.ClauseType)
            && context.AvailableAliases.Count == 0
            && ColumnScopeMode == ColumnSuggestionScope.All)
        {
            foreach (var item in GetAllTableColumns(cache, ScopeSchemas))
                yield return item;
            yield break;
        }

        // ── Path 2: bare columns from ALL tables in scope ──
        if (!ExpressionClauses.Contains(context.ClauseType) || context.AvailableAliases.Count == 0)
        {
            yield break;
        }

        bool multiTable = context.AvailableAliases.Count > 1;
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (alias, fullTableName) in context.AvailableAliases)
        {
            // CTE branch: if the alias resolves to a CTE in scope, yield its
            // projected columns and skip the schema-cache lookup. The continue
            // applies regardless of column count — a token-recovered CTE with
            // no columns yields nothing, but we still avoid wrongly resolving
            // the name to a same-named real table (or logging a confusing
            // "table not in cache" warning).
            if (context.AvailableCtes.TryGetValue(alias, out var cteCols))
            {
                foreach (var colName in cteCols)
                {
                    var displayText = multiTable ? $"{alias}.{colName}" : colName;
                    if (!seenColumns.Add(displayText)) continue;
                    yield return new CompletionItem
                    {
                        DisplayText   = displayText,
                        InsertText    = displayText,
                        ObjectType    = (int)CompletionObjectType.Column,
                        SecondaryText = "(CTE column) • " + alias,
                        SourceObject  = alias,
                        SortPriority  = 30
                    };
                }
                continue;
            }

            // Temp-table branch (Spec 030): if the alias resolves to (or is) a #temp table, yield its
            // tracked columns and skip the schema-cache lookup. Mirrors the CTE branch above.
            if (context.AvailableTempTables.TryGetValue(BareTableName(fullTableName), out var tmpCols)
                || context.AvailableTempTables.TryGetValue(alias, out tmpCols))
            {
                foreach (var colName in tmpCols)
                {
                    var displayText = multiTable ? $"{alias}.{colName}" : colName;
                    if (!seenColumns.Add(displayText)) continue;
                    yield return new CompletionItem
                    {
                        DisplayText   = displayText,
                        InsertText    = displayText,
                        ObjectType    = (int)CompletionObjectType.Column,
                        SecondaryText = "(temp table column) • " + alias,
                        SourceObject  = fullTableName,
                        SortPriority  = 30
                    };
                }
                continue;
            }

            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            var dbObject = cache.FindObject(schemaName, tableName);
            if (dbObject == null)
            {
                // Try all schemas if not found in dbo
                foreach (var schema in cache.Schemas.Values)
                {
                    dbObject = cache.FindObject(schema.SchemaName, tableName);
                    if (dbObject != null) { schemaName = schema.SchemaName; break; }
                }
            }
            if (dbObject == null)
            {
                Log.Debug("ColumnProvider (bare path): table {Schema}.{Table} not found in cache", schemaName, tableName);
                continue;
            }
            if (!dbObject.ColumnsLoaded || dbObject.Columns.Count == 0)
            {
                Log.Debug("ColumnProvider (bare path): columns not loaded for {Table}", dbObject.FullName);
                continue;
            }

            var fkColumnNames = BuildFkColumnSet(cache, schemaName, tableName);

            foreach (var column in dbObject.Columns)
            {
                // Qualify with alias when multiple tables are in scope so the user
                // can disambiguate. Single-table queries get plain column names.
                var bareDisplay = column.ColumnName;
                var qualifiedDisplay = $"{alias}.{column.ColumnName}";

                int priority;
                if (column.IsPrimaryKey) priority = 10;
                else if (fkColumnNames.Contains(column.ColumnName)) priority = 20;
                else priority = 30;

                // Bare form: the default. In single-table queries that's the only
                // thing the user normally wants. In multi-table queries we skip it
                // (qualified is mandatory) — the existing `multiTable` branch.
                if (!multiTable)
                {
                    if (seenColumns.Add(bareDisplay))
                    {
                        yield return new CompletionItem
                        {
                            DisplayText = bareDisplay,
                            InsertText = bareDisplay,
                            ObjectType = (int)CompletionObjectType.Column,
                            SecondaryText = FormatSecondaryText(column) + " • " + tableName,
                            SourceObject = dbObject.FullName,
                            SortPriority = priority
                        };
                    }
                }

                // Qualified `alias.column` form: emit when the surrounding clause
                // is ambiguity-prone (ORDER BY / GROUP BY / HAVING) so the user can
                // pick the disambiguated variant after `SELECT col, *`, OR when
                // multiTable is true (the previous behaviour where every clause
                // qualifies). In WHERE / SELECT-list / JOIN ON / UpdateSet
                // single-table contexts the bare form stays the only suggestion to
                // avoid duplicate-noise; SQL Server resolves those without
                // ambiguity errors.
                bool emitQualified = multiTable ||
                    AmbiguityProneClauses.Contains(context.ClauseType);

                if (emitQualified && seenColumns.Add(qualifiedDisplay))
                {
                    yield return new CompletionItem
                    {
                        DisplayText = qualifiedDisplay,
                        InsertText = qualifiedDisplay,
                        ObjectType = (int)CompletionObjectType.Column,
                        SecondaryText = FormatSecondaryText(column) + " • " + tableName,
                        SourceObject = dbObject.FullName,
                        // Qualified items rank slightly lower than bare so that in
                        // single-table queries the bare form stays the default.
                        SortPriority = priority + 5
                    };
                }
            }
        }
    }

    /// <summary>
    /// The bare (unqualified) table name — the last dot-delimited segment. The alias resolver
    /// schema-qualifies references (e.g. <c>dbo.#t</c>), but the temp-table tracker keys by the bare
    /// name (<c>#t</c>), so temp lookups strip the prefix.
    /// </summary>
    private static string BareTableName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
    }

    /// <summary>
    /// Spec 030 T032 / FR-012 — columns from every column-loaded user table/view, used when
    /// ColumnScope=All and no table is referenced yet. Tables whose columns haven't loaded
    /// (Phase B background load) are skipped — never force a load here. Items are bare column
    /// names with the owning table in the secondary text; the popup filter narrows as the user types.
    /// <para>FR-016: only schemas in <paramref name="scopeSchemas"/> are considered (empty = all).</para>
    /// </summary>
    private static IEnumerable<CompletionItem> GetAllTableColumns(DatabaseCache cache,
        ISet<string>? scopeSchemas = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in cache.Schemas.Values)
        {
            // FR-016: skip schemas that are outside the connection scope.
            if (scopeSchemas != null && scopeSchemas.Count > 0
                && !scopeSchemas.Contains(schema.SchemaName))
                continue;

            foreach (var obj in cache.GetObjectsInSchema(schema.SchemaName))
            {
                if (obj.ObjectType != DbObjectType.Table && obj.ObjectType != DbObjectType.View)
                    continue;
                if (!obj.ColumnsLoaded || obj.Columns.Count == 0)
                    continue;

                // Snapshot the column list before iterating — Phase B may still be
                // Add()-ing to the underlying List<Column> on a background thread.
                var columns = obj.Columns.ToArray();
                foreach (var column in columns)
                {
                    if (!seen.Add($"{obj.FullName}.{column.ColumnName}"))
                        continue;

                    int priority = column.IsPrimaryKey ? 10 : 30;
                    yield return new CompletionItem
                    {
                        DisplayText = column.ColumnName,
                        InsertText = column.ColumnName,
                        ObjectType = (int)CompletionObjectType.Column,
                        SecondaryText = FormatSecondaryText(column) + " • " + obj.ObjectName,
                        SourceObject = obj.FullName,
                        SortPriority = priority,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Yields columns for a single table identified by <see cref="CursorContext.DotPrefix"/>
    /// (the original "alias." / "table." behavior).
    /// </summary>
    private static IEnumerable<CompletionItem> GetDotQualifiedColumns(CursorContext context, DatabaseCache cache)
    {
        string schemaName;
        string tableName;

        // CTE branch: if the dot-prefix is a CTE name (e.g. "Cte1.|"), yield its
        // projected columns and stop. CTEs aren't in the schema cache, so falling
        // through to FindObject would either return nothing or — worse — return a
        // real table that happens to share the CTE's name. We yield-break even if
        // the column list is empty (token-based CTE fallback gives names without
        // columns when the parser can't recover); silent no-result is correct
        // there, schema-cache lookup is not.
        if (context.AvailableCtes.TryGetValue(context.DotPrefix, out var cteCols))
        {
            foreach (var colName in cteCols)
            {
                yield return new CompletionItem
                {
                    DisplayText   = colName,
                    InsertText    = colName,
                    ObjectType    = (int)CompletionObjectType.Column,
                    SecondaryText = "(CTE column)",
                    SourceObject  = context.DotPrefix,
                    SortPriority  = 30
                };
            }
            yield break;
        }

        // Temp-table branch (Spec 030): #temp columns, reached directly ("#t.|") or via an alias
        // ("x.|" where x → #t). Mirrors the CTE branch; temp tables aren't in the schema cache.
        var tempKey = context.AvailableAliases.TryGetValue(context.DotPrefix, out var tempResolved)
            ? BareTableName(tempResolved)   // alias resolves to e.g. "dbo.#t"; temp tracker keys by "#t"
            : context.DotPrefix;
        if (context.AvailableTempTables.TryGetValue(tempKey, out var tmpCols))
        {
            foreach (var colName in tmpCols)
            {
                yield return new CompletionItem
                {
                    DisplayText   = colName,
                    InsertText    = colName,
                    ObjectType    = (int)CompletionObjectType.Column,
                    SecondaryText = "(temp table column)",
                    SourceObject  = tempKey,
                    SortPriority  = 30
                };
            }
            yield break;
        }

        if (context.AvailableAliases.TryGetValue(context.DotPrefix, out var fullTableName))
        {
            var parts = fullTableName.Split('.');
            schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            tableName = parts.Length >= 2 ? parts[1] : parts[0];
        }
        else
        {
            schemaName = "dbo";
            tableName = context.DotPrefix;
        }

        var dbObject = cache.FindObject(schemaName, tableName);
        if (dbObject == null)
        {
            foreach (var schema in cache.Schemas.Values)
            {
                dbObject = cache.FindObject(schema.SchemaName, tableName);
                if (dbObject != null) { schemaName = schema.SchemaName; break; }
            }
        }
        if (dbObject == null)
        {
            Log.Debug("ColumnProvider: table {Schema}.{Table} not found in cache", schemaName, tableName);
            yield break;
        }

        if (!dbObject.ColumnsLoaded || dbObject.Columns.Count == 0)
        {
            Log.Debug("ColumnProvider: columns not loaded for {Table}", dbObject.FullName);
            yield break;
        }

        var fkColumnNames = BuildFkColumnSet(cache, schemaName, tableName);

        foreach (var column in dbObject.Columns)
        {
            int priority;
            if (column.IsPrimaryKey) priority = 10;
            else if (fkColumnNames.Contains(column.ColumnName)) priority = 20;
            else priority = 30;

            yield return new CompletionItem
            {
                DisplayText = column.ColumnName,
                InsertText = column.ColumnName,
                ObjectType = (int)CompletionObjectType.Column,
                SecondaryText = FormatSecondaryText(column),
                SourceObject = dbObject.FullName,
                SortPriority = priority
            };
        }
    }

    /// <summary>
    /// Builds the set of column names that are part of any FK on the given table
    /// (used to bump FK columns higher in the completion ranking).
    /// </summary>
    private static HashSet<string> BuildFkColumnSet(DatabaseCache cache, string schemaName, string tableName)
    {
        var fkColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in cache.GetForeignKeysForTable(schemaName, tableName))
        {
            if (fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var col in fk.ParentColumns)
                    fkColumnNames.Add(col);
            }
            if (fk.ReferencedSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                fk.ReferencedTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var col in fk.ReferencedColumns)
                    fkColumnNames.Add(col);
            }
        }
        return fkColumnNames;
    }

    private static string FormatSecondaryText(Column column)
    {
        var parts = new List<string>(3)
        {
            column.TypeDisplay,
            column.IsNullable ? "NULL" : "NOT NULL"
        };

        if (column.IsPrimaryKey)
        {
            parts.Add("PK");
        }

        if (column.IsIdentity)
        {
            parts.Add("IDENTITY");
        }

        if (column.IsComputed)
        {
            parts.Add("COMPUTED");
        }

        return string.Join(", ", parts);
    }
}
