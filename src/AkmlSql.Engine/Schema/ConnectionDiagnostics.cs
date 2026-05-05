using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Schema;

/// <summary>
/// Helpers that turn a <see cref="SqlConnection"/> or raw connection string into a
/// short, passwordless description suitable for dropping into log messages. Every
/// engine-side SQL operation should feed its connection string through
/// <see cref="Describe"/> so the next "why did schema not load?" investigation can
/// read the log and immediately see which server, which database, and which auth
/// method the engine actually tried.
/// </summary>
public static class ConnectionDiagnostics
{
    /// <summary>
    /// Returns a short, safe-to-log description of the connection: server,
    /// initial catalog, and authentication method. Passwords are never included.
    /// Returns <c>"(invalid connection string)"</c> if the string can't be parsed.
    /// </summary>
    public static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(null connection string)";
        }

        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            var auth = DescribeAuth(b);
            var catalog = string.IsNullOrWhiteSpace(b.InitialCatalog) ? "(none)" : b.InitialCatalog;
            var server = string.IsNullOrWhiteSpace(b.DataSource) ? "(none)" : b.DataSource;
            var timeout = b.ConnectTimeout;
            return $"server='{server}' catalog='{catalog}' auth='{auth}' timeout={timeout}s";
        }
        catch
        {
            return "(invalid connection string)";
        }
    }

    /// <summary>
    /// Extracts just the authentication-method label — useful for compact log
    /// lines that already include server/db elsewhere.
    /// </summary>
    public static string DescribeAuth(SqlConnectionStringBuilder b)
    {
        // "Authentication=..." takes precedence over Integrated Security in
        // Microsoft.Data.SqlClient; report whichever is active.
        if (b.Authentication != SqlAuthenticationMethod.NotSpecified)
        {
            return b.Authentication.ToString();
        }
        if (b.IntegratedSecurity)
        {
            return "IntegratedSecurity";
        }
        if (!string.IsNullOrEmpty(b.UserID))
        {
            return $"SqlPassword (user='{b.UserID}')";
        }
        return "(unspecified)";
    }
}
