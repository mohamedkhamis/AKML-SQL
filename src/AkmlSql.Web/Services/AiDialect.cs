using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Privacy;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (FR-080). What the AI features need to know about the active connection: the SQL language an answer must be
/// written in and, for PostgreSQL, the server version. It follows the connection, so switching from SQL Server to
/// PostgreSQL changes the next prompt and nothing else has to be told.
/// </summary>
public interface IAiDialectContext
{
    /// <summary>The dialect of the active connection; SQL Server when not connected, as before PostgreSQL support.</summary>
    SqlDialect Dialect { get; }

    /// <summary>The PostgreSQL server version (for example <c>16.4</c>), or null when unknown or not PostgreSQL.</summary>
    string? ServerVersion { get; }
}

internal sealed class AiDialectContext : IAiDialectContext
{
    private readonly ISqlConnectionService _connection;

    public AiDialectContext(ISqlConnectionService connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public SqlDialect Dialect => _connection.IsConnected ? SqlDialects.From(_connection.Kind) : SqlDialect.SqlServer;

    public string? ServerVersion => Dialect == SqlDialect.PostgreSql ? _connection.ServerVersionText : null;
}

/// <summary>
/// Spec 042 (FR-082). The error a Fix answer is asked about. For SQL Server that is a message and an error number, as it has
/// always been; for PostgreSQL there is no number, and the SQLSTATE code, the server's message, detail and hint are what the
/// model needs.
/// </summary>
public sealed record AiErrorInfo(string Message, int Number = 0, string? SqlState = null, string? Detail = null, string? Hint = null)
{
    /// <summary>What Fix sent when no error was at hand: the text it always sent.</summary>
    public static AiErrorInfo None { get; } = new("(no error)", 0);
}

/// <summary>
/// Spec 042 (FR-081). Nothing was sent: the literals of a PostgreSQL script could not be removed and the user chose not to
/// send it as it is. The message is shown to the user as it stands.
/// </summary>
public sealed class AiNotSentException : InvalidOperationException
{
    public AiNotSentException(string message) : base(message) { }
}

/// <summary>The outcome of asking the engine to remove the literals from PostgreSQL text.</summary>
/// <param name="Ok">True when every literal was replaced.</param>
/// <param name="Text">The text with placeholders in place of the literals; empty when <paramref name="Ok"/> is false.</param>
/// <param name="Placeholders">Each placeholder as it appears in <paramref name="Text"/> and the literal it replaced.</param>
/// <param name="Error">Why not, in words fit for the user; never the script.</param>
public sealed record PostgresRedaction(bool Ok, string Text, IReadOnlyDictionary<string, string> Placeholders, string? Error)
{
    public static PostgresRedaction Failed(string error) =>
        new(false, string.Empty, new Dictionary<string, string>(), error);
}

/// <summary>
/// Spec 042 (FR-081, R20). Removes the literals from PostgreSQL text before it goes to a cloud AI provider. The engine does it
/// (the browser has no PostgreSQL scanner); a missing engine or a failure is a failed redaction, never a silent pass-through.
/// </summary>
public interface IPostgresRedactor
{
    Task<PostgresRedaction> RedactAsync(string text, CancellationToken ct);
}

internal sealed class EnginePostgresRedactor : IPostgresRedactor
{
    private readonly IEngineBridge _bridge;

