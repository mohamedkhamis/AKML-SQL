using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 task T104. Resolves the canonical (server, database)
    /// pair the web client uses as the composite IndexedDB schema-cache key
    /// (<c>specs/021-web-edition/contracts/schema-cache-shape.md</c>, clarification 3 in
    /// spec.md). The pair is stable across host-string variations: two distinct DNS aliases
    /// pointing at the same SQL Server resolve to the same identity and therefore share one
    /// cache entry.
    ///
    /// <para>
    /// The handler depends on two callbacks rather than concrete services so it can be
    /// unit-tested without standing up a live SQL Server connection:
    /// <list type="bullet">
    ///   <item><paramref name="databaseLookup"/> returns the database name currently
    ///   bound to the session (typically <c>SessionManager.GetSession(id)?.DatabaseName</c>).</item>
    ///   <item><paramref name="identityResolver"/> issues the actual canonical-identity
    ///   query against the session's open connection (typically
    ///   <c>SELECT @@SERVERNAME</c> with <c>SERVERPROPERTY('ServerName')</c> fallback).</item>
    /// </list>
    /// The production wiring lives on the engine side; the handler itself stays pure.
    /// </para>
    /// </summary>
    public sealed class SchemaIdentifyHandler : IRpcRequestHandler<SchemaIdentifyRequest, SchemaIdentifyResponse>
    {
        private readonly Func<string, string?> _databaseLookup;
        private readonly Func<string, string?> _identityResolver;

        public SchemaIdentifyHandler(
            Func<string, string?> databaseLookup,
            Func<string, string?> identityResolver)
        {
            _databaseLookup = databaseLookup ?? throw new ArgumentNullException(nameof(databaseLookup));
            _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        }

        public int RequestMessageType => MessageTypes.SchemaIdentifyRequest;
        public int ResponseMessageType => MessageTypes.SchemaIdentifyResponse;

        public Task<SchemaIdentifyResponse> HandleAsync(
            SchemaIdentifyRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var response = new SchemaIdentifyResponse { SessionId = request.SessionId };

            if (string.IsNullOrEmpty(request.SessionId))
            {
                response.ErrorMessage = "SessionId is required.";
                return Task.FromResult(response);
            }

            string? databaseName;
            string? identity;
            try
            {
                databaseName = _databaseLookup(request.SessionId);
                identity = _identityResolver(request.SessionId);
            }
            catch (Exception ex)
            {
                // The resolver runs SQL; bubbling the exception to the wire would crash the
                // bridge. Report the failure as "no connection" with the message so the
                // browser surfaces it in the diagnostics bundle (FR-013a).
                response.ErrorMessage = $"Failed to resolve schema identity: {ex.Message}";
                return Task.FromResult(response);
            }

            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(databaseName))
            {
                response.ErrorMessage = "Engine has no live connection for this session.";
                return Task.FromResult(response);
            }

            response.ServerCanonicalIdentity = identity!;
            response.DatabaseName = databaseName!;
            response.HasConnection = true;
            return Task.FromResult(response);
        }
    }
}
