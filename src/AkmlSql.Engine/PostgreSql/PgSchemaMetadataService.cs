using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Index = AkmlSql.Engine.Schema.Models.Index;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T079, FR-030 to FR-046, research R12, contracts/catalog-queries.md) - fills a <see cref="DatabaseCache"/> from
/// PostgreSQL's catalogs in the same two phases as SQL Server: Phase A names the schemas and objects fast enough for the first
/// completion; Phase B adds the columns, keys, foreign keys, indexes and parameters in the background. The rules the queries
/// follow are the engine's own: no ORDER BY (the cache sorts), direct joins to <c>pg_namespace</c> (no per-row
/// <c>::regclass</c> or <c>obj_description</c> calls), one round trip per step.
///
/// <para>
/// What PostgreSQL adds: a schema the connected role cannot use is listed and marked
/// (<see cref="DatabaseCache.InaccessibleSchemas"/>) instead of failing the load; the system schemas <c>pg_catalog</c> and
/// <c>information_schema</c> are loaded for their relations only (the reference carries the functions) and recorded as
/// <see cref="DatabaseCache.HiddenSchemas"/>, so names resolve and the explorer can show them on request; the search path is
/// captured for unqualified completion.
/// </para>
/// </summary>
public sealed class PgSchemaMetadataService : ISchemaMetadataProvider
{
    public DatabaseKind Kind => DatabaseKind.PostgreSql;

    private static readonly string[] SystemSchemas = { "pg_catalog", "information_schema" };

    // Every schema that is not toast storage or a session's temporary schema. System schemas are in; they are marked hidden.
    private const string SchemasSql =
        "SELECT n.oid, n.nspname, pg_catalog.has_schema_privilege(n.oid, 'USAGE') " +
        "FROM pg_catalog.pg_namespace n " +
        "WHERE n.nspname NOT LIKE 'pg\\_toast%' AND n.nspname NOT LIKE 'pg\\_temp\\_%' " +
        "AND (n.nspname NOT LIKE 'pg\\_%' OR n.nspname = 'pg_catalog')";

    // Relations in the schemas the role can use. Descriptions come from a join on pg_description, not a per-row function call.
    private const string RelationsSql =
        "SELECT c.oid, n.nspname, c.relname, c.relkind::text, c.reltuples::bigint, d.description " +
        "FROM pg_catalog.pg_class c " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
        "LEFT JOIN pg_catalog.pg_description d ON d.objoid = c.oid AND d.classoid = 'pg_catalog.pg_class'::regclass AND d.objsubid = 0 " +
        "WHERE c.relkind IN ('r','p','f','v','m','S') AND n.nspname = ANY($1)";

    // Functions and procedures of the user's schemas only: pg_catalog's several thousand functions are the reference's business.
    private const string RoutinesSql =
        "SELECT p.oid, n.nspname, p.proname, p.prokind::text, p.proretset, d.description " +
        "FROM pg_catalog.pg_proc p " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace " +
        "LEFT JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_proc'::regclass AND d.objsubid = 0 " +
        "WHERE n.nspname = ANY($1)";

    private const string ColumnsSql =
        "SELECT a.attrelid, a.attnum, a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod), a.atttypmod, a.attnotnull, " +
        "a.attidentity::text, a.attgenerated::text, pg_catalog.pg_get_expr(def.adbin, def.adrelid), ds.description " +
        "FROM pg_catalog.pg_attribute a " +
        "JOIN pg_catalog.pg_class c ON c.oid = a.attrelid " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
        "LEFT JOIN pg_catalog.pg_attrdef def ON def.adrelid = a.attrelid AND def.adnum = a.attnum " +
        "LEFT JOIN pg_catalog.pg_description ds ON ds.objoid = a.attrelid AND ds.classoid = 'pg_catalog.pg_class'::regclass AND ds.objsubid = a.attnum " +
        "WHERE a.attnum > 0 AND NOT a.attisdropped AND c.relkind IN ('r','p','f','v','m') AND n.nspname = ANY($1)";

