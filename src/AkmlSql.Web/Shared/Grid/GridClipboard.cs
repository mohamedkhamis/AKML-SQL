using AkmlSql.Core.Text;

namespace AkmlSql.Web.Shared.Grid;

/// <summary>The forms a grid selection can be copied in (FR-009 tab-separated; FR-031 Copy as).</summary>
public enum CopyFormat
{
    Tsv,
    Csv,
    Json,
    Markdown,
    Insert,
}

/// <summary>What a copy put on the clipboard, and what the user should be told about it.</summary>
/// <param name="EngineCutCount">How many copied values the engine had cut (copied as their indicator).</param>
/// <param name="Truncated">The text was over the limit and only a prefix was copied.</param>
public sealed record GridCopyResult(string Text, int RowCount, int EngineCutCount, bool Truncated);

/// <summary>
/// Spec 041 (research R5, FR-009, FR-031) — turns a grid selection into clipboard text. Copied text is
/// the text the grid shows, except that a value the column width hides is copied in full and a value the
/// engine cut is copied as its indicator (and counted, so the grid can say so). Rows are copied in
/// display order (a sort is respected); the copied columns are the union of the selected ones, and a
/// cell outside a disjoint selection copies as empty. Ctrl+C copies tab-separated text with each line
/// break replaced by a space unless "retain line breaks" is on; the Copy as formats come from
/// <see cref="GridTextFormats"/>, the same code the SSMS edition's Copy As uses.
/// </summary>
public static class GridClipboard
{
    /// <summary>Above this many characters only a prefix is copied (browsers and spreadsheets choke).</summary>
    public const int MaxCopyChars = 20_000_000;

    /// <param name="displayToFetched">The fetched row shown at each display position (the sort permutation).</param>
    /// <param name="textAt">The display text of a cell by fetched row and column; null for SQL NULL.</param>
    public static GridCopyResult Build(
        GridSelection selection,
        IReadOnlyList<string> headers,
        IReadOnlyList<int> displayToFetched,
        Func<int, int, string?> textAt,
        CopyFormat format,
        bool includeHeader,
        bool retainLineBreaks,
        string? tableName = null,
        int maxChars = MaxCopyChars)
    {
        var rows = selection.SelectedRows();
        var cols = selection.SelectedColumns();
        if (rows.Count == 0 || cols.Count == 0) return new GridCopyResult(string.Empty, 0, 0, false);

        var head = cols.Select(c => c < headers.Count ? headers[c] : string.Empty).ToArray();
        var data = new List<string[]>(rows.Count);
        int cut = 0;
        foreach (var r in rows)
        {
            int fetched = r < displayToFetched.Count ? displayToFetched[r] : r;
            var line = new string[cols.Count];
            for (int i = 0; i < cols.Count; i++)
            {
                int c = cols[i];
                if (!selection.IsSelected(r, c))
                {
                    line[i] = string.Empty;
                    continue;
                }
                var value = textAt(fetched, c);
                if (value == null)
                {
                    line[i] = GridTextFormats.NullText;
                    continue;
                }
                if (EngineCutIndicator.IsIndicator(value)) cut++;
                line[i] = format == CopyFormat.Tsv && !retainLineBreaks ? GridTextFormats.FlattenLineBreaks(value) : value;
            }
            data.Add(line);
        }

        string text = format switch
        {
            CopyFormat.Csv => GridTextFormats.Csv(head, data),
            CopyFormat.Json => GridTextFormats.Json(head, data),
            CopyFormat.Markdown => GridTextFormats.Markdown(head, data),
            CopyFormat.Insert => GridTextFormats.Insert(head, data, string.IsNullOrWhiteSpace(tableName) ? GridTextFormats.PlaceholderTable : tableName),
            _ => GridTextFormats.Tsv(head, data, includeHeader),
        };

        bool truncated = text.Length > maxChars;
        if (truncated) text = text.Substring(0, maxChars);
        return new GridCopyResult(text, rows.Count, cut, truncated);
    }
}
