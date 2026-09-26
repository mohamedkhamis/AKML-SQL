using System.Globalization;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// A reporting range the owner can pick in the admin portal: today, yesterday, or the last N days.
/// </summary>
/// <param name="Key">Stable query-string value (<c>today</c>, <c>yesterday</c>, <c>7</c> …).</param>
/// <param name="Label">What the toolbar button says.</param>
/// <param name="Days">How many calendar days the range covers, today included where it applies.</param>
/// <param name="EndsNow">
/// True for ranges that run up to this moment (today, last N days); false for a range that is
/// already over (yesterday). Decides how the comparison period is cut — see
/// <see cref="ReportWindow.Previous"/>.
/// </param>
public sealed record ReportRange(string Key, string Label, int Days, bool EndsNow)
{
    public static readonly ReportRange Today = new("today", "Today", 1, EndsNow: true);
    public static readonly ReportRange Yesterday = new("yesterday", "Yesterday", 1, EndsNow: false);
    public static readonly ReportRange Last7 = new("7", "7 days", 7, EndsNow: true);
    public static readonly ReportRange Last30 = new("30", "30 days", 30, EndsNow: true);
    public static readonly ReportRange Last90 = new("90", "90 days", 90, EndsNow: true);
    public static readonly ReportRange Last365 = new("365", "12 months", 365, EndsNow: true);

    /// <summary>Every range the toolbar offers, in display order.</summary>
    public static readonly IReadOnlyList<ReportRange> All = [Today, Yesterday, Last7, Last30, Last90, Last365];

    /// <summary>The range used when none is specified.</summary>
    public static ReportRange Default => Last30;

    /// <summary>True for Today and Yesterday: one calendar day, so per-day figures are exact.</summary>
    public bool IsSingleDay => Days == 1;

    /// <summary>The range in running text: "today", "yesterday", "the last 7 days", "the last 12 months".</summary>
    public string Phrase => IsSingleDay ? Label.ToLowerInvariant() : $"the last {Label}";

    /// <summary>Largest window a day count may request (about a century).</summary>
    public const int MaxDays = 36_500;

    /// <summary>
    /// Resolves a query-string value to a range, falling back to <see cref="Default"/> for anything
    /// unrecognised — the query string is user input, and a bad one should not break the portal.
    /// </summary>
    public static ReportRange Parse(string? key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Default;

    /// <summary>
    /// A "last N days" range for an arbitrary N. Used by the compatibility overloads that still
    /// take a day count; the named ranges are returned when N matches one.
    /// </summary>
    public static ReportRange LastDays(int days)
    {
        // No product limit here: the compatibility overloads accepted any positive day count, and
        // a wrapper that silently narrows the window is not compatible (the UI clamps its own
        // query-string input separately). The ceiling -- a century -- only stops an absurd value
        // from running the start date off the beginning of the calendar.
        var clamped = Math.Clamp(days, 1, MaxDays);
        return All.FirstOrDefault(r => r.EndsNow && r.Days == clamped && r != Today)
            ?? new ReportRange(clamped.ToString(CultureInfo.InvariantCulture), $"{clamped} days", clamped, EndsNow: true);
    }
}

/// <summary>
/// A reporting range resolved to concrete instants: <c>[FromUtc, ToUtc)</c>, with the calendar
/// days it covers in the owner's timezone.
/// <para>
/// This replaced windows computed as UTC calendar days with only a lower bound
/// (<c>WHERE day &gt;= $since</c>). That had two consequences. It could not express "yesterday" at
/// all, because yesterday needs an upper bound too. And every day boundary sat at UTC midnight,
/// which in Cairo is 02:00 or 03:00 depending on the season — so a download at 01:00 local time was
/// reported on the previous day, and "today" did not start until three hours into the owner's
/// actual day.
/// </para>
/// <para>
/// Every boundary here is a local midnight converted to UTC through the timezone's own rules, so
/// daylight-saving changes are handled rather than approximated with a fixed offset: a fixed
/// "+3 hours" is wrong for half of every year in Egypt.
/// </para>
/// </summary>
public sealed record ReportWindow
{
    public required ReportRange Range { get; init; }

