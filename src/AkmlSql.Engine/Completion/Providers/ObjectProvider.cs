using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides schema-aware object completions (tables, views, procedures, functions, schemas).
/// Handles plain names, schema-qualified names (schema.object), and context-aware filtering.
/// </summary>
public class ObjectProvider : ICompletionProvider
{
    public string Name => "Object";

    private static readonly HashSet<ClauseType> ObjectClauseTypes =
    [
        ClauseType.From,
        ClauseType.JoinTable,
        ClauseType.UpdateTable,
        ClauseType.Exec,
        ClauseType.Create,
        ClauseType.Alter,
        ClauseType.Delete,
        ClauseType.InsertColumns,
        ClauseType.UpdateSet,
        ClauseType.JoinOn
    ];

    private static readonly HashSet<DbObjectType> FromJoinObjectTypes =
    [
        DbObjectType.Table,
        DbObjectType.View,
        DbObjectType.TableFunction,
        DbObjectType.InlineFunction,
        DbObjectType.Synonym
    ];

    private static readonly HashSet<DbObjectType> ExecObjectTypes =
    [
        DbObjectType.Procedure
    ];

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        if (cache is null)
        {
            return false;
        }

        // Handle dot-qualified: schema.object or database.schema
        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            // If DotPrefix is a known alias, let ColumnProvider handle it (#19)
            if (context.AvailableAliases.ContainsKey(context.DotPrefix))
                return false;

            // Check if DotPrefix is a known schema name
            if (cache.Schemas.ContainsKey(context.DotPrefix))
            {
                return true;
            }

