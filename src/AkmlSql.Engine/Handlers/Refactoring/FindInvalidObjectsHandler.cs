using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Handlers.Refactoring;

/// <summary>
/// Spec 030 / T058 / US5 (FR-019, R8) — replaces <c>FindInvalidObjectsHandlerStub</c>.
/// Scans the target database for objects whose definitions reference an entity that can no
/// longer be resolved (a dropped table, renamed column, missing synonym target, etc.) by
/// querying <c>sys.sql_expression_dependencies</c>: a row with <c>referenced_id IS NULL</c>
/// names a referenced entity SQL Server could not bind, which is the classic "invalid object"
/// signal Redgate SQL Prompt's Find Invalid Objects surfaces.
/// <para>
/// The raw <c>referenced_id IS NULL</c> set is noisy, so the SQL only does the cheap reduction
/// (join + <c>is_ms_shipped = 0</c>) and the testable discrimination lives in
/// <see cref="MapInvalidObjects"/>: cross-database, linked-server, temp-table, ambiguous, and
/// nameless references are NOT broken local objects and are dropped there. Keeping that logic in
/// a pure sync mapper lets the unit tests cover it without a live SQL Server.
/// </para>
/// <para>
/// Registered (raw) by <c>EngineHandlerRegistry</c> via <c>RpcRouter.RegisterRaw</c> with the
/// shared <c>lookupSession</c> closure, mirroring <c>NavigationRequestHandler</c>. The handler
/// returns a single response with <see cref="FindInvalidObjectsResponse.IsFinalChunk"/> set —
/// the one-shot <c>HandleAsync</c> contract cannot push incremental frames, so chunking is a
/// shell-side display concern, not an engine push.
/// </para>
/// </summary>
internal sealed class FindInvalidObjectsHandler
{
    /// <summary>
    /// Hard cap on emitted records. This is a frame-size safety valve (the 16 MB IPC frame
    /// limit), NOT the request's <c>ChunkSize</c> — capping at ChunkSize would silently drop
    /// invalid objects. In practice a database rarely has thousands of broken references.
    /// </summary>
    private const int MaxRecords = 10_000;

    /// <summary>
    /// One row projected from <c>sys.sql_expression_dependencies</c> joined to
    /// <c>sys.objects</c>/<c>sys.schemas</c>. The handler fills these from the live reader; the
    /// pure mapper consumes them, so tests can feed rows directly with no DB.
    /// </summary>
    internal readonly record struct DependencyRow(
        string Schema,
        string Name,
        string TypeCode,
        string? ReferencedServer,
        string? ReferencedDatabase,
        string? ReferencedSchema,
        string? ReferencedEntity,
        bool IsAmbiguous,
        bool IsCallerDependent = false);

    public async Task<RpcMessage?> HandleAsync(
        RpcMessage request,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        try
        {
            if (request.Payload == null)
            {
                return Error("Payload required", request.RequestId);
            }

            var req = MessagePackSerializer.Deserialize<FindInvalidObjectsRequest>(request.Payload);
            var (connectionString, sessionDatabase) = sessionLookup(req.SessionId);

            if (string.IsNullOrEmpty(connectionString))
            {
                return Error("No active database connection for this session", request.RequestId);
            }

            // The request names the database to scan; sys.sql_expression_dependencies is
            // database-scoped, so switch to it. Fall back to the session's database when the
            // request omits one.
            var targetDatabase = !string.IsNullOrEmpty(req.DatabaseName)
                ? req.DatabaseName
                : sessionDatabase;

            var rows = await ReadDependencyRowsAsync(connectionString, targetDatabase, ct);
            var records = MapInvalidObjects(rows, DateTime.UtcNow);

            Log.Debug("FindInvalidObjects: scanned {Scanned} dependency rows, {Invalid} invalid in {Db}",
                rows.Count, records.Length, targetDatabase ?? "(session db)");

            return Response(request.RequestId, new FindInvalidObjectsResponse
            {
                Status = 0,
                Records = records,
                IsFinalChunk = true,
                TotalScanned = rows.Count,
            });
        }
        catch (SqlException ex) when (IsPermissionDenied(ex))
        {
            Log.Warning(ex, "FindInvalidObjects: permission denied");
            return Response(request.RequestId, new FindInvalidObjectsResponse
            {
                Status = 1, // PermissionDenied
                IsFinalChunk = true,
                ErrorMessage = "Permission denied. VIEW DEFINITION on the database is required to scan for invalid objects.",
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FindInvalidObjects failed");
            return Error($"Find Invalid Objects failed: {ex.Message}", request.RequestId);
        }
    }

    /// <summary>
    /// Opens a connection (switching to <paramref name="targetDatabase"/> when supplied) and
    /// projects the candidate dependency rows. SQL does only the cheap reduction; the
    /// validity discrimination happens in <see cref="MapInvalidObjects"/>.
    /// </summary>
    private static async Task<IReadOnlyList<DependencyRow>> ReadDependencyRowsAsync(
        string connectionString, string? targetDatabase, CancellationToken ct)
    {
        var rows = new List<DependencyRow>();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        if (!string.IsNullOrEmpty(targetDatabase) &&
            !string.Equals(conn.Database, targetDatabase, StringComparison.OrdinalIgnoreCase))
        {
            // ChangeDatabase is parameter-safe (no T-SQL injection surface, unlike USE [...]).
            conn.ChangeDatabase(targetDatabase);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                s.name                       AS schema_name,
                o.name                       AS object_name,
                o.type                       AS type_code,
                d.referenced_server_name     AS referenced_server,
                d.referenced_database_name   AS referenced_database,
                d.referenced_schema_name     AS referenced_schema,
                d.referenced_entity_name     AS referenced_entity,
                d.is_ambiguous               AS is_ambiguous,
                d.is_caller_dependent        AS is_caller_dependent
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects o ON o.object_id = d.referencing_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE d.referenced_id IS NULL
              AND o.is_ms_shipped = 0";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new DependencyRow(
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                TypeCode: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ReferencedServer: reader.IsDBNull(3) ? null : reader.GetString(3),
                ReferencedDatabase: reader.IsDBNull(4) ? null : reader.GetString(4),
                ReferencedSchema: reader.IsDBNull(5) ? null : reader.GetString(5),
                ReferencedEntity: reader.IsDBNull(6) ? null : reader.GetString(6),
                IsAmbiguous: !reader.IsDBNull(7) && reader.GetBoolean(7),
                IsCallerDependent: !reader.IsDBNull(8) && reader.GetBoolean(8)));
        }

        return rows;
    }

