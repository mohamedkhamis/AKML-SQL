using System.Collections.Concurrent;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Schema;

public enum PopulationPhase
{
    NotLoaded,
    PhaseA,   // Names only (<500ms)
    PhaseB,   // Columns + FKs (background)
    Complete
}

public class DatabaseCache
{
    public string CacheKey { get; set; } = string.Empty; // server:database
    public PopulationPhase Phase { get; set; } = PopulationPhase.NotLoaded;
    public ConcurrentDictionary<string, SchemaEntry> Schemas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ForeignKey> ForeignKeys { get; set; } = [];
    public DateTime LastFullRefresh { get; set; }
    public int LastChangeChecksum { get; set; }
    public bool IsStale { get; set; }

    public DatabaseObject? FindObject(string schemaName, string objectName)
    {
        if (Schemas.TryGetValue(schemaName, out var schema))
            return schema.Objects.FirstOrDefault(o =>
                o.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public IEnumerable<DatabaseObject> GetAllObjects()
    {
        return Schemas.Values.SelectMany(s => s.Objects);
    }

    public IEnumerable<DatabaseObject> GetObjectsInSchema(string schemaName)
    {
        if (Schemas.TryGetValue(schemaName, out var schema))
            return schema.Objects;
        return [];
    }

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

    public List<ForeignKey> GetForeignKeysForTable(string schemaName, string tableName)
    {
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