    /// <summary>Start of the window, inclusive.</summary>
    public required DateTimeOffset FromUtc { get; init; }

    /// <summary>
    /// End of the window, exclusive. For a range that runs up to the present this is the end of
    /// TODAY, not "now": nothing has been recorded after now, so the two select the same rows --
    /// but a hard "&lt; now" would drop an event recorded in the same instant the report was asked
    /// for, which is a real race on a busy page and a certain one in any test that logs then reads.
    /// </summary>
    public required DateTimeOffset ToUtc { get; init; }

    /// <summary>First local calendar day in the window.</summary>
    public required DateOnly FirstDay { get; init; }

    /// <summary>Last local calendar day in the window, inclusive.</summary>
    public required DateOnly LastDay { get; init; }

    /// <summary>The timezone the day boundaries are in.</summary>
    public required TimeZoneInfo Zone { get; init; }

    /// <summary>The moment the window was resolved at.</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>The local calendar days the window covers, in order.</summary>
    public IEnumerable<DateOnly> Days
    {
        get
        {
            for (var day = FirstDay; day <= LastDay; day = day.AddDays(1))
                yield return day;
        }
    }

    /// <summary>Resolves <paramref name="range"/> at <paramref name="now"/> in <paramref name="zone"/>.</summary>
    public static ReportWindow Resolve(ReportRange range, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(zone);

        var today = LocalDay(now, zone);

        if (!range.EndsNow)
        {
            // Yesterday: the whole previous calendar day, both ends at a local midnight.
            var day = today.AddDays(-1);
            return new ReportWindow
            {
                Range = range,
                FromUtc = StartOfDayUtc(day, zone),
                ToUtc = StartOfDayUtc(today, zone),
                FirstDay = day,
                LastDay = day,
                Zone = zone,
                Now = now,
            };
        }

        var first = today.AddDays(-(range.Days - 1));
        return new ReportWindow
        {
            Range = range,
            FromUtc = StartOfDayUtc(first, zone),
            ToUtc = StartOfDayUtc(today.AddDays(1), zone),
            FirstDay = first,
            LastDay = today,
            Zone = zone,
            Now = now,
        };
    }

    /// <summary>
    /// The comparison window: the same range, <see cref="ReportRange.Days"/> calendar days earlier.
    /// <para>
    /// For a range that runs up to now, the comparison is cut at the same time of day. "Today so far"
    /// is compared with yesterday up to the same hour, not with all of yesterday: comparing four
    /// hours against twenty-four would show every morning as a collapse, which is the kind of number
    /// that is precisely wrong rather than roughly right. The same rule makes "last 7 days" compare
    /// with the seven days before it up to the same moment.
    /// </para>
    /// <para>
    /// Days are shifted as local calendar days, not as multiples of 24 hours, so a comparison that
    /// spans a daylight-saving change still lines up midnight with midnight.
    /// </para>
    /// </summary>
    public ReportWindow Previous()
    {
        var shift = Range.Days;
        var first = FirstDay.AddDays(-shift);
        var last = LastDay.AddDays(-shift);

        DateTimeOffset to;
        if (Range.EndsNow)
        {
            // Same local time of day, N days earlier.
            var localNow = TimeZoneInfo.ConvertTime(Now, Zone).DateTime;
            to = LocalToUtc(localNow.AddDays(-shift), Zone);
        }
        else
        {
            to = StartOfDayUtc(last.AddDays(1), Zone);
        }

        return this with
        {
            FromUtc = StartOfDayUtc(first, Zone),
            ToUtc = to,
            FirstDay = first,
            LastDay = last,
        };
    }

    /// <summary>
    /// What <see cref="Previous"/> covers, in words, for the change indicators: "yesterday until
    /// 10:00", "the day before", "the previous 7 days". Naming the cut-off matters for Today — a
    /// reader who assumes "vs yesterday" means all of yesterday will misread every morning's figure.
    /// </summary>
    public string ComparisonLabel()
    {
        if (Range.IsSingleDay && Range.EndsNow)
        {
            var at = TimeZoneInfo.ConvertTime(Now, Zone).ToString("HH:mm", CultureInfo.InvariantCulture);
            return $"yesterday until {at}";
        }

        return Range.EndsNow ? $"the previous {Range.Label}" : "the day before";
    }

