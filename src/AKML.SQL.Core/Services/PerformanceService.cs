using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for monitoring and optimizing performance.
/// Implements Story 11.1: Performance Monitoring.
/// </summary>
public class PerformanceService : IPerformanceService
{
    private readonly ILogger<PerformanceService> _logger;
    private readonly ConcurrentDictionary<string, PerformanceMetric> _metrics;
    private readonly ConcurrentQueue<OperationTiming> _recentTimings;
    private readonly int _maxRecentTimings;
    private bool _isEnabled;

    public PerformanceService(ILogger<PerformanceService> logger)
    {
        _logger = logger;
        _metrics = new ConcurrentDictionary<string, PerformanceMetric>();
        _recentTimings = new ConcurrentQueue<OperationTiming>();
        _maxRecentTimings = 1000;
        _isEnabled = true;
    }

    /// <summary>
    /// Enables or disables performance monitoring.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        _logger.LogInformation("Performance monitoring {Status}", enabled ? "enabled" : "disabled");
    }

    /// <summary>
    /// Gets whether monitoring is enabled.
    /// </summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Starts timing an operation.
    /// </summary>
    public IDisposable StartOperation(string operationName)
    {
        if (!_isEnabled)
            return new NoOpDisposable();

        return new OperationTimer(this, operationName);
    }

    /// <summary>
    /// Records an operation timing.
    /// </summary>
    public void RecordTiming(string operationName, TimeSpan duration)
    {
        if (!_isEnabled)
            return;

        var timing = new OperationTiming
        {
            OperationName = operationName,
            Duration = duration,
            Timestamp = DateTime.UtcNow
        };

        // Update metrics
        _metrics.AddOrUpdate(
            operationName,
            _ => new PerformanceMetric
            {
                OperationName = operationName,
                TotalCount = 1,
                TotalDuration = duration,
                MinDuration = duration,
                MaxDuration = duration,
                LastDuration = duration,
                LastTimestamp = timing.Timestamp
            },
            (_, existing) =>
            {
                existing.TotalCount++;
                existing.TotalDuration += duration;
                existing.MinDuration = TimeSpan.FromTicks(Math.Min(existing.MinDuration.Ticks, duration.Ticks));
                existing.MaxDuration = TimeSpan.FromTicks(Math.Max(existing.MaxDuration.Ticks, duration.Ticks));
                existing.LastDuration = duration;
                existing.LastTimestamp = timing.Timestamp;
                return existing;
            });

        // Add to recent timings
        _recentTimings.Enqueue(timing);
        while (_recentTimings.Count > _maxRecentTimings)
        {
            _recentTimings.TryDequeue(out _);
        }

        // Log slow operations
        if (duration.TotalMilliseconds > 100)
        {
            _logger.LogWarning("Slow operation: {Operation} took {Duration}ms", operationName, duration.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Gets metrics for a specific operation.
    /// </summary>
    public PerformanceMetric? GetMetric(string operationName)
    {
        return _metrics.TryGetValue(operationName, out var metric) ? metric : null;
    }

    /// <summary>
    /// Gets all performance metrics.
    /// </summary>
    public IReadOnlyList<PerformanceMetric> GetAllMetrics()
    {
        return _metrics.Values.OrderByDescending(m => m.TotalCount).ToList();
    }

    /// <summary>
    /// Gets recent operation timings.
    /// </summary>
    public IReadOnlyList<OperationTiming> GetRecentTimings(int count = 100)
    {
        return _recentTimings
            .OrderByDescending(t => t.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets slow operations above a threshold.
    /// </summary>
    public IReadOnlyList<OperationTiming> GetSlowOperations(TimeSpan threshold, int count = 50)
    {
        return _recentTimings
            .Where(t => t.Duration > threshold)
            .OrderByDescending(t => t.Duration)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        _metrics.Clear();
        while (_recentTimings.TryDequeue(out _)) { }
        _logger.LogInformation("Performance metrics reset");
    }

    /// <summary>
    /// Gets a performance summary.
    /// </summary>
    public PerformanceSummary GetSummary()
    {
        var metrics = GetAllMetrics();
        var totalOperations = metrics.Sum(m => m.TotalCount);
        var totalDuration = metrics.Sum(m => m.TotalDuration.TotalMilliseconds);

        return new PerformanceSummary
        {
            TotalOperations = totalOperations,
            TotalDurationMs = totalDuration,
            AverageDurationMs = totalOperations > 0 ? totalDuration / totalOperations : 0,
            UniqueOperations = metrics.Count,
            SlowestOperation = metrics.OrderByDescending(m => m.MaxDuration).FirstOrDefault()?.OperationName,
            MostFrequentOperation = metrics.OrderByDescending(m => m.TotalCount).FirstOrDefault()?.OperationName,
            StartTime = metrics.Min(m => m.LastTimestamp),
            EndTime = metrics.Max(m => m.LastTimestamp)
        };
    }

    private class OperationTimer : IDisposable
    {
        private readonly PerformanceService _service;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;

        public OperationTimer(PerformanceService service, string operationName)
        {
            _service = service;
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _service.RecordTiming(_operationName, _stopwatch.Elapsed);
        }
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// Interface for performance service.
/// </summary>
public interface IPerformanceService
{
    void SetEnabled(bool enabled);
    bool IsEnabled { get; }
    IDisposable StartOperation(string operationName);
    void RecordTiming(string operationName, TimeSpan duration);
    PerformanceMetric? GetMetric(string operationName);
    IReadOnlyList<PerformanceMetric> GetAllMetrics();
    IReadOnlyList<OperationTiming> GetRecentTimings(int count = 100);
    IReadOnlyList<OperationTiming> GetSlowOperations(TimeSpan threshold, int count = 50);
    void Reset();
    PerformanceSummary GetSummary();
}

/// <summary>
/// Performance metric for a specific operation.
/// </summary>
public class PerformanceMetric
{
    public string OperationName { get; set; } = string.Empty;
    public long TotalCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan MinDuration { get; set; }
    public TimeSpan MaxDuration { get; set; }
    public TimeSpan LastDuration { get; set; }
    public DateTime LastTimestamp { get; set; }

    public TimeSpan AverageDuration => TotalCount > 0
        ? TimeSpan.FromTicks(TotalDuration.Ticks / TotalCount)
        : TimeSpan.Zero;
}

/// <summary>
/// Single operation timing record.
/// </summary>
public class OperationTiming
{
    public string OperationName { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Performance summary statistics.
/// </summary>
public class PerformanceSummary
{
    public long TotalOperations { get; set; }
    public double TotalDurationMs { get; set; }
    public double AverageDurationMs { get; set; }
    public int UniqueOperations { get; set; }
    public string? SlowestOperation { get; set; }
    public string? MostFrequentOperation { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