            // Could be database.schema scenario — handle for future extensibility
            return true;
        }

        // Handle non-dot contexts where objects are expected
        return ObjectClauseTypes.Contains(context.ClauseType);
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache is null)
        {
            yield break;
        }

        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            // T047: Multi-part name completion
            foreach (var item in GetDotQualifiedCompletions(context, cache))
                yield return item;
            yield break;
        }

        // No dot: return objects from default schema (dbo) + all schema names
        var allowedTypes = GetAllowedObjectTypes(context.ClauseType);

        // ── FK annotation for JOIN/FROM table suggestions ──
        // Build a set of tables that are FK-related to any already-referenced table
        // in the current statement. In JoinTable/From context those tables get a
        // visual marker ("FK → <other>") in their SecondaryText AND a priority boost
        // so they appear at the top of the suggestion list.
        var fkRelated = BuildFkRelatedLookup(context, cache);

        // In JoinTable context JoinProvider owns FK-related suggestions — it emits the
        // full "TABLE ON left.fk = right.pk" insertion text. Skip them here to avoid
        // showing each FK target twice (once as a full join clause, once as a bare name).
        // In From context we still emit everything — the first table has no prior
        // references to FK-join against, so JoinProvider wouldn't run anyway.
        var skipFkTables = context.ClauseType == ClauseType.JoinTable && fkRelated.Count > 0;

        // First, yield objects from default schema (dbo) with higher priority
        foreach (var obj in GetFilteredObjects(cache, "dbo", allowedTypes))
        {
            if (skipFkTables && fkRelated.ContainsKey(obj.FullName))
                continue;
            yield return ToCompletionItem(obj, sortPriorityBase: 100, fkRelated: fkRelated);
        }

        // Yield objects from non-dbo schemas, schema-qualified
        foreach (var schema in cache.Schemas.Values)
        {
            if (schema.SchemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var obj in GetFilteredObjects(cache, schema.SchemaName, allowedTypes))
            {
                if (skipFkTables && fkRelated.ContainsKey(obj.FullName))
                    continue;
                yield return ToCompletionItem(obj, sortPriorityBase: 200, includeSchema: true, fkRelated: fkRelated);
            }
        }

        // Yield schema names as completions
        foreach (var schemaName in cache.GetSchemaNames())
        {
            yield return new CompletionItem
            {
                DisplayText = schemaName,
                InsertText = schemaName,
                ObjectType = (int)CompletionObjectType.Schema,
                SecondaryText = "Schema",
                SortPriority = 300
            };
        }
    }

    /// <summary>
    /// Builds a map of "schema.table" → "related existing table name" for every table
    /// that has a foreign key relationship (in either direction) with any already-
    /// referenced table in <see cref="CursorContext.AvailableAliases"/>. Returns an
    /// empty dictionary when the clause context is not JOIN/FROM or when there are
    /// no existing table references.
    /// </summary>
    private static Dictionary<string, string> BuildFkRelatedLookup(CursorContext context, DatabaseCache cache)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.ClauseType != ClauseType.JoinTable && context.ClauseType != ClauseType.From)
        {
            return result;
        }

        foreach (var (_, fullTableName) in context.AvailableAliases)
        {
            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            foreach (var fk in cache.GetForeignKeysForTable(schemaName, tableName))
            {
                // Identify the "other" side of the relationship.
                string otherSchema, otherTable;
                bool isParent = fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                                fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase);
                if (isParent)
                {
                    otherSchema = fk.ReferencedSchema;
                    otherTable = fk.ReferencedTable;
                }
                else
                {
                    otherSchema = fk.ParentSchema;
                    otherTable = fk.ParentTable;
                }

                var key = $"{otherSchema}.{otherTable}";
                if (!result.ContainsKey(key))
                {
                    result[key] = tableName;
                }
            }
        }

        return result;
    }

    private static IEnumerable<CompletionItem> GetDotQualifiedCompletions(CursorContext context, DatabaseCache cache)
    {
        var prefix = context.DotPrefix;

        // Check if prefix contains a dot (database.schema scenario)
        var dotIndex = prefix.IndexOf('.');
        if (dotIndex >= 0)
        {
            // T047: database.schema.object — for now we only handle the schema part
            // Extract the schema name (part after the dot)
            var schemaName = prefix[(dotIndex + 1)..];
            var allowedTypes = GetAllowedObjectTypes(context.ClauseType);

            foreach (var obj in GetFilteredObjects(cache, schemaName, allowedTypes))
            {
                yield return ToCompletionItem(obj, sortPriorityBase: 100, fkRelated: null);
            }
            yield break;
        }

        // prefix is a schema name: return objects in that schema
        if (cache.Schemas.ContainsKey(prefix))
        {
            var allowedTypes = GetAllowedObjectTypes(context.ClauseType);
            foreach (var obj in GetFilteredObjects(cache, prefix, allowedTypes))
            {
                yield return ToCompletionItem(obj, sortPriorityBase: 100, fkRelated: null);
            }
            yield break;
        }

        // prefix might be a database name: return schema names
        // (Future: resolve actual database, for now return all schemas)
        foreach (var schemaName in cache.GetSchemaNames())
        {
            yield return new CompletionItem
            {
                DisplayText = schemaName,
                InsertText = schemaName,
                ObjectType = (int)CompletionObjectType.Schema,
                SecondaryText = "Schema",
                SortPriority = 100
            };
        }
    }

    /// <summary>
    /// T049: Context-aware filtering — returns the set of allowed object types based on clause.
    /// </summary>
    private static HashSet<DbObjectType>? GetAllowedObjectTypes(ClauseType clauseType)
    {
        return clauseType switch
        {
            ClauseType.Exec => ExecObjectTypes,
            ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn or ClauseType.Delete or ClauseType.UpdateTable => FromJoinObjectTypes,
            _ => null // null means all types allowed
        };
    }

    private static IEnumerable<DatabaseObject> GetFilteredObjects(
        DatabaseCache cache, string schemaName, HashSet<DbObjectType>? allowedTypes)
    {
        var objects = cache.GetObjectsInSchema(schemaName);

        if (allowedTypes is not null)
        {
            objects = objects.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        // T050: Rank by ApproxRowCount descending (tables/views), then alphabetical
        return objects
            .OrderByDescending(o => o.ApproxRowCount)
            .ThenBy(o => o.ObjectName, StringComparer.OrdinalIgnoreCase);
    }

    private static CompletionItem ToCompletionItem(
        DatabaseObject obj,
        int sortPriorityBase,
        bool includeSchema = false,
        Dictionary<string, string>? fkRelated = null)
    {
        var displayText = includeSchema ? obj.FullName : obj.ObjectName;
        var insertText = includeSchema ? obj.FullName : obj.ObjectName;

        var completionType = MapObjectType(obj.ObjectType);

        var secondaryText = obj.ObjectType switch
        {
            DbObjectType.Table => obj.ApproxRowCount > 0
                ? $"Table (~{FormatRowCount(obj.ApproxRowCount)} rows)"
                : "Table",
            DbObjectType.View => "View",
            DbObjectType.Procedure => "Procedure",
            DbObjectType.ScalarFunction => "Scalar Function",
            DbObjectType.TableFunction => "Table Function",
            DbObjectType.InlineFunction => "Inline Function",
            DbObjectType.Synonym => "Synonym",
            DbObjectType.Sequence => "Sequence",
            _ => string.Empty
        };

        // T050: Higher row count => lower sort priority number => ranked higher
        var sortPriority = sortPriorityBase;
        if (obj.ApproxRowCount > 0)
        {
            // Subtract a bonus based on log of row count (max bonus ~50)
            var bonus = (int)Math.Min(50, Math.Log10(obj.ApproxRowCount + 1) * 10);
            sortPriority -= bonus;
        }

        // ── FK annotation ──
        // When this table has a foreign-key relationship with a table already in
        // the current query (JOIN/FROM context), surface that in the secondary
        // text and boost the sort priority so it appears near the top.
        if (fkRelated != null && fkRelated.TryGetValue(obj.FullName, out var relatedTo))
        {
            secondaryText = $"{secondaryText}  •  \uD83D\uDD11 FK ↔ {relatedTo}";
            sortPriority -= 500; // strong bump: FK-related tables are almost always the right pick
        }

        return new CompletionItem
        {
            DisplayText = displayText,
            InsertText = insertText,
            ObjectType = (int)completionType,
            SecondaryText = secondaryText,
            SourceObject = obj.FullName,
            SortPriority = sortPriority
        };
    }

    private static CompletionObjectType MapObjectType(DbObjectType dbType)
    {
        return dbType switch
        {
            DbObjectType.Table => CompletionObjectType.Table,
            DbObjectType.View => CompletionObjectType.View,
            DbObjectType.Procedure => CompletionObjectType.Procedure,
            DbObjectType.ScalarFunction => CompletionObjectType.Function,
            DbObjectType.TableFunction => CompletionObjectType.Function,
            DbObjectType.InlineFunction => CompletionObjectType.Function,
            DbObjectType.Synonym => CompletionObjectType.Table, // Synonyms show as tables
            DbObjectType.Sequence => CompletionObjectType.Table,
            _ => CompletionObjectType.Table
        };
    }

    private static string FormatRowCount(long count)
    {
        return count switch
        {
            >= 1_000_000_000 => $"{count / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{count / 1_000_000.0:F1}M",
            >= 1_000 => $"{count / 1_000.0:F1}K",
            _ => count.ToString()
        };
    }
}
