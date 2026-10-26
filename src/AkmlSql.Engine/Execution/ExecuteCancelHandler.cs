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
    /// <para>Spec 043 (research R3): the WebSocket transport answers a browser's requests side by side and reads notifications
    /// while a request runs, so this cancel reaches an execute that is RUNNING, not only one still queued on the session gate:
    /// the CTS is linked into the command, SqlClient (or Npgsql) asks the server to stop the statement, and the batches after it
    /// are skipped (RunningStatementCancelIntegrationTests). The execute registers its CTS as soon as it is read, so a cancel
    /// also stops one still queued behind another run on the session or still waiting for its connection to open. The
    /// execute's time limit and the row/byte caps still bound a runaway script.</para>
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
