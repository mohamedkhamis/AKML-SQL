using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Shell.Shared.Ipc;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests;

/// <summary>
/// The installed run on 1.26.1123.1213 logged "[PipeRpc] WriteAsync req#0 _writeLock acquired in 300.9569ms (CONTENDED)" with no
/// other write in flight: the time was measured from before a log line, so a stall of the SSMS process read as lock contention.
/// A writer now reports a wait only when it found the lock taken, and only the time it waited.
/// </summary>
public sealed class PipeRpcWriteLockTests
{
    [Fact]
    public async Task A_free_lock_is_taken_without_a_wait()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var waited = await PipeRpcClient.EnterAsync(gate, CancellationToken.None);

        Assert.Null(waited);
        Assert.Equal(0, gate.CurrentCount);
    }

    [Fact]
    public async Task A_writer_that_finds_the_lock_taken_reports_how_long_it_waited()
    {
        using var gate = new SemaphoreSlim(1, 1);
        gate.Wait();

        var entering = PipeRpcClient.EnterAsync(gate, CancellationToken.None);
        await Task.Delay(150);
        Assert.False(entering.IsCompleted);
        gate.Release();
        var waited = await entering;

        Assert.NotNull(waited);
        var ms = waited.GetValueOrDefault().TotalMilliseconds;
        Assert.True(ms >= 100, $"waited {ms} ms");
        Assert.Equal(0, gate.CurrentCount);
    }

    [Fact]
    public async Task A_cancelled_writer_does_not_take_a_free_lock()
    {
        using var gate = new SemaphoreSlim(1, 1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PipeRpcClient.EnterAsync(gate, cancelled.Token));

        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task A_writer_cancelled_while_waiting_gives_up_without_the_lock()
    {
        using var gate = new SemaphoreSlim(1, 1);
        gate.Wait();
        using var timeout = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PipeRpcClient.EnterAsync(gate, timeout.Token));

        Assert.Equal(0, gate.CurrentCount);   // still the first holder's
        gate.Release();
        Assert.Equal(1, gate.CurrentCount);
    }
}
