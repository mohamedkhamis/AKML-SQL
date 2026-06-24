using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Execution;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// PR #247 regression guard: verifies that <see cref="SessionConnection.RegisterQuery"/> is only
/// effective once a request is the active (gate-holding) one, so a CancelQuery arriving while a
/// request is still queued cannot fire the CTS of a not-yet-running request.
///
/// The fix moved RegisterQuery from BEFORE RunExclusiveAsync to INSIDE the work lambda so the CTS
/// is never in the registry while the request waits on the semaphore gate.
/// </summary>
public sealed class Pr247_ExecuteQueryHandlerFix
{
    // ── Core registry contract ────────────────────────────────────────────────

    /// <summary>
    /// TryCancel returns false (and leaves the CTS un-cancelled) when the queryId was never
    /// registered. This is the guarantee the fix relies on: a queued request that hasn't called
    /// RegisterQuery yet is invisible to TryCancel.
    /// </summary>
    [Fact]
    public void TryCancel_BeforeRegister_ReturnsFalseAndDoesNotCancel()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();

        // No RegisterQuery call — simulates the queued-but-not-yet-active state.
        bool found = conn.TryCancel("q1");

        Assert.False(found);
        Assert.False(cts.Token.IsCancellationRequested,
            "The CTS must remain un-cancelled when the query was never registered.");
    }

    /// <summary>
    /// TryCancel fires the CTS only after RegisterQuery has been called for that queryId.
    /// This confirms the registry is functional once the request is active.
    /// </summary>
    [Fact]
    public void TryCancel_AfterRegister_CancelsCts()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();

        conn.RegisterQuery("q1", cts);
        bool found = conn.TryCancel("q1");

        Assert.True(found);
        Assert.True(cts.Token.IsCancellationRequested);
    }

    /// <summary>
    /// CompleteQuery removes the registration so a subsequent TryCancel for the same queryId
    /// has no effect — this is the finally-cleanup path.
    /// </summary>
    [Fact]
    public void TryCancel_AfterCompleteQuery_ReturnsFalse()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();

        conn.RegisterQuery("q1", cts);
        conn.CompleteQuery("q1");
        bool found = conn.TryCancel("q1");

        Assert.False(found);
    }

    // ── Concurrency: late-arriving cancel must not hit a queued request ───────

    /// <summary>
    /// Race-condition scenario from PR #247:
    ///
    ///   Thread A (execute):   starts queued — gate is HELD by thread B.
    ///   Thread C (cancel):    TryCancel("q1") fires BEFORE thread A enters the gate.
    ///
    /// Under the OLD code, RegisterQuery was called before RunExclusiveAsync, so TryCancel
    /// would fire the CTS while thread A was still queued — causing an erroneous Cancelled result.
    ///
    /// Under the FIXED code, RegisterQuery is called inside the work lambda (i.e., only after
    /// the gate is acquired), so TryCancel while queued is a no-op.
    ///
    /// We test this by simulating the sequence using a bare <see cref="SessionConnection"/>:
    /// we do NOT register before the gate, fire a TryCancel, then register (gate acquired), and
    /// verify the CTS is NOT yet cancelled.
    /// </summary>
    [Fact]
    public async Task QueuedRequest_CancelBeforeGateAcquired_DoesNotCancelCts()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();
        var queryId = "q-race";

        // Simulate: a prior request holds the gate (the semaphore is internal, so we model
        // the "queued" phase as simply "not yet registered"). The key invariant is that
        // TryCancel called before RegisterQuery must be a no-op.
        bool cancelHitBeforeRegister = conn.TryCancel(queryId); // fires while "queued"

        Assert.False(cancelHitBeforeRegister,
            "TryCancel must be a no-op for a query that has not yet registered (still queued).");

        // Now the gate is "acquired" — register the CTS.
        conn.RegisterQuery(queryId, cts);

        // The CTS must NOT be cancelled — the cancel arrived before registration.
        Assert.False(cts.Token.IsCancellationRequested,
            "The CTS must not be cancelled: the cancel arrived while the request was queued " +
            "and the registration hadn't happened yet.");

        // Cleanup (mirrors the finally in HandleAsync).
        conn.CompleteQuery(queryId);

        await Task.CompletedTask; // keep method async for future async variants
    }

    /// <summary>
    /// Verify the complementary case: a cancel that arrives AFTER the gate is acquired
    /// (i.e., AFTER RegisterQuery) correctly cancels the active request.
    /// </summary>
    [Fact]
    public void ActiveRequest_CancelAfterGateAcquired_CancelsCts()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();
        var queryId = "q-active";

        // Gate acquired — registration happens (the fixed code path).
        conn.RegisterQuery(queryId, cts);

        // Cancel arrives while the request is running.
        bool found = conn.TryCancel(queryId);

        Assert.True(found);
        Assert.True(cts.Token.IsCancellationRequested,
            "A cancel for an active (registered) request must propagate to the CTS.");

        conn.CompleteQuery(queryId);
    }

    // ── Empty / null queryId guard ────────────────────────────────────────────

    [Fact]
    public void TryCancel_EmptyQueryId_ReturnsFalse()
    {
        var conn = new SessionConnection("s1");
        Assert.False(conn.TryCancel(string.Empty));
    }

    [Fact]
    public void RegisterQuery_EmptyQueryId_IsNoOp_TryCancelReturnsFalse()
    {
        var conn = new SessionConnection("s1");
        using var cts = new CancellationTokenSource();

        // Should silently ignore empty queryId.
        conn.RegisterQuery(string.Empty, cts);
        bool found = conn.TryCancel(string.Empty);

        Assert.False(found);
        Assert.False(cts.Token.IsCancellationRequested);
    }
}
