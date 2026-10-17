using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (FR-090, FR-092; research R18, tasks T036) - tells the in-browser services which dialect the active
/// connection speaks. The browser still carries T-SQL language code (the ScriptDom formatter, the analyser, the
/// offline completion, quick-info and signature fallbacks, the lightweight refactorings). None of it may ever see
/// PostgreSQL text, because it would mangle or misreport it. A service asks the gate first. Where the engine has a
/// PostgreSQL implementation the service sends the work there; where it has none the surface is off and says
/// <see cref="DialectMessages.NotAvailable"/>:
/// <list type="bullet">
/// <item><description>completion, quick info, signature help: the engine (<c>CompletionService</c> and the services beside it);</description></item>
/// <item><description>Format SQL, the Format Styles preview: the engine (<c>FormatterService.FormatAsync</c> / <c>PreviewAsync</c>);</description></item>
/// <item><description>Analyse and the Problems tab: the engine (<c>AnalyserService</c> with <c>IPostgresAnalysisEngine</c>);</description></item>
/// <item><description>run, Parse, edit results, History, AI: the engine, by the connection's kind;</description></item>
/// <item><description>the synchronous in-browser <c>FormatterService.Format</c>, the Refactor menu and Find invalid objects: off
/// (<see cref="UnsupportedSurfaces"/>, FR-092).</description></item>
/// </list>
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
