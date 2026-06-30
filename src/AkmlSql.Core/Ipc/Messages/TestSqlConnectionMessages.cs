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
    }
}
