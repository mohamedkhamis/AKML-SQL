using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>Keeps the engine bridge connected without the user having to ask.</summary>
public interface IEngineAutoConnect
{
    /// <summary>Why the last attempt did not end in a live bridge. Null while things are fine.</summary>
    EngineConnectFailure? LastFailure { get; }

    /// <summary>Raised whenever <see cref="LastFailure"/> changes, so the status bar can re-render.</summary>
    event Action? FailureChanged;

    /// <summary>
    /// Begin keeping the bridge up. Idempotent — safe to call from every page that loads.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Try again right now, cancelling any scheduled backoff. This is what the "Retry" button in the
    /// status bar calls, and what a tab regaining focus triggers.
    /// </summary>
    Task RetryNowAsync();
}

/// <summary>
/// Why the bridge is not up, in terms a person can act on.
/// </summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Message">One sentence, written for the status bar.</param>
/// <param name="NextAttemptAt">When the next automatic attempt is due, if one is scheduled.</param>
public sealed record EngineConnectFailure(
    EngineConnectFailureKind Kind,
    string Message,
    DateTimeOffset? NextAttemptAt);

/// <summary>The distinct situations worth telling the user apart.</summary>
public enum EngineConnectFailureKind
{
    /// <summary>No engine has ever been paired in this browser.</summary>
    NotPaired,

    /// <summary>Paired, but nothing is listening — the service is probably stopped.</summary>
    Unreachable,

    /// <summary>Reached the engine, but it wants a pairing PIN (the stored token was rejected).</summary>
    PairingRequired,

    /// <summary>Reached the engine and something else went wrong.</summary>
    Rejected,
}