    /// <summary>
    /// Pure projection of candidate dependency rows to <see cref="InvalidObjectRecord"/>s,
    /// applying the noise-exclusion rules. A row is a genuine broken LOCAL reference only when
    /// it names a same-database, non-temp, unambiguous entity that could not be resolved.
    /// Unit-testable without a database.
    /// </summary>
    internal static InvalidObjectRecord[] MapInvalidObjects(
        IReadOnlyList<DependencyRow> rows, DateTime scannedAtUtc)
    {
        var records = new List<InvalidObjectRecord>();

        foreach (var row in rows)
        {
            if (records.Count >= MaxRecords)
            {
                break;
            }

            // Exclude references the engine cannot meaningfully call "invalid local objects":
            // referenced_id is NULL BY DESIGN for runtime-resolved (caller-dependent) refs —
            // e.g. unqualified EXEC SomeProc, or an unqualified table resolved against the
            // caller's default schema — which are perfectly valid. Without this guard, Find
            // Invalid Objects floods with false positives on any DB that has stored procedures.
            if (row.IsCallerDependent) continue;
            if (row.IsAmbiguous) continue;                                  // SQL couldn't bind a single target
            if (!string.IsNullOrEmpty(row.ReferencedServer)) continue;      // linked-server ref
            if (!string.IsNullOrEmpty(row.ReferencedDatabase)) continue;    // cross-database ref
            if (string.IsNullOrEmpty(row.ReferencedEntity)) continue;       // nothing actionable to name
            if (row.ReferencedEntity!.StartsWith('#')) continue;            // temp table / temp proc

            var missing = string.IsNullOrEmpty(row.ReferencedSchema)
                ? row.ReferencedEntity
                : $"{row.ReferencedSchema}.{row.ReferencedEntity}";

            records.Add(new InvalidObjectRecord
            {
                Schema = row.Schema,
                Name = row.Name,
                Type = MapTypeCode(row.TypeCode),
                ErrorMessage = $"References '{missing}', which cannot be resolved (invalid object reference).",
                SourceLine = null, // sys.sql_expression_dependencies carries no line info
                MissingDependency = missing,
                ScannedAtUtc = scannedAtUtc,
            });
        }

        return records.ToArray();
    }

    /// <summary>
    /// Maps a <c>sys.objects.type</c> two-char code to the friendly
    /// <see cref="InvalidObjectRecord.Type"/> enum
    /// (0 = Table, 1 = View, 2 = Procedure, 3 = Function, 4 = Trigger, 5 = Synonym).
    /// Unknown codes fall back to Table (0).
    /// </summary>
    internal static int MapTypeCode(string? typeCode)
    {
        return (typeCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "U" => 0,   // user table
            "V" => 1,   // view
            "P" or "PC" => 2,   // (CLR) stored procedure
            "FN" or "IF" or "TF" or "FS" or "FT" => 3,  // scalar / inline TVF / multi-statement TVF / CLR function
            "TR" or "TA" => 4,  // DML / CLR trigger
            "SN" => 5,  // synonym
            _ => 0,
        };
    }

    private static bool IsPermissionDenied(SqlException ex)
    {
        // 229/230 = permission denied on object/column; 262 = permission denied (statement-level,
        // e.g. VIEW DEFINITION / metadata visibility). These surface as PermissionDenied to the shell.
        foreach (SqlError error in ex.Errors)
        {
            if (error.Number is 229 or 230 or 262)
            {
                return true;
            }
        }

        return false;
    }

    private static RpcMessage Response(int requestId, FindInvalidObjectsResponse response)
        => RpcResponseFactory.CreateResponse(MessageTypes.FindInvalidObjectsResult, requestId, response);

    private static RpcMessage Error(string message, int requestId)
        => Response(requestId, new FindInvalidObjectsResponse
        {
            Status = 2, // Error
            IsFinalChunk = true,
            ErrorMessage = message,
        });
}
