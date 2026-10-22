using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Shell → Engine (spec 029): validate a SQL-auth connection string by opening a short-timeout
    /// test connection. Carries the password inside the connection string; it travels only over the
    /// ACL'd named pipe and is never logged (the handler logs via ConnectionDiagnostics.Describe).
    /// </summary>
    [MessagePackObject]
    public class TestSqlConnectionRequest
    {
        [Key(0)]
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>Spec 042: the <see cref="DatabaseKind"/> to test with (0 SQL Server, 1 PostgreSQL).</summary>
        [Key(1)]
        public int Kind { get; set; }

        /// <summary>Spec 043 follow-up: as <see cref="ConnectionInfo.CopyPasswordFromSessionId"/>, for the test a connect starts with.</summary>
        [Key(2)]
        public string? CopyPasswordFromSessionId { get; set; }

        /// <summary>
        /// Spec 043 follow-up (<c>credentials.sqlserver.v1</c>): a web connect's test saves the typed SQL Server password on the
        /// engine once the server accepted it (the test is the open that proves it, so no second login is made for it).
        /// </summary>
        [Key(3)]
        public bool RememberPassword { get; set; }
    }

    /// <summary>Engine → Shell (spec 029): result of <see cref="TestSqlConnectionRequest"/>.</summary>
    [MessagePackObject]
    public class TestSqlConnectionResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>SQL error text on failure (e.g. "Login failed for user 'sa'."); null on success.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>Spec 042 (FR-002): the server's product and version, e.g. <c>PostgreSQL 18.1</c>; null on failure or from older engines.</summary>
        [Key(2)]
        public string? ServerVersion { get; set; }

        /// <summary>Spec 042: a stable id from <see cref="DialectErrors"/> when the failure has one (a password is needed, credentials are required); otherwise null.</summary>
        [Key(3)]
        public string? ErrorCode { get; set; }
    }
}
