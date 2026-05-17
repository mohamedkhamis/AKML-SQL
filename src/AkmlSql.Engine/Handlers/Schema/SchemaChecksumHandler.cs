using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 schema-cache change detection. The browser polls
    /// every 30 s with (sessionId, server, db); the handler asks the engine's
    /// connection (resolved via a checksum-fetcher callback) for the current
    /// <c>CHECKSUM_AGG(BINARY_CHECKSUM(...))</c> result and returns it. The browser
    /// compares string-equal to its cached value and triggers a Phase A refresh on
    /// drift.
    ///
    /// <para>
    /// Same callback-pure design as <see cref="SchemaIdentifyHandler"/>: the handler
    /// takes a <c>Func&lt;string, string&gt;</c> that maps sessionId to the checksum.
    /// Production wiring resolves it from the active SqlConnection on the session;
    /// tests pass a stub.
    /// </para>
    /// </summary>
    public sealed class SchemaChecksumHandler : IRpcRequestHandler<SchemaChecksumRequest, SchemaChecksumResponse>
    {
        private readonly Func<string, string?> _checksumFetcher;

        public SchemaChecksumHandler(Func<string, string?> checksumFetcher)
        {
            _checksumFetcher = checksumFetcher ?? throw new ArgumentNullException(nameof(checksumFetcher));
        }

        public int RequestMessageType => MessageTypes.SchemaChecksumRequest;
        public int ResponseMessageType => MessageTypes.SchemaChecksumResponse;

        public Task<SchemaChecksumResponse> HandleAsync(
            SchemaChecksumRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var response = new SchemaChecksumResponse
            {
                SessionId = request.SessionId,
                ServerCanonicalIdentity = request.ServerCanonicalIdentity,
                DatabaseName = request.DatabaseName,
            };

            if (string.IsNullOrEmpty(request.SessionId))
            {
                response.ErrorMessage = "SessionId is required.";
                return Task.FromResult(response);
            }

            string? checksum;
            try
            {
                checksum = _checksumFetcher(request.SessionId);
            }
            catch (Exception ex)
            {
                response.ErrorMessage = $"Checksum fetch failed: {ex.Message}";
                return Task.FromResult(response);
            }

            if (string.IsNullOrEmpty(checksum))
            {
                response.ErrorMessage = "Engine has no live connection for this session.";
                return Task.FromResult(response);
            }

            response.Checksum = checksum!;
            response.HasConnection = true;
            return Task.FromResult(response);
        }
    }
}
