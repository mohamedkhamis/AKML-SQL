using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides schema-aware object completions (tables, views, procedures, functions, schemas).
/// Handles plain names, schema-qualified names (schema.object), and context-aware filtering.
/// </summary>
public class ObjectProvider : ICompletionProvider
{
    public string Name => "Object";

    /// <summary>
    /// When false, system stored procedures from <see cref="SystemProcDictionary"/> are
    /// excluded from Exec-context completions.
    /// Set by <see cref="CompletionEngine"/> before each request.
    /// </summary>
    public bool IncludeSystemObjects { get; set; } = true;

    /// <summary>
    /// Controls how object names are qualified in <see cref="InsertText"/>.
    /// Set by <see cref="CompletionEngine"/> before each request.
    /// Default <see cref="SchemaQualifyMode.Always"/> (SQL Prompt parity).
    /// </summary>
    public SchemaQualifyMode SchemaQualifyMode { get; set; } = SchemaQualifyMode.Always;

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
        // CTE suggestions don't need a schema cache — they come from the current doc.
        if (!context.PrecedingDot &&
            context.AvailableCtes.Count > 0 &&
            (context.ClauseType is ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn))
        {
            return true;
        }

        // System procs from SystemProcDictionary don't need a cache — they come from a static
        // list. When IncludeSystemObjects is enabled and we're in an EXEC context, we can handle
        // the request even without a cache.
        if (cache is null && IncludeSystemObjects && context.ClauseType == ClauseType.Exec && !context.PrecedingDot)
        {
            return true;
        }

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
        if (!ObjectClauseTypes.Contains(context.ClauseType))
            return false;

        // SQL Standard sequencing: after a FROM-target identifier we expect a
        // clause keyword (WHERE / GROUP BY / ORDER BY / JOIN / UNION / etc.) or
        // a comma for another table — NOT another bare table name. Suppress
        // ObjectProvider so KeywordProvider's "AfterFrom" list dominates here.
        // Same rule for JoinTable (after `JOIN <target>`) and UpdateTable.
        if (IsAfterTableTargetIdentifier(context))
            return false;

        return true;
    }

    /// <summary>
    /// True when the cursor sits immediately past a table-target identifier in a
    /// FROM/JOIN/UPDATE clause — i.e., the previous non-whitespace token is an
    /// identifier (the table name or its alias) or a closing paren (end of a
    /// derived-table expression). This is the position where the user expects
    /// the NEXT clause keyword, not another table.
    /// </summary>
    private static bool IsAfterTableTargetIdentifier(CursorContext context)
    {
        if (context.ClauseType is not (ClauseType.From or ClauseType.JoinTable or ClauseType.UpdateTable))
            return false;
        var prev = context.PrecedingToken;
        if (prev == null) return false;
        return prev.TokenType is TSqlTokenType.Identifier
            or TSqlTokenType.QuotedIdentifier
            or TSqlTokenType.RightParenthesis;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        // CTE names come BEFORE schema objects with a bumped priority — a CTE defined
        // in the same statement is almost always the target when the user types a
        // FROM/JOIN clause inside a subsequent CTE or the final SELECT.
        if (!context.PrecedingDot &&
            context.AvailableCtes.Count > 0 &&
            context.ClauseType is ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn)
        {
            foreach (var cteName in context.AvailableCtes.Keys)
            {
                yield return new CompletionItem
                {
                    DisplayText = cteName,
                    InsertText = cteName,
                    ObjectType = (int)CompletionObjectType.Table,
                    SecondaryText = "CTE",
                    SortPriority = 50 // above dbo tables (100) and non-dbo (200)
                };
            }
        }

        // When there is no schema cache, we can still offer system stored procedures for
        // EXEC context — they come from SystemProcDictionary, not the cache.
        if (cache is null)
        {
            if (IncludeSystemObjects && context.ClauseType == ClauseType.Exec && !context.PrecedingDot)
            {
                foreach (var item in SystemProcDictionary.GetCompletionItems())
                    yield return item;
            }
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

        // First, yield objects from default schema (dbo) with higher priority.
        // When SchemaMode is Always, dbo objects also get the "dbo." prefix in InsertText.
        bool qualifyDbo = SchemaQualifyMode == SchemaQualifyMode.Always;
        foreach (var obj in GetFilteredObjects(cache, "dbo", allowedTypes))
        {
            if (skipFkTables && fkRelated.ContainsKey(obj.FullName))
                continue;
            yield return ToCompletionItem(obj, sortPriorityBase: 100, includeSchema: qualifyDbo, fkRelated: fkRelated);
        }

        // Yield objects from non-dbo schemas, schema-qualified.
        // NonDefaultOnly and Always both qualify non-dbo objects; Never skips the prefix.
        bool qualifyNonDbo = SchemaQualifyMode != SchemaQualifyMode.Never;
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
                yield return ToCompletionItem(obj, sortPriorityBase: 200, includeSchema: qualifyNonDbo, fkRelated: fkRelated);
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

        // Yield system stored procedures in EXEC context — gated on IncludeSystemObjects.
        // SystemProcDictionary contains ms-shipped system procs not present in the user schema cache.
        if (IncludeSystemObjects && context.ClauseType == ClauseType.Exec)
        {
            foreach (var item in SystemProcDictionary.GetCompletionItems())
                yield return item;
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
