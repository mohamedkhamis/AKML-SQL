using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    /// <summary>What Fix says in place of a detail it does not send (<see cref="Protect"/>).</summary>
    public const string WithheldDetail = "(not sent: it can hold data from the table)";

    /// <summary>What Fix puts in place of a quoted value in a data error's message (<see cref="Protect"/>).</summary>
    public const string WithheldValue = "(value not sent)";

    private readonly PlaceholderRestorer? _restorer;
    private readonly IReadOnlyDictionary<string, string>? _placeholders;
    private readonly bool _protect;

    private AiOutbound(string text, IReadOnlyDictionary<string, string>? placeholders, bool withheld, bool protect)
    {
        Text = text;
        _placeholders = placeholders;
        _restorer = placeholders == null ? null : new PlaceholderRestorer(placeholders);
        Withheld = withheld;
        _protect = protect;
    }

    /// <summary>The text to send.</summary>
    public string Text { get; }

    /// <summary>True when nothing may be sent (the literals could not be removed and nobody could be asked).</summary>
    public bool Withheld { get; }

    /// <summary>True when the literals were removed from <see cref="Text"/>.</summary>
    public bool IsRedacted => _placeholders is { Count: > 0 };

    /// <summary>True when the text is PostgreSQL bound for a provider outside this machine: the error that goes with it is protected too (<see cref="Protect"/>).</summary>
    public bool IsProtected => _protect;

    /// <summary>The text goes out as it is (a local provider, or SQL Server).</summary>
    public static AiOutbound Unchanged(string text) => new(text, null, false, false);

    /// <summary>The literals were removed (there may have been none to remove); the answer's placeholders are put back.</summary>
    public static AiOutbound Redacted(string text, IReadOnlyDictionary<string, string> placeholders) =>
        new(text, placeholders, false, true);

    /// <summary>
    /// The user chose to send the script with its values when the literals could not be removed. The script goes as it is; the
    /// server's error is still protected, because its detail can hold rows the script never had and the user was not asked about those.
    /// </summary>
    public static AiOutbound SentWithValues(string text) => new(text, null, false, true);

    /// <summary>Nothing may be sent.</summary>
    public static AiOutbound NotSent { get; } = new(string.Empty, null, true, false);

    /// <summary>
    /// Spec 042 (FR-081): the server's error as it may go with PostgreSQL text to a provider outside this machine. The server
    /// repeats values from the script in its message (<c>invalid input syntax for type integer: "abc"</c>), values from rows in the
    /// message of a data error (SQLSTATE class 22, the same shape with a value the script never had), and whole rows in its detail
    /// (<c>Key (email)=(x) already exists</c>, <c>Failing row contains (...)</c>). The message and the hint get the script's literals
    /// replaced by the placeholders the script carries; in a data error every other quoted value of the message is replaced by
    /// <see cref="WithheldValue"/>; the detail is not sent. An error that goes with an unprotected script (a local provider, SQL
    /// Server) is returned as it is.
    /// </summary>
    public AiErrorInfo Protect(AiErrorInfo error)
    {
        if (!_protect) return error;
        var message = Mask(error.Message);
        if (IsDataException(error.SqlState)) message = HideQuotedValues(message);
        return error with
        {
            Message = message,
            Hint = error.Hint is null ? null : Mask(error.Hint),
            Detail = string.IsNullOrWhiteSpace(error.Detail) ? error.Detail : WithheldDetail,
        };
    }

    /// <summary>SQLSTATE class 22 (data exception): the message names the offending value, which may come from a row.</summary>
    private static bool IsDataException(string? sqlState) =>
        sqlState is { Length: >= 2 } && sqlState.StartsWith("22", StringComparison.Ordinal);

    // Every quoted run of the message but a placeholder's name (which stands for a value the script named and the model may see as such).
    private static string HideQuotedValues(string message) =>
        Regex.Replace(message, "\"([^\"]*)\"", m => Regex.IsMatch(m.Groups[1].Value, "^__(STR|NUM)_[0-9]+__$") ? m.Value : "\"" + WithheldValue + "\"");

    /// <summary>
    /// The script's literals, as the server might repeat them - as written (<c>'O''Brien'</c>, which becomes the placeholder as written,
    /// <c>'__STR_0__'</c>) and as a bare value (<c>O'Brien</c>, which becomes the placeholder's name) - replaced in one pass, longest form
    /// first so a value inside another is masked as part of it, and only as whole words where a form begins or ends with a word
    /// character, so a literal <c>'in'</c> is not found inside "invalid". A form with no letter or digit (a space, a comma, a dash) is in
    /// every sentence and names nothing, and a one-character number is in every message ("LINE 1"); they are left alone.
    /// </summary>
    private string Mask(string text)
    {
        if (_placeholders is not { Count: > 0 } || text.Length == 0) return text;

        var forms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in _placeholders)
        {
            var isString = pair.Key.Contains("__STR_", StringComparison.Ordinal);
            if (Maskable(pair.Value, isString)) forms.TryAdd(pair.Value, pair.Key);
            var bare = BareValue(pair.Value);
            if (Maskable(bare, isString)) forms.TryAdd(bare, PlaceholderName(pair.Key));
        }
        if (forms.Count == 0) return text;

        var pattern = string.Join("|", forms.Keys.OrderByDescending(f => f.Length).Select(Bounded));
        return Regex.Replace(text, pattern, m => forms[m.Value]);
    }

    private static bool Maskable(string form, bool isString) =>
        form.Any(char.IsLetterOrDigit) && (isString || form.Length >= 2);

    private static string Bounded(string form)
    {
        var start = IsWordChar(form[0]) ? "(?<![A-Za-z0-9_])" : string.Empty;
        var end = IsWordChar(form[^1]) ? "(?![A-Za-z0-9_])" : string.Empty;
        return start + Regex.Escape(form) + end;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // '__STR_0__' and $tag$__STR_2__$tag$ -> __STR_0__ / __STR_2__; __NUM_1__ stays.
    private static string PlaceholderName(string placeholder)
    {
        var first = placeholder.IndexOf("__", StringComparison.Ordinal);
        var last = placeholder.LastIndexOf("__", StringComparison.Ordinal);
        return first < 0 || last < first ? placeholder : placeholder.Substring(first, last + 2 - first);
    }

    // The value as the server would repeat it: a string without its quotes and prefix (E'it''s' -> it's), a dollar-quoted body
    // without its tags, a number as written.
    private static string BareValue(string literal)
    {
        if (literal.Length >= 2 && literal[^1] == '\'')
        {
            var open = literal.IndexOf('\'');
            if (open >= 0 && open < literal.Length - 1) return literal.Substring(open + 1, literal.Length - open - 2).Replace("''", "'");
        }
        if (literal.Length >= 2 && literal[0] == '$')
        {
            var tagEnd = literal.IndexOf('$', 1) + 1;
            if (tagEnd > 1 && literal.Length >= 2 * tagEnd) return literal.Substring(tagEnd, literal.Length - 2 * tagEnd);
        }
        return literal;
    }

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
        return AiOutbound.SentWithValues(sql);
    }
}
