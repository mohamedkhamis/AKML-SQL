using System;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Detects statement-terminating delimiters (semicolon <c>;</c> or <c>GO</c> batch separator)
    /// and auto-formats the completed statement.
    /// </summary>
    internal class FormatOnDelimiterHandler
    {
        private bool _enabled;

        /// <summary>
        /// Enables or disables format-on-delimiter.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        /// <summary>
        /// Gets whether format-on-delimiter is currently enabled.
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Checks whether the typed character (or typed word) constitutes a statement delimiter.
        /// </summary>
        /// <param name="typedChar">The character that was just typed.</param>
        /// <param name="lineText">The full text of the current line (after the keystroke).</param>
        /// <returns>True if a delimiter was detected.</returns>
        public bool IsDelimiter(char typedChar, string lineText)
        {
            // Semicolon terminates a statement
            if (typedChar == ';')
                return true;

            // Check if the line is a GO batch separator (case-insensitive, standalone)
            if (typedChar is 'o' or 'O' && IsGoBatchSeparator(lineText))
                return true;

            return false;
        }

        /// <summary>
        /// Attempts to format the completed statement ending at the delimiter position.
        /// </summary>
        /// <param name="fullText">The entire document text.</param>
        /// <param name="delimiterOffset">Character offset where the delimiter was typed.</param>
        /// <param name="formattedText">The formatted output if formatting was applied.</param>
        /// <returns>True if formatting was applied; false otherwise.</returns>
        public bool TryFormatCompletedStatement(string fullText, int delimiterOffset, out string formattedText)
        {
            formattedText = fullText;

            if (!_enabled || string.IsNullOrWhiteSpace(fullText))
                return false;

            try
            {
                // TODO: Wire to formatter pipeline via engine IPC when available.
                // The implementation needs to:
                // 1. Find the start of the current statement (previous ; or GO or start-of-file)
                // 2. Extract the statement text
                // 3. Format it via the engine
                // 4. Replace the statement region in the document
                Log.Debug(
                    "Format on delimiter: delimiter at offset {Offset}, formatting stub invoked",
                    delimiterOffset);

                // Stub: return unmodified until engine IPC wiring is complete
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Format on delimiter failed at offset {Offset}", delimiterOffset);
            }

            return false;
        }

        /// <summary>
        /// Determines if the line text is a standalone GO batch separator.
        /// </summary>
        private static bool IsGoBatchSeparator(string lineText)
        {
            if (string.IsNullOrWhiteSpace(lineText))
                return false;

            var trimmed = lineText.Trim();

            // GO optionally followed by a repeat count (e.g., "GO 5")
            if (string.Equals(trimmed, "GO", StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.Length > 2
                && trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(trimmed[2]))
            {
                var afterGo = trimmed.Substring(3).Trim();
                return int.TryParse(afterGo, out _);
            }

            return false;
        }
    }
}
