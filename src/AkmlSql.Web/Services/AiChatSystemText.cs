using AkmlSql.Engine.Ai;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (FR-080, FR-090). The system text of the AI chat panel for the dialect of the active connection. SQL Server keeps the
/// text the panel always had, word for word; PostgreSQL names PostgreSQL, forbids T-SQL and says nothing about SQL Server.
/// </summary>
internal static class AiChatSystemText
{
    public const string SqlServer =
        "You are a SQL Server expert assisting an experienced developer through " +
        "AKML SQL. Be concise. When you give SQL, use canonical T-SQL formatting. " +
        "When you're unsure of the schema, ask before guessing.";

    public static string For(SqlDialect dialect, string? serverVersion)
    {
        if (dialect != SqlDialect.PostgreSql) return SqlServer;

        var version = string.IsNullOrWhiteSpace(serverVersion)
            ? string.Empty
            : serverVersion.TrimStart().StartsWith("PostgreSQL", System.StringComparison.OrdinalIgnoreCase)
                ? $" The server is {serverVersion.Trim()}."
                : $" The server is PostgreSQL {serverVersion.Trim()}.";

        return "You are a PostgreSQL expert assisting an experienced developer through AKML SQL." + version +
               " Be concise. When you give SQL, use PostgreSQL syntax with conventional formatting. " +
               "Do not use T-SQL. When you're unsure of the schema, ask before guessing.";
    }
}