/// <summary>
/// The single owner of "is the bridge up, and if not, keep trying".
///
/// <para>
/// Before this existed, startup fired one connect attempt and forgot about it. That is fine on the
/// happy path and useless otherwise: open the browser a moment before the engine service finishes
/// starting — after a reboot, say — and the page sits Offline indefinitely, no matter how healthy
/// the engine becomes a second later. The only way out was for the user to know to go to Settings
/// and click Connect, which is precisely the manual step this is meant to remove.
/// </para>
///
/// <para>
/// So attempts repeat on a bounded backoff, and two cheap signals short-circuit the wait: the tab
/// becoming visible again, and the browser reporting it is back online. Between them they cover
/// almost every real recovery — a user who fixes the engine then switches back to the tab gets a
/// live bridge by the time they have looked at it.
/// </para>
///
/// <para>
/// Backoff is capped rather than infinite-doubling, because the failure this is most often riding
/// out is "the service takes twenty seconds to start", not "the server is gone for an hour". A cap
/// of one minute keeps a forgotten tab cheap while still recovering promptly.
/// </para>
/// </summary>
internal sealed class EngineAutoConnect : IEngineAutoConnect, IAsyncDisposable
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromSeconds(60),
    ];

    private readonly IEngineBridge _bridge;
    private readonly IConnectionStore _connections;
    private readonly IPairingTokenVault _vault;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    // Released by RetryNowAsync to cut a backoff wait short. A semaphore rather than a
    // CancellationToken because a wake must INTERRUPT the delay, not tear the loop down: an earlier
    // version cancelled and restarted the loop, and the restart raced the old loop's cleanup — the
    // new call found the gate still held, returned immediately, and the retry loop silently stopped
    // existing. A page that fired a wake on focus (which every page does) then never retried again.
    private readonly SemaphoreSlim _wake = new(0);

    // Guards loop ownership only; never held across a wait.
    private readonly object _lifecycle = new();
    private bool _loopRunning;
    private CancellationTokenSource? _shutdown;

    public EngineAutoConnect(
        IEngineBridge bridge,
        IConnectionStore connections,
        IPairingTokenVault vault,
        IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public EngineConnectFailure? LastFailure { get; private set; }

    public event Action? FailureChanged;

    public Task StartAsync()
    {
        EnsureLoop();
        return Task.CompletedTask;
    }

    public Task RetryNowAsync()
    {
        // If a loop is already running, just cut its wait short. If it has finished — because the
        // bridge went live, or because the pairing needs a human — start a fresh one.
        lock (_lifecycle)
        {
            if (_loopRunning)
            {
                // Release at most one permit; extra wakes while already awake are meaningless.
                if (_wake.CurrentCount == 0) _wake.Release();
                return Task.CompletedTask;
            }
        }
        EnsureLoop();
        return Task.CompletedTask;
    }

    private void EnsureLoop()
    {
        lock (_lifecycle)
        {
            if (_loopRunning) return;
            _loopRunning = true;
            _shutdown ??= new CancellationTokenSource();
        }
        _ = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        var ct = _shutdown?.Token ?? CancellationToken.None;

        try
        {
            var attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                if (_bridge.State == BridgeState.Open)
                {
                    SetFailure(null);
                    return;
                }

                var outcome = await TryOnceAsync(ct).ConfigureAwait(false);
                if (outcome is null)
                {
                    SetFailure(null);
                    return;
                }

                // A missing pairing is not something waiting will fix, and hammering it would just
                // trip the engine's PIN rate-limiter. Report and stop; RetryNowAsync restarts the
                // loop once the user has done their part.
                if (outcome.Kind is EngineConnectFailureKind.NotPaired or EngineConnectFailureKind.PairingRequired)
                {
                    SetFailure(outcome);
                    return;
                }

                var wait = Backoff[Math.Min(attempt, Backoff.Length - 1)];
                attempt++;
                SetFailure(outcome with { NextAttemptAt = DateTimeOffset.UtcNow.Add(wait) });

                // Wait for the backoff OR a wake, whichever comes first. A wake also resets the
                // schedule: someone who just started the engine should get a prompt attempt, not
                // the forty-second step the loop had climbed to.
                bool woken;
                try { woken = await _wake.WaitAsync(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                if (woken) attempt = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            lock (_lifecycle) { _loopRunning = false; }
        }
    }

    /// <summary>One attempt. Returns null on success, or why it failed.</summary>
    private async Task<EngineConnectFailure?> TryOnceAsync(CancellationToken ct)
    {
        try
        {
            var activeId = await _connections.GetActiveIdAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(activeId))
            {
                return new EngineConnectFailure(
                    EngineConnectFailureKind.NotPaired,
                    "No engine paired yet.",
                    null);
            }

            var conn = await _connections.GetAsync(activeId).ConfigureAwait(false);
            if (conn is null)
            {
                return new EngineConnectFailure(
                    EngineConnectFailureKind.NotPaired,
                    "The saved engine connection is gone.",
                    null);
            }

            string? bearer = null;
            if (!conn.IsLocalhost)
            {
                // A missing or unreadable token is not fatal here: the engine will answer
                // PinRequired and we report that, which is far more useful than a raw exception.
                try { bearer = await _vault.RetrieveAsync(conn.Id).ConfigureAwait(false); }
                catch (Exception) { bearer = null; }
            }

            var response = await _bridge.ConnectAsync(conn, bearer, pairingPin: null, ct).ConfigureAwait(false);

            if (response.Status == HandshakeStatus.Ok)
            {
                // Persist whichever scheme answered so the next startup is a single attempt.
                conn.LastConnectedAt = DateTimeOffset.UtcNow;
                try { await _connections.UpdateAsync(conn).ConfigureAwait(false); } catch (Exception) { }
                _diagnostics.Log(DiagnosticLevel.Info, "bridge",
                    $"Engine connected automatically ({_bridge.ConnectedUrl}).");
                return null;
            }

            return response.Status switch
            {
                HandshakeStatus.PinRequired or HandshakeStatus.PinInvalid => new EngineConnectFailure(
                    EngineConnectFailureKind.PairingRequired,
                    "The engine needs pairing again — open Settings to enter a new PIN.",
                    null),
                _ => new EngineConnectFailure(
                    EngineConnectFailureKind.Rejected,
                    response.ErrorMessage ?? "The engine declined the connection.",
                    null),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Trace, "bridge", $"Auto-connect attempt failed: {ex.Message}");
            return new EngineConnectFailure(
                EngineConnectFailureKind.Unreachable,
                "The engine is not responding — it may be starting up or stopped.",
                null);
        }
    }

    private void SetFailure(EngineConnectFailure? failure)
    {
        var changed = LastFailure?.Kind != failure?.Kind
                      || LastFailure?.NextAttemptAt != failure?.NextAttemptAt;
        LastFailure = failure;
        if (changed) FailureChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? shutdown;
        lock (_lifecycle) { shutdown = _shutdown; _shutdown = null; }

        if (shutdown is not null)
        {
            try { await shutdown.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
            shutdown.Dispose();
        }
        _wake.Dispose();
    }
}
