using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Analysis
{
    /// <summary>
    /// Typed handler for SessionSuppression (MessageType 36 -> 136): the "Disable RULE for this
    /// session" quick fix and the Manage Rules dialog's session strip.
    ///
    /// <para>
    /// Backed by <see cref="SessionSuppressionStore"/>, which lives only in this process. Because
    /// the engine is started per shell instance, its lifetime is exactly the IDE session — which is
    /// what makes this scope different from the config.json one: closing SSMS clears it, and
    /// nothing was ever written to the user's script or settings.
    /// </para>
    ///
    /// <para>
    /// Every action returns the full list, so the caller can render the current state from any
    /// response without a follow-up List call.
    /// </para>
    /// </summary>
    public sealed class SessionSuppressionHandler
        : IRpcRequestHandler<SessionSuppressionRequest, SessionSuppressionResponse>
    {
        private readonly SessionSuppressionStore _store;

        public SessionSuppressionHandler(SessionSuppressionStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public int RequestMessageType => MessageTypes.SessionSuppression;
        public int ResponseMessageType => MessageTypes.SessionSuppressionResult;
        public bool AllowsEmptyPayload => true;

        public Task<SessionSuppressionResponse> HandleAsync(
            SessionSuppressionRequest request, RpcContext ctx, CancellationToken ct)
        {
            try
            {
                var action = request?.Action ?? SessionSuppressionActions.List;
                var ruleId = request?.RuleId ?? string.Empty;

                switch (action)
                {
                    case SessionSuppressionActions.Add:
                        _store.Add(ruleId);
                        Log.Information("SessionSuppression: {Rule} suppressed for this session", ruleId);
                        break;

                    case SessionSuppressionActions.Remove:
                        _store.Remove(ruleId);
                        Log.Information("SessionSuppression: {Rule} restored for this session", ruleId);
                        break;

                    case SessionSuppressionActions.Clear:
                        _store.Clear();
                        Log.Information("SessionSuppression: all session suppressions cleared");
                        break;

                    case SessionSuppressionActions.List:
                        break;

                    default:
                        return Task.FromResult(new SessionSuppressionResponse
                        {
                            Success = false,
                            SuppressedRules = _store.Snapshot(),
                            Error = $"Unknown session-suppression action {action}.",
                        });
                }

                return Task.FromResult(new SessionSuppressionResponse
                {
                    Success = true,
                    SuppressedRules = _store.Snapshot(),
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SessionSuppression failed");
                return Task.FromResult(new SessionSuppressionResponse
                {
                    Success = false,
                    Error = ex.Message,
                });
            }
        }
    }
}