    public EnginePostgresRedactor(IEngineBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<PostgresRedaction> RedactAsync(string text, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open)
            return PostgresRedaction.Failed("No engine is connected, so the literals cannot be removed.");
        if (!_bridge.Advertises(SqlConnectionService.CapabilityPostgreSql))
            return PostgresRedaction.Failed("The paired engine does not support PostgreSQL, so the literals cannot be removed.");

        try
        {
            var response = await _bridge.SendAsync<RedactRequest, RedactResponse>(
                MessageTypes.RedactRequest,
                new RedactRequest { Dialect = DatabaseKindNames.PostgreSql, Text = text },
                ct).ConfigureAwait(false);

            if (response == null) return PostgresRedaction.Failed("The engine did not answer.");
            if (!response.Ok) return PostgresRedaction.Failed(response.Error ?? "The engine could not remove the literals.");

            return new PostgresRedaction(true, response.Text ?? string.Empty, response.Placeholders ?? new Dictionary<string, string>(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PostgresRedaction.Failed("The engine could not remove the literals: " + ex.Message);
        }
    }
}

/// <summary>What may be sent to a provider in place of a piece of SQL, and how to read the answer that comes back.</summary>
internal sealed class AiOutbound
{
    private readonly PlaceholderRestorer? _restorer;

    private AiOutbound(string text, PlaceholderRestorer? restorer, bool withheld)
    {
        Text = text;
        _restorer = restorer;
        Withheld = withheld;
    }

    /// <summary>The text to send.</summary>
    public string Text { get; }

    /// <summary>True when nothing may be sent (the literals could not be removed and nobody could be asked).</summary>
    public bool Withheld { get; }

    /// <summary>The text goes out as it is.</summary>
    public static AiOutbound Unchanged(string text) => new(text, null, false);

    /// <summary>The literals were removed; the answer's placeholders are put back.</summary>
    public static AiOutbound Redacted(string text, IReadOnlyDictionary<string, string> placeholders) =>
        new(text, new PlaceholderRestorer(placeholders), false);

    /// <summary>Nothing may be sent.</summary>
    public static AiOutbound NotSent { get; } = new(string.Empty, null, true);

    /// <summary>The answer with the literals put back.</summary>
    public string Restore(string answer) => _restorer == null ? answer : _restorer.Restore(answer);

    /// <summary>A streamed answer with the literals put back, even when a placeholder arrives in two pieces.</summary>
    public async IAsyncEnumerable<string> RestoreStream(
        IAsyncEnumerable<string> pieces, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_restorer == null || _restorer.IsEmpty)
        {
            await foreach (var piece in pieces.WithCancellation(ct).ConfigureAwait(false)) yield return piece;
            yield break;
        }

        var stream = _restorer.BeginStream();
        await foreach (var piece in pieces.WithCancellation(ct).ConfigureAwait(false))
        {
            var ready = stream.Push(piece);
            if (ready.Length > 0) yield return ready;
        }

        var rest = stream.Flush();
        if (rest.Length > 0) yield return rest;
    }
}

/// <summary>
/// Spec 042 (FR-080, FR-081). Decides what PostgreSQL SQL may be sent to the active provider. A local provider keeps the text on
/// the machine, so it gets the script as it is. A cloud provider gets it with the literals removed by the engine. When they cannot
/// be removed the user is asked, once per request and in words that do not quote the script, and "Cancel" sends nothing. SQL
/// Server text is never touched: its behaviour is what it was before PostgreSQL support.
/// </summary>
internal sealed class AiOutboundGate
{
    public const string ConsentDialogId = "ai-redaction-consent";
    public const string NotSentMessage = "Not sent: the literals in this script could not be removed, and you chose not to send it as it is.";

    private readonly IAiDialectContext? _dialect;
    private readonly IPostgresRedactor? _redactor;
    private readonly IDialogService? _dialogs;

    public AiOutboundGate(IAiDialectContext? dialect, IPostgresRedactor? redactor, IDialogService? dialogs)
    {
        _dialect = dialect;
        _redactor = redactor;
        _dialogs = dialogs;
    }

    public SqlDialect Dialect => _dialect?.Dialect ?? SqlDialect.SqlServer;

    public string? ServerVersion => Dialect == SqlDialect.PostgreSql ? _dialect?.ServerVersion : null;

    /// <param name="providerId">The provider the text is going to.</param>
    /// <param name="sql">The SQL (a script, a selection, the text before the caret).</param>
    /// <param name="askUser">
    /// True when a person is waiting on the answer and can be asked; false for ghost text, which fails silently and so sends
    /// nothing when the literals cannot be removed.
    /// </param>
    /// <exception cref="AiNotSentException">The literals could not be removed and the user chose not to send it as it is.</exception>
    public async Task<AiOutbound> PrepareSqlAsync(string providerId, string sql, bool askUser, CancellationToken ct)
    {
        if (Dialect != SqlDialect.PostgreSql || string.IsNullOrWhiteSpace(sql) || AiProviders.IsLocal(providerId))
            return AiOutbound.Unchanged(sql);

        var redaction = _redactor == null
            ? PostgresRedaction.Failed("No redaction service is available.")
            : await _redactor.RedactAsync(sql, ct).ConfigureAwait(false);
        if (redaction.Ok) return AiOutbound.Redacted(redaction.Text, redaction.Placeholders);

        if (!askUser) return AiOutbound.NotSent;

        var sendAnyway = _dialogs != null && await _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = "Send without removing literals?",
            Message = "Literals could not be removed from this script.",
            Details = new[]
            {
                redaction.Error ?? "The engine could not read the script.",
                "If you send it anyway, the provider receives the script exactly as it is, including its values.",
            },
            ConfirmLabel = "Send anyway",
            CancelLabel = "Cancel",
            Destructive = true,
            DialogId = ConsentDialogId,
        }).ConfigureAwait(false);

        if (!sendAnyway) throw new AiNotSentException(NotSentMessage);
        return AiOutbound.Unchanged(sql);
    }
}
