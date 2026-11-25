using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Pairing
{
    /// <summary>
    /// Spec 047 (ENG-001, <c>pairing.manage.v1</c>, contracts/engine-pairing.md §5): the browsers paired with this engine,
    /// newest first, with the caller's own marked. The token store is null when the engine needs no pairing (loopback only);
    /// then nothing is in use. Served only on the web bridge: a paired browser already holds the engine account's power, so
    /// listing and revoking other pairings gives it nothing new; the named pipe (SSMS) has no pairings to manage.
    /// </summary>
    public sealed class PairingListHandler : IRpcRequestHandler<PairingListRequest, PairingListResponse>
    {
        private readonly Func<BearerTokenStore?> _tokens;

        public PairingListHandler(Func<BearerTokenStore?> tokens) =>
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

        public int RequestMessageType => MessageTypes.PairingListRequest;
        public int ResponseMessageType => MessageTypes.PairingListResponse;
        public bool AllowsEmptyPayload => true;

        public Task<PairingListResponse> HandleAsync(PairingListRequest request, RpcContext ctx, CancellationToken ct)
        {
            PairingRequests.EnsureBridge();
            var store = _tokens();
            if (store == null) return Task.FromResult(new PairingListResponse { PairingUsed = false });

            var current = BridgeConnection.CurrentTokenHash;
            var entries = store.List()
                .OrderByDescending(e => e.MintedAt)
                .Select(e => new PairingEntry
                {
                    Id = e.TokenHash,
                    Label = string.IsNullOrWhiteSpace(e.BrowserLabel) ? "Web edition" : e.BrowserLabel,
                    MintedAt = e.MintedAt,
                    LastUsedAt = e.LastUsedAt,
                    ExpiresAt = e.TtlExpiresAt,
                    IsCurrent = current != null && string.Equals(e.TokenHash, current, StringComparison.Ordinal),
                })
                .ToArray();
            return Task.FromResult(new PairingListResponse { PairingUsed = true, Entries = entries });
        }
    }

    /// <summary>
    /// Spec 047 (ENG-001): removes one pairing and closes the bridge connections that authenticated with it, so a revoke
    /// takes effect at once rather than at that browser's next connect. Nothing else ever removes a pairing.
    /// </summary>
    public sealed class PairingRevokeHandler : IRpcRequestHandler<PairingRevokeRequest, PairingRevokeResponse>
    {
        private readonly Func<BearerTokenStore?> _tokens;
        private readonly Func<string, int> _closeConnections;

        public PairingRevokeHandler(Func<BearerTokenStore?> tokens, Func<string, int>? closeConnections = null)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _closeConnections = closeConnections ?? BridgeConnection.CloseConnectionsWithToken;
        }

        public int RequestMessageType => MessageTypes.PairingRevokeRequest;
        public int ResponseMessageType => MessageTypes.PairingRevokeResponse;

        public Task<PairingRevokeResponse> HandleAsync(PairingRevokeRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            PairingRequests.EnsureBridge();
            var store = _tokens();
            if (store == null)
                return Task.FromResult(new PairingRevokeResponse { Ok = false, Message = "This engine does not use pairing." });

            var entries = store.List();
            var target = entries.FirstOrDefault(e => string.Equals(e.TokenHash, request.Id, StringComparison.Ordinal));
            if (target == null || !store.RevokeByHash(target.TokenHash))
                return Task.FromResult(new PairingRevokeResponse { Ok = false, Message = "That pairing no longer exists." });

            var current = BridgeConnection.CurrentTokenHash;
            var caller = entries.FirstOrDefault(e => current != null && string.Equals(e.TokenHash, current, StringComparison.Ordinal));
            var closed = _closeConnections(target.TokenHash);
            // Labels only: a token hash is never logged.
            Log.Information("Pairing revoked: {Label} (by {Caller}); {Closed} open connection(s) closed",
                LabelOf(target.BrowserLabel), caller != null ? LabelOf(caller.BrowserLabel) : "a connection that needed no pairing", closed);
            return Task.FromResult(new PairingRevokeResponse { Ok = true, ClosedConnections = closed });
        }

        private static string LabelOf(string label) => string.IsNullOrWhiteSpace(label) ? "Web edition" : label;
    }

    internal static class PairingRequests
    {
        /// <summary>Pairings belong to the web bridge; a request from the named pipe is refused.</summary>
        public static void EnsureBridge()
        {
            if (BridgeSourceIp.Current is null)
                throw new InvalidOperationException("Pairing management is available on the web bridge only.");
        }
    }
}
