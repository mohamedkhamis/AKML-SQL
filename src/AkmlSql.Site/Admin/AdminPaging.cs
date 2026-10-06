using System.Globalization;

namespace AkmlSql.Site.Admin;

public static class AdminPaging
{
    public const int PageSize = 50;
    public static int Parse(string? raw) => int.TryParse(raw, NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var page) ? Math.Max(0, page) : 0;
    public static int Clamp(string? raw, long total) =>
        (int)Math.Min(Parse(raw), Math.Max(0, (total - 1) / PageSize));
    public static string? Search(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim()[..Math.Min(raw.Trim().Length, 200)];
}
