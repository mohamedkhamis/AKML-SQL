using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Web/Shell → Engine (spec 030): enumerate the user-accessible databases on a server so the
    /// "Connect to SQL Server" dialog can offer a database dropdown instead of a free-text field.
    /// Carries a connection string pointed at <c>master</c>; any password inside it travels only
    /// over the ACL'd pipe/bridge and is never logged (the handler logs via ConnectionDiagnostics).
    /// </summary>
    [MessagePackObject]
    public class ListDatabasesRequest
    {
        [Key(0)]
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Spec 041 (R25, contracts/ipc.md): when set and <see cref="ConnectionString"/> is empty, list the databases
        /// of this session's server with the connection the engine already holds (the browser never resends a
        /// password). Sent only when the engine advertises <c>session.database.v1</c>.
        /// </summary>
        [Key(1)]
        public string? SessionId { get; set; }

        /// <summary>Spec 042: the <see cref="DatabaseKind"/> when no <see cref="SessionId"/> is given (0 SQL Server, 1 PostgreSQL).</summary>
        [Key(2)]
        public int Kind { get; set; }
    }

    /// <summary>Engine → Web/Shell (spec 030): result of <see cref="ListDatabasesRequest"/>.</summary>
    [MessagePackObject]
    public class ListDatabasesResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>Online, accessible database names (user DBs first, then system), sorted; empty on failure.</summary>
        [Key(1)]
        public List<string> Databases { get; set; } = new();

        /// <summary>SQL/connection error text on failure (e.g. "Login failed for user 'sa'."); null on success.</summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }

        /// <summary>Spec 042: a stable id from <see cref="DialectErrors"/> when the failure has one; otherwise null.</summary>
        [Key(3)]
        public string? ErrorCode { get; set; }
    }
}
