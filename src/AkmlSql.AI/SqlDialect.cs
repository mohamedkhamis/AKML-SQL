using AkmlSql.Core;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// Spec 042 (FR-080). The SQL language an AI answer must be written in. It follows the kind of the active connection,
/// so a prompt never asks for T-SQL on a PostgreSQL connection or the other way round. <see cref="SqlServer"/> is the
/// default everywhere: callers that do not name a dialect get the text that existed before PostgreSQL support.
/// </summary>
public enum SqlDialect
{
    SqlServer = 0,
    PostgreSql = 1,
}

/// <summary>Mapping from the connection kind to the prompt dialect.</summary>
public static class SqlDialects
{
    /// <summary>The dialect for a connection kind. An unknown kind is SQL Server, the safe default.</summary>
    public static SqlDialect From(DatabaseKind kind) =>
        kind == DatabaseKind.PostgreSql ? SqlDialect.PostgreSql : SqlDialect.SqlServer;
}
