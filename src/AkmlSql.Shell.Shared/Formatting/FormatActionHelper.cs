using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Shared helper for format action commands to apply formatted text to the editor buffer.
    /// </summary>
    internal static class FormatActionHelper
    {
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
