using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T056-T058: Provides JOIN table completions based on foreign key relationships.
/// When the cursor is in a FROM clause after a JOIN keyword, suggests tables that have
/// FK relationships with already-referenced tables, including auto-generated ON clauses.
/// </summary>
public class JoinProvider : ICompletionProvider
{
    public string Name => "Join";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Activate when in FROM or JoinTable clause and there are already
        // referenced tables (aliases) available for FK-based suggestions
        if (context.ClauseType != ClauseType.From && context.ClauseType != ClauseType.JoinTable)
        {
            return false;
        }

        if (cache == null)
        {
            return false;
        }

        // Must have at least one table already referenced to suggest FK-based joins
        return context.AvailableAliases.Count > 0;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null)
        {
            yield break;
        }

        // Collect all FK-related tables for each referenced table
        var seenJoins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (alias, fullTableName) in context.AvailableAliases)
        {
            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            var foreignKeys = cache.GetForeignKeysForTable(schemaName, tableName);

            foreach (var fk in foreignKeys)
            {
                // Determine which side is the "other" table
                string otherSchema, otherTable;
                List<string> otherColumns, existingColumns;

                bool isParent = fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                                fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase);

                if (isParent)
                {
                    otherSchema = fk.ReferencedSchema;
                    otherTable = fk.ReferencedTable;
                    otherColumns = fk.ReferencedColumns;
                    existingColumns = fk.ParentColumns;
                }
                else
                {
                    otherSchema = fk.ParentSchema;
                    otherTable = fk.ParentTable;
                    otherColumns = fk.ParentColumns;
                    existingColumns = fk.ReferencedColumns;
                }

                var otherFullName = $"{otherSchema}.{otherTable}";

                // Skip if this table is already referenced or already suggested
                if (context.AvailableAliases.Values.Any(v =>
                    v.Equals(otherFullName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var joinKey = $"{otherFullName}|{fk.FkName}";
                if (!seenJoins.Add(joinKey))
                {
                    continue;
                }

                // T058: Generate alias for the join target
                var joinAlias = GenerateAlias(otherTable, context.AvailableAliases);

                // T057: Build ON clause
                var onClause = BuildOnClause(joinAlias, otherColumns, alias, existingColumns);

                var qualifiedName = otherSchema.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                    ? otherTable
                    : otherFullName;

                var insertText = $"{qualifiedName} {joinAlias} ON {onClause}";

                // SQL Prompt style: show "TableName alias" as display, "ON a.Col = b.Col" as secondary
                var displayText = $"{otherTable} {joinAlias}";
                var secondaryText = $"ON {onClause}";

                yield return new CompletionItem
                {
                    DisplayText = displayText,
                    InsertText = insertText,
                    ObjectType = (int)CompletionObjectType.Table,
                    SecondaryText = secondaryText,
                    SourceObject = otherFullName,
                    SortPriority = 10
                };
            }
        }
    }

    /// <summary>
    /// T057: Build ON clause text for a foreign key relationship.
    /// Handles multi-column FKs: "ON a.col1 = b.col1 AND a.col2 = b.col2"
    /// </summary>
    private static string BuildOnClause(
        string joinAlias,
        List<string> joinColumns,
        string existingAlias,
        List<string> existingColumns)
    {
        var parts = new List<string>();

        int count = Math.Min(joinColumns.Count, existingColumns.Count);
        for (int i = 0; i < count; i++)
        {
            parts.Add($"{joinAlias}.{joinColumns[i]} = {existingAlias}.{existingColumns[i]}");
        }

        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// T058: Generate a short alias from a table name using PascalCase first letters.
    /// E.g., "OrderDetails" -> "od", "CustomerAddress" -> "ca"
    /// Checks for conflicts with existing aliases and appends a number if needed.
    /// </summary>
    internal static string GenerateAlias(string tableName, Dictionary<string, string> existingAliases)
    {
        var alias = ExtractPascalCaseInitials(tableName);

        if (string.IsNullOrEmpty(alias))
        {
            alias = tableName.Length >= 2
                ? tableName[..2].ToLowerInvariant()
                : tableName.ToLowerInvariant();
        }

        // Ensure no conflict with existing aliases
        var candidate = alias;
        int suffix = 2;
        while (existingAliases.ContainsKey(candidate))
        {
            candidate = $"{alias}{suffix}";
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Extract PascalCase initials: "OrderDetails" -> "od", "Order" -> "o"
    /// </summary>
    private static string ExtractPascalCaseInitials(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var initials = new List<char>();

        for (int i = 0; i < name.Length; i++)
        {
            if (i == 0 || char.IsUpper(name[i]) || name[i] == '_')
            {
                char c = name[i] == '_' && i + 1 < name.Length ? name[i + 1] : name[i];
                if (char.IsLetter(c))
                {
                    initials.Add(char.ToLowerInvariant(c));
                }
            }
        }

        return initials.Count > 0 ? new string(initials.ToArray()) : string.Empty;
    }
}
