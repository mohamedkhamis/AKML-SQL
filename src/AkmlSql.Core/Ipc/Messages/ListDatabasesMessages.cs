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
    }
}
