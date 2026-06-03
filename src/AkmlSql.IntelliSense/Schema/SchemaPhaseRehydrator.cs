using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Schema;

/// <summary>
/// Spec 028 (M6 — AI parity closure) task T003. The reverse of
/// <c>AkmlSql.Engine.Handlers.Schema.SchemaPhaseSerializer</c>: reconstructs a
/// <see cref="DatabaseCache"/> from the flat <see cref="SchemaPhasePayload"/> bytes the
/// browser cached under <c>SchemaSnapshot.PhaseA</c> / <c>.PhaseB</c>.
///
/// <para>
/// M5 (spec 027) deliberately deferred this mapper (research Decision 3 there): the only
/// consumer then was cached-heavyweight refactoring, and the offline completion path reads
/// the payload directly. M6 needs it because schema-aware AI prompting reuses the canonical
/// <c>SchemaContextBuilder</c>, which is coupled to <see cref="DatabaseCache"/> /
/// <see cref="DatabaseObject"/> — so the browser must hand it a rehydrated cache rather than
/// fork a second schema-text generator. Building it here also unblocks the M5
/// cached-heavyweight follow-up for free.
/// </para>
///
/// <para>
/// WASM-safe: depends only on existing models — no <c>System.IO</c>, no SqlClient, no native.
/// The round-trip is lossy for fields the serializer never ships (row counts, indexes,
/// max-length/precision, object ids); the rehydrated cache faithfully reproduces everything
/// <c>SchemaContextBuilder</c> consumes (names, object types, columns + types + PK flags,
/// descriptions, foreign keys). See <c>SchemaPhaseRehydratorTests</c> for the invariant.
/// </para>
/// </summary>
public static class SchemaPhaseRehydrator
{
    /// <summary>
    /// Rehydrate a <see cref="DatabaseCache"/> from the cached phase payloads. Prefers the
    /// Phase B payload (it carries columns + foreign keys); falls back to Phase A (object
    /// names only) when B is absent. Returns an empty cache when both are null.
    /// </summary>
    /// <param name="cacheKey">The <c>"server:database"</c> identity to stamp on the cache.</param>
    /// <param name="phaseA">The cached Phase A payload (names/types), or null.</param>
    /// <param name="phaseB">The cached Phase B payload (columns/FKs), or null.</param>
    public static DatabaseCache Rehydrate(string cacheKey, SchemaPhasePayload? phaseA, SchemaPhasePayload? phaseB)
    {
        // Phase B supersedes Phase A for schema/object/column data; B alone, A alone, or
        // neither are all valid inputs.
        var source = phaseB ?? phaseA;

        var cache = new DatabaseCache
        {
            CacheKey = cacheKey ?? string.Empty,
            Phase = source == null ? PopulationPhase.NotLoaded : (PopulationPhase)source.Phase,
            LastChangeChecksum = 0,
            IsStale = false,
            PermissionDenied = false,
        };

        if (source != null)
        {
            foreach (var schema in source.Schemas)
            {
                var entry = new SchemaEntry
                {
                    SchemaName = schema.Name,
                    Objects = schema.Objects.Select(MapObject).ToList(),
                };
                cache.Schemas[schema.Name] = entry;
            }
        }

        // Foreign keys only ever ship in the Phase B payload.
        if (phaseB != null)
        {
            cache.ForeignKeys = phaseB.ForeignKeys.Select(MapForeignKey).ToList();
        }

        cache.RebuildFkIndex();
        return cache;
    }

    private static DatabaseObject MapObject(SchemaPhaseObject o) => new()
    {
        SchemaName = o.SchemaName,
        ObjectName = o.ObjectName,
        ObjectType = (DbObjectType)o.ObjectType,
        Description = o.Description,
        Columns = o.Columns.Select(MapColumn).ToList(),
        Parameters = o.Parameters.Select(MapParameter).ToList(),
        // The serializer ships columns only in Phase B; treat their presence as "loaded".
        ColumnsLoaded = o.Columns.Length > 0,
    };

    private static Column MapColumn(SchemaPhaseColumn c) => new()
    {
        ColumnName = c.Name,
        TypeName = c.TypeName,
        IsNullable = c.IsNullable,
        IsPrimaryKey = c.IsPrimaryKey,
        Description = c.Description,
        MaxLength = c.MaxLength,
        Precision = c.Precision,
        Scale = c.Scale,
    };

    private static Parameter MapParameter(SchemaPhaseParameter p) => new()
    {
        ParameterName = p.Name,
        TypeName = p.TypeName,
        IsOutput = p.IsOutput,
        HasDefault = p.HasDefault,
    };

    private static ForeignKey MapForeignKey(SchemaPhaseForeignKey fk) => new()
    {
        FkName = fk.Name,
        ParentSchema = fk.ParentSchema,
        ParentTable = fk.ParentTable,
        ParentColumns = fk.ParentColumns.ToList(),
        ReferencedSchema = fk.ReferencedSchema,
        ReferencedTable = fk.ReferencedTable,
        ReferencedColumns = fk.ReferencedColumns.ToList(),
    };
}
