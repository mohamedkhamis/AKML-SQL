using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Serves a Phase B snapshot of the
    /// engine's <c>DatabaseCache</c> (schemas + objects + columns + foreign keys).
    /// Same callback-pure shape as <see cref="SchemaPhaseAHandler"/>; the lookup
    /// callback returns the Phase B view bytes plus checksum.
    /// </summary>
    public sealed class SchemaPhaseBHandler : IRpcRequestHandler<SchemaPhaseBRequest, SchemaPhaseBResponse>
    {
        private readonly Func<string, string, (byte[]? PhaseB, string? Checksum)> _phaseLookup;

        public SchemaPhaseBHandler(Func<string, string, (byte[]?, string?)> phaseLookup)
        {
            _phaseLookup = phaseLookup ?? throw new ArgumentNullException(nameof(phaseLookup));
        }

        public int RequestMessageType => MessageTypes.SchemaPhaseBRequest;
        public int ResponseMessageType => MessageTypes.SchemaPhaseBResponse;

        public Task<SchemaPhaseBResponse> HandleAsync(
            SchemaPhaseBRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var response = new SchemaPhaseBResponse
            {
                SessionId = request.SessionId,
                DatabaseName = request.DatabaseName,
            };

            if (string.IsNullOrEmpty(request.SessionId))
            {
                response.ErrorMessage = "SessionId is required.";
                return Task.FromResult(response);
            }
            if (string.IsNullOrEmpty(request.DatabaseName))
            {
                response.ErrorMessage = "DatabaseName is required.";
                return Task.FromResult(response);
            }

            byte[]? payload;
            string? checksum;
            try
            {
                (payload, checksum) = _phaseLookup(request.SessionId, request.DatabaseName);
            }
            catch (Exception ex)
            {
                response.ErrorMessage = $"Phase B snapshot failed: {ex.Message}";
                return Task.FromResult(response);
            }

            if (payload == null || payload.Length == 0)
            {
                response.ErrorMessage = "Engine has no cached schema for this session/database.";
                return Task.FromResult(response);
            }

            response.PhaseB = payload!;
            response.Checksum = checksum ?? string.Empty;
            response.HasConnection = true;
            return Task.FromResult(response);
        }
    }
}
