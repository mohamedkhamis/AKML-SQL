using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Ai.Context;

/// <summary>
/// Builds a <see cref="SchemaContext"/> from a <see cref="DatabaseCache"/> for inclusion in AI prompts.
/// Spec 036 (US1) inventory-first assembly (contracts/schema-context.md Part 2): the FULL object
/// inventory is always included at level 1; prompt-named objects and their FK 1-hop neighbours are
/// promoted to the requested detail level; the object budget is applied LAST and truncation is
/// signalled explicitly. Relevance may only promote detail — it can never remove inventory.
/// Compression levels control the detail a PROMOTED object carries:
/// <list type="bullet">
///   <item>Level 1: Schema, Name, Type, ApproxRowCount only (ghost-text latency path; no promotion)</item>
///   <item>Level 2: Add column details (name, type, nullable, PK)</item>
///   <item>Level 3: Add PK columns list, indexes, FK relationships</item>
///   <item>Level 4: Add Description from extended properties</item>
/// </list>
/// <para>
/// Spec 021 T121: refactored to take a <c>cacheLookup</c> delegate rather than the engine-only
/// <c>SchemaCacheManager</c>. The engine wires <c>(sid, db) => schemaCacheManager.GetCache(sid, db)</c>
/// — engine caches are keyed by SESSION ID (<c>ConnectionChangedHandler</c> creates them via
/// <c>GetOrCreateCache(request.SessionId, ...)</c>), so the lookup key is the session id, never the
/// connection string (passing the raw connection string always misses — see ScriptAsHandler).
/// The web edition (M6) wires its IndexedDB cache lookup and ignores the key. Decouples this
/// assembly from AkmlSql.Engine and lets it run in WASM.
/// </para>
/// </summary>
public class SchemaContextBuilder
{
    private readonly Func<string, string, DatabaseCache?> _cacheLookup;

    public SchemaContextBuilder(Func<string, string, DatabaseCache?> cacheLookup)
    {
        _cacheLookup = cacheLookup ?? throw new ArgumentNullException(nameof(cacheLookup));
    }

