using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides column completions when typing after an alias dot (e.g., "o." where o is an alias for Orders).
/// Resolves alias → table via AvailableAliases, then pulls columns from DatabaseCache.
/// Ranking: PK columns first (priority 10), FK columns second (priority 20), then by ordinal (priority 30).
/// </summary>
public class ColumnProvider : ICompletionProvider
{
    public string Name => "Column";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Only handle when there's a dot prefix that matches a known alias
        if (!context.PrecedingDot || string.IsNullOrEmpty(context.DotPrefix))
            return false;

        if (cache == null)
            return false;

        return context.AvailableAliases.ContainsKey(context.DotPrefix);
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null || string.IsNullOrEmpty(context.DotPrefix))
            yield break;

        if (!context.AvailableAliases.TryGetValue(context.DotPrefix, out var fullTableName))
            yield break;

        // Parse "schema.table" from the full name
        var parts = fullTableName.Split('.');
        var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
        var tableName = parts.Length >= 2 ? parts[1] : parts[0];

        var dbObject = cache.FindObject(schemaName, tableName);
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

        // Build a set of FK column names for this table
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

        foreach (var column in dbObject.Columns)
        {
            int priority;
            if (column.IsPrimaryKey)
                priority = 10;
            else if (fkColumnNames.Contains(column.ColumnName))
                priority = 20;
            else
                priority = 30;

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

    private static string FormatSecondaryText(Column column)
    {
        var parts = new List<string>(3);
        parts.Add(column.TypeDisplay);
        parts.Add(column.IsNullable ? "NULL" : "NOT NULL");

        if (column.IsPrimaryKey)
            parts.Add("PK");
        if (column.IsIdentity)
            parts.Add("IDENTITY");
        if (column.IsComputed)
            parts.Add("COMPUTED");

        return string.Join(", ", parts);
    }
}
