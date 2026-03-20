using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Adds or removes the AS keyword on alias definitions in the active SQL document.
    /// </summary>
    internal static class ToggleAsCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdToggleAs);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null) return;

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);
                if (string.IsNullOrEmpty(documentText)) return;

                var action = new AkmlSql.Formatting.Actions.ToggleAsKeywordAction();
                var profile = new AkmlSql.Formatting.Profiles.FormattingProfile();
                var result = action.Execute(documentText, profile);

                if (result.Success && result.WasModified)
                {
                    FormatActionHelper.ApplyFormattedText(buffer, result.FormattedText);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Toggle AS keyword command failed");
            }
        }
    }
}