    private const string IndexesSql =
        "SELECT i.indrelid, ic.relname, i.indisprimary, i.indisunique, i.indkey::int2[], i.indoption::int2[] " +
        "FROM pg_catalog.pg_index i " +
        "JOIN pg_catalog.pg_class ic ON ic.oid = i.indexrelid " +
        "JOIN pg_catalog.pg_class tc ON tc.oid = i.indrelid " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = tc.relnamespace " +
        "WHERE i.indisvalid AND n.nspname = ANY($1)";

    private const string ForeignKeysSql =
        "SELECT con.conname, con.conrelid, con.confrelid, con.conkey, con.confkey, con.confdeltype::text, con.confupdtype::text " +
        "FROM pg_catalog.pg_constraint con " +
        "JOIN pg_catalog.pg_class c ON c.oid = con.conrelid " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE con.contype = 'f' AND n.nspname = ANY($1)";

    private const string ParametersSql =
        "SELECT p.oid, p.proargnames, p.proargtypes::oid[], p.proallargtypes, p.proargmodes::text[], p.pronargdefaults::int " +
        "FROM pg_catalog.pg_proc p " +
        "JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace " +
        "WHERE n.nspname = ANY($1)";

    private const string TypeNamesSql =
        "SELECT t.oid, pg_catalog.format_type(t.oid, NULL) FROM pg_catalog.pg_type t WHERE t.oid = ANY($1)";

    private const string DatabasesSql =
        "SELECT datname FROM pg_catalog.pg_database WHERE datallowconn AND NOT datistemplate AND pg_catalog.has_database_privilege(datname, 'CONNECT')";

    // ── The databases of the server (FR-030) ────────────────────────────────────────────────────

