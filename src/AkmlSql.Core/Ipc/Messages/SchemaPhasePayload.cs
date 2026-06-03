using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Flat MessagePack-serialisable view of
    /// the engine's <c>DatabaseCache</c>. Shipped inside <see cref="SchemaPhaseAResponse.PhaseA"/>
    /// and <see cref="SchemaPhaseBResponse.PhaseB"/> as opaque <c>byte[]</c>; the
    /// browser stores the bytes verbatim in <c>SchemaSnapshot.PhaseA</c> / <c>.PhaseB</c>
    /// and only deserialises when it needs to serve a completion offline.
    ///
    /// <para>
    /// The Phase A view leaves <c>Objects[].Columns</c> empty and ships an empty
    /// <see cref="ForeignKeys"/> array; the Phase B view fills both. This mirrors the
    /// engine's two-phase population strategy (Phase A &lt; 500 ms target; Phase B
    /// loads in the background).
    /// </para>
    /// </summary>
    [MessagePackObject]
    public sealed class SchemaPhasePayload
    {
        [Key(0)] public string DatabaseName { get; set; } = string.Empty;

        /// <summary>Engine's <c>PopulationPhase</c> enum value at serialisation time.</summary>
        [Key(1)] public int Phase { get; set; }

        /// <summary>Echo of the engine's cache checksum so the browser can pair payload + drift.</summary>
        [Key(2)] public string Checksum { get; set; } = string.Empty;

        [Key(3)] public SchemaPhaseSchema[] Schemas { get; set; } = System.Array.Empty<SchemaPhaseSchema>();

        /// <summary>Empty in Phase A payloads.</summary>
        [Key(4)] public SchemaPhaseForeignKey[] ForeignKeys { get; set; } = System.Array.Empty<SchemaPhaseForeignKey>();
    }

    [MessagePackObject]
    public sealed class SchemaPhaseSchema
    {
        [Key(0)] public string Name { get; set; } = string.Empty;
        [Key(1)] public SchemaPhaseObject[] Objects { get; set; } = System.Array.Empty<SchemaPhaseObject>();
    }

    [MessagePackObject]
    public sealed class SchemaPhaseObject
    {
        [Key(0)] public string SchemaName { get; set; } = string.Empty;
        [Key(1)] public string ObjectName { get; set; } = string.Empty;

        /// <summary>Maps to engine's <c>DbObjectType</c> enum.</summary>
        [Key(2)] public int ObjectType { get; set; }

        /// <summary>Empty in Phase A payloads.</summary>
        [Key(3)] public SchemaPhaseColumn[] Columns { get; set; } = System.Array.Empty<SchemaPhaseColumn>();

        /// <summary>Procedure / function parameter list. Empty for tables / views and in Phase A payloads.</summary>
        [Key(4)] public SchemaPhaseParameter[] Parameters { get; set; } = System.Array.Empty<SchemaPhaseParameter>();

        /// <summary>MS_Description extended property, if any. Used as the QuickInfo description.</summary>
        [Key(5)] public string? Description { get; set; }
    }

    [MessagePackObject]
    public sealed class SchemaPhaseColumn
    {
        [Key(0)] public string Name { get; set; } = string.Empty;
        [Key(1)] public string TypeName { get; set; } = string.Empty;
        [Key(2)] public bool IsNullable { get; set; }
        [Key(3)] public bool IsPrimaryKey { get; set; }
        [Key(4)] public string? Description { get; set; }

        // Spec 028 (M6): carry the type facets so a rehydrated column's TypeDisplay
        // reconstructs sized types correctly (e.g. nvarchar(100), decimal(18,2)) instead of
        // nvarchar(0)/decimal(0,0). Additive keys — older payloads default these to 0.
        [Key(5)] public int MaxLength { get; set; }
        [Key(6)] public int Precision { get; set; }
        [Key(7)] public int Scale { get; set; }
    }

    [MessagePackObject]
    public sealed class SchemaPhaseParameter
    {
        [Key(0)] public string Name { get; set; } = string.Empty;
        [Key(1)] public string TypeName { get; set; } = string.Empty;
        [Key(2)] public bool IsOutput { get; set; }
        [Key(3)] public bool HasDefault { get; set; }
    }

    [MessagePackObject]
    public sealed class SchemaPhaseForeignKey
    {
        [Key(0)] public string Name { get; set; } = string.Empty;
        [Key(1)] public string ParentSchema { get; set; } = string.Empty;
        [Key(2)] public string ParentTable { get; set; } = string.Empty;
        [Key(3)] public string[] ParentColumns { get; set; } = System.Array.Empty<string>();
        [Key(4)] public string ReferencedSchema { get; set; } = string.Empty;
        [Key(5)] public string ReferencedTable { get; set; } = string.Empty;
        [Key(6)] public string[] ReferencedColumns { get; set; } = System.Array.Empty<string>();
    }
}
