using System;
using System.Collections.Generic;
using System.Globalization;
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
    public const string BucketToday = "Today";
    public const string BucketThisWeek = "This Week";
    public const string BucketTwoMonths = "Two Months Ago";
    public const string BucketOlder = "Older";

    /// <summary>
    /// Classifies an ISO-8601 timestamp into one of four contiguous buckets relative to <paramref name="now"/>:
    /// Today (same calendar day), This Week (the prior six days), Two Months Ago (7–59 days back),
    /// Older (60+ days, or an unparseable value). Mirrors the desktop taxonomy.
    /// </summary>
    public static string DateBucket(string? executedAtIso, DateTime now)
    {
        if (DateTime.TryParse(executedAtIso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
        {
            var d = dt.ToLocalTime().Date;
            var today = now.Date;
            if (d == today) return BucketToday;
            if (d > today.AddDays(-7)) return BucketThisWeek;
            if (d > today.AddDays(-60)) return BucketTwoMonths;
        }
        return BucketOlder;
    }

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
    /// The engine stamps <c>executed_at</c> and <c>content_hash</c> itself.</summary>
    public static HistoryRecordRequest BuildRecordRequest(
        string sql, ExecuteQueryResult result, string? server, string? database) => new()
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
    };
}
