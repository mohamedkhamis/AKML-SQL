using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US1) T017/T018/T020 — the chat panel's onboarding card (FR-015 – FR-021).
    /// With no usable agent the panel shows the card instead of the greeting, disables the
    /// input and Send, and offers exactly one primary action; once a usable agent exists the
    /// greeting (naming the resolved chat agent) replaces the card with no restart. The
    /// reason wordings are the six FR-021 strings, verbatim.
    ///
    /// <para>Panel tests set <see cref="AiChatPanel.TestSettingsProvider"/> so the outcome never
    /// depends on the machine's real config.json; the provider is restored after each test.
    /// The "AkmlSql ThemeRegistry" collection serialises this class against the other
    /// panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatEmptyStateTests
    {
        // ── T017: card vs greeting, input gating, one primary action ─────────

        [StaFact]
        public void No_agents_shows_the_card_not_the_greeting_and_disables_sending()
        {
            WithSettings(new AppSettings(), () =>
            {
                var panel = new AiChatPanel();

                // FR-015: the card is shown and the greeting bubble is absent.
                var card = Assert.Single(FindAll<AiChatEmptyState>(panel));
                Assert.DoesNotContain(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text");
                Assert.Contains(FindAll<TextBlock>(card), t => t.Text == "No AI agent is set up.");

                // FR-016: exactly one primary action, carrying an accessible name.
                Assert.Single(FindAll<Button>(card));
                Assert.Single(FindAll<Button>(panel),
                    b => AutomationProperties.GetName(b) == "Add AI agent");

                // FR-018: input and Send are disabled while no agent can answer.
                Assert.False(FindChatInput(panel).IsEnabled);
                Assert.False(FindSendButton(panel).IsEnabled);
            });
        }

        [StaFact]
        public void A_usable_agent_replaces_the_card_with_a_greeting_naming_it_and_enables_sending()
        {
            var settings = new AppSettings();
            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                Assert.Single(FindAll<AiChatEmptyState>(panel));

                // The user saves one agent in Options; the panel must pick it up with no restart
                // (FR-019 — the forced refresh is the path the card's button takes).
                var agent = UsableAgent("Claude (work)");
                settings.Ai.Agents.Add(agent);
                settings.Ai.ActiveAgentId = agent.Id;
                panel.RefreshConfiguration(forceRefresh: true);

                Assert.Empty(FindAll<AiChatEmptyState>(panel));
                var greeting = Assert.Single(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text");
                Assert.Contains("AI SQL assistant", greeting.Text);
                Assert.Contains("Claude (work)", greeting.Text);
                Assert.True(FindChatInput(panel).IsEnabled);
                Assert.True(FindSendButton(panel).IsEnabled);
            });
        }

        [StaFact]
        public void Cancelling_options_without_adding_an_agent_leaves_the_card_in_place()
        {
            WithSettings(new AppSettings(), () =>
            {
                var panel = new AiChatPanel();
                var card = Assert.Single(FindAll<AiChatEmptyState>(panel));

                // FR-020: the dialog returned with nothing saved — the forced refresh finds the
                // same state and the card (the very same instance) stays.
                panel.RefreshConfiguration(forceRefresh: true);

                Assert.Same(card, Assert.Single(FindAll<AiChatEmptyState>(panel)));
                Assert.False(FindChatInput(panel).IsEnabled);
            });
        }

        // ── T018: the six FR-021 reason wordings, verbatim ───────────────────

        [Fact]
        public void Reason_with_no_agents_at_all()
        {
            var (text, agentId) = AiChatEmptyState.DetectReason(new AppSettings().Ai);

            Assert.Equal("No AI agent is set up.", text);
            Assert.Equal(string.Empty, agentId);
        }

        [Fact]
        public void Reason_when_the_agent_needs_a_key_names_the_agent()
        {
            var agent = UnusableAgent("Claude (work)", provider: "anthropic",
                model: "claude-sonnet-4-6", apiKey: "", endpoint: "");

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(agent));

            Assert.Equal("Claude (work) needs an API key before it can answer.", text);
            Assert.Equal(agent.Id, agentId);
        }

        [Fact]
        public void Reason_when_the_agent_needs_a_model_names_the_agent()
        {
            var agent = UnusableAgent("Claude (work)", provider: "anthropic",
                model: "", apiKey: "sk-test", endpoint: "");

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(agent));

            Assert.Equal("Claude (work) has no model selected.", text);
            Assert.Equal(agent.Id, agentId);
        }

        [Fact]
        public void Reason_when_the_agent_needs_an_endpoint_names_the_agent()
        {
            var agent = UnusableAgent("Azure", provider: "azure",
                model: "gpt-4o", apiKey: "sk-test", endpoint: "");

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(agent));

            Assert.Equal("Azure needs an endpoint URL.", text);
            Assert.Equal(agent.Id, agentId);
        }

        [Fact]
        public void Reason_when_the_key_will_not_decrypt_names_the_agent()
        {
            // A syntactically valid dpapi: blob this user cannot decrypt (bad HMAC) — the same
            // construction AiProviderModelAutofillTests uses for the PR #251 contract.
            var agent = UnusableAgent("Claude (work)", provider: "anthropic",
                model: "claude-sonnet-4-6",
                apiKey: "dpapi:" + Convert.ToBase64String(new byte[64]), endpoint: "");

            // S1 sees a non-empty key and calls the agent usable; the shell-observable half of
            // V23 (the key will not unwrap on this machine) is what the card reports.
            Assert.True(AiAgentResolver.IsUsable(agent));
            Assert.False(AiChatEmptyState.CanAnswer(agent));

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(agent));

            Assert.Equal("Claude (work)'s stored API key could not be read on this machine — re-enter it.", text);
            Assert.Equal(agent.Id, agentId);
        }

        [Fact]
        public void Reason_when_every_agent_is_disabled()
        {
            var first = UnusableAgent("Claude (work)", provider: "anthropic",
                model: "claude-sonnet-4-6", apiKey: "", endpoint: "");
            first.Enabled = false;
            var second = UsableAgent("Kimi");
            second.Enabled = false;

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(first, second));

            Assert.Equal("Every AI agent is turned off.", text);
            Assert.Equal(first.Id, agentId);
        }

        [Fact]
        public void Several_unusable_agents_name_the_first_in_list_order()
        {
            var first = UnusableAgent("Claude (work)", provider: "anthropic",
                model: "claude-sonnet-4-6", apiKey: "", endpoint: "");
            var second = UnusableAgent("Kimi", provider: "kimi",
                model: "kimi-latest", apiKey: "", endpoint: "");

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(first, second));

            Assert.Equal("Claude (work) needs an API key before it can answer.", text);
            Assert.Equal(first.Id, agentId);
        }

        [Fact]
        public void Reason_when_the_agent_has_no_provider_uses_the_needs_model_wording_and_names_it()
        {
            // FR-021's closed set has no provider wording; an agent with no provider cannot
            // have a usable model, so the needs-model bucket applies. What it must NOT say is
            // "No AI agent is set up." — the card's button deep-links THIS existing agent.
            var agent = UnusableAgent("Claude (work)", provider: "",
                model: "claude-sonnet-4-6", apiKey: "", endpoint: "");

            var (text, agentId) = AiChatEmptyState.DetectReason(AiWith(agent));

            Assert.Equal("Claude (work) has no model selected.", text);
            Assert.Equal(agent.Id, agentId);
        }

        // ── T020: the refresh is a no-op when the signature is unchanged ──────

        [StaFact]
        public void Refresh_with_an_unchanged_signature_does_not_touch_the_visual_tree()
        {
            WithSettings(new AppSettings(), () =>
            {
                var panel = new AiChatPanel();
                var card = Assert.Single(FindAll<AiChatEmptyState>(panel));
                var buttonCount = FindAll<Button>(panel).Count;
                var textCount = FindAll<TextBlock>(panel).Count;

                // The 2-second tick calls this on every pass; with the same signature it must
                // return without rebuilding anything.
                panel.RefreshConfiguration();
                panel.RefreshConfiguration();

                Assert.Same(card, Assert.Single(FindAll<AiChatEmptyState>(panel)));
                Assert.Equal(buttonCount, FindAll<Button>(panel).Count);
                Assert.Equal(textCount, FindAll<TextBlock>(panel).Count);
            });
        }

        [StaFact]
        public void Refresh_with_an_unchanged_signature_keeps_the_same_greeting_bubble()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var greeting = Assert.Single(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text");

                panel.RefreshConfiguration();
                panel.RefreshConfiguration();

                Assert.Same(greeting, Assert.Single(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text"));
            });
        }

        [StaFact]
        public void Renaming_the_chat_agent_re_renders_the_greeting_and_picker_without_a_rebuild()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                var greeting = Assert.Single(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text");
                Assert.Contains("Claude (work)", greeting.Text);

                // The user renames the agent in Options and saves; the next refresh must pick
                // the new name up (FR-037) — the name is part of the signature now, so this is
                // a re-render of the SAME panel, not a rebuild.
                agent.Name = "Claude K2";
                panel.RefreshConfiguration(forceRefresh: true);

                var renamed = Assert.Single(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text");
                Assert.NotSame(greeting, renamed);
                Assert.Contains("Claude K2", renamed.Text);
                Assert.DoesNotContain("Claude (work)", renamed.Text);

                var picker = Assert.Single(FindAll<ComboBox>(panel),
                    c => AutomationProperties.GetName(c) == AiAgentPicker.ComboAutomationName);
                Assert.Equal("Claude K2", picker.SelectedItem);
            });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void WithSettings(AppSettings settings, Action body)
        {
            var prior = AiChatPanel.TestSettingsProvider;
            AiChatPanel.TestSettingsProvider = () => settings;
            try
            {
                body();
            }
            finally
            {
                AiChatPanel.TestSettingsProvider = prior;
            }
        }

        private static AiSettings AiWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents)
                settings.Ai.Agents.Add(agent);
            return settings.Ai;
        }

        private static AiAgent UsableAgent(string name)
            => UnusableAgent(name, "anthropic", "claude-sonnet-4-6", "sk-test", "");

        private static AiAgent UnusableAgent(string name, string provider, string model,
            string apiKey, string endpoint)
            => new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = apiKey,
                Endpoint = endpoint,
                Enabled = true,
            };

        private static TextBox FindChatInput(DependencyObject root)
        {
            foreach (var tb in FindAll<TextBox>(root))
            {
                if (AutomationProperties.GetName(tb) == "Chat input")
                    return tb;
            }
            throw new InvalidOperationException("Chat input TextBox not found");
        }

        private static Button FindSendButton(DependencyObject root)
        {
            foreach (var b in FindAll<Button>(root))
            {
                if ("Send".Equals(b.Content as string, StringComparison.Ordinal))
                    return b;
            }
            throw new InvalidOperationException("Send button not found");
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, found);
            return found;
        }

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
