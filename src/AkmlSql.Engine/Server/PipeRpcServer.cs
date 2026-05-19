using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Core.Ipc;
using Serilog;
#pragma warning disable CA1416 // Windows-only API surface (named pipes, ACL).

namespace AkmlSql.Engine.Server;

/// <summary>
/// Named-pipe transport. Spec 022 (M0 closure) -- P2 / US2: trimmed to frame I/O + pipe
/// lifecycle only. Service construction and handler registration live in
/// <see cref="EngineComposition"/> + <see cref="EngineHandlerRegistry"/>. Dispatch routes via
/// <see cref="RpcRouter.RouteAsync"/>.
/// </summary>
public sealed class PipeRpcServer
{
    private readonly string _pipeName;
    private readonly RpcContext _ctx;
    private readonly RpcRouter _router;

    public PipeRpcServer(string pipeName, RpcContext ctx, RpcRouter router)
    {
        _pipeName = pipeName;
        _ctx = ctx;
        _router = router;
    }

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
        try
        {
            var response = await _router.RouteAsync(message, _ctx, ct).ConfigureAwait(false);
            if (response == null && !_router.IsRegistered(message.MessageType))
            {
                Log.Warning("Unknown message type: {Type}", message.MessageType);
            }
            return response;
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
}
