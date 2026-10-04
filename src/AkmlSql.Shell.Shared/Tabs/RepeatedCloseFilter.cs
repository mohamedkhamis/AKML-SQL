#nullable enable
using System;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// SSMS raises <c>DocumentClosing</c> more than once for a single tab close: once when the close
    /// starts (before the "save changes?" prompt) and twice more as the tab goes away. Handlers that
    /// record something per close keep one of these and act on the first event only — otherwise a
    /// closed query gets extra "Not executed" History rows and Reopen Closed Tab has to be pressed
    /// three times.
    /// </summary>
    internal sealed class RepeatedCloseFilter
    {
        /// <summary>
        /// How long after a close the same document and text still count as that close reported
        /// again. Long enough for the save prompt to be answered.
        /// </summary>
        internal static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

        private readonly object _gate = new object();
        private string? _name;
        private string? _text;
        private DateTime _at;
        private bool _seen;

        /// <summary>
        /// True when this close names the document and text of the close just handled, within
        /// <see cref="Window"/> — SSMS reporting it again. Otherwise it becomes the close just handled.
        /// </summary>
        internal bool IsRepeat(string? documentName, string? text, DateTime nowUtc)
        {
            lock (_gate)
            {
                if (_seen && IsSameClose(_name, _text, _at, documentName, text, nowUtc)) return true;

                _seen = true;
                _name = documentName;
                _text = text;
                _at = nowUtc;
                return false;
            }
        }

        /// <summary>
        /// Whether a close of <paramref name="documentName"/> with <paramref name="text"/> at
        /// <paramref name="atUtc"/> repeats the earlier close described by the <c>previous*</c> values.
        /// </summary>
        internal static bool IsSameClose(string? previousName, string? previousText, DateTime previousAtUtc,
            string? documentName, string? text, DateTime atUtc) =>
            string.Equals(previousName, documentName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previousText, text, StringComparison.Ordinal)
            && atUtc >= previousAtUtc
            && atUtc - previousAtUtc < Window;
    }
}
