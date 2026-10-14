using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.PostgreSql.Completion;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T030, T106, R4) - the PostgreSQL language services the engine hands a PostgreSQL session's requests to:
/// completion, quick info, signature help and wildcard expansion. They are composed once, only when
/// <c>postgreSql.enabled</c> is on, so a machine with the dialect off carries none of it. The handlers choose between these and the
/// T-SQL services by the session's <c>Kind</c>; nothing here is shared with the T-SQL engine but the result types and the schema cache.
/// </summary>
public sealed class PgServices
{
    public PgCompletionEngine Completion { get; } = new();

    public PgQuickInfoService QuickInfo { get; } = new();

    public PgSignatureService Signature { get; } = new();

    public PgWildcardExpander Wildcard { get; } = new();

    /// <summary>Completion for a PostgreSQL session, with the IntelliSense settings the user chose (FR-052 version gating included).</summary>
    public CompletionResponse Complete(string documentText, int offset, DatabaseCache? cache, SessionState session, AppSettings settings)
    {
        var intelliSense = settings.IntelliSense;
        var options = new PgCompletionOptions
        {
            ServerMajor = ServerMajor(session),
            MaxSuggestions = Math.Max(5, Math.Min(200, intelliSense.MaxSuggestions)),
            FuzzyMatch = intelliSense.FuzzyMatch,
            IncludeKeywords = intelliSense.SuggestionTypes.IncludeKeywords,
            IncludeSystemObjects = intelliSense.SuggestionTypes.IncludeSystemObjects,
            ShowDataTypes = intelliSense.ShowDataTypes,
            ShowNullability = intelliSense.ShowNullability,
            ShowKeyIndicators = intelliSense.ShowPkFk,
            JoinAssist = intelliSense.JoinAssist,
        };
        return Completion.GetCompletions(documentText, offset, cache, options);
    }

    /// <summary>
    /// The server's major release (18 for <c>18.1</c>). When the version has not been read yet the newest is assumed, so a fresh
    /// connection is offered everything instead of nothing.
    /// </summary>
    public static int ServerMajor(SessionState? session)
    {
        var text = session?.ServerVersionText;
        if (string.IsNullOrWhiteSpace(text)) return 18;
        var end = text!.IndexOf('.');
        var head = end < 0 ? text : text.Substring(0, end);
        return int.TryParse(head, out var major) && major > 0 ? major : 18;
    }
}
