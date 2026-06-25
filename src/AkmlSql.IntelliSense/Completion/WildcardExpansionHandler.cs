using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Completion;

/// <summary>
/// Handles wildcard expansion requests. Parses SQL to find FROM-clause tables,
/// resolves aliases, fetches columns from schema cache, returns grouped column data.
/// </summary>
public class WildcardExpansionHandler
{
    private readonly TsqlParserService _parserService;
    private readonly AliasResolver _aliasResolver = new();
    private readonly CteResolver _cteResolver = new();

    public WildcardExpansionHandler(TsqlParserService parserService)
    {
        _parserService = parserService;
    }

    /// <summary>
    /// Resolve FROM-clause tables and return their columns grouped by table.
    /// </summary>
    public WildcardExpansionResponse Handle(string documentText, int cursorOffset, string? qualifier, DatabaseCache? cache)
    {
        if (cache == null)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No schema cache available" };
        }

        // Resolve aliases: try AST first, fall back to token-based
        var aliases = ResolveAliases(documentText, cursorOffset);
        if (aliases.Count == 0)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No tables found in FROM clause" };
        }

        // Resolve CTE names → projected column lists. When the FROM clause references
        // a CTE (WITH cte AS (SELECT a, b FROM t) SELECT * FROM cte), expanding `*`
        // must produce the CTE's projected columns (a, b), NOT the underlying table's
        // columns (which would mistakenly include columns the CTE didn't project).
        var script = _parserService.ParseWithSuffix(documentText, out _);
        var cteColumns = _cteResolver.ResolveCtes(script, cursorOffset);

        // Recovery: if the full document failed to yield any CTEs but we DO have
        // FROM-clause aliases (so the cursor is in a real FROM), parse only the
        // text BEFORE the cursor. The prefix typically ends at "SELECT  *" which
        // is incomplete on its own, but the WITH clause that precedes it is
        // syntactically well-formed and CteResolver can extract its columns.
        // This handles SQL Prompt parity for the common "broken stuff after the
        // statement I'm working on" pattern.
        if (cteColumns.Count == 0 && cursorOffset > 0 && cursorOffset <= documentText.Length)
        {
            var prefix = documentText.Substring(0, cursorOffset);
            var prefixScript = _parserService.ParseWithSuffix(prefix, out _);
            if (prefixScript != null)
            {
                // Position the cursor at end-of-prefix — all CTEs in the WITH
                // clause are visible there because the cursor lies past their bodies.
                cteColumns = _cteResolver.ResolveCtes(prefixScript, prefix.Length);
            }
        }

        // Filter by qualifier if specified
        Dictionary<string, string> targetAliases;
        if (!string.IsNullOrEmpty(qualifier))
        {
            if (aliases.TryGetValue(qualifier, out var fullName))
            {
                targetAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { qualifier, fullName }
                };
            }
            else
            {
                return new WildcardExpansionResponse { Success = false, ErrorMessage = $"Qualifier '{qualifier}' not found" };
            }
        }
        else
        {
            targetAliases = aliases;
        }

        // Build table groups with columns
        var tableGroups = new List<WildcardTableGroup>();

        foreach (var (aliasOrTable, fullTableName) in targetAliases)
        {
            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            // Skip derived tables
            if (tableName.StartsWith("(derived:"))
                continue;

            // CTE: AliasResolver sees WITH cte AS (...) SELECT * FROM cte as a
            // NamedTableReference {Schema=dbo, Table=cte}, so the alias arrives here
            // looking like a real table. Check the CTE map first — if it matches,
            // expand to the CTE's projected columns (no type info available).
            if (cteColumns.TryGetValue(tableName, out var cteCols) && cteCols.Count > 0)
            {
                var cteWildcardColumns = cteCols.Select(name => new WildcardColumn
                {
                    ColumnName = name,
                    TypeDisplay = "(CTE column)"
                }).ToArray();

                tableGroups.Add(new WildcardTableGroup
                {
                    TableName = tableName,
                    Qualifier = aliasOrTable,
                    Columns = cteWildcardColumns
                });
                continue;
            }

            var dbObject = cache.FindObject(schemaName, tableName);
            if (dbObject == null)
            {
                Log.Debug("WildcardExpansion: table {Schema}.{Table} not in cache", schemaName, tableName);
                continue;
            }

            if (!dbObject.ColumnsLoaded || dbObject.Columns.Count == 0)
            {
                Log.Debug("WildcardExpansion: columns not loaded for {Table}", dbObject.FullName);
                continue;
            }

            // Order: PK first, then by ordinal (ColumnId)
            var orderedColumns = dbObject.Columns
                .OrderByDescending(c => c.IsPrimaryKey)
                .ThenBy(c => c.ColumnId)
                .ToList();

            var columns = orderedColumns.Select(c => new WildcardColumn
            {
                ColumnName = c.ColumnName,
                TypeDisplay = FormatTypeDisplay(c)
            }).ToArray();

            tableGroups.Add(new WildcardTableGroup
            {
                TableName = tableName,
                Qualifier = aliasOrTable,
                Columns = columns
            });
        }

        if (tableGroups.Count == 0)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No columns available for resolved tables" };
        }

        return new WildcardExpansionResponse
        {
            Success = true,
            Tables = tableGroups.ToArray()
        };
    }

    private Dictionary<string, string> ResolveAliases(string documentText, int cursorOffset)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Use the scope-aware resolver: for SELECT * FROM Cte1, return only Cte1 —
        // not the tables that Cte1's body references, nor sibling CTE bodies, nor
        // unrelated subqueries. Wildcard expansion needs the cursor's immediate FROM
        // scope, not every NamedTableReference in the statement.
        var script = _parserService.ParseWithSuffix(documentText, out _);
        if (script != null)
        {
            var resolved = _aliasResolver.ResolveAliasesInCursorScope(script, cursorOffset);
            foreach (var (alias, tableRef) in resolved)
                aliases[alias] = tableRef.FullName;
        }

        // Fallback to token-based if AST produced nothing (e.g. partial SQL that
        // can't be parsed). Token-based extraction is already cursor-position-aware.
        if (aliases.Count == 0)
        {
            var tokens = _parserService.GetTokenStream(documentText);
            var fallback = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);
            foreach (var (alias, fullName) in fallback)
                aliases[alias] = fullName;
        }

        return aliases;
    }

    private static string FormatTypeDisplay(Column column)
    {
        var parts = new List<string>(4) { column.TypeDisplay };
        parts.Add(column.IsNullable ? "NULL" : "NOT NULL");
        if (column.IsPrimaryKey) parts.Add("PK");
        if (column.IsIdentity) parts.Add("IDENTITY");
        if (column.IsComputed) parts.Add("COMPUTED");
        return string.Join(", ", parts);
    }
}
