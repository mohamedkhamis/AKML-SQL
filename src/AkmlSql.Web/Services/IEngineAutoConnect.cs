using System;
using System.Linq;
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

    /// <summary>
    /// Connect to this page's engine when present, otherwise the local engine at the default port.
    /// Reuses a saved pairing; false when none answers or it needs a PIN, so the caller can open Settings.
    /// </summary>
    Task<bool> PairLocalEngineAsync() => Task.FromResult(false);
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
    private readonly IPageEngine? _pageEngine;

    // Released by RetryNowAsync to cut a backoff wait short. A semaphore rather than a
    // CancellationToken because a wake must INTERRUPT the delay, not tear the loop down: an earlier
    // version cancelled and restarted the loop, and the restart raced the old loop's cleanup — the
    // new call found the gate still held, returned immediately, and the retry loop silently stopped
    // existing. A page that fired a wake on focus (which every page does) then never retried again.
    private readonly SemaphoreSlim _wake = new(0);

    // Guards loop ownership only; never held across a wait.
    private readonly object _lifecycle = new();
    private bool _loopRunning;
    private bool _disposed;
    private CancellationTokenSource? _shutdown;

    public EngineAutoConnect(
        IEngineBridge bridge,
        IConnectionStore connections,
        IPairingTokenVault vault,
        IDiagnosticsRingBuffer diagnostics,
        IPageEngine? pageEngine = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _pageEngine = pageEngine;
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
            if (_disposed) return Task.CompletedTask;
            if (_loopRunning)
            {
                // Release at most one permit; extra wakes while already awake are meaningless.
                // A permit can still be left behind if the loop exits between this check and the
                // next wait -- harmless, because the next loop's first backoff simply returns
                // immediately, which is the same thing a wake would have done anyway.
                if (_wake.CurrentCount == 0) _wake.Release();
                return Task.CompletedTask;
            }
        }
        EnsureLoop();
        return Task.CompletedTask;
    }

    private void EnsureLoop()
    {
        CancellationToken ct;
        lock (_lifecycle)
        {
            if (_disposed) return;
            if (_loopRunning) return;
            _loopRunning = true;
            _shutdown ??= new CancellationTokenSource();

            // Read the token HERE, under the lock, and hand it to the loop.
            //
            // Reading it inside RunLoopAsync was a latent trap: that read sat before the try, so if
            // DisposeAsync had raced in between and disposed the source, .Token threw
            // ObjectDisposedException, the finally that clears _loopRunning never ran, and the flag
            // stayed true forever -- after which EnsureLoop and RetryNowAsync both early-return and
            // auto-connect is silently dead for the rest of the page's life. The exception went
            // unobserved too, so nothing said why. That is exactly the failure this class exists to
            // prevent, so it is worth the extra parameter.
            ct = _shutdown.Token;
        }
        _ = RunLoopAsync(ct);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
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
            var conn = string.IsNullOrEmpty(activeId) ? null : await _connections.GetAsync(activeId).ConfigureAwait(false);
            var isNew = false;
            PageEngineInfo? pageInfo = null;
            if (conn is null)
            {
                if (!string.IsNullOrEmpty(activeId)) await _connections.SetActiveIdAsync(null).ConfigureAwait(false);
                var page = _pageEngine == null ? new PageEngineResult(PageEngineKind.Static) : await _pageEngine.GetAsync(ct).ConfigureAwait(false);
                if (page.Kind == PageEngineKind.Unknown)
                    return new(EngineConnectFailureKind.Unreachable, "Checking this page's engine…", null);
                if (page.Kind != PageEngineKind.Engine || page.Engine == null)
                    return new(EngineConnectFailureKind.NotPaired, "No engine paired yet.", null);
                pageInfo = page.Engine;
                conn = (await _connections.ListAsync().ConfigureAwait(false)).FirstOrDefault(c =>
                    c.Host.Equals(pageInfo.Host, StringComparison.OrdinalIgnoreCase) && c.Port == pageInfo.Port);
                isNew = conn == null;
                conn ??= NewPageConnection(pageInfo);
                conn.ResolvedScheme = pageInfo.Scheme;
                conn.StrictScheme = pageInfo.Scheme == "wss";
                if (!isNew)
                {
                    // Keep the scheme on disk before making this record active for the next retry.
                    await _connections.UpdateAsync(conn).ConfigureAwait(false);
                    await _connections.SetActiveIdAsync(conn.Id).ConfigureAwait(false);
                }
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
                await ApplyHandshakeAsync(conn, response).ConfigureAwait(false);
                if (isNew) await _connections.AddAsync(conn).ConfigureAwait(false);
                else { try { await _connections.UpdateAsync(conn).ConfigureAwait(false); } catch (Exception) { } }
                if (pageInfo != null)
                {
                    await _connections.SetActiveIdAsync(conn.Id).ConfigureAwait(false);
                    if (_pageEngine != null) _pageEngine.PairingPrefill = null;
                }
                _diagnostics.Log(DiagnosticLevel.Info, "bridge",
                    $"Engine connected automatically ({_bridge.ConnectedUrl}).");
                return null;
            }

            if (pageInfo != null && response.Status is HandshakeStatus.PinRequired or HandshakeStatus.PinInvalid)
            {
                if (_pageEngine != null) _pageEngine.PairingPrefill = pageInfo;
                return new(EngineConnectFailureKind.PairingRequired, "This engine needs its pairing PIN. Pair it in Settings › Connections.", null);
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

    public async Task<bool> PairLocalEngineAsync()
    {
        var connection = new EngineConnection
        {
            Id = Guid.NewGuid().ToString(),
            Name = "This computer",
            Host = "localhost",
            Port = EngineEndpoint.DefaultPort,
            IsLocalhost = true,
        };
        try
        {
            var isNew = true;
            PageEngineInfo? pageInfo = null;
            if (_pageEngine != null && await _pageEngine.GetAsync().ConfigureAwait(false) is { Kind: PageEngineKind.Engine, Engine: { } info })
            {
                pageInfo = info;
                var existing = (await _connections.ListAsync().ConfigureAwait(false)).FirstOrDefault(c =>
                    c.Host.Equals(info.Host, StringComparison.OrdinalIgnoreCase) && c.Port == info.Port);
                isNew = existing == null;
                connection = existing ?? NewPageConnection(info);
                connection.ResolvedScheme = info.Scheme;
                connection.StrictScheme = info.Scheme == "wss";
            }
            string? bearer = null;
            if (!connection.IsLocalhost)
            {
                try { bearer = await _vault.RetrieveAsync(connection.Id).ConfigureAwait(false); }
                catch (Exception) { /* The engine can request a PIN when the saved token is unavailable. */ }
            }
            var response = await _bridge.ConnectAsync(connection, bearer, pairingPin: null, CancellationToken.None).ConfigureAwait(false);
            if (response.Status != HandshakeStatus.Ok)
            {
                if (pageInfo != null && response.Status is HandshakeStatus.PinRequired or HandshakeStatus.PinInvalid)
                    _pageEngine!.PairingPrefill = pageInfo;
                _diagnostics.Log(DiagnosticLevel.Info, "connections", $"The local engine answered {response.Status}; pairing is left to Settings.");
                return false;
            }
            await ApplyHandshakeAsync(connection, response).ConfigureAwait(false);
            if (isNew) await _connections.AddAsync(connection).ConfigureAwait(false);
            else await _connections.UpdateAsync(connection).ConfigureAwait(false);
            await _connections.SetActiveIdAsync(connection.Id).ConfigureAwait(false);
            if (_pageEngine != null) _pageEngine.PairingPrefill = null;
            _diagnostics.Log(DiagnosticLevel.Info, "connections", "Paired with the engine on this computer.");
            SetFailure(null);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Info, "connections", $"No local engine answered: {ex.Message}");
            return false;
        }
    }

    private static EngineConnection NewPageConnection(PageEngineInfo info) => new()
    {
        Host = info.Host, Port = info.Port, ResolvedScheme = info.Scheme, StrictScheme = info.Scheme == "wss",
        IsLocalhost = EngineEndpoint.IsLoopbackHost(info.Host),
        Name = EngineEndpoint.IsLoopbackHost(info.Host) ? "This computer" : info.Host,
    };

    private async Task ApplyHandshakeAsync(EngineConnection connection, HandshakeResponse response)
    {
        if (!string.IsNullOrEmpty(response.NewBearerToken))
        {
            connection.IsLocalhost = false;
            connection.BearerTokenWrappedRef = await _vault.StoreAsync(connection.Id, response.NewBearerToken,
                DateTimeOffset.UtcNow.AddDays(60)).ConfigureAwait(false);
        }
        connection.LastConnectedAt = DateTimeOffset.UtcNow;
        connection.LastKnownEngineVersion = response.EngineVersion;
        connection.LastKnownCapabilities = response.EngineCapabilities;
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
        lock (_lifecycle) { _disposed = true; shutdown = _shutdown; _shutdown = null; }

        if (shutdown is not null)
        {
            try { await shutdown.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
            shutdown.Dispose();
        }
        _wake.Dispose();
    }
}
