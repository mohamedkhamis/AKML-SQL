using System;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. NOTIFICATION handler (registered via RegisterRaw returning a null frame,
    /// like AiStreamCancel=78). Signals the per-session CancellationTokenSource for a QueryId.
    ///
    /// <para>HONEST SCOPE: on the single serial browser socket this reliably cancels a QUEUED execute
    /// (one still waiting on the per-session semaphore) and is the CTS-registry hook. It cannot
    /// interrupt a command that is actively block-awaiting on the same socket — the ExecuteCancel
    /// frame can't be read until the active command returns. The real bound on a runaway active query
    /// is commandTimeout + the row/byte cap. Kept so queued-cancel works now and as the wiring point
    /// for a future control-channel.</para>
    /// </summary>
    public sealed class ExecuteCancelHandler
    {
        private readonly SessionConnectionRegistry _registry;

        public ExecuteCancelHandler(SessionConnectionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Handle(RpcMessage request)
        {
            try
            {
                if (request.Payload == null) return;
                var req = MessagePackSerializer.Deserialize<ExecuteCancelRequest>(request.Payload);
                var conn = _registry.TryGet(req.SessionId);
                bool found = conn != null && conn.TryCancel(req.QueryId);
                Log.Debug("ExecuteCancel: session={Session} queryId={QueryId} found={Found}",
                    req.SessionId, req.QueryId, found);
            }
            catch (Exception ex)
            {
                // Notification — ack and drop. Never throw out of a fire-and-forget path.
                Log.Debug(ex, "ExecuteCancel: ignored malformed/late cancel.");
            }
        }
    }
}
