namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// Spec 042 (FR-014, research R7) - the position arithmetic between what PostgreSQL reports and what an editor needs.
/// The server and the parser count characters (Unicode code points) from 1; .NET strings and editors count UTF-16 units
/// from 0. They differ only after a character outside the Basic Multilingual Plane (an emoji, some CJK), which takes two
/// units, and that is exactly when a naive mapping would put the caret in the wrong place.
/// </summary>
public static class PgPositions
{
    /// <summary>
    /// The 0-based UTF-16 offset of a 1-based code-point position within <paramref name="text"/>. Positions below 1
    /// map to the start and positions past the end to the end.
    /// </summary>
    public static int ToUtf16Offset(string text, int codePointPosition)
    {
        var offset = 0;
        var count = 1;
        while (offset < text.Length && count < codePointPosition)
        {
            offset += char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;
            count++;
        }
        return offset;
    }

    /// <summary>The 1-based line and column (in UTF-16 units, as an editor counts) of a 0-based offset. LF, CRLF and CR end a line.</summary>
    public static (int Line, int Column) ToLineColumn(string text, int offset)
    {
        var end = Math.Max(0, Math.Min(offset, text.Length));
        var line = 1;
        var column = 1;
        for (var i = 0; i < end; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                line++;
                column = 1;
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    // CRLF: the LF that follows ends the line; the CR itself takes no column.
                    continue;
                }
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return (line, column);
    }

    /// <summary>
    /// Finds the line and column of offsets that are asked for in increasing order, continuing from the previous answer instead of
    /// counting from the start of the text again: a script of thousands of statements stays linear.
    /// </summary>
    public sealed class Cursor
    {
        private readonly string _text;
        private int _offset;
        private int _line = 1;
        private int _column = 1;

        public Cursor(string text) => _text = text;

        /// <summary>The 1-based line and column of <paramref name="offset"/>; an offset before the previous one starts again from the beginning.</summary>
        public (int Line, int Column) MoveTo(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, _text.Length));
            if (offset < _offset)
            {
                _offset = 0;
                _line = 1;
                _column = 1;
            }

            for (; _offset < offset; _offset++)
            {
                var c = _text[_offset];
                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else if (c == '\r')
                {
                    // CRLF: the LF that follows ends the line; the CR itself takes no column.
                    if (_offset + 1 < _text.Length && _text[_offset + 1] == '\n') continue;
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
            }
            return (_line, _column);
        }
    }

    /// <summary>
    /// The 0-based offset in the whole script of a position the server reported inside one statement: that statement was
    /// sent on its own, so the position is relative to its text.
    /// </summary>
    public static int ToDocumentOffset(PgStatement statement, int codePointPositionInStatement) =>
        statement.Start + ToUtf16Offset(statement.Text, codePointPositionInStatement);
}
