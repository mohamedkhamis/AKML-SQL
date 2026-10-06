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
/// the site down. Runs hourly and after retention changes; failures retry after one minute.
/// </para>
/// </summary>
public sealed class MaintenanceHostedService : BackgroundService
{
    private readonly AnalyticsStore _store;
    private readonly SiteSettingsStore _settings;
    private readonly AnalyticsOptions _options;
    private readonly string? _siteHost;
    private readonly ILogger<MaintenanceHostedService> _logger;
    private readonly SemaphoreSlim _requested = new(0, 1);
    private readonly SemaphoreSlim _pass = new(1, 1);
    private MaintenanceSnapshot _snapshot = new();
    public MaintenanceSnapshot Snapshot => Volatile.Read(ref _snapshot);

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
        _settings.RetentionChanged += RequestRun;

        _siteHost = Uri.TryCreate(site.Value.BaseUrl, UriKind.Absolute, out var canonical)
            ? canonical.Host
            : null;
    }

    public void RequestRun()
    {
        try { _requested.Release(); }
        catch (SemaphoreFullException) { /* A pass is already requested. */ }
    }

    public long? GetIdentifiableBacklog()
    {
        try { return _store.CountIdentifiableBacklog(_settings.Current.IdentifiableRetentionDays, DateTimeOffset.UtcNow); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not measure identifiable retention backlog.");
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);
            await _requested.WaitAsync(Snapshot.Failed ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1), stoppingToken);
        }
    }

    public override void Dispose()
    {
        _settings.RetentionChanged -= RequestRun;
        base.Dispose();
    }

    /// <summary>
    /// One maintenance pass. Public so a test can run it deterministically rather than racing a
    /// background task.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _pass.WaitAsync(cancellationToken);
        _snapshot = Snapshot with { LastAttemptUtc = DateTimeOffset.UtcNow, Running = true };
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // The same pass runs hourly and promptly after a retention-setting change.
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
            // Nulls the two columns on retained rows, so country and version totals still
            // reconcile after the personal detail is gone (SC-008).
            var deIdentified = _store.DeIdentify(_settings.Current.IdentifiableRetentionDays);
            if (deIdentified > 0)
            {
                _logger.LogInformation(
                    "Analytics retention: de-identified {Rows} row(s) older than {Days} days.",
                    deIdentified, _settings.Current.IdentifiableRetentionDays);
            }
            _snapshot = Snapshot with { LastSuccessUtc = DateTimeOffset.UtcNow, Failed = false,
                Pruned = prunedRows, DeIdentified = deIdentified, ReferrersCorrected = correctedReferrers };
        }
        catch (Exception ex)
        {
            // Keep serving requests, surface the failure and retry in one minute.
            _logger.LogError(ex, "Analytics maintenance pass failed; the site is unaffected.");
            _snapshot = Snapshot with { Failed = true };
        }
        finally
        {
            _snapshot = Snapshot with { Running = false };
            _pass.Release();
        }
    }
}
