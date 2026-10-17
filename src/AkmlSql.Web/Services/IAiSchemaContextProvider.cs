using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Schema;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 028 (M6) task T007. Resolves the schema text an AI prompt needs for the active
/// database, filtered by the feature's active privacy <b>disclosure</b> mode (US1).
///
/// <para>
/// Schema comes entirely from the M5 IndexedDB cache (<see cref="ISchemaCacheStore"/>) — no
/// engine round-trip (PRD §4.1). The cached <see cref="SchemaSnapshot"/> holds one-way
/// <c>SchemaPhasePayload</c> bytes; <see cref="SchemaPhaseRehydrator"/> reconstructs a
/// <c>DatabaseCache</c> so the canonical <see cref="SchemaContextBuilder"/> can run, exactly
/// as the engine does. "No schema" returns the empty string on every path (FR-007).
/// </para>
/// </summary>
public interface IAiSchemaContextProvider
{
    /// <summary>
    /// Build the schema text for <paramref name="featureId"/> per its active privacy mode.
    /// Returns the empty string for "no schema" mode, when no schema is cached, or when the
    /// snapshot cannot be read. <paramref name="promptOrSql"/> drives relevance filtering for
    /// the full-schema modes; the names-only mode lists all object names (bounded by the
    /// object cap), since the privacy intent is "names, nothing else".
    /// </summary>
    Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct);
}

internal sealed class AiSchemaContextProvider : IAiSchemaContextProvider
{
    private readonly ISchemaCacheStore _cache;
    private readonly IAiFeatureSettings _settings;
    private readonly ISqlConnectionService _connection;

    public AiSchemaContextProvider(ISchemaCacheStore cache, IAiFeatureSettings settings, ISqlConnectionService connection)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>The database a prompt is about: the cache key SchemaSync stores it under, and its kind.</summary>
    private readonly record struct Target(string Server, string Database, AkmlSql.Core.DatabaseKind Kind);

    private Target? ActiveTarget()
    {
        if (!_connection.IsConnected) return null;
        var server = _connection.SchemaIdentity ?? _connection.Server;
        var database = _connection.Database;
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database)) return null;
        return new Target(server!, database!, _connection.Kind);
    }

    public async Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct)
    {
        // Review WEB-07: the schema of exactly the database the editor is connected to, captured before anything is awaited —
        // never "the most recently used snapshot", which could be another database, another server or the other kind (another
        // tab touches it; a newly selected database has no snapshot yet). If the connection moves while this runs, nothing
        // is sent rather than the old database's schema under the new dialect.
        var target = ActiveTarget();
        var mode = await _settings.ResolveModeAsync(featureId).ConfigureAwait(false);

        // FR-007: the no-schema guarantee holds unconditionally — return before touching the cache.
        if (mode == AiPrivacyMode.NoSchema)
        {
            return string.Empty;
        }

        if (target is not { } t) return string.Empty;   // not connected: there is no active database to describe
        var active = await _cache.GetAsync(t.Server, t.Database).ConfigureAwait(false);
        if (active == null || ActiveTarget() != t)
        {
            return string.Empty; // no snapshot of THIS database yet, or the connection moved: degrade to none, never throw.
        }

        var cache = SchemaPhaseRehydrator.Rehydrate(
            active.CompositeKey,
            Deserialize(active.PhaseA),
            Deserialize(active.PhaseB));

        // Bound the context to the provider budget. Ghost text uses a tight cap for latency.
        var maxObjects = string.Equals(featureId, "ghosttext", StringComparison.Ordinal) ? 50 : 500;

        // "Schema names only": table + column names, no data types, no FKs (FR-003). The shared
        // SchemaContextFormatter has no names-without-types level, so emit a minimal names-only
        // view directly from the rehydrated cache (no fork of the relevance/FK logic — names only).
        if (mode == AiPrivacyMode.SchemaNamesOnly)
        {
            return BuildNamesOnly(cache, maxObjects);
        }

        // FullSchema and FullyLocal: full disclosure via the canonical builder (level 4).
        var builder = new SchemaContextBuilder((_, _) => cache);
        var context = await builder.BuildAsync(
            sessionId: "web",
            sessionLookup: _ => (active.ServerCanonicalIdentity, active.DatabaseName),
            prompt: promptOrSql,
            compressionLevel: 4,
            maxObjects: maxObjects).ConfigureAwait(false);

        return ActiveTarget() == t ? SchemaContextFormatter.Format(context) : string.Empty;
    }

    private static SchemaPhasePayload? Deserialize(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try { return MessagePackSerializer.Deserialize<SchemaPhasePayload>(bytes); }
        catch (MessagePackSerializationException) { return null; }
    }

    private static string BuildNamesOnly(DatabaseCache cache, int maxObjects)
    {
        var objects = cache.GetAllObjects().Take(maxObjects).ToList();
        if (objects.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Database schema (names only):");
        foreach (var obj in objects)
        {
            if (obj.Columns.Count > 0)
            {
                sb.Append("- ").Append(obj.SchemaName).Append('.').Append(obj.ObjectName)
                  .Append(" (").Append(string.Join(", ", obj.Columns.Select(c => c.ColumnName))).AppendLine(")");
            }
            else
            {
                sb.Append("- ").Append(obj.SchemaName).Append('.').AppendLine(obj.ObjectName);
            }
        }
        return sb.ToString();
    }
}
