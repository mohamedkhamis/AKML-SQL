using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Periodically purges expired and excess history entries based on retention settings.
/// Runs on startup and then every 24 hours.
/// </summary>
public sealed class HistoryRetentionService(HistoryDatabase database, HistorySettings settings) : IDisposable
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(24);

    private readonly HistoryDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly HistorySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Starts the retention service: runs an immediate purge, then schedules periodic purges.
    /// </summary>
    public async Task StartAsync()
    {
        // Run initial purge on startup
        try
        {
            await _database.PurgeExpiredEntriesAsync(_settings.RetentionDays, _settings.MaxEntries);
            // FR-039: version-preserving trim — drop old version snapshots while keeping
            // each query's latest version and all execution records.
            await _database.PurgeOldVersionsAsync(_settings.RetentionDays);
            Log.Information("History retention: initial purge completed (retention={Days}d, maxEntries={Max})",
                _settings.RetentionDays, _settings.MaxEntries);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "History retention: initial purge failed");
        }

        // Schedule periodic purge every 24 hours
        _timer = new Timer(OnTimerElapsed, null, PurgeInterval, PurgeInterval);
    }

    private void OnTimerElapsed(object? state)
    {
        // Fire-and-forget async purge from timer callback
        _ = RunPurgeAsync();
    }

    private async Task RunPurgeAsync()
    {
        try
        {
            await _database.PurgeExpiredEntriesAsync(_settings.RetentionDays, _settings.MaxEntries);
            // FR-039: version-preserving trim — drop old version snapshots while keeping
            // each query's latest version and all execution records.
            await _database.PurgeOldVersionsAsync(_settings.RetentionDays);
            Log.Debug("History retention: periodic purge completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "History retention: periodic purge failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
