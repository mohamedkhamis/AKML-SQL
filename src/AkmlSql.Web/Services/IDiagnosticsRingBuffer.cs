using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T048 (FR-005a). Fixed-size in-memory ring buffer
/// with a debounced periodic flush to IndexedDB. The bundle-export path
/// (<c>Pages/Diagnostics.razor</c>) reads the snapshot and packages it as a ZIP
/// alongside engine-side log tails (M3 capability <c>diagnostics.engine-log-tail.v1</c>).
/// </summary>
public interface IDiagnosticsRingBuffer
{
    /// <summary>Append a new entry. Drops the oldest entry when the buffer is full.</summary>
    void Log(DiagnosticLevel level, string source, string message, object? data = null);

    /// <summary>Snapshot in chronological order (oldest first).</summary>
    IReadOnlyList<DiagnosticEntry> Snapshot();

    /// <summary>Drop every entry in memory; the next flush will clear IndexedDB too.</summary>
    void Clear();

    /// <summary>Force-flush the in-memory ring to IndexedDB.</summary>
    Task FlushAsync();

    /// <summary>Restore the ring from IndexedDB. Called once on startup.</summary>
    Task RestoreAsync();
}

public enum DiagnosticLevel { Trace, Info, Warn, Error }

public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    string Source,
    string Message,
    object? Data);

internal sealed class DiagnosticsRingBuffer : IDiagnosticsRingBuffer
{
    private const string PersistenceKey = "ring";
    private const int CapacityBound = 2048;

    private readonly IIndexedDbAdapter _store;
    private readonly ConcurrentQueue<DiagnosticEntry> _entries = new();
    private int _pendingFlushes;

    public DiagnosticsRingBuffer(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Log(DiagnosticLevel level, string source, string message, object? data = null)
    {
        _entries.Enqueue(new DiagnosticEntry(DateTimeOffset.UtcNow, level, source, message, data));
        while (_entries.Count > CapacityBound && _entries.TryDequeue(out _)) { }
        ScheduleFlush();
    }

    public IReadOnlyList<DiagnosticEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        ScheduleFlush();
    }

    public Task FlushAsync()
    {
        var snapshot = Snapshot();
        try
        {
            var json = JsonSerializer.Serialize(snapshot);
            return _store.SetAsync(StoreNames.Diagnostics, PersistenceKey, json);
        }
        catch
        {
            // Best-effort -- diagnostics persistence must never throw to the caller.
            return Task.CompletedTask;
        }
    }

    public async Task RestoreAsync()
    {
        try
        {
            var raw = await _store.GetAsync(StoreNames.Diagnostics, PersistenceKey).ConfigureAwait(false);
            if (string.IsNullOrEmpty(raw)) return;
            var entries = JsonSerializer.Deserialize<DiagnosticEntry[]>(raw);
            if (entries == null) return;
            foreach (var e in entries) _entries.Enqueue(e);
            while (_entries.Count > CapacityBound && _entries.TryDequeue(out _)) { }
        }
        catch
        {
            // Corrupt record -- ignore.
        }
    }

    private void ScheduleFlush()
    {
        // Coalesce flush requests: at most one pending flush at a time. We deliberately
        // do NOT await; the flush is fire-and-forget on a background continuation.
        if (Interlocked.CompareExchange(ref _pendingFlushes, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(250).ConfigureAwait(false); }
            catch { /* swallow */ }
            Interlocked.Exchange(ref _pendingFlushes, 0);
            await FlushAsync().ConfigureAwait(false);
        });
    }
}