    /// <summary>The local calendar day <paramref name="instant"/> falls on.</summary>
    public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// The UTC instant a local calendar day begins.
    /// <para>
    /// Usually local midnight — but not always. Egypt starts summer time AT midnight: the clock goes
    /// from 23:59:59 straight to 01:00, so 00:00 does not exist that day, and converting it throws.
    /// A day starts at its first instant that does exist, so an invalid midnight is moved forward
    /// to the first valid minute.
    /// </para>
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        LocalToUtc(day.ToDateTime(TimeOnly.MinValue), zone);

    /// <summary>
    /// Converts a local wall-clock time to UTC, resolving the two cases a daylight-saving change
    /// creates: a time that never happened (moved forward to the first one that did) and a time
    /// that happened twice (the FIRST occurrence, so a day or window starts at its earliest instant).
    /// </summary>
    public static DateTimeOffset LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // A gap is at most a few hours; step a minute at a time, bounded so a pathological zone
        // cannot loop forever.
        for (var i = 0; i < 24 * 60 && zone.IsInvalidTime(wall); i++)
            wall = wall.AddMinutes(1);

        if (zone.IsAmbiguousTime(wall))
        {
            // The larger offset belongs to the first occurrence (summer time, before the clocks go
            // back), and a larger offset means an EARLIER UTC instant.
            var offset = zone.GetAmbiguousTimeOffsets(wall).Max();
            return new DateTimeOffset(wall, offset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// One-line description for the page, naming the timezone so a figure is never ambiguous about
    /// which "today" it means: "Today · Cairo time (UTC+03:00)".
    /// </summary>
    public string Describe()
    {
        var offset = Zone.GetUtcOffset(Now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var zoneName = FriendlyZoneName(Zone);
        var when = Range.IsSingleDay
            ? $"{Range.Label} ({FirstDay.ToString("ddd d MMM", CultureInfo.InvariantCulture)})"
            : $"Last {Range.Label} ({FirstDay.ToString("d MMM", CultureInfo.InvariantCulture)} – {LastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)})";
        return $"{when} · {zoneName} time (UTC{sign}{offset.Duration():hh\\:mm})";
    }

    /// <summary>The zone as a person would name it: "Cairo", "UTC".</summary>
    public string ZoneName => FriendlyZoneName(Zone);

    private static string FriendlyZoneName(TimeZoneInfo zone)
    {
        if (zone == TimeZoneInfo.Utc || zone.Id is "UTC" or "Etc/UTC" or "Coordinated Universal Time")
            return "UTC";
        if (zone.Id is "Egypt Standard Time" or "Africa/Cairo")
            return "Cairo";

        // "(UTC+02:00) Cairo" → "Cairo"; fall back to the id for anything unexpected.
        var display = zone.DisplayName;
        var close = display.IndexOf(')', StringComparison.Ordinal);
        return close >= 0 && close + 1 < display.Length ? display[(close + 1)..].Trim() : zone.Id;
    }
}

/// <summary>
/// The reporting timezone and the current instant, as a service.
/// <para>
/// Exists so that something which only needs to NAME a period -- the portal layout printing
/// "Today (Tue 22 Sep) · Cairo time" -- depends on a clock, not on the analytics database. The
/// store is built with the same zone (see Program.cs), so the two cannot disagree about when a day
/// begins.
/// </para>
/// </summary>
public sealed class ReportClock(TimeZoneInfo zone, Func<DateTimeOffset>? now = null)
{
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>The timezone report windows resolve in.</summary>
    public TimeZoneInfo Zone { get; } = zone ?? throw new ArgumentNullException(nameof(zone));

    /// <summary>Resolves <paramref name="range"/> at the current instant.</summary>
    public ReportWindow Resolve(ReportRange range) => ReportWindow.Resolve(range, _now(), Zone);
}
