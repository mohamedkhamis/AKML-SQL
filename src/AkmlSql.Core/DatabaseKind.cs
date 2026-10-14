using System;

namespace AkmlSql.Core
{
    /// <summary>
    /// Spec 042 (PostgreSQL support). Which kind of database server a connection, session, schema
    /// cache, history entry or analysis rule belongs to. The numeric values are the wire values
    /// (MessagePack <c>Kind</c> keys), so they never change; SQL Server is 0 so that every existing
    /// record, message and config file keeps its meaning when the key is absent.
    /// </summary>
    public enum DatabaseKind
    {
        SqlServer = 0,
        PostgreSql = 1,
    }

    /// <summary>
    /// Spec 042 (data-model §9). The state of the transaction on a PostgreSQL session. The engine
    /// tracks it per statement (Npgsql exposes no public transaction status): <c>BEGIN</c> opens it,
    /// <c>COMMIT</c>/<c>ROLLBACK</c> close it, an error while open aborts it, and SQLSTATE 25P02
    /// (in_failed_sql_transaction) always means aborted. SQL Server sessions stay <see cref="None"/>.
    /// </summary>
    public enum TransactionState
    {
        None = 0,
        Open = 1,
        Aborted = 2,
    }

    /// <summary>
    /// String forms of <see cref="DatabaseKind"/> used where a number would be opaque: SQLite
    /// columns, JSON, the history search syntax and cache keys. Unknown or empty text is SQL Server,
    /// the safe default for anything written before spec 042.
    /// </summary>
    public static class DatabaseKindNames
    {
        public const string SqlServer = "sqlserver";
        public const string PostgreSql = "postgresql";

        /// <summary>The canonical lower-case name (<c>sqlserver</c> / <c>postgresql</c>).</summary>
        public static string ToName(DatabaseKind kind)
        {
            return kind == DatabaseKind.PostgreSql ? PostgreSql : SqlServer;
        }

        /// <summary>
        /// Parses a canonical name or a common alias (<c>pg</c>, <c>postgres</c>, <c>mssql</c>,
        /// <c>tsql</c>), ignoring case and surrounding white space. Anything else is SQL Server.
        /// </summary>
        public static DatabaseKind Parse(string? text)
        {
            return TryParse(text, out var kind) ? kind : DatabaseKind.SqlServer;
        }

        /// <summary>True when <paramref name="text"/> is a known kind name or alias.</summary>
        public static bool TryParse(string? text, out DatabaseKind kind)
        {
            kind = DatabaseKind.SqlServer;
            if (text == null) return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "postgresql":
                case "postgres":
                case "pg":
                case "pgsql":
                    kind = DatabaseKind.PostgreSql;
                    return true;
                case "sqlserver":
                case "mssql":
                case "tsql":
                case "t-sql":
                    kind = DatabaseKind.SqlServer;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Converts a wire value; any value other than 1 is SQL Server.</summary>
        public static DatabaseKind FromInt(int value)
        {
            return value == (int)DatabaseKind.PostgreSql ? DatabaseKind.PostgreSql : DatabaseKind.SqlServer;
        }
    }
}
