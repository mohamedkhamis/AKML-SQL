using System.Collections.Concurrent;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) — M1 scaffold (T028). FR-005a (diagnostic ring buffer).
/// Real implementation with IndexedDB persistence and bundle-export wiring lands at T048
/// in Phase 3 (M2).
/// </summary>
public interface IDiagnosticsRingBuffer
{
    void Log(DiagnosticLevel level, string source, string message, object? data = null);
    IReadOnlyList<DiagnosticEntry> Snapshot();
    void Clear();
}

public enum DiagnosticLevel { Trace, Info, Warn, Error }

public sealed record DiagnosticEntry(DateTimeOffset Timestamp, DiagnosticLevel Level, string Source, string Message, object? Data);

internal sealed class StubDiagnosticsRingBuffer : IDiagnosticsRingBuffer
{
    private readonly ConcurrentQueue<DiagnosticEntry> _entries = new();
    private const int CapacityBound = 2048;

    public void Log(DiagnosticLevel level, string source, string message, object? data = null)
    {
        _entries.Enqueue(new DiagnosticEntry(DateTimeOffset.UtcNow, level, source, message, data));
        while (_entries.Count > CapacityBound && _entries.TryDequeue(out _)) { }
    }

    public IReadOnlyList<DiagnosticEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }
}
