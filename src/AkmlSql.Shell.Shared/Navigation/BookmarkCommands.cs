#nullable enable
using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Navigation
{
    /// <summary>
    /// VS commands for bookmark toggle (Ctrl+B,T), next bookmark (Ctrl+B,N),
    /// and previous bookmark (Ctrl+B,P).
    /// Uses <see cref="BookmarkManager"/> for state and <see cref="BookmarkTagger"/>
    /// to refresh glyph indicators.
    /// </summary>
    internal sealed class BookmarkCommands
    {
        public static BookmarkCommands? ToggleInstance { get; private set; }
        public static BookmarkCommands? NextInstance { get; private set; }
        public static BookmarkCommands? PrevInstance { get; private set; }

        private readonly BookmarkAction _action;

        private enum BookmarkAction { Toggle, Next, Previous }

        private BookmarkCommands(Package package, OleMenuCommandService commandService,
            int commandId, BookmarkAction action)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            _action = action;

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, commandId);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Registers all three bookmark commands: Toggle, Next, Previous.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            ToggleInstance = new BookmarkCommands(
                package, commandService, CommandIds.CmdBookmarkToggle, BookmarkAction.Toggle);
            NextInstance = new BookmarkCommands(
                package, commandService, CommandIds.CmdBookmarkNext, BookmarkAction.Next);
            PrevInstance = new BookmarkCommands(
                package, commandService, CommandIds.CmdBookmarkPrev, BookmarkAction.Previous);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is OleMenuCommand cmd)
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) { cmd.Enabled = false; return; }
                textManager.GetActiveView(1, null, out var textView);
                cmd.Enabled = textView != null;
            }
        }

        private void Execute(object sender, EventArgs e)
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

                textView.GetCaretPos(out var caretLine, out _);

                // Get the text view ID from the buffer properties (shared with BookmarkTagger)
                var textViewId = GetTextViewId(buffer);

                switch (_action)
                {
                    case BookmarkAction.Toggle:
                        BookmarkManager.Toggle(textViewId, caretLine);
                        RefreshTagger(buffer);
                        Log.Debug("BookmarkCommands: toggled bookmark at line {Line} in {ViewId}",
                            caretLine, textViewId);
                        break;

                    case BookmarkAction.Next:
                        var nextLine = BookmarkManager.NextBookmark(textViewId, caretLine);
                        if (nextLine >= 0)
                        {
                            textView.SetCaretPos(nextLine, 0);
                            textView.CenterLines(nextLine, 1);
                        }
                        break;

                    case BookmarkAction.Previous:
                        var prevLine = BookmarkManager.PreviousBookmark(textViewId, caretLine);
                        if (prevLine >= 0)
                        {
                            textView.SetCaretPos(prevLine, 0);
                            textView.CenterLines(prevLine, 1);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BookmarkCommands: command failed ({Action})", _action);
            }
        }

        /// <summary>
        /// Gets or creates the text view ID stored in the buffer's property bag.
        /// This ID is shared between <see cref="BookmarkTagger"/> and this command.
        /// </summary>
        private static string GetTextViewId(IVsTextBuffer buffer)
        {
            // Access the managed ITextBuffer through the Properties service
            // The text view ID is stored as a property by BookmarkTagger
            if (buffer is Microsoft.VisualStudio.Text.ITextBuffer managedBuffer)
            {
                return managedBuffer.Properties.GetOrCreateSingletonProperty(
                    "AkmlSqlTextViewId", () => Guid.NewGuid().ToString("N"));
            }

            // Fallback: use the buffer's hash code as an identifier
            return "buffer-" + buffer.GetHashCode().ToString("X8");
        }

        /// <summary>
        /// Finds the <see cref="BookmarkTagger"/> associated with the buffer and
        /// forces a refresh so glyph indicators update immediately.
        /// </summary>
        private static void RefreshTagger(IVsTextBuffer buffer)
        {
            if (buffer is Microsoft.VisualStudio.Text.ITextBuffer managedBuffer)
            {
                if (managedBuffer.Properties.TryGetProperty(typeof(BookmarkTagger), out object taggerObj))
                {
                    if (taggerObj is BookmarkTagger tagger)
                    {
                        tagger.Refresh();
                    }
                }
            }
        }
    }
}
