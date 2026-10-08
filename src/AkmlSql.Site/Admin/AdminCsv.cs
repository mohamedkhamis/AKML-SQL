using System.Globalization;
using System.Text;

namespace AkmlSql.Site.Admin;

/// <summary>Streams rows without buffering a whole export, preserving text in spreadsheet cells.</summary>
public static class AdminCsv
{
    public static IResult Download(HttpContext http, string name, IEnumerable<object?[]> rows)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Stream(async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: true);
            foreach (var row in rows)
            {
                http.RequestAborted.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(Line(row).AsMemory(), http.RequestAborted);
            }
        }, "text/csv; charset=utf-8", name);
    }

    internal static string Line(params object?[] cells) => string.Join(',', cells.Select(Cell));

    private static string Cell(object? cell)
    {
        var value = cell is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) ?? "" : cell?.ToString() ?? "";
        // Quoting alone does not stop spreadsheet formula evaluation.
        var trimmed = value.TrimStart();
        if (cell is string && trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
