using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T101: Renders SQL text in a <see cref="RichTextBox"/> with basic syntax coloring.
    /// Targets .NET Framework 4.7.2 (Shell.Shared). Uses programmatic WPF only.
    /// </summary>
    internal sealed class SqlPreviewRenderer
    {
        // SQL keyword sets for colorization
        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "APPLY", "ON",
            "AS", "WITH", "CASE", "WHEN", "THEN", "ELSE", "END",
            "BEGIN", "IF", "WHILE", "TRY", "CATCH", "THROW", "RETURN",
            "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER",
            "GROUP", "BY", "ORDER", "HAVING", "DISTINCT", "TOP", "OVER", "PARTITION",
            "UNION", "ALL", "EXCEPT", "INTERSECT",
            "NULL", "IS", "LIKE", "EXEC", "EXECUTE", "DECLARE", "GO",
            "ASC", "DESC", "OUTPUT", "RETURNS", "NVARCHAR", "VARCHAR", "INT",
            "BIGINT", "BIT", "DATETIME", "FLOAT", "DECIMAL", "CHAR", "TEXT",
            "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT",
            "IDENTITY", "CLUSTERED", "NONCLUSTERED", "UNIQUE",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "COUNT", "SUM", "AVG", "MIN", "MAX",
        };

        private static readonly HashSet<string> BuiltInFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "GETDATE", "GETUTCDATE", "DATEADD", "DATEDIFF", "DATENAME", "DATEPART",
            "CONVERT", "CAST", "ISNULL", "COALESCE", "NULLIF",
            "LEN", "LEFT", "RIGHT", "SUBSTRING", "CHARINDEX", "REPLACE",
            "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
            "COUNT", "SUM", "AVG", "MIN", "MAX",
            "ABS", "CEILING", "FLOOR", "ROUND", "POWER",
            "NEWID", "SCOPE_IDENTITY", "OBJECT_ID", "DB_NAME",
        };

        private readonly RichTextBox _richTextBox;
        private SolidColorBrush _keywordBrush;
        private SolidColorBrush _functionBrush;
        private SolidColorBrush _stringBrush;
        private SolidColorBrush _commentBrush;
        private SolidColorBrush _numberBrush;
        private SolidColorBrush _defaultBrush;
        private SolidColorBrush _backgroundBrush;

        public SqlPreviewRenderer(RichTextBox richTextBox)
        {
            _richTextBox = richTextBox ?? throw new ArgumentNullException(nameof(richTextBox));
            ApplyThemeColors();
            ConfigureRichTextBox();
        }

        /// <summary>
        /// Renders SQL text with syntax coloring into the RichTextBox.
        /// </summary>
        public void Render(string sql)
        {
            if (sql == null) sql = string.Empty;

            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PagePadding = new Thickness(4),
                Background = _backgroundBrush,
            };

            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 1,
            };

            Tokenize(sql, paragraph);

            doc.Blocks.Add(paragraph);
            _richTextBox.Document = doc;
        }

        /// <summary>
        /// Refreshes colors based on the current theme.
        /// </summary>
        public void ApplyThemeColors()
        {
            var theme = ThemeManager.Instance;
            var isDark = theme.DetectTheme() == VsThemeKind.Dark;

            if (isDark)
            {
                _keywordBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214));     // Blue
                _functionBrush = new SolidColorBrush(Color.FromRgb(220, 220, 170));    // Light yellow
                _stringBrush = new SolidColorBrush(Color.FromRgb(206, 145, 120));      // Orange
                _commentBrush = new SolidColorBrush(Color.FromRgb(106, 153, 85));      // Green
                _numberBrush = new SolidColorBrush(Color.FromRgb(181, 206, 168));      // Light green
                _defaultBrush = new SolidColorBrush(Color.FromRgb(212, 212, 212));     // Light gray
                _backgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));     // Dark bg
            }
            else
            {
                _keywordBrush = new SolidColorBrush(Color.FromRgb(0, 0, 255));         // Blue
                _functionBrush = new SolidColorBrush(Color.FromRgb(116, 83, 31));      // Brown
                _stringBrush = new SolidColorBrush(Color.FromRgb(163, 21, 21));        // Red
                _commentBrush = new SolidColorBrush(Color.FromRgb(0, 128, 0));         // Green
                _numberBrush = new SolidColorBrush(Color.FromRgb(9, 134, 88));         // Teal
                _defaultBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));           // Black
                _backgroundBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));  // White
            }

            // Freeze brushes for performance
            _keywordBrush.Freeze();
            _functionBrush.Freeze();
            _stringBrush.Freeze();
            _commentBrush.Freeze();
            _numberBrush.Freeze();
            _defaultBrush.Freeze();
            _backgroundBrush.Freeze();
        }

        // -------------------------------------------------------------------
        // Tokenizer — simple SQL lexer for syntax coloring
        // -------------------------------------------------------------------

        private void Tokenize(string sql, Paragraph paragraph)
        {
            int i = 0;
            int len = sql.Length;

            while (i < len)
            {
                char c = sql[i];

                // Line comment: --
                if (c == '-' && i + 1 < len && sql[i + 1] == '-')
                {
                    int start = i;
                    while (i < len && sql[i] != '\n')
                        i++;
                    AddRun(paragraph, sql.Substring(start, i - start), _commentBrush);
                    continue;
                }

                // Block comment: /* ... */
                if (c == '/' && i + 1 < len && sql[i + 1] == '*')
                {
                    int start = i;
                    i += 2;
                    while (i + 1 < len && !(sql[i] == '*' && sql[i + 1] == '/'))
                        i++;
                    if (i + 1 < len) i += 2;
                    AddRun(paragraph, sql.Substring(start, i - start), _commentBrush);
                    continue;
                }

                // String literal: '...'
                if (c == '\'')
                {
                    int start = i;
                    i++;
                    while (i < len)
                    {
                        if (sql[i] == '\'' && i + 1 < len && sql[i + 1] == '\'')
                        {
                            i += 2; // Escaped quote
                        }
                        else if (sql[i] == '\'')
                        {
                            i++;
                            break;
                        }
                        else
                        {
                            i++;
                        }
                    }
                    AddRun(paragraph, sql.Substring(start, i - start), _stringBrush);
                    continue;
                }

                // N-prefixed string literal: N'...'
                if (c is 'N' or 'n' && i + 1 < len && sql[i + 1] == '\'')
                {
                    int start = i;
                    i += 2;
                    while (i < len)
                    {
                        if (sql[i] == '\'' && i + 1 < len && sql[i + 1] == '\'')
                        {
                            i += 2;
                        }
                        else if (sql[i] == '\'')
                        {
                            i++;
                            break;
                        }
                        else
                        {
                            i++;
                        }
                    }
                    AddRun(paragraph, sql.Substring(start, i - start), _stringBrush);
                    continue;
                }

                // Number
                if (char.IsDigit(c) || (c == '.' && i + 1 < len && char.IsDigit(sql[i + 1])))
                {
                    int start = i;
                    while (i < len && (char.IsDigit(sql[i]) || sql[i] == '.'))
                        i++;
                    AddRun(paragraph, sql.Substring(start, i - start), _numberBrush);
                    continue;
                }

                // Identifier / keyword
                if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
                {
                    int start = i;
                    while (i < len && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@' || sql[i] == '#'))
                        i++;
                    var word = sql.Substring(start, i - start);

                    if (Keywords.Contains(word))
                        AddRun(paragraph, word, _keywordBrush);
                    else if (BuiltInFunctions.Contains(word))
                        AddRun(paragraph, word, _functionBrush);
                    else
                        AddRun(paragraph, word, _defaultBrush);
                    continue;
                }

                // Bracketed identifier: [...]
                if (c == '[')
                {
                    int start = i;
                    i++;
                    while (i < len && sql[i] != ']')
                        i++;
                    if (i < len) i++; // closing bracket
                    AddRun(paragraph, sql.Substring(start, i - start), _defaultBrush);
                    continue;
                }

                // Newline
                if (c is '\r' or '\n')
                {
                    if (c == '\r' && i + 1 < len && sql[i + 1] == '\n')
                        i++;
                    i++;
                    paragraph.Inlines.Add(new LineBreak());
                    continue;
                }

                // Whitespace and operators — pass through
                int wsStart = i;
                while (i < len && !char.IsLetterOrDigit(sql[i]) && sql[i] != '_' && sql[i] != '@'
                    && sql[i] != '#' && sql[i] != '\'' && sql[i] != '-' && sql[i] != '/'
                    && sql[i] != '[' && sql[i] != '\r' && sql[i] != '\n'
                    && !(sql[i] == '.' && i + 1 < len && char.IsDigit(sql[i + 1])))
                {
                    i++;
                    if (i > wsStart + 200) break; // safety
                }
                if (i == wsStart) i++; // Advance at least one character to prevent infinite loop

                AddRun(paragraph, sql.Substring(wsStart, i - wsStart), _defaultBrush);
            }
        }

        private static void AddRun(Paragraph paragraph, string text, SolidColorBrush foreground)
        {
            if (string.IsNullOrEmpty(text)) return;
            paragraph.Inlines.Add(new Run(text) { Foreground = foreground });
        }

        private void ConfigureRichTextBox()
        {
            _richTextBox.IsReadOnly = true;
            _richTextBox.BorderThickness = new Thickness(0);
            _richTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _richTextBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _richTextBox.Background = _backgroundBrush;
        }
    }
}
