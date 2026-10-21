using AkmlSql.Core;

namespace AkmlSql.Engine.Schema;

/// <summary>
/// Spec 042 (research R12, plan Complexity Tracking) - the catalog operations the engine calls by database kind:
/// fill a <see cref="DatabaseCache"/> in the two phases and list the databases of a server. The SQL Server
/// implementation is the existing <see cref="SchemaMetadataService"/>, unchanged; PostgreSQL has its own over
/// <c>pg_catalog</c>. Callers pick one with <see cref="RpcContext.SchemaMetadataFor"/> from the session's kind and
/// never branch on the kind themselves.
/// </summary>
public interface ISchemaMetadataProvider
{
    DatabaseKind Kind { get; }

    /// <summary>Phase A: schemas and object names (and row estimates) - fast, so the first completion is not kept waiting.</summary>
    Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct);

    /// <summary>Phase B: columns, keys, foreign keys, parameters and descriptions - slower, in the background.</summary>
    Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct);

    /// <summary>The databases the connection may use, for the connection dialog and the database selector.</summary>
    Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct);
}
