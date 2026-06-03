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

    public AiSchemaContextProvider(ISchemaCacheStore cache, IAiFeatureSettings settings)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct)
    {
        var mode = await _settings.ResolveModeAsync(featureId).ConfigureAwait(false);

        // FR-007: the no-schema guarantee holds unconditionally — return before touching the cache.
        if (mode == AiPrivacyMode.NoSchema)
        {
            return string.Empty;
        }

        var active = await GetActiveSnapshotAsync().ConfigureAwait(false);
        if (active == null)
        {
            return string.Empty; // no cached schema → degrade to none (edge case), never throw.
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

        return SchemaContextFormatter.Format(context);
    }

    /// <summary>
    /// The active database is the most-recently-used cached snapshot (the M5 LRU-last
    /// heuristic the spec's Assumptions adopt). This is safe in practice because
    /// <c>ISchemaSync</c> bumps the foreground (server, db) every 30 s via <c>TouchAsync</c>,
    /// keeping it LRU-last; there is no competing background sync of a non-foreground DB.
    /// Follow-up: thread the editor's explicit active (server, db) identity in and resolve via
    /// <see cref="ISchemaCacheStore.GetAsync"/> to remove the multi-DB ambiguity entirely.
    /// </summary>
    private async Task<SchemaSnapshot?> GetActiveSnapshotAsync()
    {
        var all = await _cache.ListAsync().ConfigureAwait(false); // sorted by LastUsedAt ascending
        return all.Count > 0 ? all[all.Count - 1] : null;
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
