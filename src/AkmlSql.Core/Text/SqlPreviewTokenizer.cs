using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Lightweight, pure T-SQL tokenizer that backs the SQL HISTORY previews on both editions (the
    /// web History page and the desktop History tool window). Emits CONTIGUOUS spans that cover every
    /// character — so the concatenated span text always equals the input verbatim — classifying string
    /// literals, line/block comments, and a fixed keyword set; everything else is
    /// <see cref="KindDefault"/>. Intentionally simple: it colours for readability, it is not a parser.
    /// Pure C# only (netstandard2.0 + net10.0) so both editions can share it.
    /// <para>
    /// The desktop format-preview surfaces (historically the retired ProfileEditor renderer)
    /// was deliberately NOT migrated onto this tokenizer: it keeps its own richer tokenizer by design,
    /// because the format preview needs finer number/function colouring than the history previews require.
    /// </para>
    /// </summary>
    public static class SqlPreviewTokenizer
    {
        public const string KindKeyword = "keyword";
        public const string KindString = "string";
        public const string KindComment = "comment";
        public const string KindDefault = "default";

        /// <summary>
        /// The union of both history preview surfaces' keyword sets (superset), including the extra
        /// keywords (APPLY / EXCEPT / INTERSECT / RETURNS / TEXT / CLUSTERED / NONCLUSTERED / NTILE)
        /// so the web and desktop history previews colour the same words.
        /// </summary>
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "INTO", "VALUES", "SET",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "APPLY", "ON", "AS",
            "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "BETWEEN", "LIKE", "GROUP", "BY",
            "ORDER", "HAVING", "DISTINCT", "TOP", "UNION", "ALL", "EXCEPT", "INTERSECT", "CASE",
            "WHEN", "THEN", "ELSE", "END", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX",
            "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "DATABASE", "SCHEMA", "PRIMARY", "KEY",
            "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE", "CLUSTERED",
            "NONCLUSTERED", "DECLARE", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "TRAN",
            "RETURN", "RETURNS", "EXEC", "EXECUTE", "WITH", "OVER", "PARTITION", "ASC", "DESC",
            "INT", "BIGINT", "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "BIT", "TEXT", "DATE",
            "DATETIME", "DATETIME2", "DECIMAL", "NUMERIC", "FLOAT", "MONEY", "UNIQUEIDENTIFIER",
            "IDENTITY", "OUTPUT", "MERGE", "USING", "GO", "IF", "WHILE", "TRY", "CATCH", "THROW",
            "CAST", "CONVERT", "COALESCE", "ISNULL", "COUNT", "SUM", "AVG", "MIN", "MAX",
            "GETDATE", "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        };

        /// <summary>Tokenizes SQL into contiguous spans covering every character.</summary>
        public static IReadOnlyList<(int Start, int Length, string Kind)> Tokenize(string text)
        {
            var tokens = new List<(int, int, string)>();
            if (string.IsNullOrEmpty(text)) return tokens;

            int i = 0, n = text.Length, runStart = 0;
            void EmitDefault(int from, int to) { if (to > from) tokens.Add((from, to - from, KindDefault)); }

            while (i < n)
            {
                char c = text[i];
                if (c == '-' && i + 1 < n && text[i + 1] == '-')               // line comment
                {
                    EmitDefault(runStart, i);
                    int s = i; i += 2;
                    while (i < n && text[i] != '\n') i++;
                    tokens.Add((s, i - s, KindComment)); runStart = i; continue;
                }
                if (c == '/' && i + 1 < n && text[i + 1] == '*')               // block comment
                {
                    EmitDefault(runStart, i);
                    int s = i; i += 2;
                    while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/')) i++;
                    if (i < n) i += 2;
                    tokens.Add((s, i - s, KindComment)); runStart = i; continue;
                }
                if (c == '\'')                                                 // string literal
                {
                    EmitDefault(runStart, i);
                    int s = i; i++;
                    while (i < n)
                    {
                        if (text[i] == '\'')
                        {
                            if (i + 1 < n && text[i + 1] == '\'') { i += 2; continue; }
                            i++; break;
                        }
                        i++;
                    }
                    tokens.Add((s, i - s, KindString)); runStart = i; continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')      // word / keyword
                {
                    int s = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@' || text[i] == '#')) i++;
                    if (Keywords.Contains(text.Substring(s, i - s)))
                    {
                        EmitDefault(runStart, s);
                        tokens.Add((s, i - s, KindKeyword)); runStart = i;
                    }
                    continue;
                }
                i++;
            }
            EmitDefault(runStart, n);
            return tokens;
        }
    }
}
