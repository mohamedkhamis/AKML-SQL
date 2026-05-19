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
    /// <c>PipeRpcServer</c>. Lifted out so the named-pipe transport file stays focused
    /// on lifecycle + dispatch.
    ///
    /// Behaviour preserved bit-for-bit:
    ///   * Fire-and-forget (no response sent).
    ///   * Guards against racing an in-flight ConnectionChanged populate -- if Phase A
    ///     or Phase B is already running, marks the cache stale and returns instead of
    ///     clearing concurrent-dictionary entries under an active writer.
    ///   * PhaseB and Complete are both safe-to-clear states.
    /// </summary>
    public sealed class SchemaRefreshService
    {
        private readonly SessionManager _sessionManager;
        private readonly SchemaCacheManager _schemaCacheManager;
        private readonly SchemaMetadataService _schemaMetadataService;

        public SchemaRefreshService(
            SessionManager sessionManager,
            SchemaCacheManager schemaCacheManager,
            SchemaMetadataService schemaMetadataService)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _schemaCacheManager = schemaCacheManager ?? throw new ArgumentNullException(nameof(schemaCacheManager));
            _schemaMetadataService = schemaMetadataService ?? throw new ArgumentNullException(nameof(schemaMetadataService));
        }

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

            var cache = _schemaCacheManager.GetCache(req.SessionId, session.DatabaseName);
            if (cache != null && cache.Phase == PopulationPhase.PhaseA)
            {
                // PhaseA means "Phase A complete, Phase B may or may not still be running".
                // We can't safely Clear() while Phase B is mid-flight (race with GetOrAdd in
                // the background task). Mark stale and bail; the in-flight populate will
                // pick up the staleness when it finishes, or the user can retry once loading
                // settles.
                //
                // PhaseB and Complete BOTH mean "fully loaded, no background populate running"
                // -- those are safe to clear and re-run. (Earlier code rejected PhaseB too,
                // which made Ctrl+Shift+D a no-op once the cache was loaded.)
                cache.IsStale = true;
                Log.Information(
                    "SchemaRefreshRequest: populate already in progress for session={Session} db={Db} (phase={Phase}) -- marked stale, skipping concurrent reset",
                    req.SessionId, session.DatabaseName, cache.Phase);
                return;
            }

            int priorCount = 0;
            if (cache != null)
            {
                priorCount = cache.Schemas.Values.Sum(s => s.Objects.Count);

                // PopulatePhaseAAsync uses GetOrAdd + List.Add, so skipping the clear would
                // duplicate every object on a second invocation. Phase B rebuilds the FK list
                // and index, so clear that too.
                cache.Schemas.Clear();
                cache.ForeignKeys.Clear();
                cache.Phase = PopulationPhase.NotLoaded;
                cache.IsStale = false;
                Log.Information(
                    "SchemaRefreshRequest: cache cleared for session={Session} db={Db} (previously {Count} objects) -- re-running Phase A + Phase B",
                    req.SessionId, session.DatabaseName, priorCount);
            }

            var ctxSession = session;
            var ctxSessionId = req.SessionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    var c = _schemaCacheManager.GetOrCreateCache(ctxSessionId, ctxSession.DatabaseName);
                    await _schemaMetadataService.PopulatePhaseAAsync(
                        c, ctxSession.ConnectionString, CancellationToken.None);
                    _schemaCacheManager.EvictLru();
                    if (c.Phase == PopulationPhase.PhaseA)
                    {
                        await _schemaMetadataService.PopulatePhaseBAsync(
                            c, ctxSession.ConnectionString, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Manual schema refresh: background populate failed for session={Session}", ctxSessionId);
                }
            });
        }
    }
}
