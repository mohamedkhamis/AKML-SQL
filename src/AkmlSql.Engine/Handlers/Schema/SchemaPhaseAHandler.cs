using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Serves a Phase A snapshot of the
    /// engine's <c>DatabaseCache</c> for the requested (session, database).
    ///
    /// <para>
    /// Callback-pure design mirrors <see cref="SchemaChecksumHandler"/> and
    /// <see cref="SchemaIdentifyHandler"/>: the constructor takes a
    /// <c>Func&lt;sessionId, databaseName, (byte[]?, string?)&gt;</c> that returns the
    /// serialised payload bytes plus the cache checksum, or <c>(null, null)</c> when
    /// no cache exists for the session. Production wiring in
    /// <c>PipeRpcServer.Handlers.cs</c> plugs the live <c>SchemaCacheManager</c>;
    /// tests pass a stub.
    /// </para>
    ///
    /// <para><b>Naming note:</b> The existing engine convention passes <c>SessionId</c>
    /// where the cache key expects a server identity (see
    /// <c>SchemaStatusHandler</c> and <c>CompletionHandler</c>). The Phase A/B
    /// handlers follow the same pattern for consistency; the underlying inconsistency
    /// pre-dates spec 021 and is tracked separately.</para>
    /// </summary>
    public sealed class SchemaPhaseAHandler : IRpcRequestHandler<SchemaPhaseARequest, SchemaPhaseAResponse>
    {
        private readonly Func<string, string, (byte[]? PhaseA, string? Checksum)> _phaseLookup;

        public SchemaPhaseAHandler(Func<string, string, (byte[]?, string?)> phaseLookup)
        {
            _phaseLookup = phaseLookup ?? throw new ArgumentNullException(nameof(phaseLookup));
        }

        public int RequestMessageType => MessageTypes.SchemaPhaseARequest;
        public int ResponseMessageType => MessageTypes.SchemaPhaseAResponse;

        public Task<SchemaPhaseAResponse> HandleAsync(
            SchemaPhaseARequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var response = new SchemaPhaseAResponse
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
                response.ErrorMessage = $"Phase A snapshot failed: {ex.Message}";
                return Task.FromResult(response);
            }

            if (payload == null || payload.Length == 0)
            {
                response.ErrorMessage = "Engine has no cached schema for this session/database.";
                return Task.FromResult(response);
            }

            response.PhaseA = payload!;
            response.Checksum = checksum ?? string.Empty;
            response.HasConnection = true;
            return Task.FromResult(response);
        }
    }
}
