using System.Globalization;

namespace AkmlSql.Site.Admin;

/// <summary>
/// How a figure moved against the comparison period, ready to render: a short visible text, a
/// full sentence for screen readers, and a direction the stylesheet colours.
/// </summary>
/// <param name="Direction"><c>up</c>, <c>down</c>, <c>flat</c> or <c>new</c> — used as a CSS class suffix.</param>
/// <param name="Text">Visible text, e.g. "▲ 12%".</param>
/// <param name="Description">Screen-reader sentence, e.g. "Up 12% compared with yesterday until 10:00".</param>
public sealed record ChangeIndicator(string Direction, string Text, string Description)
{
    /// <summary>
    /// Change in a count, as a percentage of the previous value.
    /// <para>
    /// A previous value of zero has no meaningful percentage — "up ∞%" is not information — so a
    /// rise from nothing says "new" and states the count instead. Both zero is flat, not a 0%
    /// change: there was nothing to change.
    /// </para>
    /// </summary>
    public static ChangeIndicator ForCount(long current, long previous, string comparedWith)
    {
        if (previous == 0 && current == 0)
        {
            return new ChangeIndicator("flat", "—", $"None in this period or in {comparedWith}");
        }

        if (previous == 0)
        {
            return new ChangeIndicator(
                "new", "▲ new",
                $"Up from none in {comparedWith}");
        }

        var percent = (current - previous) * 100.0 / previous;
        var rounded = Math.Round(Math.Abs(percent), percent is > -10 and < 10 ? 1 : 0);
        var shown = rounded.ToString("0.#", CultureInfo.InvariantCulture);

        if (rounded == 0)
        {
            return new ChangeIndicator("flat", "● 0%", $"Unchanged compared with {comparedWith} ({previous:N0})");
        }

        return current > previous
            ? new ChangeIndicator("up", $"▲ {shown}%", $"Up {shown}% compared with {comparedWith} ({previous:N0})")
            : new ChangeIndicator("down", $"▼ {shown}%", $"Down {shown}% compared with {comparedWith} ({previous:N0})");
    }

    /// <summary>
    /// Change in a percentage, in percentage POINTS. A conversion rate going from 2% to 3% is up one
    /// point; calling that "up 50%" is technically true and reliably misread.
    /// </summary>
    public static ChangeIndicator ForPoints(double current, double previous, string comparedWith)
    {
        var delta = Math.Round(current - previous, 1);
        var shown = Math.Abs(delta).ToString("0.#", CultureInfo.InvariantCulture);
        var before = previous.ToString("0.#", CultureInfo.InvariantCulture);

        if (delta == 0)
        {
            return new ChangeIndicator("flat", "● 0 pts", $"Unchanged compared with {comparedWith} ({before}%)");
        }

        return delta > 0
            ? new ChangeIndicator("up", $"▲ {shown} pts", $"Up {shown} percentage points compared with {comparedWith} ({before}%)")
            : new ChangeIndicator("down", $"▼ {shown} pts", $"Down {shown} percentage points compared with {comparedWith} ({before}%)");
    }
}
