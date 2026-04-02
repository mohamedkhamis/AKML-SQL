using System;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Intercepts clipboard paste operations, detects SQL content via keyword heuristic,
    /// and optionally auto-formats the pasted text.
    /// </summary>
    internal class FormatOnPasteHandler
    {
        private bool _enabled;

        /// <summary>
        /// Enables or disables format-on-paste.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        /// <summary>
        /// Gets whether format-on-paste is currently enabled.
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Attempts to format pasted text if it looks like SQL.
        /// </summary>
        /// <param name="pastedText">The raw pasted text from the clipboard.</param>
        /// <param name="formattedText">The formatted output if formatting succeeded.</param>
        /// <returns>True if the text was formatted; false if it was left unchanged.</returns>
        public bool TryFormatPastedText(string pastedText, out string formattedText)
        {
            formattedText = pastedText;

            if (!_enabled || string.IsNullOrWhiteSpace(pastedText))
                return false;

            try
            {
                // Heuristic: check first 200 chars for SQL keywords
                var sample = pastedText.Length > 200
                    ? pastedText.Substring(0, 200)
                    : pastedText;

                if (!LooksLikeSql(sample))
                    return false;

                // TODO: Wire to formatter pipeline via engine IPC when available.
                // For now this is a stub — actual formatting will be routed through
                // PipeRpcClient to the out-of-process engine.
                Log.Debug("Format on paste: SQL detected ({Length} chars), formatting stub invoked", pastedText.Length);

                // Stub: return unmodified until engine IPC wiring is complete
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Format on paste failed");
            }

            return false;
        }

        /// <summary>
        /// Simple heuristic to determine if text looks like SQL.
        /// Returns true if at least one common SQL keyword is found.
        /// </summary>
        private static bool LooksLikeSql(string text)
        {
            var upper = text.ToUpperInvariant();
            var keywords = new[]
            {
                "SELECT", "INSERT", "UPDATE", "DELETE",
                "CREATE", "ALTER", "DROP", "EXEC", "WITH"
            };

            int matches = 0;
            foreach (var kw in keywords)
            {
                if (upper.Contains(kw))
                    matches++;
            }

            return matches >= 1;
        }
    }
}
