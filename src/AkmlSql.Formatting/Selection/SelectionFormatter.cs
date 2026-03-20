using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Selection;

public class SelectionFormatter
{
    private readonly FormatterPipeline _pipeline = new();

    public SelectionResult FormatSelection(string fullText, int selectionStart, int selectionEnd, FormattingProfile profile)
    {
        // Expand selection to enclosing statement boundaries
        var (expandedStart, expandedEnd) = ExpandToStatementBoundaries(fullText, selectionStart, selectionEnd);

        var selectedText = fullText[expandedStart..expandedEnd];
        var result = _pipeline.Format(selectedText, profile);

        return new SelectionResult
        {
            Success = result.Success,
            FormattedText = result.FormattedText,
            OriginalStart = expandedStart,
            OriginalEnd = expandedEnd,
            WasModified = result.WasModified,
            ValidationPassed = result.ValidationPassed,
            ElapsedMs = result.ElapsedMs
        };
    }

    private static (int start, int end) ExpandToStatementBoundaries(string text, int start, int end)
    {
        // Expand backwards to start of statement (find previous ; or GO or start of text)
        int expandedStart = start;
        while (expandedStart > 0)
        {
            char c = text[expandedStart - 1];
            if (c == ';')
            {
                break;
            }
            expandedStart--;
        }

        // Skip whitespace at start
        while (expandedStart < text.Length && char.IsWhiteSpace(text[expandedStart]))
            expandedStart++;

        // Expand forwards to end of statement
        int expandedEnd = end;
        while (expandedEnd < text.Length)
        {
            char c = text[expandedEnd];
            if (c == ';')
            {
                expandedEnd++; // Include the semicolon
                break;
            }
            expandedEnd++;
        }

        // Ensure we don't go past the text
        expandedEnd = Math.Min(expandedEnd, text.Length);
        expandedStart = Math.Max(expandedStart, 0);

        return (expandedStart, expandedEnd);
    }
}

public class SelectionResult
{
    public bool Success { get; set; }
    public string FormattedText { get; set; } = string.Empty;
    public int OriginalStart { get; set; }
    public int OriginalEnd { get; set; }
    public bool WasModified { get; set; }
    public bool ValidationPassed { get; set; }
    public long ElapsedMs { get; set; }
}
