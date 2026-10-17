using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using AkmlSql.PostgreSql.Analysis;
using Serilog;

namespace AkmlSql.Engine.Handlers.Analysis
{
    /// <summary>
    /// Spec 030 T052 / FR-031 — typed handler for ListAnalysisRules (MessageType 33 → 133).
    ///
    /// Builds the catalog the shell's Manage Rules dialog renders: one row per discovered
    /// <see cref="IAnalysisRule"/> (via <see cref="RuleRegistry.AllRules"/>), enriched with display
    /// metadata from <see cref="RuleMetadataCatalog"/> and the resolved enabled/severity state from
    /// <see cref="CaSettingsLoader"/>. When the request carries a FileDirectory, per-project
    /// <c>.casettings</c> overrides are resolved upward from there so the reported state matches
    /// what analysis would actually apply for that document; otherwise global defaults are used.
    ///
    /// <para>The same <see cref="RuleRegistry"/> and <see cref="CaSettingsLoader"/> instances the
    /// <see cref="AnalysisHandler"/> uses are injected here, so the casettings cache (and its file
    /// watchers / AnalysisSettingsChanged invalidation) is shared — the dialog never sees a stale
    /// resolution the analyzer wouldn't.</para>
    /// </summary>
    public sealed class ListAnalysisRulesHandler
        : IRpcRequestHandler<ListAnalysisRulesRequest, ListAnalysisRulesResponse>
    {
        private readonly RuleRegistry _registry;
        private readonly CaSettingsLoader _caSettings;
        private readonly Func<AppSettings> _settingsProvider;
        private readonly PgRuleRegistry? _pgRegistry;

        /// <param name="pgRegistry">
        /// Spec 042: the PostgreSQL rule set, present when <c>postgreSql.enabled</c> is on. Its rules are listed (with <c>Kind</c> 1)
        /// only for a request that asks for them, so a client that predates the feature sees exactly the SQL Server rules.
        /// </param>
        public ListAnalysisRulesHandler(
            RuleRegistry registry, CaSettingsLoader caSettings, Func<AppSettings> settingsProvider, PgRuleRegistry? pgRegistry = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _caSettings = caSettings ?? throw new ArgumentNullException(nameof(caSettings));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _pgRegistry = pgRegistry;
        }

        public int RequestMessageType => MessageTypes.ListAnalysisRules;
        public int ResponseMessageType => MessageTypes.ListAnalysisRulesResult;
        public bool AllowsEmptyPayload => true;

        public Task<ListAnalysisRulesResponse> HandleAsync(
            ListAnalysisRulesRequest request, RpcContext ctx, CancellationToken ct)
        {
            try
            {
                var global = _settingsProvider().CodeAnalysis;
                var dir = string.IsNullOrWhiteSpace(request?.FileDirectory) ? null : request!.FileDirectory;
                var resolved = _caSettings.Load(dir, global);

                var rules = _registry.AllRules
                    .Select(r =>
                    {
                        var meta = RuleMetadataCatalog.Get(r.RuleId);
                        return new AnalysisRuleInfoDto
                        {
                            RuleId            = r.RuleId,
                            Name              = meta.Name,
                            Category          = r.Category,
                            DefaultSeverity   = (int)r.DefaultSeverity,
                            EffectiveSeverity = (int)resolved.GetSeverity(r.RuleId, r.DefaultSeverity),
                            Enabled           = resolved.IsEnabled(r.RuleId),
                            RequiresSchema    = r.RequiresSchema,
                            AutoFixable       = meta.AutoFixable,
                            Description       = meta.Description,
                        };
                    })
                    .ToList();

                if (request?.IncludePostgreSql == true && _pgRegistry != null)
                {
                    rules.AddRange(_pgRegistry.AllRules.Select(r => new AnalysisRuleInfoDto
                    {
                        RuleId            = r.RuleId,
                        Name              = r.Name,
                        Category          = r.Category,
                        DefaultSeverity   = (int)r.DefaultSeverity,
                        EffectiveSeverity = (int)resolved.GetSeverity(r.RuleId, r.DefaultSeverity),
                        Enabled           = resolved.IsEnabled(r.RuleId),
                        RequiresSchema    = r.RequiresSchema,
                        AutoFixable       = r.AutoFixable,
                        Description       = r.Description,
                        Kind              = (int)AkmlSql.Core.DatabaseKind.PostgreSql,
                        ReferenceUrl      = r.ReferenceUrl,
                    }));
                }

                Log.Debug("ListAnalysisRules: returned {Count} rules (dir={Dir})", rules.Count, dir ?? "<global>");

                return Task.FromResult(new ListAnalysisRulesResponse
                {
                    Success = true,
                    Rules   = rules.ToArray(),
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ListAnalysisRules failed");
                return Task.FromResult(new ListAnalysisRulesResponse
                {
                    Success = false,
                    Error   = ex.Message,
                });
            }
        }
    }
}
