#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US1, FR-015) — the chat composer's placeholder overlay must never invite a
    /// question the panel cannot answer.
    ///
    /// <para>The placeholder is a sibling TextBlock floating over the input TextBox (the
    /// SnippetManagerDialog idiom), so it is NOT affected by the TextBox's IsEnabled. Its
    /// visibility was driven only by <c>TextChanged</c>, which meant that when
    /// <c>RenderAgentState</c> disabled the input because no agent can answer, the disabled
    /// composer still read "Ask a question about this database…" at full contrast and clicking it
    /// did nothing — directly contradicting the intent stated in
    /// <see cref="AiChatEmptyState"/>'s own doc comment.</para>
    ///
    /// <para>Driven through the real no-agent configuration rather than by poking IsEnabled, so
    /// the test covers the path the product actually takes. The "AkmlSql ThemeRegistry" collection
    /// serialises this class against the other panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatComposerStateTests
    {
        [StaFact]
        public void Placeholder_shows_only_while_the_composer_is_enabled_and_empty()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var input = FindInput(panel);
                var placeholder = FindPlaceholder(panel);

                // Enabled + empty: the invitation is the whole point of the overlay.
                Assert.True(input.IsEnabled);
                Assert.Equal(Visibility.Visible, placeholder.Visibility);

                // Enabled + typed: the overlay must not sit under the user's own text.
                input.Text = "select 1";
                Assert.Equal(Visibility.Collapsed, placeholder.Visibility);

                input.Text = string.Empty;
                Assert.Equal(Visibility.Visible, placeholder.Visibility);
            });
        }

        [StaFact]
        public void Disabled_composer_hides_the_placeholder_it_cannot_honour()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var input = FindInput(panel);
                var placeholder = FindPlaceholder(panel);
                Assert.Equal(Visibility.Visible, placeholder.Visibility);

                // The real path: the only agent is turned off elsewhere, so nothing can answer and
                // RenderAgentState disables the composer. The box is still EMPTY, so a
                // TextChanged-only rule leaves the placeholder visible — the defect.
                agent.Enabled = false;
                panel.RefreshConfiguration(forceRefresh: true);

                Assert.False(input.IsEnabled);
                Assert.Equal(Visibility.Collapsed, placeholder.Visibility);

                // ...and it comes back with the agent, without needing a keystroke to repaint.
                agent.Enabled = true;
                panel.RefreshConfiguration(forceRefresh: true);

                Assert.True(input.IsEnabled);
                Assert.Equal(Visibility.Visible, placeholder.Visibility);
            });
        }

        // ── Spec 046 (US3, FR-010 – FR-013): keys and hints ───────────────────

        [StaFact]
        public void Send_says_why_it_is_disabled_and_names_its_shortcut_when_it_is_not()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var send = FindSendButton(panel);
                var input = FindInput(panel);
                Assert.True(ToolTipService.GetShowOnDisabled(send), "a disabled Send must still explain itself");

                Assert.False(send.IsEnabled);
                Assert.Equal("Type a question first", send.ToolTip);

                input.Text = "which tables are largest?";
                Assert.True(send.IsEnabled);
                Assert.Equal("Send (Ctrl+Enter)", send.ToolTip);

                input.Text = "   ";
                Assert.False(send.IsEnabled);
                Assert.Equal("Type a question first", send.ToolTip);

                input.Text = "again";
                SetSending(panel, true);
                Assert.False(send.IsEnabled);
                Assert.Equal("Wait for the answer to finish", send.ToolTip);
                SetSending(panel, false);

                agent.Enabled = false;
                panel.RefreshConfiguration(forceRefresh: true);
                Assert.False(send.IsEnabled);
                Assert.Equal("No AI agent can answer yet — add one in Options", send.ToolTip);
            });
        }

        [StaFact]
        public void The_placeholder_names_both_send_keys_and_the_new_line_key()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                Assert.Equal(
                    "Ask about this database…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)",
                    FindPlaceholder(panel).Text);
            });
        }

        [StaFact]
        public void Every_header_control_explains_itself_on_hover()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                var picker = Assert.Single(LogicalTree.Descendants<ComboBox>(panel),
                    c => AutomationProperties.GetName(c) == AiAgentPicker.ComboAutomationName);
                Assert.Equal("Choose which agent answers your messages", picker.ToolTip);
                var copy = Assert.Single(LogicalTree.Descendants<Button>(panel),
                    b => AutomationProperties.GetName(b) == "Copy conversation");
                Assert.Equal("Copy the whole conversation to the clipboard", copy.ToolTip);
            });
        }

        private static void SetSending(AiChatPanel panel, bool sending)
        {
            var field = typeof(AiChatPanel).GetField("_isSending",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(panel, sending);
            panel.UpdateSendState();
        }

        // ── Spec 046 (US5, FR-040 – FR-043): New chat ─────────────────────────

        [StaFact]
        public void New_chat_is_disabled_while_empty_and_while_sending_and_says_why()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var newChat = FindNewChat(panel);
                Assert.True(ToolTipService.GetShowOnDisabled(newChat));

                Assert.False(newChat.IsEnabled);
                Assert.Equal("The conversation is already empty", newChat.ToolTip);

                ChatPanelTestText.InvokeAddUserMessage(panel, "q");
                Assert.True(newChat.IsEnabled);
                Assert.Equal("Start a new conversation — clears this one", newChat.ToolTip);

                SetSending(panel, true);
                Assert.False(newChat.IsEnabled);
                Assert.Equal("Wait for the answer to finish", newChat.ToolTip);
                SetSending(panel, false);
                Assert.True(newChat.IsEnabled);
            });
        }

        [StaFact]
        public void New_chat_clears_the_turns_keeps_binding_and_agent_and_focuses_the_composer()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;
            var priorResolver = AiChatPanel.TestBindingResolver;
            AiChatPanel.TestBindingResolver = _ => ChatBinding.ForBound("s1", @"C:\Temp\SQLQuery1.sql", "srv01", "Sales");
            try
            {
                WithSettings(settings, () =>
                {
                    var panel = new AiChatPanel();
                    ChatPanelTestText.InvokeAddUserMessage(panel, "question one");
                    ChatPanelTestText.InvokeAddAssistantMessage(panel, "answer one");
                    SeedHistory(panel, ("user", "question one"), ("assistant", "answer one"));
                    FindInput(panel).Text = "half-typed";
                    var pickerBefore = Picker(panel).SelectedItem;

                    ChatPanelTestText.Click(FindNewChat(panel));

                    Assert.Empty(ChatPanelTestText.MessageHosts(panel));
                    Assert.Equal(0, panel.HistoryCount);   // no earlier turn goes with the next message (FR-041)
                    var welcome = Assert.Single(ChatPanelTestText.FindAll<AiChatWelcomeCard>(panel));
                    Assert.Equal("Ask about srv01.Sales", welcome.TitleText);
                    Assert.Equal(pickerBefore, Picker(panel).SelectedItem);
                    Assert.Equal(string.Empty, FindInput(panel).Text);
                    Assert.True(panel.ComposerFocusRequested);
                    Assert.False(FindNewChat(panel).IsEnabled);
                });
            }
            finally
            {
                AiChatPanel.TestBindingResolver = priorResolver;
            }
        }

        private static Button FindNewChat(DependencyObject root)
            => Assert.Single(LogicalTree.Descendants<Button>(root), b => AutomationProperties.GetName(b) == "New chat");

        private static ComboBox Picker(DependencyObject root)
            => Assert.Single(LogicalTree.Descendants<ComboBox>(root),
                c => AutomationProperties.GetName(c) == AiAgentPicker.ComboAutomationName);

        private static void SeedHistory(AiChatPanel panel, params (string Role, string Content)[] turns)
        {
            var field = typeof(AiChatPanel).GetField("_history",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            var history = (List<AkmlSql.Core.Ipc.Messages.ChatTurnDto>)field!.GetValue(panel)!;
            foreach (var (role, content) in turns)
                history.Add(new AkmlSql.Core.Ipc.Messages.ChatTurnDto { Role = role, Content = content });
        }

        // ── Spec 046 (US2): working state and follow-scroll ───────────────────

        [StaFact]
        public void The_working_state_names_the_agent_being_asked()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                FindInput(panel).Text = "which tables are largest?";
                FindSendButton(panel).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                // FR-014: the line shown while the answer is in flight names who is asked.
                var working = Assert.Single(LogicalTree.Descendants<TextBlock>(panel),
                    t => AutomationProperties.GetName(t) == AiChatPanel.WorkingStateAutomationName);
                Assert.Equal("Asking Claude (work) · Anthropic · claude-sonnet-4-6…", working.Text);
            });
        }

        [Theory]
        [InlineData(500, 500, true)]    // at the bottom
        [InlineData(493, 500, true)]    // within 8 DIU
        [InlineData(491, 500, false)]   // scrolled up — leave the reader where they are
        [InlineData(0, 0, true)]        // nothing to scroll
        public void The_view_follows_only_a_reader_at_the_bottom(double offset, double scrollable, bool follows)
            => Assert.Equal(follows, AiChatPanel.ShouldFollow(offset, scrollable));

        private static Button FindSendButton(DependencyObject root)
        {
            foreach (var button in LogicalTree.Descendants<Button>(root))
            {
                if ("Send".Equals(button.Content as string, StringComparison.Ordinal))
                    return button;
            }
            throw new InvalidOperationException("Send button not found");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void WithSettings(AppSettings settings, Action body)
        {
            var prior = AiChatPanel.TestSettingsProvider;
            AiChatPanel.TestSettingsProvider = () => settings;
            try { body(); }
            finally { AiChatPanel.TestSettingsProvider = prior; }
        }

        private static AiAgent UsableAgent(string name)
            => new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "sk-test",
                Enabled = true,
            };

        /// <summary>
        /// The logical tree, like the other panel tests: the panel is never rendered here, so the
        /// visual tree does not exist yet.
        /// </summary>
        private static TextBox FindInput(DependencyObject root)
        {
            foreach (var box in LogicalTree.Descendants<TextBox>(root))
            {
                if (AutomationProperties.GetName(box) == "Chat input")
                    return box;
            }
            throw new InvalidOperationException("Chat input TextBox not found");
        }

        /// <summary>
        /// The placeholder is the TextBlock sharing the input's Grid — identified by that
        /// parentage rather than by its wording, so rephrasing the hint never breaks this gate.
        /// </summary>
        private static TextBlock FindPlaceholder(DependencyObject root)
        {
            var input = FindInput(root);
            if (LogicalTreeHelper.GetParent(input) is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBlock block) return block;
                }
            }
            throw new InvalidOperationException("Placeholder TextBlock not found beside the chat input");
        }
    }
}
