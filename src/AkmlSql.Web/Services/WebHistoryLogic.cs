using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Pure, browser-independent helpers for the web SQL History feature: date bucketing (mirrors the
/// desktop <c>DateBucketConverter</c>), source-filter derivation, and turning an execute result into
/// a <see cref="HistoryRecordRequest"/>. Kept separate from <see cref="HistoryService"/> so the
/// classification/composition logic is unit-testable without a bridge.
/// </summary>
public static class WebHistoryLogic
{
    // Mirror the shared Core bucket labels so existing web consumers/tests keep their names while
    // the classification itself lives in AkmlSql.Core.Models.History.HistoryDateBucket.
    public const string BucketToday = AkmlSql.Core.Models.History.HistoryDateBucket.Today;
    public const string BucketThisWeek = AkmlSql.Core.Models.History.HistoryDateBucket.ThisWeek;
    public const string BucketTwoMonths = AkmlSql.Core.Models.History.HistoryDateBucket.TwoMonths;
    public const string BucketOlder = AkmlSql.Core.Models.History.HistoryDateBucket.Older;

    /// <summary>
    /// Classifies an ISO-8601 timestamp into one of four contiguous buckets relative to <paramref name="now"/>:
    /// Today (same calendar day), This Week (the prior six days), Last Two Months (7–59 days back),
    /// Older (60+ days, or an unparseable value). Delegates to the shared Core bucketing helper.
    /// </summary>
    public static string DateBucket(string? executedAtIso, DateTime now) =>
        AkmlSql.Core.Models.History.HistoryDateBucket.Of(executedAtIso, now);

    /// <summary>Distinct, case-insensitive, sorted server and database names present in the entries
    /// (for the source/server filter menu) — empty values dropped. Mirrors the desktop
    /// <c>RefreshDropdownsFromEntries</c> approach (no distinct-source IPC needed).</summary>
    public static (IReadOnlyList<string> servers, IReadOnlyList<string> databases) DeriveSources(
        IEnumerable<HistoryEntryDto> entries)
    {
        var list = entries as IReadOnlyCollection<HistoryEntryDto> ?? entries.ToList();
        var servers = list.Select(e => e.Server).Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        var databases = list.Select(e => e.Database).Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Cast<string>().ToList();
        return (servers, databases);
    }

    // ── Spec 042 (FR-070): the kind of an entry ─────────────────────────────────────────────────────────

    /// <summary>The History search box after its <c>kind:</c> term is taken out (spec 042, FR-070).</summary>
    /// <param name="Text">The search text left over, words separated by single spaces; null when nothing is left.</param>
    /// <param name="Kind">The <see cref="AkmlSql.Core.DatabaseKind"/> the <c>kind:</c> term named; null when there was none.</param>
    public readonly record struct WebHistorySearch(string? Text, int? Kind);

    /// <summary>
    /// Splits a <c>kind:</c> term out of the search box: <c>kind:postgresql</c>, <c>kind:pg</c>, <c>kind:sqlserver</c>,
    /// <c>kind:mssql</c> (any case, the value may be quoted). It is not for the full-text index, so it is not left in the text.
    /// A term with a value that names no kind, a quoted phrase that contains one, and everything else stay as the text.
    /// The last kind term wins.
    /// </summary>
    public static WebHistorySearch ParseSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return new WebHistorySearch(null, null);

        int? kind = null;
        var kept = new List<string>();
        foreach (var token in SplitTokens(search))
        {
            if (TryReadKindTerm(token, out var named))
                kind = named;
            else
                kept.Add(token);
        }

        return new WebHistorySearch(kept.Count == 0 ? null : string.Join(" ", kept), kind);
    }

    private static bool TryReadKindTerm(string token, out int kind)
    {
        kind = 0;
        const string prefix = "kind:";
        if (token.Length <= prefix.Length || !token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var value = token.Substring(prefix.Length);
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') value = value.Substring(1, value.Length - 2);
        if (!AkmlSql.Core.DatabaseKindNames.TryParse(value, out var named)) return false;

        kind = (int)named;
        return true;
    }

    /// <summary>Whitespace-separated words; a double-quoted phrase is one word, so what is inside it is never a term.</summary>
    private static IEnumerable<string> SplitTokens(string text)
    {
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var c in text)
        {
            if (c == '"') inQuote = !inQuote;
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    /// <summary>The kind as the product names it everywhere: <c>SQL Server</c> or <c>PostgreSQL</c>.</summary>
    public static string KindLabel(int kind) =>
        AkmlSql.Core.DatabaseKindNames.FromInt(kind) == AkmlSql.Core.DatabaseKind.PostgreSql ? "PostgreSQL" : "SQL Server";

    /// <summary>
    /// The saved connection an entry ran on (FR-070): the one of the same kind whose server and database match, else the one of the
    /// same kind on the same server. A PostgreSQL entry's server is <c>host:port</c>, as the status bar writes it; a SQL Server
    /// entry's is the server name as typed. Never a connection of the other kind, whatever its server is called.
    /// </summary>
    public static AkmlSql.Web.Services.SavedSqlConnection? FindConnection(
        IEnumerable<AkmlSql.Web.Services.SavedSqlConnection> saved, AkmlSql.Core.DatabaseKind kind, string? server, string? database)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;

        var sameServer = saved
            .Where(c => c.Kind == kind && string.Equals(ServerOf(c), server.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return sameServer.FirstOrDefault(c => string.Equals(c.Database, database, StringComparison.OrdinalIgnoreCase))
               ?? sameServer.FirstOrDefault();
    }

    private static string ServerOf(AkmlSql.Web.Services.SavedSqlConnection c) =>
        c.Kind == AkmlSql.Core.DatabaseKind.PostgreSql ? c.Server.Trim() + ":" + c.EffectivePort : c.Server.Trim();

    /// <summary>Row count for the history record: summed result rows for a SELECT; otherwise the DML
    /// rows-affected (which is 0/-1 for a SELECT, so reads take the summed path).</summary>
    public static long DeriveRowCount(ExecuteQueryResult result)
    {
        var read = result.ResultSets?.Sum(rs => (long)(rs.Rows?.Length ?? 0)) ?? 0;
        if (read > 0) return read;
        return result.TotalRowsAffected > 0 ? result.TotalRowsAffected : 0;
    }

    /// <summary>Whether an execute outcome should be recorded in history. A NoConnection result means
    /// nothing actually ran, so it is not recorded. (<paramref name="executeStatus"/> is an
    /// <see cref="ExecuteStatus"/> int.)</summary>
    public static bool ShouldRecord(int executeStatus) => executeStatus != ExecuteStatus.NoConnection;

    /// <summary>Maps an <see cref="ExecuteStatus"/> int to a history <c>ExecutionStatus</c> int
    /// (Success=0, Error=1, Cancelled=2). Error/TimedOut/NoConnection collapse to Error.</summary>
    public static int MapStatus(int executeStatus) => executeStatus switch
    {
        ExecuteStatus.Ok => 0,        // ExecutionStatus.Success
        ExecuteStatus.Cancelled => 2, // ExecutionStatus.Cancelled
        _ => 1,                       // ExecutionStatus.Error
    };

    /// <summary>Builds the engine write payload from a completed execute + the active connection.
    /// The engine stamps <c>executed_at</c> and <c>content_hash</c> itself. <paramref name="sessionKey"/>
    /// is the persisted <see cref="EditorSessionKeys"/> grouping key (see <see cref="HistoryRecordRequest.SessionKey"/>);
    /// null from callers that don't have one yet, which the engine tolerates.</summary>
    /// <param name="documentName">Spec 041 (R64, N11): the editor document's name. A name the user chose names the
    /// history session (<c>TabTitle</c>); a default <c>SQLQuery{n}</c> is not sent, so the session reads
    /// <c>query-NN</c> as SSMS's unsaved tabs do and can still be upgraded later.</param>
    public static HistoryRecordRequest BuildRecordRequest(
        string sql, ExecuteQueryResult result, string? server, string? database, string? sessionKey = null, string? documentName = null,
        AkmlSql.Core.DatabaseKind kind = AkmlSql.Core.DatabaseKind.SqlServer) => new()
    {
        SqlText = sql ?? string.Empty,
        Truncated = false,
        Server = server,
        Database = database,
        DurationMs = result.ElapsedMs,
        RowCount = DeriveRowCount(result),
        Status = MapStatus(result.Status),
        ErrorMessage = result.ErrorMessage,
        Source = "web",
        Kind = (int)kind,   // spec 042: recorded with the run, so History can tell the two apart
        SessionKey = sessionKey,
        TabTitle = string.IsNullOrWhiteSpace(documentName) || EditorDocument.IsDefaultName(documentName) ? null : documentName,
    };
}