    public async Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct)
    {
        var names = new List<string>();
        try
        {
            await using var connection = PgConnectionFactory.Create(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(DatabasesSql, connection) { CommandTimeout = 10 };
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) names.Add(reader.GetString(0));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The completion path uses this list best-effort; the database list the dialog shows asks with its own handler,
            // which reports the failure.
            Log.Warning("PostgreSQL ListDatabases failed ({Type}) - {ConnDesc}", ex.GetType().Name, PgConnectionString.Describe(connectionString));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// The same list, but a failure to connect or to read throws instead of coming back as an empty list: the connection dialog
    /// must tell "no databases" from "wrong password" (the completion path above prefers an empty list).
    /// </summary>
    public async Task<List<string>> ListDatabasesStrictAsync(string connectionString, CancellationToken ct)
    {
        var names = new List<string>();
        await using var connection = PgConnectionFactory.Create(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(DatabasesSql, connection) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) names.Add(reader.GetString(0));
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // ── Phase A ─────────────────────────────────────────────────────────────────────────────────

    public async Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
    {
        var connDesc = PgConnectionString.Describe(connectionString);
        var started = DateTime.UtcNow;
        Log.Debug("PostgreSQL Phase A starting for {Key}: {ConnDesc}", cache.CacheKey, connDesc);

        try
        {
            await using var connection = PgConnectionFactory.Create(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var facts = await PgConnectionFactory.ReadFactsAsync(connection, ct).ConfigureAwait(false);
            cache.SearchPath = facts.SearchPath.ToList();

            // Schemas first: which exist, which this role can use, which are the system ones.
            var usable = new List<string>();
            cache.InaccessibleSchemas = new List<string>();
            cache.HiddenSchemas = new List<string>();
            await using (var command = new NpgsqlCommand(SchemasSql, connection) { CommandTimeout = 10 })
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var name = reader.GetString(1);
                    var canUse = reader.GetBoolean(2);
                    cache.Schemas.GetOrAdd(name, _ => new SchemaEntry { SchemaName = name });
                    if (Array.IndexOf(SystemSchemas, name) >= 0) cache.HiddenSchemas.Add(name);
                    if (canUse) usable.Add(name);
                    else cache.InaccessibleSchemas.Add(name);
                }
            }

            var userSchemas = usable.Where(s => Array.IndexOf(SystemSchemas, s) < 0).ToArray();

            await LoadRelationsAsync(connection, cache, usable.ToArray(), ct).ConfigureAwait(false);
            await LoadRoutinesAsync(connection, cache, userSchemas, ct).ConfigureAwait(false);

            cache.Phase = PopulationPhase.PhaseA;
            cache.LastFullRefresh = DateTime.UtcNow;
            Log.Information("PostgreSQL Phase A complete for {Key}: {Count} objects ({Elapsed:F0} ms, {ConnDesc})",
                cache.CacheKey, cache.GetAllObjects().Count(), (DateTime.UtcNow - started).TotalMilliseconds, connDesc);
        }
        catch (PostgresException ex) when (IsAccessFailure(ex))
        {
            // The role cannot open this database (wrong password, no CONNECT, unknown database). Retrying changes nothing
            // until the user connects as someone else, which arrives as a new ConnectionChanged and a new cache.
            cache.PermissionDenied = true;
            Log.Warning("PostgreSQL Phase A skipped for {Key}: access refused (SQLSTATE {SqlState}). {ConnDesc}",
                cache.CacheKey, ex.SqlState, connDesc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("PostgreSQL Phase A failed for {Key}: {Reason}. {ConnDesc}", cache.CacheKey, Brief(ex), connDesc);
            cache.IsStale = true;
        }
    }

    private static async Task LoadRelationsAsync(NpgsqlConnection connection, DatabaseCache cache, string[] schemas, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(RelationsSql, connection) { CommandTimeout = 15 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(1);
            var entry = cache.Schemas.GetOrAdd(schema, _ => new SchemaEntry { SchemaName = schema });
            entry.Objects.Add(new DatabaseObject
            {
                ObjectId = IdOf(reader.GetFieldValue<uint>(0)),
                SchemaName = schema,
                ObjectName = reader.GetString(2),
                ObjectType = RelationType(reader.GetString(3)),
                // reltuples is -1 for a table never analysed (PostgreSQL 14 and later); there is no estimate then.
                ApproxRowCount = Math.Max(0, reader.GetInt64(4)),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
    }

    private static async Task LoadRoutinesAsync(NpgsqlConnection connection, DatabaseCache cache, string[] schemas, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(RoutinesSql, connection) { CommandTimeout = 15 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(1);
            var entry = cache.Schemas.GetOrAdd(schema, _ => new SchemaEntry { SchemaName = schema });
            var kind = reader.GetString(3);
            entry.Objects.Add(new DatabaseObject
            {
                ObjectId = IdOf(reader.GetFieldValue<uint>(0)),
                SchemaName = schema,
                ObjectName = reader.GetString(2),
                // 'f' function, 'a' aggregate, 'w' window, 'p' procedure. A function that returns a set is a table function.
                ObjectType = kind == "p" ? DbObjectType.Procedure : reader.GetBoolean(4) ? DbObjectType.TableFunction : DbObjectType.ScalarFunction,
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
    }

    // ── Phase B ─────────────────────────────────────────────────────────────────────────────────

    public async Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
    {
        if (cache.Phase < PopulationPhase.PhaseA)
        {
            Log.Warning("PostgreSQL Phase B skipped for {Key}: Phase A has not completed", cache.CacheKey);
            return;
        }

        var connDesc = PgConnectionString.Describe(connectionString);
        var started = DateTime.UtcNow;
        try
        {
            await using var connection = PgConnectionFactory.Create(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var inaccessible = new HashSet<string>(cache.InaccessibleSchemas, StringComparer.OrdinalIgnoreCase);
            var allSchemas = cache.GetSchemaNames().Where(s => !inaccessible.Contains(s)).ToArray();
            var userSchemas = allSchemas.Where(s => Array.IndexOf(SystemSchemas, s) < 0).ToArray();
            var objects = new Dictionary<int, DatabaseObject>();
            foreach (var o in cache.GetAllObjects()) objects[o.ObjectId] = o;

            var columnsByNumber = await LoadColumnsAsync(connection, objects, allSchemas, ct).ConfigureAwait(false);
            await LoadIndexesAsync(connection, objects, columnsByNumber, userSchemas, ct).ConfigureAwait(false);
            await LoadForeignKeysAsync(connection, cache, objects, columnsByNumber, userSchemas, ct).ConfigureAwait(false);
            cache.RebuildFkIndex();
            await LoadParametersAsync(connection, objects, userSchemas, ct).ConfigureAwait(false);

            foreach (var o in objects.Values.Where(o => o.ObjectType is not (DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction)))
                o.ColumnsLoaded = true;
            foreach (var o in objects.Values.Where(o => o.ObjectType is DbObjectType.TableFunction)) o.ColumnsLoaded = true;

            cache.Phase = PopulationPhase.PhaseB;
            Log.Information("PostgreSQL Phase B complete for {Key} ({Elapsed:F0} ms, {ConnDesc})",
                cache.CacheKey, (DateTime.UtcNow - started).TotalMilliseconds, connDesc);
        }
        catch (PostgresException ex) when (IsAccessFailure(ex))
        {
            cache.PermissionDenied = true;
            Log.Warning("PostgreSQL Phase B skipped for {Key}: access refused (SQLSTATE {SqlState}). {ConnDesc}", cache.CacheKey, ex.SqlState, connDesc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("PostgreSQL Phase B failed for {Key}: {Reason}. {ConnDesc}", cache.CacheKey, Brief(ex), connDesc);
        }
    }

    private static async Task<Dictionary<(int Relation, short Number), Column>> LoadColumnsAsync(
        NpgsqlConnection connection, Dictionary<int, DatabaseObject> objects, string[] schemas, CancellationToken ct)
    {
        var byNumber = new Dictionary<(int, short), Column>();
        await using var command = new NpgsqlCommand(ColumnsSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var relation = IdOf(reader.GetFieldValue<uint>(0));
            if (!objects.TryGetValue(relation, out var owner)) continue;

            var number = reader.GetInt16(1);
            var typeName = reader.GetString(3);
            var typmod = reader.GetInt32(4);
            var identity = reader.GetString(6);
            var generated = reader.GetString(7);
            var expression = reader.IsDBNull(8) ? null : reader.GetString(8);
            var (length, precision, scale) = TypeDetails(typeName, typmod);

            var column = new Column
            {
                ColumnId = number,
                ColumnName = reader.GetString(2),
                TypeName = typeName,
                MaxLength = length,
                Precision = precision,
                Scale = scale,
                IsNullable = !reader.GetBoolean(5),
                IsIdentity = identity.Length > 0,
                IsComputed = generated.Length > 0,
                ComputedDefinition = generated.Length > 0 ? expression : null,
                DefaultValue = generated.Length > 0 ? null : expression,
                Description = reader.IsDBNull(9) ? null : reader.GetString(9),
            };
            owner.Columns.Add(column);
            byNumber[(relation, number)] = column;
        }

        foreach (var owner in objects.Values) owner.Columns.Sort((a, b) => a.ColumnId.CompareTo(b.ColumnId));
        return byNumber;
    }

    private static async Task LoadIndexesAsync(NpgsqlConnection connection, Dictionary<int, DatabaseObject> objects,
        Dictionary<(int Relation, short Number), Column> columns, string[] schemas, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(IndexesSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var relation = IdOf(reader.GetFieldValue<uint>(0));
            if (!objects.TryGetValue(relation, out var owner)) continue;

            var isPrimary = reader.GetBoolean(2);
            var numbers = reader.GetFieldValue<short[]>(4);
            var options = reader.IsDBNull(5) ? Array.Empty<short>() : reader.GetFieldValue<short[]>(5);

            var index = new Index
            {
                IndexName = reader.GetString(1),
                // Every PostgreSQL index is a separate structure; none orders the table's own rows the way a clustered index does.
                IndexType = IndexType.Nonclustered,
                IsPrimaryKey = isPrimary,
                IsUnique = reader.GetBoolean(3),
            };
            for (var position = 0; position < numbers.Length; position++)
            {
                // 0 is an expression, not a column: it has no name to list.
                if (numbers[position] == 0 || !columns.TryGetValue((relation, numbers[position]), out var column)) continue;
                index.Columns.Add(new IndexColumn { ColumnName = column.ColumnName, IsDescending = position < options.Length && (options[position] & 1) != 0 });
                if (isPrimary) column.IsPrimaryKey = true;
            }
            owner.Indexes.Add(index);
        }
    }

    private static async Task LoadForeignKeysAsync(NpgsqlConnection connection, DatabaseCache cache, Dictionary<int, DatabaseObject> objects,
        Dictionary<(int Relation, short Number), Column> columns, string[] schemas, CancellationToken ct)
    {
        cache.ForeignKeys = new List<ForeignKey>();
        await using var command = new NpgsqlCommand(ForeignKeysSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var parentId = IdOf(reader.GetFieldValue<uint>(1));
            var referencedId = IdOf(reader.GetFieldValue<uint>(2));
            // A table in a schema the role cannot use is not in the cache: the key is not shown rather than half shown.
            if (!objects.TryGetValue(parentId, out var parent) || !objects.TryGetValue(referencedId, out var referenced)) continue;

            var parentNumbers = reader.GetFieldValue<short[]>(3);
            var referencedNumbers = reader.GetFieldValue<short[]>(4);
            cache.ForeignKeys.Add(new ForeignKey
            {
                FkName = reader.GetString(0),
                ParentSchema = parent.SchemaName,
                ParentTable = parent.ObjectName,
                ParentColumns = parentNumbers.Select(n => columns.TryGetValue((parentId, n), out var c) ? c.ColumnName : string.Empty).ToList(),
                ReferencedSchema = referenced.SchemaName,
                ReferencedTable = referenced.ObjectName,
                ReferencedColumns = referencedNumbers.Select(n => columns.TryGetValue((referencedId, n), out var c) ? c.ColumnName : string.Empty).ToList(),
                DeleteAction = Action(reader.GetString(5)),
                UpdateAction = Action(reader.GetString(6)),
            });
        }
    }

    private static async Task LoadParametersAsync(NpgsqlConnection connection, Dictionary<int, DatabaseObject> objects, string[] schemas, CancellationToken ct)
    {
        var routines = new List<(DatabaseObject Owner, string?[]? Names, uint[] Types, string[]? Modes, int Defaults)>();
        var typeIds = new HashSet<uint>();

        await using (var command = new NpgsqlCommand(ParametersSql, connection) { CommandTimeout = 30 })
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = schemas });
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!objects.TryGetValue(IdOf(reader.GetFieldValue<uint>(0)), out var owner)) continue;
                var names = reader.IsDBNull(1) ? null : reader.GetFieldValue<string?[]>(1);
                var inTypes = reader.IsDBNull(2) ? Array.Empty<uint>() : reader.GetFieldValue<uint[]>(2);
                var allTypes = reader.IsDBNull(3) ? null : reader.GetFieldValue<uint[]>(3);
                var modes = reader.IsDBNull(4) ? null : reader.GetFieldValue<string[]>(4);
                var types = allTypes ?? inTypes;
                foreach (var t in types) typeIds.Add(t);
                routines.Add((owner, names, types, modes, reader.GetInt32(5)));
            }
        }

        var typeNames = await LoadTypeNamesAsync(connection, typeIds, ct).ConfigureAwait(false);

        foreach (var (owner, names, types, modes, defaults) in routines)
        {
            // The mode of each argument: i (in), o (out), b (inout), v (variadic), t (a column of the table the function returns).
            // Without proargmodes every argument is an input. Defaults belong to the last input arguments.
            var inputs = Enumerable.Range(0, types.Length).Where(i => ModeOf(modes, i) is 'i' or 'b' or 'v').ToList();
            var withDefault = new HashSet<int>(inputs.Skip(Math.Max(0, inputs.Count - defaults)));
            var ordinal = 0;
            for (var i = 0; i < types.Length; i++)
            {
                var mode = ModeOf(modes, i);
                var name = names != null && i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i]! : "$" + (i + 1);
                var typeName = typeNames.TryGetValue(types[i], out var t) ? t : "unknown";

                if (mode == 't')
                {
                    // A column of the table a function returns: it describes the result, not a way to call it.
                    owner.Columns.Add(new Column { ColumnId = owner.Columns.Count + 1, ColumnName = name, TypeName = typeName, IsNullable = true });
                    continue;
                }

                var (length, precision, scale) = TypeDetails(typeName, -1);
                owner.Parameters.Add(new Parameter
                {
                    ParameterId = ++ordinal,
                    ParameterName = name,
                    TypeName = typeName,
                    MaxLength = length,
                    Precision = precision,
                    Scale = scale,
                    IsOutput = mode is 'o' or 'b',
                    HasDefault = withDefault.Contains(i),
                });
            }
        }
    }

    private static async Task<Dictionary<uint, string>> LoadTypeNamesAsync(NpgsqlConnection connection, HashSet<uint> ids, CancellationToken ct)
    {
        var names = new Dictionary<uint, string>();
        if (ids.Count == 0) return names;

        await using var command = new NpgsqlCommand(TypeNamesSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Oid, Value = ids.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) names[reader.GetFieldValue<uint>(0)] = reader.GetString(1);
        return names;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    // What went wrong, without anything the statement carried: the SQLSTATE and the server's message for a server error (never its
    // DETAIL, which can echo data), the type and message for anything else (a driver message holds no password).
    private static string Brief(Exception ex) =>
        ex is PostgresException pg ? $"SQLSTATE {pg.SqlState}: {pg.MessageText}" : $"{ex.GetType().Name}: {ex.Message}";

    // OIDs are unsigned 32-bit; the cache's object ids are ints. The cast keeps every bit, so ids stay unique within a database.
    private static int IdOf(uint oid) => unchecked((int)oid);

    private static char ModeOf(string[]? modes, int index) =>
        modes != null && index < modes.Length && modes[index].Length > 0 ? modes[index][0] : 'i';

    private static DbObjectType RelationType(string relkind) => relkind switch
    {
        "v" => DbObjectType.View,
        "m" => DbObjectType.MaterializedView,
        "S" => DbObjectType.Sequence,
        _ => DbObjectType.Table,   // r, p (partitioned) and f (foreign)
    };

    private static FkAction Action(string code) => code switch
    {
        "c" => FkAction.Cascade,
        "n" => FkAction.SetNull,
        "d" => FkAction.SetDefault,
        _ => FkAction.NoAction,    // a (no action) and r (restrict) both refuse the change
    };

    /// <summary>
    /// Length, precision and scale from the declared type text and its <c>atttypmod</c>: <c>character varying(50)</c> has a
    /// typmod of 54 (length + 4), <c>numeric(10,2)</c> packs precision and scale above the same offset. Anything else is 0.
    /// </summary>
    public static (int Length, int Precision, int Scale) TypeDetails(string typeName, int typmod)
    {
        if (typmod < 4) return (0, 0, 0);

        if (typeName.StartsWith("character varying(", StringComparison.Ordinal) || typeName.StartsWith("character(", StringComparison.Ordinal))
            return (typmod - 4, 0, 0);
        if (typeName.StartsWith("numeric(", StringComparison.Ordinal))
            return (0, ((typmod - 4) >> 16) & 0xFFFF, (typmod - 4) & 0xFFFF);
        if (typeName.StartsWith("bit(", StringComparison.Ordinal) || typeName.StartsWith("bit varying(", StringComparison.Ordinal))
            return (typmod, 0, 0);
        return (0, 0, 0);
    }

    // The role was refused: bad password, no CONNECT privilege, or no such database. Not worth another attempt.
    private static bool IsAccessFailure(PostgresException ex) =>
        ex.SqlState is "28P01" or "28000" or "3D000" or "42501";
}
