#nullable enable
using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Shared helper for format action commands to apply formatted text to the editor buffer.
    /// </summary>
    internal static class FormatActionHelper
    {
        /// <summary>
        /// Spec 040 (OPT-01) — the status message shown when Options › Format "Enable SQL
        /// formatter" is off, or null when formatting is allowed.
        /// </summary>
        internal static string? FormatterDisabledMessage(Core.Config.FormatterSettings s) =>
            s.Enabled ? null : "AKML SQL formatting is off — turn it on in Options › Format.";

        /// <summary>
        /// Returns false (and says why in the status bar) when "Enable SQL formatter" is off. Every
        /// format command calls this first.
        /// </summary>
        internal static bool EnsureFormatterEnabled()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string? message;
            try { message = FormatterDisabledMessage(Core.Config.ConfigManager.Load().Formatter); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Format: could not read settings; formatting stays enabled");
                return true;
            }
            if (message == null) return true;

            try
            {
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                statusBar?.SetText(message);
            }
            catch (Exception ex) { Log.Debug(ex, "Format: status bar unavailable"); }
            Log.Information("Format: skipped because the formatter is turned off in Options");
            return false;
        }

        /// <summary>
        /// The formatting style every format request must be sent with — read FRESH from config on
        /// each call, never cached.
        /// <para>
        /// Format commands previously omitted <c>ProfileName</c> entirely, so the engine received
        /// null, fell back to <c>new FormattingProfile()</c>, and formatted with POCO defaults no
        /// matter which style was active — "I picked a style, closed the editor, and Format SQL
        /// didn't change". Reading fresh (rather than from an <c>AppSettings</c> snapshot captured at
        /// command construction) is what lets Set Active in the styles editor affect the very next
        /// format without restarting the IDE. Format is user-initiated, so a config read per invoke
        /// is not on any hot path.
        /// </para>
        /// Never returns null/empty: the engine reads those as "defaults by design" and suppresses
        /// its missing-style warning, which would quietly restore the original bug.
        /// </summary>
        public static string ResolveActiveProfileName()
        {
            try
            {
                var configured = Core.Config.ConfigManager.Load().Formatter?.ActiveProfile;
                if (!string.IsNullOrWhiteSpace(configured)) return configured!;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Format: could not read the active style from config; using the shipped default");
            }

            return new Core.Config.FormatterSettings().ActiveProfile;
        }

        public static void ApplyFormattedText(IVsTextLines buffer, string formattedText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);

                var ptr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(formattedText);
                try
                {
                    var hr = buffer.ReplaceLines(0, 0, lastLine, lastCol, ptr, formattedText.Length, null);
                    if (hr != VSConstants.S_OK)
                        Log.Warning("Failed to apply formatted text, HRESULT=0x{Hr:X8}", hr);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply formatted text to buffer");
            }
        }
    }
}
