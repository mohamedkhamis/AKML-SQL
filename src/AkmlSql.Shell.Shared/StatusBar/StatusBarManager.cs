using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.StatusBar
{
    internal static class StatusBarManager
    {
        public static void SetLoaded(IVsStatusbar statusBar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetText(statusBar, $"AKML SQL v{Core.Constants.Version}");
        }

        public static void SetFailed(IVsStatusbar statusBar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetText(statusBar, "AKML SQL [FAILED]");
        }

        private static void SetText(IVsStatusbar statusBar, string text)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                statusBar.IsFrozen(out int frozen);
                if (frozen != 0)
                    statusBar.FreezeOutput(0);
                statusBar.SetText(text);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update status bar");
            }
        }
    }
}
