using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;
using Serilog;

namespace AkmlSql.Engine.Schema
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.6 task T020 finishing. Service-class form of the
    /// schema-refresh logic that previously lived as a private method on
    /// <c>NamedPipeTransport</c>. Lifted out so the named-pipe transport file stays focused
    /// on lifecycle + dispatch.
    ///
    /// Behaviour:
    ///   * Fire-and-forget (no response sent).
    ///   * A refresh is a load of the cache like any other (the connection's own Phase A/B, a
    ///     reload after eviction), so it takes the same population claim. While another load
    ///     holds the claim the cache is marked stale and the refresh follows once that load has
    ///     ended (spec 042 FR-043: a statement that changes the catalog right after connecting
    ///     must still show), once however many refreshes arrived in the meantime.
    /// </summary>
    public sealed class SchemaRefreshService
    {
        private readonly SessionManager _sessionManager;
        private readonly SchemaCacheManager _schemaCacheManager;
        private readonly SchemaMetadataService _schemaMetadataService;
        private readonly Func<AkmlSql.Core.DatabaseKind, ISchemaMetadataProvider?>? _providerFor;

        /// <param name="providerFor">Spec 042: the catalog reader for a session's kind (null while the feature is off). Without
        /// it every session is read with <paramref name="schemaMetadataService"/>, as before.</param>
        public SchemaRefreshService(
            SessionManager sessionManager,
            SchemaCacheManager schemaCacheManager,
            SchemaMetadataService schemaMetadataService,
            Func<AkmlSql.Core.DatabaseKind, ISchemaMetadataProvider?>? providerFor = null)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));
            _schemaMetadataService = schemaMetadataService ?? throw new ArgumentNullException(nameof(schemaMetadataService));
            _providerFor = providerFor;
        }

        private ISchemaMetadataProvider? ProviderFor(AkmlSql.Core.DatabaseKind kind) =>
            _providerFor != null ? _providerFor(kind) : _schemaMetadataService;

        /// <summary>How often a refresh that found a load running looks again, and how long it keeps looking.</summary>
        internal TimeSpan SettlePollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
        internal TimeSpan SettleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        // The caches a deferred refresh is already waiting on, so a burst of DDL statements queues one refresh, not one each. Spec 043
        // follow-up (review, round 4): the cache objects, as the population claims are, not their keys - a waiter still polling a
        // cache that SchemaCacheManager.RemoveSession discarded must not swallow the refresh of the new cache under the same key.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<DatabaseCache, byte> _waiting = new(ReferenceEqualityComparer.Instance);

        public void Refresh(RefreshRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var session = !string.IsNullOrEmpty(req.SessionId)
                ? _sessionManager.GetSession(req.SessionId) : null;

            if (session == null)
            {
                Log.Warning("SchemaRefreshRequest: session='{Session}' not found -- nothing to refresh", req.SessionId);
                return;
            }
            if (string.IsNullOrEmpty(session.ConnectionString))
            {
                Log.Warning(
                    "SchemaRefreshRequest: session='{Session}' has no connection string (disconnected or SQL auth not engine-usable) -- nothing to refresh",
                    req.SessionId);
                return;
            }

            var cache = _schemaCacheManager.GetOrCreateCache(req.SessionId, session.DatabaseName);

            // A load of this cache may be running: the connection's own Phase A/B, a reload after eviction, or an earlier refresh.
            // Clearing under it would race its writes (GetOrAdd + List.Add), so a refresh takes the claim every load takes; when the
            // claim is held the cache is marked stale and the refresh follows once that load has ended. The phase alone cannot tell
            // a running load apart: a cache whose Phase B failed stays at PhaseA with nothing running, and must be refreshable.
            if (!_schemaCacheManager.TryClaimPopulation(cache))
            {
                cache.IsStale = true;
                Log.Information(
                    "SchemaRefreshRequest: a load is running for session={Session} db={Db} (phase={Phase}) -- marked stale, the refresh follows once it ends",
                    req.SessionId, session.DatabaseName, cache.Phase);
                RefreshOnceSettled(req, cache);
                return;
            }

            var provider = ProviderFor(session.Kind);
            if (provider == null)
            {
                _schemaCacheManager.ReleasePopulation(cache);
                Log.Warning("SchemaRefreshRequest: no catalog reader for kind {Kind} (session={Session})", session.Kind, req.SessionId);
                return;
            }

            // PopulatePhaseAAsync uses GetOrAdd + List.Add, so skipping the clear would duplicate every object on a second
            // invocation. Phase B rebuilds the FK list and index, so clear that too.
            var priorCount = cache.Schemas.Values.Sum(s => s.Objects.Count);
            cache.Schemas.Clear();
            cache.ForeignKeys.Clear();
            cache.Phase = PopulationPhase.NotLoaded;
            cache.IsStale = false;
            Log.Information(
                "SchemaRefreshRequest: cache cleared for session={Session} db={Db} (previously {Count} objects) -- re-running Phase A + Phase B",
                req.SessionId, session.DatabaseName, priorCount);

            var connectionString = session.ConnectionString;
            var sessionId = req.SessionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await provider.PopulatePhaseAAsync(cache, connectionString, CancellationToken.None);
                    _schemaCacheManager.EvictLru();
                    if (cache.Phase == PopulationPhase.PhaseA)
                    {
                        await provider.PopulatePhaseBAsync(cache, connectionString, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Manual schema refresh: background populate failed for session={Session}", sessionId);
                }
                finally
                {
                    _schemaCacheManager.ReleasePopulation(cache);
                }
            });
        }

        // Runs the refresh again once the load that holds the claim has ended, if it is still wanted and still the session's cache.
        private void RefreshOnceSettled(RefreshRequest req, DatabaseCache cache)
        {
            if (!_waiting.TryAdd(cache, 0)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    var deadline = DateTime.UtcNow + SettleTimeout;
                    while (_schemaCacheManager.IsPopulating(cache) && DateTime.UtcNow < deadline)
                        await Task.Delay(SettlePollInterval).ConfigureAwait(false);

                    _waiting.TryRemove(cache, out _);
                    if (_schemaCacheManager.IsPopulating(cache))
                    {
                        Log.Warning("SchemaRefreshRequest: the load of session={Session} did not end within {Timeout}; the deferred refresh is dropped",
                            req.SessionId, SettleTimeout);
                        return;
                    }
                    if (!cache.IsStale) return;                                    // reloaded by someone else meanwhile

                    // Only the cache that was marked: the session may have moved to another database, whose own load is its own business.
                    var session = _sessionManager.GetSession(req.SessionId);
                    var current = session == null ? null : _schemaCacheManager.FindCache(req.SessionId, session.DatabaseName);
                    if (!ReferenceEquals(current, cache))
                    {
                        Log.Information("SchemaRefreshRequest: session={Session} left the database whose refresh was deferred; dropped", req.SessionId);
                        return;
                    }
                    Refresh(req);
                }
                catch (Exception ex)
                {
                    _waiting.TryRemove(cache, out _);
                    Log.Error(ex, "Deferred schema refresh failed for session={Session}", req.SessionId);
                }
            });
        }
    }
}
