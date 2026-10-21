using System;
using System.Globalization;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Spec 041 (research R18, FR-022) — the Messages tab's lines, worded as SSMS words them: an error's
    /// <c>Msg 208, Level 16, State 1, Line 4</c> header (with <c>Procedure p, </c> before <c>Line</c> when
    /// the error came from one) and its text on the next line; <c>(N rows affected)</c>; PRINT text as is;
    /// the closing <c>Completion time: …</c>. The engine fills the legacy <c>Messages[]</c> strings with these
    /// and the web renders the structured messages through them, so both read the same.
    /// </summary>
    public static class SqlMessageText
    {
        /// <summary>Between an error's header and its text.</summary>
        public const string NewLine = "\n";

        /// <summary>What a script that returned no rows and no messages reports.</summary>
        public const string CommandsCompleted = "Commands completed successfully.";

        public static string Error(int number, int severity, int state, int line, string? procedure, string text)
        {
            var proc = string.IsNullOrEmpty(procedure) ? string.Empty : "Procedure " + procedure + ", ";
            return "Msg " + number.ToString(CultureInfo.InvariantCulture)
                + ", Level " + severity.ToString(CultureInfo.InvariantCulture)
                + ", State " + state.ToString(CultureInfo.InvariantCulture)
                + ", " + proc + "Line " + line.ToString(CultureInfo.InvariantCulture)
                + NewLine + (text ?? string.Empty);
        }

        /// <param name="moreExist">The set was cut at the row limit: <paramref name="count"/> rows were
        /// returned and the server had more.</param>
        public static string RowCount(long count, bool moreExist = false)
        {
            var n = count.ToString(CultureInfo.InvariantCulture);
            if (moreExist) return "(" + n + (count == 1 ? " row" : " rows") + " returned; more exist)";
            return "(" + n + (count == 1 ? " row" : " rows") + " affected)";
        }

        /// <summary>PRINT and low-severity messages read as their text.</summary>
        public static string Info(string text) => text ?? string.Empty;

        /// <summary>Lines the engine adds ("Beginning execution loop", …) read as their text.</summary>
        public static string System(string text) => text ?? string.Empty;

        /// <summary>The last line of the Messages tab, in the browser's local time.</summary>
        public static string CompletionLine(DateTimeOffset local) =>
            "Completion time: " + local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

        /// <summary>
        /// Spec 042 (FR-014): a PostgreSQL error as the Messages tab shows it - <c>SQLSTATE 42P01: message</c>, then the
        /// server's <c>DETAIL:</c> and <c>HINT:</c> on their own lines when it sent them. The position is not in the text;
        /// it is what the Problems list and the click-to-line use.
        /// </summary>
        public static string ForPostgres(ExecuteMessageDto message)
        {
            if (message == null) return string.Empty;
            var text = "SQLSTATE " + (message.SqlState ?? string.Empty) + ": " + (message.Text ?? string.Empty);
            if (!string.IsNullOrEmpty(message.Detail)) text += NewLine + "DETAIL: " + message.Detail;
            if (!string.IsNullOrEmpty(message.Hint)) text += NewLine + "HINT: " + message.Hint;
            return text;
        }

        private static string AppendDetailAndHint(string text, ExecuteMessageDto message)
        {
            if (!string.IsNullOrEmpty(message.Detail)) text += NewLine + "DETAIL: " + message.Detail;
            if (!string.IsNullOrEmpty(message.Hint)) text += NewLine + "HINT: " + message.Hint;
            return text;
        }

        /// <summary>One structured message as its line.</summary>
        public static string Format(ExecuteMessageDto message)
        {
            if (message == null) return string.Empty;
            switch (message.Kind)
            {
                case ExecuteMessageKind.Error:
                    // Spec 042: a PostgreSQL error carries a SQLSTATE and is worded its own way.
                    if (!string.IsNullOrEmpty(message.SqlState)) return ForPostgres(message);
                    // An engine-raised error (a refused script) has no server number: its text says it all.
                    if (message.Number == 0 && message.Severity == 0) return message.Text ?? string.Empty;
                    return Error(message.Number, message.Severity, message.State, message.Line, message.Procedure, message.Text);
                case ExecuteMessageKind.RowCount:
                    return string.IsNullOrEmpty(message.Text) && message.RowCount >= 0 ? RowCount(message.RowCount) : message.Text;
                case ExecuteMessageKind.System:
                    return System(message.Text);
                default:
                    // Spec 042: a PostgreSQL notice may carry the server's DETAIL and HINT; SQL Server messages never do.
                    return AppendDetailAndHint(Info(message.Text), message);
            }
        }
    }
}