    /// <summary>
    /// Builds a <see cref="SchemaContext"/> for the given session.
    /// </summary>
    /// <param name="sessionId">
    /// The session identifier used to look up connection info AND the schema cache (engine
    /// caches are keyed by session id).
    /// </param>
    /// <param name="sessionLookup">
    /// Delegate that returns (ConnectionString, DatabaseName) for a given session ID. The
    /// connection string is only used as the "bound" signal (null/empty → unbound context).
    /// </param>
    /// <param name="prompt">
    /// Optional user prompt. Objects whose name/column a prompt token matches are PROMOTED to
    /// the requested detail level (with their FK 1-hop neighbours). The rest of the inventory is
    /// always included at level 1 — matching never removes objects (FR-024/FR-025).
    /// </param>
    /// <param name="compressionLevel">Detail level for promoted objects (1-4). See class summary.</param>
    /// <param name="maxObjects">
    /// Maximum number of objects to include in the context (the explicit FR-026 budget;
    /// <c>settings.Ai.SchemaContextMaxObjects</c>, default 500). Applied LAST: promoted objects
    /// are always kept, the remaining budget is filled from the inventory in stable order, and
    /// overflow sets <see cref="SchemaContext.Truncated"/>.
    /// </param>
    /// <returns>A populated <see cref="SchemaContext"/>, or an unbound/empty one.</returns>
    public Task<SchemaContext> BuildAsync(
        string sessionId,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        string? prompt,
        int compressionLevel,
        int maxObjects = 500)
    {
        var (connectionString, databaseName) = sessionLookup(sessionId);

        var context = new SchemaContext
        {
            DatabaseName = databaseName ?? string.Empty,
            CompressionLevel = compressionLevel
        };

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
        {
            Log.Debug("SchemaContextBuilder: no connection/database for session {SessionId}", sessionId);
            return Task.FromResult(context);
        }

        var dbCache = _cacheLookup(sessionId, databaseName);
        if (dbCache == null)
        {
            Log.Debug("SchemaContextBuilder: no cache found for session {SessionId} database {Database}",
                sessionId, databaseName);
            return Task.FromResult(context);
        }

        var inventory = dbCache.GetAllObjects().ToList();
        context.TotalObjectCount = inventory.Count;
        if (inventory.Count == 0)
        {
            return Task.FromResult(context);
        }

        // Promotion: objects the prompt names, plus their FK 1-hop neighbours. Level 1 is the
        // latency path (ghost text) — names suffice, so nothing is promoted at all.
        List<DatabaseObject> promote;
        if (compressionLevel <= 1)
        {
            promote = [];
        }
        else
        {
            var tokens = string.IsNullOrWhiteSpace(prompt) ? [] : TokenizePrompt(prompt!);
            var named = tokens.Count == 0
                ? []
                : inventory.Where(obj => IsObjectRelevant(obj, tokens)).ToList();
            promote = ExpandFkConnections(named, dbCache, inventory);
        }

        var promoteNames = new HashSet<string>(
            promote.Select(o => o.FullName), StringComparer.OrdinalIgnoreCase);

        // Budget LAST: keep every promoted object, fill the rest from the inventory in stable
        // order. Overflow is explicit (Truncated + TotalObjectCount), never silent.
        List<DatabaseObject> kept;
        if (inventory.Count > maxObjects)
        {
            context.Truncated = true;
            kept = new List<DatabaseObject>(promote);
            foreach (var obj in inventory)
            {
                if (kept.Count >= maxObjects) break;
                if (promoteNames.Contains(obj.FullName)) continue; // already kept
                kept.Add(obj);
            }
        }
        else
        {
            kept = inventory;
        }

        Log.Debug("SchemaContextBuilder: {Inventory} inventory, {Promoted} promoted, {Kept} kept (max {Max}, truncated={Truncated})",
            inventory.Count, promote.Count, kept.Count, maxObjects, context.Truncated);

        // Build summaries: promoted objects at the requested level (at least 3 — keys and
        // relationships are the point of promotion), the rest of the inventory at level 1.
        var detailLevel = Math.Max(compressionLevel, 3);
        var fkLookup = BuildFkLookupForObjects(kept, dbCache);

        foreach (var obj in kept)
        {
            var level = promoteNames.Contains(obj.FullName) ? detailLevel : 1;
            context.Objects.Add(BuildObjectSummary(obj, level, fkLookup));
        }

        if (compressionLevel > 1)
        {
            foreach (var name in promoteNames)
            {
                context.DetailedObjectNames.Add(name);
            }
        }

        // Build FK summaries for relationships between included objects
        context.ForeignKeys = BuildFkSummaries(kept, dbCache);

        return Task.FromResult(context);
    }

    /// <summary>
    /// Tokenizes a prompt into lowercase words for matching. Splits on whitespace, punctuation,
    /// and common SQL delimiters. Filters out very short tokens (length &lt; 2).
    /// </summary>
    private static List<string> TokenizePrompt(string prompt)
    {
        var tokens = new List<string>();
        var separators = new[] { ' ', '\t', '\r', '\n', ',', '.', ';', '(', ')', '[', ']', '"', '\'', '`', '=', '<', '>', '!', '+', '-', '*', '/' };

        foreach (var part in prompt.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = part.ToLowerInvariant().Trim();
            if (lower.Length >= 2 && !IsSqlKeyword(lower))
            {
                tokens.Add(lower);
            }
        }

        return tokens.Distinct().ToList();
    }

