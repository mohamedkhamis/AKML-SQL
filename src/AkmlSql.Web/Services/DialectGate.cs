using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (FR-090, FR-092; research R18, tasks T036) - tells the in-browser services which dialect the active
/// connection speaks. The browser still carries T-SQL language code (the ScriptDom formatter, the analyser, the
/// offline completion, quick-info and signature fallbacks, the lightweight refactorings). None of it may ever see
/// PostgreSQL text, because it would mangle or misreport it. A service asks the gate first and answers "nothing" or
/// "not available" when the connection is PostgreSQL; the PostgreSQL work itself runs in the engine and is routed
/// there by each story.
/// </summary>
public interface IDialectGate
{
    /// <summary>The kind of the active connection; SQL Server when there is none, exactly as the editor behaved before.</summary>
    DatabaseKind Kind { get; }

    /// <summary>True when the active connection is PostgreSQL.</summary>
    bool IsPostgres { get; }
}

/// <summary>The words a user sees when a surface has no PostgreSQL implementation.</summary>
public static class DialectMessages
{
    /// <summary>Tooltip, status text and diagnostic for a disabled or refused surface (FR-092).</summary>
    public const string NotAvailable = "Not available for PostgreSQL";
}

internal sealed class DialectGate : IDialectGate
{
    private readonly ISqlConnectionService _connection;

    public DialectGate(ISqlConnectionService connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public DatabaseKind Kind => _connection.IsConnected ? _connection.Kind : DatabaseKind.SqlServer;

    public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
}
