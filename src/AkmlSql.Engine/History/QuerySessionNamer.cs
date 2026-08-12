using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AkmlSql.Engine.History;

/// <summary>
/// Pure naming rules for query sessions. No I/O, so every rule here is directly unit-testable —
/// the ordinal/persistence side lives in <see cref="QuerySessionStore"/>.
/// </summary>
internal static class QuerySessionNamer
{
    /// <summary>
    /// SSMS names an UNSAVED query document either "SQLQuery&lt;n&gt;.sql" or with a random
    /// 8-character token ("dwnhdxfq.sql"). Neither is a name a user chose, so both are treated
    /// as "no name" and replaced by query-NN.
    /// </summary>
    private static readonly Regex ScratchName = new(
        @"^(SQLQuery\d+|[a-z0-9]{8})\.sql$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Local calendar day ("yyyy-MM-dd") of a UTC instant. History stores UTC; the
    /// counter resets at LOCAL midnight, so the conversion must be explicit.</summary>
    internal static string LocalDateKey(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Utc
            ? utcInstant
            : utcInstant.ToUniversalTime();
        return utc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Zero-padded to two digits, widening past 99 ("query-100") rather than truncating.</summary>
    internal static string FormatName(int ordinal) =>
        "query-" + ordinal.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// True when the title carries no user intent. See the regex remark for the two SSMS forms.
    /// HEURISTIC — used for the one-time backfill of pre-migration rows, where the saved/unsaved
    /// distinction is already lost. A genuinely saved file named with eight alphanumeric
    /// characters ("report01.sql") is a known false positive, correctable with one rename.
    /// </summary>
    internal static bool IsScratchTabTitle(string? tabTitle) =>
        string.IsNullOrWhiteSpace(tabTitle) || ScratchName.IsMatch(tabTitle!.Trim());
}
