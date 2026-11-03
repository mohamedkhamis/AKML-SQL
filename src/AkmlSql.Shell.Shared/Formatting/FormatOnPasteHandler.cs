using System;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Intended to intercept clipboard paste operations, detect SQL content via keyword heuristic,
    /// and auto-format the pasted text.
    /// <para>
    /// NOT WIRED (UI scan 2026-11-03, S3): format on paste never runs in SSMS 22. Nothing constructs
    /// this handler, no editor command filter intercepts <c>Edit.Paste</c>, and
    /// <see cref="TryFormatPastedText"/> has no caller — the engine round-trip built for it
    /// (<see cref="FormatRequestDispatcher"/>, <c>FormatTrigger.Paste</c>) is not connected either.
    /// The rule today is therefore: pasted text is always left exactly as pasted, whatever its size,
    /// its batches (<c>GO</c>) or whether it parses, and <c>formatter.formatOnPaste</c> in config.json
    /// is kept but has no effect. Options hides the row for that reason (spec 040 OPT-01,
    /// <c>FormattingPage</c>). Wiring it means: intercept VSStd97 Paste in the editor command filter,
    /// resolve the pasted span from the buffer version, dispatch through FormatRequestDispatcher and
    /// replace the span only if the buffer is unchanged when the reply arrives.
    /// </para>
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

                // Not wired (see the class summary): no caller reaches this point in SSMS 22 and the
                // engine dispatch is not connected, so the text is always returned unchanged.
                Log.Debug("Format on paste: SQL detected ({Length} chars) but the trigger is not wired; text left as pasted", pastedText.Length);
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
