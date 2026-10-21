using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Snippets
{
    // Spec 021 (web edition) -- M0.3 task T015. Five typed handlers wrapping the existing
    // SnippetRequestHandler. SnippetExpand needs the active session's database name from
    // RpcContext.Sessions; the rest are direct wrappers.

    public sealed class SnippetExpandHandler : IRpcRequestHandler<SnippetExpandRequest, SnippetExpandResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetExpandHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetExpand;
        public int ResponseMessageType => MessageTypes.SnippetExpandResult;
        public Task<SnippetExpandResponse> HandleAsync(SnippetExpandRequest request, RpcContext ctx, CancellationToken ct)
        {
            var session = ctx.Sessions.GetSession(request.SessionId);
            // Spec 042: a PostgreSQL session expands PostgreSQL snippets
            var dialect = SnippetDialects.For(session?.Kind ?? AkmlSql.Core.DatabaseKind.SqlServer);
            // Spec 030 (re-audit): thread the server name so the $SERVER$ placeholder resolves at
            // runtime. Previously only DatabaseName was passed, so $SERVER$ silently expanded to "".
            // Spec 030 FR-037 parity: thread the SQL login so $USER$ resolves to the connected login.
            // Integrated-auth connections yield "" from UserID → resolver falls back to Environment.UserName.
            return Task.FromResult(_inner.HandleExpand(
                request,
                session?.DatabaseName,
                DeriveServerName(session),
                DeriveLogin(session),
                dialect));
        }

        // Derive the server from the session's connection string — the same scrub SessionManager /
        // SessionTrackerBridge use. Null/empty/unparseable → "" so $SERVER$ falls back to empty rather
        // than throwing. Spec 042: a PostgreSQL session's server is host:port, as the web edition names it.
        internal static string DeriveServerName(SessionState? session)
        {
            if (session == null || string.IsNullOrEmpty(session.ConnectionString)) return string.Empty;
            try
            {
                if (session.Kind == AkmlSql.Core.DatabaseKind.PostgreSql)
                {
                    var pg = new Npgsql.NpgsqlConnectionStringBuilder(session.ConnectionString);
                    return string.IsNullOrEmpty(pg.Host) ? string.Empty : pg.Host + ":" + pg.Port;
                }
                return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(session.ConnectionString).DataSource ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // Derive the SQL login from the connection string UserID. Returns "" for integrated-auth
        // (Windows auth) connections so the resolver falls back to Environment.UserName, which is a
        // coherent approximation. Note: live SYSTEM_USER query was intentionally not implemented to
        // keep the handler sync/IO-free. Spec 042: a PostgreSQL session's login is its user name.
        internal static string DeriveLogin(SessionState? session)
        {
            if (session == null || string.IsNullOrEmpty(session.ConnectionString)) return string.Empty;
            try
            {
                if (session.Kind == AkmlSql.Core.DatabaseKind.PostgreSql)
                    return new Npgsql.NpgsqlConnectionStringBuilder(session.ConnectionString).Username ?? string.Empty;
                return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(session.ConnectionString).UserID ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }

    public sealed class SnippetListHandler : IRpcRequestHandler<SnippetListRequest, SnippetListResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetListHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetList;
        public int ResponseMessageType => MessageTypes.SnippetListResult;
        public Task<SnippetListResponse> HandleAsync(SnippetListRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleList(request));
    }

    public sealed class SnippetSaveHandler : IRpcRequestHandler<SnippetSaveRequest, SnippetSaveResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetSaveHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetSave;
        public int ResponseMessageType => MessageTypes.SnippetSaveResult;
        public Task<SnippetSaveResponse> HandleAsync(SnippetSaveRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleSave(request));
    }

    public sealed class SnippetDeleteHandler : IRpcRequestHandler<SnippetDeleteRequest, SnippetDeleteResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetDeleteHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetDelete;
        public int ResponseMessageType => MessageTypes.SnippetDeleteResult;
        public Task<SnippetDeleteResponse> HandleAsync(SnippetDeleteRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleDelete(request));
    }

    public sealed class SnippetImportHandler : IRpcRequestHandler<SnippetImportRequest, SnippetImportResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetImportHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetImport;
        public int ResponseMessageType => MessageTypes.SnippetImportResult;
        public Task<SnippetImportResponse> HandleAsync(SnippetImportRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleImport(request));
    }
}
