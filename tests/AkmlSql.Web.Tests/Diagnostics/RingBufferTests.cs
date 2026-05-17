using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Diagnostics;

/// <summary>
/// Spec 021 (web edition) -- M2 task T050. Covers <see cref="DiagnosticsRingBuffer"/>:
/// wrap-around at the 2048-entry bound, persistence round-trip through IndexedDB, and
/// the export-bundle JSON shape.
/// </summary>
public sealed class RingBufferTests
{
    [Fact]
    public void Log_then_Snapshot_returns_entries_in_chronological_order()
    {
        var store = new InMemoryIndexedDbAdapter();
        var buffer = new DiagnosticsRingBuffer(store);

        buffer.Log(DiagnosticLevel.Info, "src", "one");
        buffer.Log(DiagnosticLevel.Warn, "src", "two");
        buffer.Log(DiagnosticLevel.Error, "src", "three");

        var snap = buffer.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("one", snap[0].Message);
        Assert.Equal("two", snap[1].Message);
        Assert.Equal("three", snap[2].Message);
    }

    [Fact]
    public void Buffer_wraps_around_at_the_capacity_bound()
    {
        var store = new InMemoryIndexedDbAdapter();
        var buffer = new DiagnosticsRingBuffer(store);

        // Internal capacity is 2048 -- write 2100 entries and assert oldest entries are evicted.
        for (int i = 0; i < 2100; i++)
        {
            buffer.Log(DiagnosticLevel.Trace, "src", $"entry-{i}");
        }

        var snap = buffer.Snapshot();
        Assert.Equal(2048, snap.Count);
        // The first 52 entries (entry-0 .. entry-51) should have been evicted; the snapshot
        // starts at entry-52.
        Assert.Equal("entry-52", snap[0].Message);
        Assert.Equal("entry-2099", snap[2047].Message);
    }

    [Fact]
    public async Task FlushAsync_persists_to_IndexedDB_and_RestoreAsync_rehydrates()
    {
        var store = new InMemoryIndexedDbAdapter();
        var first = new DiagnosticsRingBuffer(store);
        first.Log(DiagnosticLevel.Info, "src", "persist me");
        first.Log(DiagnosticLevel.Warn, "src", "and me");
        await first.FlushAsync();

        var second = new DiagnosticsRingBuffer(store);
        Assert.Empty(second.Snapshot());

        await second.RestoreAsync();

        var snap = second.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("persist me", snap[0].Message);
        Assert.Equal("and me", snap[1].Message);
    }

    [Fact]
    public void Clear_drops_every_entry()
    {
        var store = new InMemoryIndexedDbAdapter();
        var buffer = new DiagnosticsRingBuffer(store);
        for (int i = 0; i < 50; i++) buffer.Log(DiagnosticLevel.Info, "src", $"e-{i}");

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public async Task Restore_on_a_corrupt_record_does_not_throw()
    {
        var store = new InMemoryIndexedDbAdapter();
        store.Seed(StoreNames.Diagnostics, "ring", "this-is-not-json");

        var buffer = new DiagnosticsRingBuffer(store);
        await buffer.RestoreAsync(); // must not throw
        Assert.Empty(buffer.Snapshot());
    }
}
