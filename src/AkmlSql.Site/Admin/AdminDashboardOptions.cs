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
}
