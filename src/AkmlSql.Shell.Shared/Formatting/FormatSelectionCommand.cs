using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    internal static class FormatSelectionCommand
    {
        private static readonly Guid CommandSet = PackageGuids.AkmlSqlCmdSet;
        private const int CommandId = CommandIds.CmdFormatSelection;

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(CommandSet, CommandId);
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

                // Get selection
                textView.GetSelection(out var startLine, out var startCol, out var endLine, out var endCol);

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                // Get position offsets
                buffer.GetPositionOfLineIndex(startLine, startCol, out var startOffset);
                buffer.GetPositionOfLineIndex(endLine, endCol, out var endOffset);

                // If no selection, do nothing
                if (startOffset == endOffset) return;

                // Get full document for context
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var fullText);
                if (string.IsNullOrEmpty(fullText)) return;

                // Format selection
                var formatter = new AkmlSql.Formatting.Selection.SelectionFormatter();
                var profile = new AkmlSql.Formatting.Profiles.FormattingProfile();
                var result = formatter.FormatSelection(fullText, startOffset, endOffset, profile);

                if (result.Success && result.WasModified)
                {
                    var replaceSpan = new TextSpan();
                    buffer.GetLineIndexOfPosition(result.OriginalStart, out replaceSpan.iStartLine, out replaceSpan.iStartIndex);
                    buffer.GetLineIndexOfPosition(result.OriginalEnd, out replaceSpan.iEndLine, out replaceSpan.iEndIndex);

                    textView.ReplaceTextOnLine(
                        replaceSpan.iStartLine,
                        replaceSpan.iStartIndex,
                        result.OriginalEnd - result.OriginalStart,
                        result.FormattedText,
                        result.FormattedText.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Format selection command failed");
            }
        }
    }
}
