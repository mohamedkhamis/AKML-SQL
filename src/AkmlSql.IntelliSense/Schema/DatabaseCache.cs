using System.Collections.Concurrent;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Schema;

/// <summary>Population phase of a <see cref="DatabaseCache"/>.</summary>
public enum PopulationPhase
{
    /// <summary>Cache has not been populated yet.</summary>
    NotLoaded,
    /// <summary>Phase A complete: schema names and object names loaded (&lt;500 ms target).</summary>
    PhaseA,
    /// <summary>Phase B complete: columns, FKs, parameters, and descriptions loaded in background.</summary>
    PhaseB,
    /// <summary>All metadata loaded and no refresh is pending.</summary>
    Complete
}

/// <summary>
/// In-memory cache for a single server:database combination. Holds schema objects, columns,
/// and foreign keys populated by <c>SchemaMetadataService</c> in two phases.
/// Thread-safe reads via <see cref="ConcurrentDictionary"/>; writes are serialized by
/// <c>SchemaCacheManager</c>'s per-cache semaphore.
/// </summary>
public class DatabaseCache
{
    /// <summary>Cache identity key in the form <c>"server:database"</c>.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>Current population phase. Phase A is the minimum needed for IntelliSense.</summary>
    public PopulationPhase Phase { get; set; } = PopulationPhase.NotLoaded;

    /// <summary>Schema entries keyed by schema name (case-insensitive).</summary>
    public ConcurrentDictionary<string, SchemaEntry> Schemas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All foreign key relationships in this database. Indexed by <see cref="RebuildFkIndex"/>.</summary>
    public List<ForeignKey> ForeignKeys { get; set; } = [];

    /// <summary>UTC timestamp of the last complete Phase A + B refresh cycle.</summary>
    public DateTime LastFullRefresh { get; set; }

    /// <summary>
    /// Last <c>CHECKSUM_AGG(BINARY_CHECKSUM(...))</c> value from <c>sys.objects</c>.
    /// Used by <c>ChangeDetector</c> to detect schema modifications efficiently.
    /// </summary>
    public int LastChangeChecksum { get; set; }

    /// <summary><c>true</c> when change detection has determined that cached data is out of date.</summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Terminal flag set when Phase A fails with a login/permission SQL error
    /// (4060, 18456, 18452, 916). Once set, the server is telling us the current
    /// identity cannot access this database — no amount of retrying will fix it
    /// until the user reconnects with a different identity, which arrives as a
    /// brand-new ConnectionChanged (and hence a brand-new cache). Callers that
    /// schedule background work (Phase A population, DatabaseProvider prefetch,
    /// periodic change detection) MUST check this flag first to avoid repeat
    /// log noise and wasted round-trips.
    /// </summary>
    public bool PermissionDenied { get; set; }

    // Index for O(1) FK lookup keyed by "schema.table" (both parent and referenced sides)
    private Dictionary<string, List<ForeignKey>>? _fkByTable;

    /// <summary>
    /// Finds a database object by schema and name. Returns <c>null</c> if not found.
    /// O(objects-per-schema) — for hot paths prefer building a lookup dictionary first.
    /// </summary>
    public DatabaseObject? FindObject(string schemaName, string objectName)
    {
        if (Schemas.TryGetValue(schemaName, out var schema))
        {
            return schema.Objects.FirstOrDefault(o =>
                o.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>Returns all database objects across all schemas in this cache.</summary>
    public IEnumerable<DatabaseObject> GetAllObjects()
    {
        return Schemas.Values.SelectMany(s => s.Objects);
    }

    /// <summary>Returns all database objects in the specified schema, or an empty sequence if the schema is not found.</summary>
    public IEnumerable<DatabaseObject> GetObjectsInSchema(string schemaName)
    {
        if (Schemas.TryGetValue(schemaName, out var schema))
        {
            return schema.Objects;
        }

        return [];
    }

    /// <summary>Returns the names of all schemas currently in the cache.</summary>
    public IEnumerable<string> GetSchemaNames()
    {
        return Schemas.Keys;
    }

    /// <summary>
    /// Checks whether columns have been loaded for a specific table.
    /// Used for lazy column loading decisions — the caller can trigger
    /// SchemaMetadataService.PopulatePhaseBAsync if columns are needed but not yet loaded.
    /// </summary>
    public bool AreColumnsLoaded(string schemaName, string objectName)
    {
        var obj = FindObject(schemaName, objectName);
        return obj?.ColumnsLoaded == true;
    }

    /// <summary>
    /// Builds an O(1) FK lookup dictionary keyed by "schema.table".
    /// Call after loading ForeignKeys to enable fast lookups.
    /// </summary>
    public void RebuildFkIndex()
    {
        var index = new Dictionary<string, List<ForeignKey>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in ForeignKeys)
        {
            var parentKey = $"{fk.ParentSchema}.{fk.ParentTable}";
            if (!index.TryGetValue(parentKey, out var pList))
                index[parentKey] = pList = [];
            pList.Add(fk);

            var refKey = $"{fk.ReferencedSchema}.{fk.ReferencedTable}";
            if (!string.Equals(parentKey, refKey, StringComparison.OrdinalIgnoreCase))
            {
                if (!index.TryGetValue(refKey, out var rList))
                    index[refKey] = rList = [];
                rList.Add(fk);
            }
        }
        _fkByTable = index;
    }

    public List<ForeignKey> GetForeignKeysForTable(string schemaName, string tableName)
    {
        if (_fkByTable != null)
        {
            var key = $"{schemaName}.{tableName}";
            return _fkByTable.TryGetValue(key, out var list) ? list : [];
        }

        // Fallback to linear scan if index not yet built
        return ForeignKeys.Where(fk =>
            (fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
             fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase)) ||
            (fk.ReferencedSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
             fk.ReferencedTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }
}

public class SchemaEntry
{
    public string SchemaName { get; set; } = string.Empty;
    public List<DatabaseObject> Objects { get; set; } = [];
}
