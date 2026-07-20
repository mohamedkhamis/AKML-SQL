using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Desktop AI Chat parity with the web edition: every message bubble carries a small copy
    /// button that puts the whole message on the clipboard and shows a transient "Copied"
    /// state. Before this, bubbles were plain TextBlocks — not selectable, no affordance — so
    /// a chat message could not be copied from SSMS at all (only code-action SQL could).
    ///
    /// <para>Single test on purpose: <see cref="AiChatPanel"/> attaches the process-global
    /// ThemeRegistry resource dictionary, which becomes owned by the first STA thread that
    /// touches it — and [StaFact] gives每 test its own STA thread, so a second construction
    /// in the same run throws a cross-thread InvalidOperationException. One thread, one panel.</para>
    /// </summary>
    public class AiChatPanelCopyButtonTests
    {
        [StaFact]
        public void Every_bubble_gets_a_copy_button_that_copies_the_whole_message()
        {
            var panel = new AiChatPanel();

            // The constructor's welcome message is the one guaranteed bubble.
            var button = Assert.Single(FindCopyButtons(panel));

            WithClipboardRetry(() => { Clipboard.SetText("sentinel-before-copy"); return string.Empty; });
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Contains("Copied", button.Content?.ToString());
            Assert.Contains("AI SQL assistant", WithClipboardRetry(Clipboard.GetText));
        }

        /// <summary>The Windows clipboard is shared machine state; transient
        /// CLIPBRD_E_CANT_OPEN contention is normal — retry briefly.</summary>
        private static string WithClipboardRetry(System.Func<string> op)
        {
            for (var i = 0; ; i++)
            {
                try { return op(); }
                catch (System.Runtime.InteropServices.COMException) when (i < 20)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        private static List<Button> FindCopyButtons(DependencyObject root)
        {
            var found = new List<Button>();
            Walk(root, found);
            return found;
        }

        private static void Walk(DependencyObject node, List<Button> found)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is Button b && AutomationProperties.GetName(b) == "Copy message")
                    found.Add(b);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
