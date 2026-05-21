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
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var message = await FrameProtocol.ReadFramedAsync(pipe, ct);
            if (message == null) break;

            var response = await DispatchAsync(message, ct);
            if (response != null)
            {
                await FrameProtocol.WriteFramedAsync(pipe, response, ct);
            }
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
