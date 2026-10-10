using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Spec 041 (research R5, R12, R13) — the results-grid text formats both editions share: the SSMS
    /// edition's "Copy As" menu (moved here from <c>Shell.Shared/Productivity/Grid/GridCopyAsMenu</c>,
    /// which now delegates) and the web edition's Copy, Copy as and Save results as.
    ///
    /// <para>Behaviour is pinned byte for byte to what the SSMS formatters produced before the move
    /// (<c>GridTextFormatsTests</c> + <c>GridTextFormats.goldens.json</c>), quirks included: numbers are
    /// detected with <c>NumberStyles.Any</c> (so <c>1,000</c> is a number), JSON writes <c>12.50</c> as
    /// <c>12.5</c>, Markdown escapes <c>|</c> in cells but not headers. Lines end in an explicit CRLF — the
    /// browser runtime's <c>AppendLine</c> would write a bare LF.</para>
    /// <para>Values arrive as display text; <see cref="NullText"/> marks SQL NULL.</para>
    /// </summary>
    public static class GridTextFormats
    {
        /// <summary>How a SQL NULL cell is written in every format.</summary>
        public const string NullText = "NULL";

        /// <summary>The INSERT format's target when no table name is known.</summary>
        public const string PlaceholderTable = "[TableName]";

        private const string Crlf = "\r\n";

        public static string Csv(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", headers.Select(EscapeCsvField))).Append(Crlf);
            foreach (var row in rows)
            {
                sb.Append(string.Join(",", row.Select(EscapeCsvField))).Append(Crlf);
            }
            return sb.ToString();
        }

        /// <summary>Tab-separated; Ctrl+C copies without the header line, Ctrl+Shift+C with it.</summary>
        public static string Tsv(string[] headers, IReadOnlyList<string[]> rows, bool includeHeader = true)
        {
            var sb = new StringBuilder();
            if (includeHeader) sb.Append(string.Join("\t", headers)).Append(Crlf);
            foreach (var row in rows)
            {
                sb.Append(string.Join("\t", row)).Append(Crlf);
            }
            return sb.ToString();
        }

        public static string Json(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("[").Append(Crlf);
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("  {");
                var row = rows[r];
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append('"');
                    sb.Append(EscapeJsonString(headers[c]));
                    sb.Append("\": ");
                    AppendJsonValue(sb, row[c]);
                }
                sb.Append('}');
                if (r < rows.Count - 1) sb.Append(',');
                sb.Append(Crlf);
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string Xml(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Append(Crlf);
            sb.Append("<rows>").Append(Crlf);
            foreach (var row in rows)
            {
                sb.Append("  <row>");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    var tag = SanitizeXmlTag(headers[c]);
                    sb.Append('<').Append(tag).Append('>');
                    sb.Append(EscapeXml(row[c]));
                    sb.Append("</").Append(tag).Append('>');
                }
                sb.Append("</row>").Append(Crlf);
            }
            sb.Append("</rows>");
            return sb.ToString();
        }

        public static string Html(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<table>").Append(Crlf);
            sb.Append("  <thead><tr>");
            foreach (var h in headers)
            {
                sb.Append("<th>").Append(EscapeHtml(h)).Append("</th>");
            }
            sb.Append("</tr></thead>").Append(Crlf);
            sb.Append("  <tbody>").Append(Crlf);
            foreach (var row in rows)
            {
                sb.Append("    <tr>");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    sb.Append("<td>").Append(EscapeHtml(row[c])).Append("</td>");
                }
                sb.Append("</tr>").Append(Crlf);
            }
            sb.Append("  </tbody>").Append(Crlf);
            sb.Append("</table>");
            return sb.ToString();
        }

        /// <summary>One INSERT per row. <paramref name="tableName"/> is written as given (already quoted).</summary>
        public static string Insert(string[] headers, IReadOnlyList<string[]> rows, string tableName = PlaceholderTable)
        {
            var sb = new StringBuilder();
            var columnList = string.Join(", ", headers.Select(h => "[" + h + "]"));
            foreach (var row in rows)
            {
                sb.Append("INSERT INTO ").Append(tableName).Append(" (");
                sb.Append(columnList);
                sb.Append(") VALUES (");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(QuoteSqlValue(row[c]));
                }
                sb.Append(");").Append(Crlf);
            }
            return sb.ToString();
        }

        /// <summary>A WHERE … IN (…) over the first column's values (NULLs excluded with a note).</summary>
        public static string InClause(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            var columnName = headers.Length > 0 ? headers[0] : "Column";

            var values = new List<string>();
            bool hasNulls = false;
            foreach (var row in rows)
            {
                var val = row.Length > 0 ? row[0] : NullText;
                if (val == NullText)
                {
                    hasNulls = true;
                    continue;
                }
                values.Add(val);
            }

            var formattedValues = new List<string>();
            foreach (var val in values)
            {
                if (long.TryParse(val, out _))
                {
                    formattedValues.Add(val);
                }
                else if (double.TryParse(val, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    formattedValues.Add(val);
                }
                else
                {
                    formattedValues.Add("'" + val.Replace("'", "''") + "'");
                }
            }

            sb.Append("WHERE [");
            sb.Append(columnName);
            sb.Append("] IN (");
            sb.Append(string.Join(", ", formattedValues));
            sb.Append(')');

            if (hasNulls)
            {
                sb.Append(Crlf);
                sb.Append("-- NULL values excluded");
            }

            if (values.Count > 1000)
            {
                sb.Append(Crlf);
                sb.Append("-- Note: SQL Server IN clause limited to ~1000 values");
            }

            sb.Append(Crlf);
            return sb.ToString();
        }

        public static string Markdown(string[] headers, IReadOnlyList<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("| ");
            sb.Append(string.Join(" | ", headers));
            sb.Append(" |").Append(Crlf);
            sb.Append("| ");
            sb.Append(string.Join(" | ", headers.Select(_ => "---")));
            sb.Append(" |").Append(Crlf);
            foreach (var row in rows)
            {
                sb.Append("| ");
                var values = new string[headers.Length];
                for (int c = 0; c < headers.Length; c++)
                {
                    values[c] = c < row.Length ? row[c].Replace("|", "\\|") : "";
                }
                sb.Append(string.Join(" | ", values));
                sb.Append(" |").Append(Crlf);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Spec 041 FR-030 — "Save results as": a header row then one line per row, comma- or
        /// tab-delimited. A value holding the delimiter, a double quote or a line break is enclosed in
        /// double quotes with inner quotes doubled. With <paramref name="retainLineBreaks"/> off (the SSMS
        /// default) a line break inside a value becomes a space, so every row stays on one line.
        /// </summary>
        public static string Delimited(string[] headers, IReadOnlyList<string[]> rows, char delimiter, bool retainLineBreaks)
        {
            var sb = new StringBuilder();
            AppendDelimitedLine(sb, headers, delimiter, retainLineBreaks);
            foreach (var row in rows) AppendDelimitedLine(sb, row, delimiter, retainLineBreaks);
            return sb.ToString();
        }

        /// <summary>Quote one value as a SQL literal: NULL, numbers bare, booleans as 1/0, text as N'…'.</summary>
        public static string QuoteSqlValue(string value)
        {
            if (value == NullText) return NullText;

            if (long.TryParse(value, out _)) return value;
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return value;

            if (value is "True" or "true") return "1";
            if (value is "False" or "false") return "0";

            return "N'" + value.Replace("'", "''") + "'";
        }

        /// <summary>Replace every CR, LF or CRLF in <paramref name="value"/> with one space.</summary>
        public static string FlattenLineBreaks(string value)
        {
            if (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0) return value;
            return value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void AppendDelimitedLine(StringBuilder sb, string[] fields, char delimiter, bool retainLineBreaks)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(delimiter);
                var value = fields[i] ?? string.Empty;
                if (!retainLineBreaks) value = FlattenLineBreaks(value);
                bool quote = value.IndexOf(delimiter) >= 0 || value.IndexOf('"') >= 0
                             || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
                if (quote) sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                else sb.Append(value);
            }
            sb.Append(Crlf);
        }

        private static string EscapeCsvField(string value)
        {
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private static string EscapeJsonString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static void AppendJsonValue(StringBuilder sb, string value)
        {
            if (value == NullText)
            {
                sb.Append("null");
                return;
            }

            if (long.TryParse(value, out var longVal))
            {
                sb.Append(longVal);
                return;
            }
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
            {
                sb.Append(dblVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (value is "True" or "true" or "False" or "false")
            {
                sb.Append(value.ToLowerInvariant());
                return;
            }

            sb.Append('"');
            sb.Append(EscapeJsonString(value));
            sb.Append('"');
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string EscapeHtml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string SanitizeXmlTag(string name)
        {
            var sanitized = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (i == 0 && !char.IsLetter(ch) && ch != '_')
                {
                    sanitized.Append('_');
                }
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                {
                    sanitized.Append(ch);
                }
                else
                {
                    sanitized.Append('_');
                }
            }
            return sanitized.Length > 0 ? sanitized.ToString() : "column";
        }
    }
}
