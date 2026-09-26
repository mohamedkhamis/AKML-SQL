using System.Globalization;
using AkmlSql.Site.Analytics;

namespace AkmlSql.Site.Admin;

/// <summary>
/// ADM-003: the dashboard's reporting window. It was hardcoded to 30 days even though
/// <c>AnalyticsStore.GetSummary</c> already took the window as a parameter — the plumbing existed,
/// the control did not.
/// <para>
/// The window travels as a query-string value rather than component state, which keeps the
/// dashboard static-SSR (no interactive render mode) and makes a chosen range a shareable,
/// bookmarkable URL. Shared by the page and the CSV export so both honour the same selection.
/// </para>
/// </summary>
public static class AdminDashboardOptions
{
    /// <summary>Windows offered in the UI, in days.</summary>
    public static readonly int[] Ranges = [7, 30, 90, 365];

    /// <summary>Window used when none is specified.</summary>
    public const int DefaultDays = 30;

    /// <summary>Upper bound on a hand-typed window — the query string is user input.</summary>
    public const int MaxDays = 3650;

    /// <summary>
    /// Clamps a requested window to something sane. A missing, zero, negative or absurd value
    /// falls back to the default rather than erroring: a bad query string should not break the
    /// owner's dashboard.
    /// </summary>
    public static int NormalizeDays(int? requested) => requested switch
    {
        null or < 1 => DefaultDays,
        > MaxDays => MaxDays,
        var days => days.Value,
    };

    /// <summary>Human label for a window ("7 days", "12 months").</summary>
    public static string Label(int days) => days switch
    {
        365 => "12 months",
        1 => "1 day",
        _ => $"{days} days",
    };

    /// <summary>
    /// Resolves the <c>days</c> query-string value to a reporting range. Every page and every CSV
    /// export goes through this one function, so they cannot disagree about what a URL means.
    /// <para>
    /// The parameter keeps its old name and its old numeric values (<c>?days=30</c>), so every
    /// bookmark and shared link made before Today/Yesterday existed still opens the same report.
    /// New values are the range keys: <c>?days=today</c>, <c>?days=yesterday</c>.
    /// </para>
    /// <para>
    /// A number that is not one of the offered ranges is still honoured the way it always was --
    /// clamped by <see cref="NormalizeDays"/> -- and anything else falls back to the default
    /// (contract A4.4: a bad query string must not break the owner's portal).
    /// </para>
    /// </summary>
    public static ReportRange ResolveRange(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ReportRange.Default;
        }

        var named = ReportRange.All.FirstOrDefault(r => string.Equals(r.Key, raw.Trim(), StringComparison.OrdinalIgnoreCase));
        if (named is not null)
        {
            return named;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= 1
            ? ReportRange.LastDays(NormalizeDays(days))
            : ReportRange.Default;
    }
}