    /// <summary>
    /// Returns true if the word is a common SQL keyword that should not be used for object matching.
    /// </summary>
    private static bool IsSqlKeyword(string word)
    {
        return word switch
        {
            "select" or "from" or "where" or "join" or "inner" or "left" or "right" or "outer" or "cross"
                or "on" or "and" or "or" or "not" or "in" or "is" or "null" or "like" or "between"
                or "insert" or "into" or "update" or "delete" or "set" or "values" or "create" or "alter"
                or "drop" or "table" or "view" or "index" or "procedure" or "function" or "trigger"
                or "exec" or "execute" or "declare" or "begin" or "end" or "if" or "else" or "while"
                or "case" or "when" or "then" or "as" or "by" or "order" or "group" or "having"
                or "with" or "union" or "all" or "top" or "distinct" or "count" or "sum" or "avg"
                or "min" or "max" or "asc" or "desc" or "the" or "for" or "this" or "that" or "how"
                or "what" or "show" or "me" or "get" or "find" or "list" or "write" or "query"
                or "can" or "you" or "help" => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a database object is relevant to any of the prompt tokens.
    /// Matches against object name, schema name, and column names (case-insensitive contains).
    /// </summary>
    private static bool IsObjectRelevant(DatabaseObject obj, List<string> tokens)
    {
        var objNameLower = obj.ObjectName.ToLowerInvariant();
        var schemaNameLower = obj.SchemaName.ToLowerInvariant();

        foreach (var token in tokens)
        {
            // Match against object name
            if (objNameLower.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }

            // Match against schema.object combined
            if ($"{schemaNameLower}.{objNameLower}".Contains(token, StringComparison.Ordinal))
            {
                return true;
            }

            // Match against column names (if loaded)
            if (obj.ColumnsLoaded)
            {
                foreach (var col in obj.Columns)
                {
                    if (col.ColumnName.ToLowerInvariant().Contains(token, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Expands the matched object set to include tables connected via FK relationships (1-hop).
    /// Preserves the original matched objects and appends connected tables at the end.
    /// </summary>
    private static List<DatabaseObject> ExpandFkConnections(
        List<DatabaseObject> matchedObjects,
        DatabaseCache dbCache,
        List<DatabaseObject> allObjects)
    {
        var resultSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DatabaseObject>();

        // Add all matched objects first
        foreach (var obj in matchedObjects)
        {
            if (resultSet.Add(obj.FullName))
            {
                result.Add(obj);
            }
        }

        // Build a quick lookup of all objects by full name
        var allByName = new Dictionary<string, DatabaseObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in allObjects)
        {
            allByName.TryAdd(obj.FullName, obj);
        }

        // For each matched object, find 1-hop FK connections
        foreach (var obj in matchedObjects)
        {
            var fks = dbCache.GetForeignKeysForTable(obj.SchemaName, obj.ObjectName);
            foreach (var fk in fks)
            {
                // Add parent table if not already included
                if (resultSet.Add(fk.ParentFullName) && allByName.TryGetValue(fk.ParentFullName, out var parentObj))
                {
                    result.Add(parentObj);
                }

                // Add referenced table if not already included
                if (resultSet.Add(fk.ReferencedFullName) && allByName.TryGetValue(fk.ReferencedFullName, out var refObj))
                {
                    result.Add(refObj);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a lookup of foreign keys grouped by parent table full name, for the given objects.
    /// </summary>
    private static Dictionary<string, List<ForeignKey>> BuildFkLookupForObjects(
        List<DatabaseObject> objects,
        DatabaseCache dbCache)
    {
        var lookup = new Dictionary<string, List<ForeignKey>>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in objects)
        {
            var fks = dbCache.GetForeignKeysForTable(obj.SchemaName, obj.ObjectName);
            if (fks.Count > 0)
            {
                // Only include FKs where this object is the parent
                var parentFks = fks.Where(fk =>
                    fk.ParentSchema.Equals(obj.SchemaName, StringComparison.OrdinalIgnoreCase) &&
                    fk.ParentTable.Equals(obj.ObjectName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (parentFks.Count > 0)
                {
                    lookup[obj.FullName] = parentFks;
                }
            }
        }

        return lookup;
    }

    /// <summary>
    /// Builds a <see cref="SchemaObjectSummary"/> for a single database object at the given compression level.
    /// </summary>
    private static SchemaObjectSummary BuildObjectSummary(
        DatabaseObject obj,
        int compressionLevel,
        Dictionary<string, List<ForeignKey>> fkLookup)
    {
        var summary = new SchemaObjectSummary
        {
            Schema = obj.SchemaName,
            Name = obj.ObjectName,
            Type = MapObjectType(obj.ObjectType),
            ApproxRowCount = obj.ApproxRowCount
        };

        // Level 2+: Add column details
        if (compressionLevel >= 2 && obj.ColumnsLoaded)
        {
            summary.Columns = [];
            foreach (var col in obj.Columns)
            {
                var colSummary = new ColumnSummary
                {
                    Name = col.ColumnName,
                    Type = col.TypeDisplay,
                    IsNullable = col.IsNullable,
                    IsPrimaryKey = col.IsPrimaryKey
                };

                // Add FK target annotation if this column is part of a FK relationship
                if (fkLookup.TryGetValue(obj.FullName, out var fks))
                {
                    foreach (var fk in fks)
                    {
                        var idx = fk.ParentColumns.FindIndex(c =>
                            c.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0 && idx < fk.ReferencedColumns.Count)
                        {
                            colSummary.ForeignKeyTarget =
                                $"{fk.ReferencedFullName}.{fk.ReferencedColumns[idx]}";
                            break;
                        }
                    }
                }

                summary.Columns.Add(colSummary);
            }
        }

        // Level 3+: Add PK columns, indexes, FK relationships
        if (compressionLevel >= 3)
        {
            // Primary key columns
            if (obj.ColumnsLoaded)
            {
                var pkCols = obj.Columns
                    .Where(c => c.IsPrimaryKey)
                    .Select(c => c.ColumnName)
                    .ToList();
                if (pkCols.Count > 0)
                {
                    summary.PrimaryKey = pkCols;
                }
            }

            // Indexes (non-PK)
            if (obj.Indexes.Count > 0)
            {
                var indexNames = obj.Indexes
                    .Where(ix => !ix.IsPrimaryKey)
                    .Select(ix => ix.IndexName)
                    .ToList();
                if (indexNames.Count > 0)
                {
                    summary.Indexes = indexNames;
                }
            }
        }

        // Level 4: Add description
        if (compressionLevel >= 4)
        {
            summary.Description = obj.Description;
        }

        return summary;
    }

    /// <summary>
    /// Maps <see cref="DbObjectType"/> to a human-readable string for AI context.
    /// </summary>
    private static string MapObjectType(DbObjectType type)
    {
        return type switch
        {
            DbObjectType.Table => "Table",
            DbObjectType.View => "View",
            DbObjectType.Procedure => "Procedure",
            DbObjectType.ScalarFunction => "Function",
            DbObjectType.TableFunction => "Function",
            DbObjectType.InlineFunction => "Function",
            DbObjectType.Synonym => "Synonym",
            DbObjectType.Sequence => "Sequence",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Builds the list of <see cref="FkSummary"/> for FK relationships between included objects.
    /// Only includes FKs where both parent and referenced tables are in the final object list.
    /// </summary>
    private static List<FkSummary> BuildFkSummaries(List<DatabaseObject> objects, DatabaseCache dbCache)
    {
        var includedNames = new HashSet<string>(
            objects.Select(o => o.FullName),
            StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var summaries = new List<FkSummary>();

        foreach (var obj in objects)
        {
            var fks = dbCache.GetForeignKeysForTable(obj.SchemaName, obj.ObjectName);
            foreach (var fk in fks)
            {
                // Only include if both sides are in the filtered set
                if (!includedNames.Contains(fk.ParentFullName) ||
                    !includedNames.Contains(fk.ReferencedFullName))
                {
                    continue;
                }

                // Deduplicate by FK name
                if (!seen.Add(fk.FkName))
                {
                    continue;
                }

                // Create one FkSummary per column pair in the FK
                for (int i = 0; i < fk.ParentColumns.Count && i < fk.ReferencedColumns.Count; i++)
                {
                    summaries.Add(new FkSummary
                    {
                        ParentTable = fk.ParentFullName,
                        ParentColumn = fk.ParentColumns[i],
                        ReferencedTable = fk.ReferencedFullName,
                        ReferencedColumn = fk.ReferencedColumns[i]
                    });
                }
            }
        }

        return summaries;
    }
}
