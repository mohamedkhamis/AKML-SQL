using System.Globalization;
using AkmlSql.Core.Text;

namespace AkmlSql.Web.Shared.Grid;

/// <summary>
/// Spec 041 (research R8, FR-002) — the words the grid uses about one cell's value. A value the engine
/// itself cut (very large text or binary) arrives as <c>[text N chars]</c> / <c>[binary N bytes]</c>:
/// the grid says so, copies it verbatim and counts it, and never pretends the whole value is available.
/// </summary>
public static class GridCellText
{
    /// <summary>Characters of a value shown on hover (the full value is in View value).</summary>
    public const int TooltipChars = 1_000;

    /// <summary>The title an engine-cut cell carries.</summary>
    public const string EngineCutTitle = "The engine returned only this value's size; the value itself was not sent.";

    public static bool IsEngineCutIndicator(string? value) => EngineCutIndicator.IsIndicator(value);

    /// <summary>"70,000 characters", "1,048,576 bytes", "NULL".</summary>
    public static string DescribeSize(string? value)
    {
        if (value == null) return "NULL";
        if (EngineCutIndicator.TryParse(value, out var kind, out var size))
        {
            return kind == EngineCutKind.Binary ? Count(size, "byte") : Count(size, "character");
        }
        return Count(value.Length, "character");
    }

    /// <summary>Hover text for a cell: its first <see cref="TooltipChars"/> characters; null when empty.</summary>
    public static string? Tooltip(string? display)
    {
        if (string.IsNullOrEmpty(display)) return null;
        return display.Length <= TooltipChars ? display : display.Substring(0, TooltipChars) + "…";
    }

    private static string Count(long n, string unit) =>
        n.ToString("N0", CultureInfo.InvariantCulture) + " " + unit + (n == 1 ? string.Empty : "s");
}
