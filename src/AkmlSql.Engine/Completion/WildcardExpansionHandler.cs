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

        // Try AST-based resolution first
        var script = _parserService.ParseWithSuffix(documentText, out _);
        if (script != null)
        {
            var resolved = _aliasResolver.ResolveAliases(script, cursorOffset);
            foreach (var (alias, tableRef) in resolved)
                aliases[alias] = tableRef.FullName;
        }

        // Fallback to token-based if AST produced nothing
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
