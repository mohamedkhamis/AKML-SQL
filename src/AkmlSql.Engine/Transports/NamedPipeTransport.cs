using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Core.Ipc;
using Serilog;
#pragma warning disable CA1416 // Windows-only API surface (named pipes, ACL).

namespace AkmlSql.Engine.Transports;

/// <summary>
/// Named-pipe transport (spec 022 M0 closure). Owns the pipe ACL, accept loop and framed
/// read/write only -- no service construction or handler registration. T027: implements
/// <see cref="IRpcTransport"/> like the in-process and WebSocket transports; each decoded
/// <see cref="RpcMessage"/> is forwarded to the <see cref="RequestReceived"/> subscriber
/// (the host wires <see cref="RpcRouter.RouteAsync"/>).
/// </summary>
public sealed class NamedPipeTransport : IRpcTransport
{
    private readonly string _pipeName;
    private CancellationTokenSource? _acceptCts;
    private Task? _acceptLoop;
    private bool _disposed;

    public NamedPipeTransport(string pipeName) => _pipeName = pipeName;

    /// <inheritdoc />
    public event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NamedPipeTransport));
        if (_acceptLoop != null) throw new InvalidOperationException("NamedPipeTransport already started.");
        _acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => RunAsync(_acceptCts.Token));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the accept loop, blocking until <paramref name="ct"/> is cancelled. The direct
    /// entry point for <c>EngineHost</c>; <see cref="StartAsync"/> wraps it on a background task.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipeSecurity = CreatePipeSecurity();
            await using var pipe = NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 65536,
                outBufferSize: 65536,
                pipeSecurity);

            Log.Information("Waiting for client connection on pipe {Pipe}", _pipeName);
            await pipe.WaitForConnectionAsync(ct);
            Log.Information("Client connected.");

            try
            {
                await HandleClientAsync(pipe, ct);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Client disconnected (pipe broken).");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling client.");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Serializes response frames: concurrent AI dispatches and the serial loop must never
        // interleave partial frames on the pipe (frame = 8-byte header + payload).
        using var writeLock = new SemaphoreSlim(1, 1);

        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var message = await FrameProtocol.ReadFramedAsync(pipe, ct);
            if (message == null) break;

            if (IsLongRunningType(message.MessageType))
            {
                // AI requests run for seconds-to-minutes (a provider call the shell may even
                // have abandoned — the dispatch token is the pipe-lifetime token, so nothing
                // cancels the handler when the caller times out). Handling them inline froze
                // the WHOLE engine: completions, schema-status polls ("Refreshing schema
                // cache…" forever) and every queued AI request ("A task was canceled") sat
                // behind one slow call. Dispatch them on the pool; responses correlate by
                // RequestId on the shell side, so out-of-order delivery is already handled.
                _ = Task.Run(() => DispatchAndRespondAsync(pipe, message, writeLock, ct), ct);
            }
            else
            {
                // Fast handlers stay strictly serial — the pre-existing ordering/thread-safety
                // contract for sessions, history (SQLite) and schema cache is untouched.
                await DispatchAndRespondAsync(pipe, message, writeLock, ct);
            }
        }
    }

    /// <summary>The AI request family (70–78) — the only handlers that block on provider I/O.</summary>
    private static bool IsLongRunningType(int messageType) =>
        messageType >= MessageTypes.AiTextToSql && messageType <= MessageTypes.AiStreamCancel;

    private async Task DispatchAndRespondAsync(
        NamedPipeServerStream pipe, RpcMessage message, SemaphoreSlim writeLock, CancellationToken ct)
    {
        try
        {
            var response = await DispatchAsync(message, ct);
            if (response == null) return;

            await writeLock.WaitAsync(ct);
            try
            {
                await FrameProtocol.WriteFramedAsync(pipe, response, ct);
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or client disconnect — the read loop observes ct/pipe state itself.
        }
        catch (IOException ex)
        {
            // Pipe broke while a late AI response was being written; the read loop notices too.
            Log.Debug(ex, "NamedPipeTransport: pipe closed while writing response for type {Type}",
                message.MessageType);
        }
        catch (ObjectDisposedException)
        {
            // Pipe/lock disposed after disconnect while a background AI dispatch was finishing.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "NamedPipeTransport: background dispatch failed for type {Type}",
                message.MessageType);
        }
    }

    private async Task<RpcMessage?> DispatchAsync(RpcMessage message, CancellationToken ct)
    {
        var handler = RequestReceived;
        if (handler == null)
        {
            Log.Error("NamedPipeTransport: no RequestReceived subscriber; dropping message type {Type}",
                message.MessageType);
            return RpcResponseFactory.CreateErrorResponse("Engine transport not wired.", message.RequestId);
        }

        try
        {
            return await handler(message, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error dispatching message type {Type}", message.MessageType);
            return RpcResponseFactory.CreateErrorResponse(ex.Message, message.RequestId);
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
            security.AddAccessRule(new PipeAccessRule(
                currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { _acceptCts?.Cancel(); } catch { /* ignore */ }
        if (_acceptLoop != null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch { /* swallow accept-loop shutdown errors (incl. OCE) */ }
        }
        _acceptCts?.Dispose();
    }
}
