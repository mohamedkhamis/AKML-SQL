using System.Collections.Generic;
using System.Linq;
using System.Text;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// What a column's values are, for the formats that write numbers bare (JSON, INSERT). <see cref="Unknown"/>
    /// keeps the guess from the text alone (the SSMS edition's pinned behaviour); a known kind decides by the
    /// column's type, so a varchar <c>'00501'</c> stays text and <c>1,234</c> is never written as two values.
    /// </summary>
    public enum GridValueKind
    {
        Unknown = 0,
        Number = 1,
        Text = 2,
        Binary = 3,
    }

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

        public static string Json(string[] headers, IReadOnlyList<string[]> rows) => Json(headers, rows, null);

        /// <param name="kinds">Each column's kind (by position in <paramref name="headers"/>); null or
        /// <see cref="GridValueKind.Unknown"/> guesses from the text.</param>
        public static string Json(string[] headers, IReadOnlyList<string[]> rows, IReadOnlyList<GridValueKind>? kinds)
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
                    AppendJsonValue(sb, row[c], KindAt(kinds, c));
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
        public static string Insert(string[] headers, IReadOnlyList<string[]> rows, string tableName = PlaceholderTable) =>
            Insert(headers, rows, tableName, null);

        /// <param name="kinds">Each column's kind (by position in <paramref name="headers"/>); null or
        /// <see cref="GridValueKind.Unknown"/> guesses from the text.</param>
        public static string Insert(string[] headers, IReadOnlyList<string[]> rows, string tableName, IReadOnlyList<GridValueKind>? kinds)
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
                    sb.Append(QuoteSqlValue(row[c], KindAt(kinds, c)));
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

        /// <summary>Quote one value as a SQL literal for a column of known <paramref name="kind"/>: NULL; a number
        /// bare when it reads as one (else as text); binary as its <c>0x</c> literal; anything else as N'…'.</summary>
        public static string QuoteSqlValue(string value, GridValueKind kind)
        {
            if (kind == GridValueKind.Unknown) return QuoteSqlValue(value);
            if (value == NullText) return NullText;
            if (kind == GridValueKind.Number && IsPlainNumber(value)) return value;
            if (kind == GridValueKind.Binary && IsHexLiteral(value)) return value;
            return "N'" + value.Replace("'", "''") + "'";
        }

        /// <summary>The kind of a column's values from its declared type, else its CLR hint.</summary>
        public static GridValueKind KindOf(SqlTypeName? type, int clrHint)
        {
            switch (type?.BaseName ?? string.Empty)
            {
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                case "float":
                case "real":
                    return GridValueKind.Number;
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion":
                    return GridValueKind.Binary;
                case "sql_variant":
                    return GridValueKind.Unknown;
                case "":
                    break;
                default:
                    return GridValueKind.Text;
            }
            switch (clrHint)
            {
                case ClrTypeHint.Int64:
                case ClrTypeHint.Double:
                case ClrTypeHint.Decimal:
                case ClrTypeHint.Bool:
                    return GridValueKind.Number;
                case ClrTypeHint.Binary:
                    return GridValueKind.Binary;
                case ClrTypeHint.Variant:
                    return GridValueKind.Unknown;
                default:
                    return GridValueKind.Text;
            }
        }

        private static GridValueKind KindAt(IReadOnlyList<GridValueKind>? kinds, int c) =>
            kinds != null && c < kinds.Count ? kinds[c] : GridValueKind.Unknown;

        /// <summary>A finite number written the invariant way (sign, digits, point, exponent): valid as a T-SQL
        /// literal and as a JSON number. No group separators, currency, spaces, leading zeros, NaN or Infinity.</summary>
        internal static bool IsPlainNumber(string value)
        {
            int i = 0, n = value.Length;
            if (i < n && value[i] == '-') i++;
            int intStart = i;
            while (i < n && value[i] >= '0' && value[i] <= '9') i++;
            int intDigits = i - intStart;
            if (intDigits == 0) return false;
            if (intDigits > 1 && value[intStart] == '0') return false;
            if (i < n && value[i] == '.')
            {
                i++;
                int fracStart = i;
                while (i < n && value[i] >= '0' && value[i] <= '9') i++;
                if (i == fracStart) return false;
            }
            if (i < n && (value[i] == 'e' || value[i] == 'E'))
            {
                i++;
                if (i < n && (value[i] == '+' || value[i] == '-')) i++;
                int expStart = i;
                while (i < n && value[i] >= '0' && value[i] <= '9') i++;
                if (i == expStart) return false;
            }
            return i == n;
        }

        private static bool IsHexLiteral(string value)
        {
            if (value.Length < 2 || value[0] != '0' || (value[1] != 'x' && value[1] != 'X')) return false;
            for (int i = 2; i < value.Length; i++)
            {
                char ch = value[i];
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (!hex) return false;
            }
            return true;
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

        private static void AppendJsonValue(StringBuilder sb, string value, GridValueKind kind)
        {
            if (value == NullText)
            {
                sb.Append("null");
                return;
            }

            if (kind != GridValueKind.Unknown)
            {
                // A known number is written as the grid shows it (no trip through double, so decimal(38,10) keeps
                // its digits); anything that is not a plain number, and every other kind, is a string.
                if (kind == GridValueKind.Number && IsPlainNumber(value)) sb.Append(value);
                else sb.Append('"').Append(EscapeJsonString(value)).Append('"');
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
