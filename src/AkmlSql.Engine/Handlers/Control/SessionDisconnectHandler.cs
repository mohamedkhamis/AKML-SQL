using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    /// <summary>
    /// The web edition's Disconnect (225 → 226, <c>session.disconnect.v1</c>). Before this the browser only forgot its side:
    /// the engine kept the persistent connection — and any open transaction with its locks — until an idle sweep twenty
    /// minutes later. Now the session's connection is closed (an open transaction rolled back, never committed) and the
    /// session holds no connection, so requests answer "no connection" until the next <c>ConnectionChanged</c>.
    /// </summary>
    public sealed class SessionDisconnectHandler : IRpcRequestHandler<SessionDisconnectRequest, SessionDisconnectResponse>
    {
        /// <summary>How long a command already running on the connection may keep it before the reply says so.</summary>
        internal static readonly TimeSpan RunningCommandWait = TimeSpan.FromSeconds(15);

        public int RequestMessageType => MessageTypes.SessionDisconnect;
        public int ResponseMessageType => MessageTypes.SessionDisconnectResult;

        public async Task<SessionDisconnectResponse> HandleAsync(SessionDisconnectRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var sessionId = request.SessionId ?? string.Empty;
            if (sessionId.Length == 0) return new SessionDisconnectResponse { Ok = false, ErrorMessage = "No session was named." };

            // Forget the connection first: a request that arrives while the close waits must not reopen it.
            ctx.Sessions.Disconnect(sessionId);
            DatabaseProvider.InvalidateSession(sessionId);

            try
            {
                bool rolledBack = ctx.SessionConnections != null
                    && await ctx.SessionConnections.CloseAsync(sessionId, RunningCommandWait, ct).ConfigureAwait(false);
                Log.Information("SessionDisconnect: session {Session} closed (rolledBack={RolledBack})", sessionId, rolledBack);
                return new SessionDisconnectResponse { Ok = true, RolledBack = rolledBack };
            }
            catch (TimeoutException)
            {
                return new SessionDisconnectResponse
                {
                    Ok = false,
                    ErrorMessage = "A statement is still running on the connection. It was asked to stop; the connection closes, " +
                                   "and an open transaction rolls back, when it ends.",
                };
            }
        }
    }
}
