using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T056-T058: Provides JOIN table completions based on foreign key relationships.
/// When the cursor is in a FROM clause after a JOIN keyword, suggests tables that have
/// FK relationships with already-referenced tables, including auto-generated ON clauses.
/// <para>
/// Controlled by two engine toggles:
/// <list type="bullet">
///   <item><c>JoinAssistEnabled</c> — gates the provider entirely. When off, nothing runs.</item>
///   <item><c>UseAliases</c> — mirrors <c>TableAliasEnabled</c>. When on, emits
///   <c>Orders o ON o.CustomerId = c.Id</c>. When off, emits
///   <c>Orders ON Orders.CustomerId = c.Id</c> using the bare table name.</item>
/// </list>
/// </para>
/// </summary>
public class JoinProvider : ICompletionProvider
{
    public string Name => "Join";

    /// <summary>
    /// When <c>true</c>, insertion text includes a generated alias for the JOIN target
    /// (<c>Orders o ON ...</c>). When <c>false</c>, the bare table name is used on both
    /// sides of the ON clause. Set by <see cref="CompletionEngine"/> before each call.
    /// </summary>
    public bool UseAliases { get; set; }

    /// <summary>
    /// Controls how the JOIN target table is qualified in the insertion text — same policy as
    /// <see cref="ObjectProvider.SchemaQualifyMode"/>, so a committed FK-join suggestion writes
    /// <c>dbo.Orders o ON …</c> exactly like committing the table from the plain list.
    /// Set by <see cref="CompletionEngine"/> before each call.
    /// </summary>
    public AkmlSql.Core.Config.SchemaQualifyMode SchemaQualifyMode { get; set; } = AkmlSql.Core.Config.SchemaQualifyMode.Always;

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Activate ONLY when in JoinTable clause (after a JOIN keyword) — never in plain FROM.
        // In a plain FROM context the user is choosing the FIRST table; suggesting an
        // FK-joined table here would insert "TableName alias ON ..." which is wrong.
        if (context.ClauseType != ClauseType.JoinTable)
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
            var (schemaName, tableName) = FkHelpers.SplitName(fullTableName);

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

                // Qualify the join target per the engine's schema policy (Always → "dbo.Orders";
                // NonDefaultOnly → bare for dbo; Never → bare everywhere).
                var qualifiedName = SchemaQualifyMode switch
                {
                    AkmlSql.Core.Config.SchemaQualifyMode.Always => otherFullName,
                    AkmlSql.Core.Config.SchemaQualifyMode.Never => otherTable,
                    _ => otherSchema.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                        ? otherTable
                        : otherFullName,
                };

                // When UseAliases is off, both sides of the ON clause fall back to bare
                // qualified names. The existing side (`alias`) is the user's alias from
                // the query if they wrote one, otherwise the table name itself, so it's
                // already valid either way.
                string targetReference;
                string displayText;
                if (UseAliases)
                {
                    var joinAlias = GenerateAlias(otherTable, context.AvailableAliases);
                    targetReference = joinAlias;
                    displayText = $"{otherTable} {joinAlias}";
                }
                else
                {
                    targetReference = qualifiedName;
                    displayText = otherTable;
                }

                var onClause = FkHelpers.BuildFkPredicate(targetReference, otherColumns, alias, existingColumns);
                var insertText = UseAliases
                    ? $"{qualifiedName} {targetReference} ON {onClause}"
                    : $"{qualifiedName} ON {onClause}";
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
