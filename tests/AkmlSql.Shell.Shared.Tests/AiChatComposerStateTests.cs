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
