using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Ipc.Messages;
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
    /// <para>Spec 036 (US3) extended coverage: bubbles now host a read-only selectable
    /// <see cref="TextBox"/> (FR-016/FR-017), multiple SQL blocks get individually labelled copy
    /// actions (FR-015), the header carries a copy-conversation action (FR-018), clipboard
    /// failure is surfaced and re-copyable (FR-019), and every copy control has an accessible
    /// name and is a tab stop (FR-020).</para>
    ///
    /// <para>Panel construction note: <see cref="AiChatPanel"/> merges the process-global
    /// ThemeRegistry dictionary. Tests that need a live panel live in this class (and the
    /// "AkmlSql ThemeRegistry" collection serialises them against the other panel classes).</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatPanelCopyButtonTests
    {
        [StaFact]
        public void Every_bubble_gets_a_copy_button_that_copies_the_whole_message()
        {
            var panel = new AiChatPanel();

            // The constructor's welcome message is the one guaranteed bubble.
            var button = Assert.Single(FindCopyButtons(panel, "Copy message"));

            WithClipboardRetry(() => { Clipboard.SetText("sentinel-before-copy"); return string.Empty; });
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Contains("Copied", button.Content?.ToString());
            Assert.Contains("AI SQL assistant", WithClipboardRetry(Clipboard.GetText));
        }

        /// <summary>FR-016/FR-017: the text-host swap (TextBlock → read-only TextBox) preserves the
        /// per-message copy affordance AND makes message text selectable.</summary>
        [StaFact]
        public void Message_text_is_a_selectable_read_only_text_host()
        {
            var panel = new AiChatPanel();

            var host = Assert.Single(FindAll<TextBox>(panel, "Message text"));
            Assert.True(host.IsReadOnly);
            Assert.Equal(0.0, host.BorderThickness.Left);
            Assert.True(host.Focusable); // keyboard selection + Ctrl+C need focus

            // The per-message copy button still copies the full text (FR-016).
            var copyButton = Assert.Single(FindCopyButtons(panel, "Copy message"));
            WithClipboardRetry(() => { Clipboard.SetText("sentinel"); return string.Empty; });
            copyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(host.Text, WithClipboardRetry(Clipboard.GetText));
        }

        /// <summary>FR-015: a message with two SQL blocks gets one labelled action per block, and
        /// each action copies only its own SQL (no prose, no fences).</summary>
        [StaFact]
        public void Each_sql_block_gets_its_own_labelled_copy_action()
        {
            var panel = new AiChatPanel();
            var actions = new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
                new() { Label = "Copy Script 2", ActionType = "copyToClipboard", Code = "SELECT 2;" },
            };
            InvokeAddAssistantMessage(panel, "Here are two scripts.", actions);

            var blockButtons = FindAll<Button>(panel, "Copy SQL block 1 of 2");
            var first = Assert.Single(blockButtons);
            var second = Assert.Single(FindAll<Button>(panel, "Copy SQL block 2 of 2"));

            Assert.Equal("Copy SQL block 1 of 2", first.Content);
            Assert.Equal("Copy SQL block 2 of 2", second.Content);

            WithClipboardRetry(() => { Clipboard.SetText("sentinel"); return string.Empty; });
            second.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("SELECT 2;", WithClipboardRetry(Clipboard.GetText));
        }

        /// <summary>FR-018: the conversation copy attributes every turn to its speaker, in order.</summary>
        [StaFact]
        public void Copy_conversation_attributes_and_orders_every_turn()
        {
            var panel = new AiChatPanel();
            SeedHistory(panel,
                ("user", "what tables do I have?"),
                ("assistant", "You have Customers and Orders."),
                ("user", "thanks"));

            var button = Assert.Single(FindCopyButtons(panel, "Copy conversation"));
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            var text = WithClipboardRetry(Clipboard.GetText).Replace("\r\n", "\n");
            Assert.Contains("You:\nwhat tables do I have?", text);
            Assert.Contains("Assistant:\nYou have Customers and Orders.", text);
            Assert.True(text.IndexOf("what tables", StringComparison.Ordinal)
                        < text.IndexOf("Customers and Orders", StringComparison.Ordinal),
                "turn order must be preserved");
            Assert.True(text.IndexOf("Customers and Orders", StringComparison.Ordinal)
                        < text.IndexOf("thanks", StringComparison.Ordinal),
                "turn order must be preserved");
            Assert.Contains("Copied", button.Content?.ToString());
        }

        /// <summary>FR-019: a clipboard failure is surfaced on the button and the message stays
        /// re-copyable — the bubble is never removed. Uses a real clipboard lock (OpenClipboard)
        /// so the failure path is genuinely exercised.</summary>
        [StaFact]
        public void Clipboard_failure_is_surfaced_and_the_message_stays_recopyable()
        {
            var panel = new AiChatPanel();
            var button = Assert.Single(FindCopyButtons(panel, "Copy message"));
            var bubblesBefore = CountBubbles(panel);

            Assert.True(OpenClipboard(IntPtr.Zero), "test could not open the clipboard");
            try
            {
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            finally
            {
                CloseClipboard();
            }

            Assert.Contains("Copy failed", button.Content?.ToString());
            Assert.Equal(bubblesBefore, CountBubbles(panel)); // bubble intact

            // Once the clipboard is free again the same button copies successfully.
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Contains("Copied", button.Content?.ToString());
            Assert.Contains("AI SQL assistant", WithClipboardRetry(Clipboard.GetText));
        }

        /// <summary>FR-020: every copy control carries an AutomationProperties.Name and is a
        /// keyboard-reachable tab stop.</summary>
        [StaFact]
        public void Every_copy_control_has_an_accessible_name_and_is_keyboard_reachable()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "two blocks", new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
                new() { Label = "Copy Script 2", ActionType = "copyToClipboard", Code = "SELECT 2;" },
            });

            var copyControls = new List<Button>();
            Walk<Button>(panel, b =>
            {
                var name = AutomationProperties.GetName(b);
                if (name == "Copy message" || name == "Copy conversation"
                    || (name?.StartsWith("Copy SQL block", StringComparison.Ordinal) ?? false))
                {
                    copyControls.Add(b);
                }
            });

            // 2 message-copy buttons (welcome bubble + the added bubble) + 2 SQL block buttons
            // + 1 conversation button.
            Assert.Equal(5, copyControls.Count);
            foreach (var control in copyControls)
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                Assert.True(control.IsTabStop,
                    $"'{AutomationProperties.GetName(control)}' must be keyboard-reachable");
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────

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

        private static void InvokeAddAssistantMessage(AiChatPanel panel, string text, List<CodeActionDto> actions)
        {
            var method = typeof(AiChatPanel).GetMethod("AddAssistantMessage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(panel, new object[] { text, actions });
        }

        private static void SeedHistory(AiChatPanel panel, params (string Role, string Content)[] turns)
        {
            var field = typeof(AiChatPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var history = (List<ChatTurnDto>)field!.GetValue(panel)!;
            foreach (var (role, content) in turns)
            {
                history.Add(new ChatTurnDto { Role = role, Content = content });
            }
        }

        /// <summary>Bubbles are counted by their read-only message text hosts.</summary>
        private static int CountBubbles(DependencyObject root)
        {
            var count = 0;
            Walk(root, (TextBox tb) =>
            {
                if (AutomationProperties.GetName(tb) == "Message text")
                    count++;
            });
            return count;
        }

        private static List<Button> FindCopyButtons(DependencyObject root, string automationName)
            => FindAll<Button>(root, automationName);

        private static List<T> FindAll<T>(DependencyObject root, string automationName) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, (T match) =>
            {
                if (AutomationProperties.GetName(match) == automationName)
                    found.Add(match);
            });
            return found;
        }

        private static void Walk<T>(DependencyObject node, Action<T> visit) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    visit(match);
                if (child is DependencyObject d)
                    Walk(d, visit);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();
    }
}
