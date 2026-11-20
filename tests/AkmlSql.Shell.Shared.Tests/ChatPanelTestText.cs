#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 046 (US2): shared readers for the chat panel's text. An assistant answer's prose is a
    /// read-only <see cref="RichTextBox"/> since spec 046 (user messages stay a read-only
    /// <see cref="TextBox"/>), so tests read message text through <see cref="TextBoxBase"/> here
    /// instead of each test class assuming a <see cref="TextBox"/>.
    /// </summary>
    internal static class ChatPanelTestText
    {
        /// <summary>The text a text host shows, without the trailing paragraph break a FlowDocument adds.</summary>
        internal static string Of(TextBoxBase host) => host switch
        {
            TextBox box => box.Text ?? string.Empty,
            RichTextBox rich => new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.TrimEnd('\r', '\n'),
            _ => string.Empty,
        };

        /// <summary>Every message text host (user messages and answer prose), in order.</summary>
        internal static List<TextBoxBase> MessageHosts(DependencyObject root)
            => FindAll<TextBoxBase>(root)
                .Where(h => AutomationProperties.GetName(h) == ChatMessageRenderer.MessageTextAutomationName)
                .ToList();

        /// <summary>The text of every message host, in order.</summary>
        internal static List<string> MessageTexts(DependencyObject root)
            => MessageHosts(root).Select(Of).ToList();

        /// <summary>All user-visible text: TextBlocks plus every read-only text host.</summary>
        internal static List<string> VisibleTexts(DependencyObject root)
        {
            var texts = FindAll<TextBlock>(root).Select(t => t.Text ?? string.Empty).ToList();
            texts.AddRange(FindAll<TextBoxBase>(root).Where(h => h.IsReadOnly).Select(Of));
            return texts;
        }

        internal static void InvokeAddAssistantMessage(AiChatPanel panel, string text,
            List<CodeActionDto>? actions = null, string? agentName = null, string? selectedAgentName = null)
        {
            var method = typeof(AiChatPanel).GetMethod("AddAssistantMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(panel, new object?[] { text, actions, agentName, selectedAgentName });
        }

        internal static void InvokeAddUserMessage(AiChatPanel panel, string text)
        {
            var method = typeof(AiChatPanel).GetMethod("AddUserMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(panel, new object?[] { text });
        }

        internal static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        internal static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, found);
            return found;
        }

        internal static List<T> FindByName<T>(DependencyObject root, string automationName) where T : DependencyObject
            => FindAll<T>(root).Where(e => AutomationProperties.GetName(e) == automationName).ToList();

        private static void Walk<T>(DependencyObject node, List<T> found) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    found.Add(match);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
