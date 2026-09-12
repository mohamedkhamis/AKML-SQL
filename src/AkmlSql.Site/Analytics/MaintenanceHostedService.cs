using AkmlSql.Site.Seo;
using AkmlSql.Site.Settings;
using Microsoft.Extensions.Options;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Spec 038 T026 (US1): database maintenance, moved off the startup path.
/// <para>
/// <c>Prune</c> and <c>ClearSameOriginReferrers</c> used to run inline in <c>Program.cs</c> after
/// <c>app.Build()</c>, which meant the first request after every deploy or app-pool recycle waited
/// behind them. Neither is deploy <i>validation</i> — the eager singleton resolution above them is,
/// and that deliberately stays inline so a broken deploy still fails fast. Pruning old rows and
/// repairing historical referrers are housekeeping, and no visitor should ever wait for housekeeping.
/// </para>
/// <para>
/// Everything runs on a background task after start, wrapped so a maintenance failure can never take
/// the site down: the worst outcome of a failed prune is some old rows surviving another day.
/// </para>
/// </summary>
public sealed class MaintenanceHostedService : IHostedService
{
    private readonly AnalyticsStore _store;
    private readonly SiteSettingsStore _settings;
    private readonly AnalyticsOptions _options;
    private readonly string? _siteHost;
    private readonly ILogger<MaintenanceHostedService> _logger;

    public MaintenanceHostedService(
        AnalyticsStore store,
        SiteSettingsStore settings,
        IOptions<AnalyticsOptions> options,
        IOptions<SiteOptions> site,
        ILogger<MaintenanceHostedService> logger)
    {
        _store = store;
        _settings = settings;
        _options = options.Value;
        _logger = logger;

        _siteHost = Uri.TryCreate(site.Value.BaseUrl, UriKind.Absolute, out var canonical)
            ? canonical.Host
            : null;
    }

    /// <summary>Kicks the maintenance pass onto a background task and returns immediately.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// One maintenance pass. Public so a test can run it deterministically rather than racing a
    /// background task.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            // ADM-004: retention prune. Once per boot rather than on a timer — the tables gain a few
            // thousand rows a day at most, so a prune per deploy or recycle is ample.
            var prunedRows = _store.Prune(_options.RetentionDays);
            if (prunedRows > 0)
            {
                _logger.LogInformation(
                    "Analytics retention: pruned {Rows} row(s) older than {Days} days.",
                    prunedRows, _options.RetentionDays);
            }

            // Repair history written before same-origin referrers were filtered at write time:
            // internal navigation had made the site its own top referrer. Only the referrer columns
            // are cleared, never a row, and the operation is idempotent — after the first run it
            // corrects nothing.
            var correctedReferrers = _store.ClearSameOriginReferrers(_siteHost);
            if (correctedReferrers > 0)
            {
                _logger.LogInformation(
                    "Analytics: cleared self-referrer on {Rows} historical row(s) for host {Host}.",
                    correctedReferrers, _siteHost);
            }

            // Spec 038 T076 (US3): erase identifiable detail past the owner's retention setting.
            // Runs BEFORE the prune above would ever reach these rows, and nulls the two columns
            // rather than deleting the row, so country and version totals for old periods still
            // reconcile after the personal detail is gone (SC-008).
            var deIdentified = _store.DeIdentify(_settings.Current.IdentifiableRetentionDays);
            if (deIdentified > 0)
            {
                _logger.LogInformation(
                    "Analytics retention: de-identified {Rows} row(s) older than {Days} days.",
                    deIdentified, _settings.Current.IdentifiableRetentionDays);
            }
        }
        catch (Exception ex)
        {
            // Maintenance must never take the site down. The worst outcome of a failure here is that
            // some old rows survive until the next recycle.
            _logger.LogError(ex, "Analytics maintenance pass failed; the site is unaffected.");
        }

        return Task.CompletedTask;
    }
}
