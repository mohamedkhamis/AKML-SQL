using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Serilog;

namespace AkmlSql.Shell.Shared.Ipc
{
    public class PipeRpcClient(string pipeName) : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<RpcMessage>> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _nextRequestId;
        private CancellationTokenSource _readerCts;
        private Task _readerTask;
        private bool _disposed;
        private CancellationTokenSource _heartbeatCts;
        private Task _heartbeatTask;

        /// <summary>
        /// Raised when the engine is detected as unresponsive (heartbeat timeout).
        /// </summary>
        public event EventHandler EngineUnresponsive;

        public bool IsConnected => _pipe?.IsConnected == true;

        public async Task ConnectAsync(int maxRetries = 15, int retryDelayMs = 300, int connectTimeoutMs = 2000, CancellationToken ct = default)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(connectTimeoutMs, ct);
                    Log.Information("Connected to engine pipe {Pipe}", pipeName);

                    _readerCts = new CancellationTokenSource();
                    _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token));
                    return;
                }
                catch (TimeoutException) when (attempt < maxRetries - 1)
                {
                    await Task.Delay(retryDelayMs, ct);
                }
            }

            throw new IOException($"Failed to connect to engine pipe after {maxRetries} attempts.");
        }

        public async Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct = default)
        {
            var msg = new RpcMessage
            {
                MessageType = messageType,
                RequestId = 0,
                Payload = MessagePackSerializer.Serialize(payload)
            };

            await WriteAsync(msg, ct);
        }

        public async Task<T> SendRequestAsync<T, TPayload>(int messageType, TPayload payload, int timeoutMs = 5000, CancellationToken ct = default)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = tcs;
            Log.Information("[PipeRpc] SendRequest req#{Req} type={Type} timeout={TimeoutMs}ms pending.Count={Count}",
                requestId, messageType, timeoutMs, _pending.Count);

            try
            {
                var msg = new RpcMessage
                {
                    MessageType = messageType,
                    RequestId = requestId,
                    Payload = MessagePackSerializer.Serialize(payload)
                };

                // Arm the timeout BEFORE the write — if the engine has stopped reading,
                // pipe back-pressure (or contention on _writeLock) would otherwise block
                // WriteAsync indefinitely because the timeout isn't in play yet.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);

                await WriteAsync(msg, timeoutCts.Token).ConfigureAwait(false);

                var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled());
                var awaitTs = DateTime.UtcNow;
                Log.Information("[PipeRpc] req#{Req} awaiting tcs.Task", requestId);
                try
                {
                    RpcMessage response;
                    try
                    {
                        response = await tcs.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("[PipeRpc] req#{Req} CANCELLED after {Ms}ms (inner timeout fired)",
                            requestId, (DateTime.UtcNow - awaitTs).TotalMilliseconds);
                        throw;
                    }
                    Log.Information("[PipeRpc] req#{Req} tcs.Task COMPLETED in {Ms}ms (msgType={MsgType})",
                        requestId, (DateTime.UtcNow - awaitTs).TotalMilliseconds, response.MessageType);
                    if (response.MessageType == MessageTypes.Error)
                    {
                        var error = MessagePackSerializer.Deserialize<ErrorInfo>(response.Payload);
                        throw new InvalidOperationException($"Engine error {error.Code}: {error.Message}");
                    }
                    return MessagePackSerializer.Deserialize<T>(response.Payload);
                }
                finally
                {
                    registration.Dispose();
                }
            }
            finally
            {
                _pending.TryRemove(requestId, out _);
                Log.Debug("[PipeRpc] req#{Req} cleanup, remaining pending={Count}", requestId, _pending.Count);
            }
        }

        private async Task WriteAsync(RpcMessage msg, CancellationToken ct)
        {
            var lockTs = DateTime.UtcNow;
            Log.Information("[PipeRpc] WriteAsync req#{Req} type={Type} payload={Bytes}B awaiting _writeLock",
                msg.RequestId, msg.MessageType, msg.Payload?.Length ?? 0);
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            var lockMs = (DateTime.UtcNow - lockTs).TotalMilliseconds;
            if (lockMs > 50)
                Log.Warning("[PipeRpc] WriteAsync req#{Req} _writeLock acquired in {Ms}ms (CONTENDED)", msg.RequestId, lockMs);
            else
                Log.Debug("[PipeRpc] WriteAsync req#{Req} _writeLock acquired in {Ms}ms", msg.RequestId, lockMs);
            try
            {
                var wrTs = DateTime.UtcNow;
                await FrameProtocol.WriteFramedAsync(_pipe, msg, ct).ConfigureAwait(false);
                Log.Debug("[PipeRpc] WriteAsync req#{Req} WriteFramedAsync done in {Ms}ms",
                    msg.RequestId, (DateTime.UtcNow - wrTs).TotalMilliseconds);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            int frameCount = 0;
            try
            {
                while (!ct.IsCancellationRequested && _pipe.IsConnected)
                {
                    var msg = await FrameProtocol.ReadFramedAsync(_pipe, ct).ConfigureAwait(false);
                    if (msg == null)
                    {
                        Log.Information("[PipeRpc] ReadLoop received null frame after {Count} frames — pipe closed", frameCount);
                        break;
                    }
                    frameCount++;
                    if ((frameCount & 0xFF) == 0)
                        Log.Debug("[PipeRpc] ReadLoop received {Count} frames so far (latest type={Type} req#{Req})",
                            frameCount, msg.MessageType, msg.RequestId);

                    if (msg.RequestId != 0 && _pending.TryGetValue(msg.RequestId, out var tcs))
                    {
                        tcs.TrySetResult(msg);
                    }
                    else if (msg.RequestId != 0)
                    {
                        Log.Warning("[PipeRpc] ReadLoop: orphan response req#{Req} type={Type} — no pending entry (timed out?)",
                            msg.RequestId, msg.MessageType);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                Log.Debug(ex, "Pipe reader: connection closed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Pipe reader error");
            }
            finally
            {
                if (_pending.Count > 0)
                    Log.Warning("[PipeRpc] ReadLoop EXIT — failing {Count} pending requests with 'Engine disconnected'", _pending.Count);
                foreach (var tcs in _pending.Values)
                    tcs.TrySetException(new IOException("Engine disconnected"));
                _pending.Clear();
            }
        }

        /// <summary>
        /// T091: Starts a heartbeat that sends Ping every 15s and expects Pong within 5s.
        /// If the engine does not respond, the EngineUnresponsive event is raised.
        /// </summary>
        public void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
        }

        /// <summary>
        /// Stops the heartbeat loop.
        /// </summary>
        public void StopHeartbeat()
        {
            if (_heartbeatCts != null)
            {
                _heartbeatCts.Cancel();
                try { _heartbeatTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* Intentional: best-effort task wait during cleanup */ }
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
            }
            _heartbeatTask = null;
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, ct);
                    if (!IsConnected)
                    {
                        break;
                    }

                    // Send Ping, expect Pong within 5 seconds
                    var pingPayload = new EngineStatusInfo();
                    await SendRequestAsync<EngineStatusInfo, EngineStatusInfo>(MessageTypes.Ping, pingPayload, timeoutMs: 5000, ct: ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Heartbeat: engine unresponsive");
                    EngineUnresponsive?.Invoke(this, EventArgs.Empty);
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopHeartbeat();
            _readerCts?.Cancel();
            try { _readerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* Intentional: best-effort task wait during cleanup */ }
            _pipe?.Dispose();
            _writeLock.Dispose();
            _readerCts?.Dispose();
        }
    }
}
