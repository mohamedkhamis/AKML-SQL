#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US3) T050–T052 — the chat panel's agent picker (FR-037 – FR-044) and per-answer
    /// attribution (FR-042/FR-052); (US6) T095 — the once-stated notice when a refresh finds the
    /// chat assignment cleared by normalisation or by another host (FR-049). The picker lists the
    /// usable agents plus a trailing "Add agent…" entry, shows the RESOLVED chat agent (S3: the
    /// chat assignment when set, otherwise the active agent), writes <c>FeatureAgents.Chat</c> —
    /// never <c>ActiveAgentId</c> (research R7) — and persists through the shared save-and-notify
    /// path without clearing the conversation.
    ///
    /// <para>Panel tests set <see cref="AiChatPanel.TestSettingsProvider"/> so the outcome never
    /// depends on the machine's real config.json, and swap
    /// <see cref="AiChatPanel.ShowOptionsRoute"/> with a recording fake because the real route
    /// opens a modal dialog. Tests that let a selection reach <c>ConfigManager.Save</c> redirect
    /// <c>AKML_APP_DATA_ROOT</c> to a temp root for the duration of the test. The
    /// "AkmlSql ThemeRegistry" collection serialises this class against the other
    /// panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatAgentPickerTests
    {
        // ── T050: listing, resolved selection, one-agent case, Add route ─────

        [StaFact]
        public void Picker_lists_only_enabled_usable_agents_by_name_plus_the_add_entry()
        {
            var usable = UsableAgent("Claude (work)");
            var disabled = UsableAgent("Kimi");
            disabled.Enabled = false;
            var keyless = UsableAgent("NoKey");
            keyless.ApiKey = string.Empty;   // anthropic requires a key — S1-unusable, hidden

            var picker = new AiAgentPicker();
            picker.SetAgents(new[] { usable, disabled, keyless }, usable.Id);

            Assert.Equal(
                new[] { "Claude (work)", AiAgentPicker.AddAgentDisplayText },
                ComboItems(picker));
        }

        [StaFact]
        public void Picker_shows_the_resolved_chat_agent_assignment_then_active()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                // S3/FR-037: the assignment wins when set.
                Assert.Equal("Kimi", FindPickerCombo(panel).SelectedItem);

                // With no assignment the picker follows the active agent — a user who never
                // touches the picker still sees who answers.
                settings.Ai.FeatureAgents.Chat = string.Empty;
                panel.RefreshConfiguration(forceRefresh: true);
                Assert.Equal("Claude (work)", FindPickerCombo(panel).SelectedItem);
            });
        }

        [StaFact]
        public void Picker_is_shown_with_exactly_one_agent()
        {
            var settings = new AppSettings();
            var only = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(only);
            settings.Ai.ActiveAgentId = only.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                // FR-044: even with one agent the user must always know who is answering.
                var combo = FindPickerCombo(panel);
                Assert.Equal(new[] { "Claude (work)", AiAgentPicker.AddAgentDisplayText }, ComboItems(panel));
                Assert.Equal("Claude (work)", combo.SelectedItem);
            });
        }

        [StaFact]
        public void Picker_combo_is_sized_to_fit_and_carries_the_themed_menu_item_style()
        {
            // Built in the constructor — no load needed for size or the themed item style.
            var picker = new AiAgentPicker();
            var combo = FindPickerCombo(picker);

            // "chat menu header of select model is small" — the picker must be a comfortable size.
            Assert.True(combo.MinWidth >= 150, $"MinWidth {combo.MinWidth} is too small to fit");
            Assert.True(combo.MinHeight >= 24, $"MinHeight {combo.MinHeight} is too small to fit");
            // The light-mode menu redesign themes the item container (null under High Contrast
            // only, where the stock system-color rendering is the accessible one).
            Assert.NotNull(combo.ItemContainerStyle);
        }

        [StaFact]
        public void Add_agent_entry_routes_through_ShowOptions()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;

            string? seenPage = null;
            string? seenAgent = "unset";
            WithSeams(settings, (page, agentId) =>
            {
                seenPage = page;
                seenAgent = agentId;
                return false;   // the user cancels Options (FR-020 analogue: nothing changes)
            }, () =>
            {
                var panel = new AiChatPanel();
                var combo = FindPickerCombo(panel);

                combo.SelectedIndex = combo.Items.Count - 1;   // "Add agent…" is always last

                // FR-041: the same deep-link route the onboarding card uses, with no agent id.
                Assert.Equal("AI Assistance", seenPage);
                Assert.Null(seenAgent);
                // A cancel leaves the previous resolved selection in place.
                Assert.Equal("Claude (work)", combo.SelectedItem);
                Assert.Equal(string.Empty, settings.Ai.FeatureAgents.Chat);
            });
        }

        // ── T051: R7 — FeatureAgents.Chat, never ActiveAgentId ───────────────

        [StaFact]
        public void Selecting_an_agent_writes_feature_agents_chat_and_never_active_agent_id()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;

            using var redirect = new AppDataRedirect();
            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                FindPickerCombo(panel).SelectedItem = "Kimi";

                // R7 — the decision most likely to be implemented wrong.
                Assert.Equal(other.Id, settings.Ai.FeatureAgents.Chat);
                Assert.Equal(active.Id, settings.Ai.ActiveAgentId);

                // FR-040: the choice reached disk through the shared save-and-notify path.
                Assert.Equal(other.Id, ConfigManager.Load().Ai.FeatureAgents.Chat);
                Assert.Equal(active.Id, ConfigManager.Load().Ai.ActiveAgentId);
            });
        }

        [StaFact]
        public void Selecting_the_active_agent_clears_the_assignment_to_empty()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            using var redirect = new AppDataRedirect();
            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                Assert.Equal("Kimi", FindPickerCombo(panel).SelectedItem);

                // S3: picking the agent that is already active means "follow the active agent".
                FindPickerCombo(panel).SelectedItem = "Claude (work)";

                Assert.Equal(string.Empty, settings.Ai.FeatureAgents.Chat);
                Assert.Equal(active.Id, settings.Ai.ActiveAgentId);
            });
        }

        // ── T052: persistence, conversation, attribution ─────────────────────

        [StaFact]
        public void Selection_survives_a_panel_rebuild()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;   // as persisted by an earlier selection

            WithSettings(settings, () =>
            {
                var first = new AiChatPanel();
                Assert.Equal("Kimi", FindPickerCombo(first).SelectedItem);

                // FR-040: close/reopen — a fresh panel resolves the same selection.
                var reopened = new AiChatPanel();
                Assert.Equal("Kimi", FindPickerCombo(reopened).SelectedItem);
            });
        }

        [StaFact]
        public void Conversation_survives_an_agent_switch()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;

            using var redirect = new AppDataRedirect();
            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, "first answer", "Claude (work)");
                SeedHistory(panel, ("user", "question one"), ("assistant", "first answer"));
                var bubblesBefore = FindAll<TextBox>(panel)
                    .FindAll(tb => AutomationProperties.GetName(tb) == "Message text").Count;

                FindPickerCombo(panel).SelectedItem = "Kimi";

                // FR-039: the conversation is NOT cleared — same bubbles, same history.
                var bubblesAfter = FindAll<TextBox>(panel)
                    .FindAll(tb => AutomationProperties.GetName(tb) == "Message text").Count;
                Assert.Equal(bubblesBefore, bubblesAfter);
                Assert.Equal(2, HistoryOf(panel).Count);
                Assert.Contains(FindAll<TextBox>(panel),
                    tb => AutomationProperties.GetName(tb) == "Message text" && tb.Text == "first answer");
            });
        }

        [StaFact]
        public void Assistant_answers_carry_their_agent_name_in_order()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, "answer one", "Claude (work)");
                InvokeAddAssistantMessage(panel, "answer two", "Kimi");

                // FR-042: in a mixed conversation every answer says who produced it.
                var captions = FindAll<TextBlock>(panel)
                    .FindAll(tb => AutomationProperties.GetName(tb) == AiChatPanel.AttributionAutomationName);
                Assert.Equal(new[] { "Claude (work)", "Kimi" }, captions.ConvertAll(c => c.Text));
            });
        }

        [StaFact]
        public void Null_agent_name_renders_no_attribution()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, "answer from an older engine");

                // Older-engine tolerance: no attribution rather than a guessed one.
                Assert.Empty(FindAll<TextBlock>(panel)
                    .FindAll(tb => AutomationProperties.GetName(tb) == AiChatPanel.AttributionAutomationName));
            });
        }

        [Fact]
        public void Attribution_text_states_a_fallback_plainly()
        {
            Assert.Null(AiChatPanel.AttributionText(null, "Claude (work)"));
            Assert.Null(AiChatPanel.AttributionText("  ", "Claude (work)"));
            Assert.Equal("Kimi", AiChatPanel.AttributionText("Kimi", "Kimi"));
            Assert.Equal("Kimi", AiChatPanel.AttributionText("Kimi", null));
            Assert.Equal(
                "Kimi answered — Claude (work) was unavailable.",
                AiChatPanel.AttributionText("Kimi", "Claude (work)"));
        }

        [StaFact]
        public void A_fallback_answer_says_who_answered_and_who_was_unavailable()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, "answer", "Kimi", "Claude (work)");

                // FR-052: the fallback is stated, not hidden behind a swapped name.
                var caption = Assert.Single(FindAll<TextBlock>(panel)
                    .FindAll(tb => AutomationProperties.GetName(tb) == AiChatPanel.AttributionAutomationName));
                Assert.Equal("Kimi answered — Claude (work) was unavailable.", caption.Text);
            });
        }

        [StaFact]
        public void Agent_added_via_the_picker_becomes_the_selection()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;
            var added = UsableAgent("Kimi");

            using var redirect = new AppDataRedirect();
            WithSeams(settings, (page, agentId) =>
            {
                Assert.Equal("AI Assistance", page);
                Assert.Null(agentId);
                settings.Ai.Agents.Add(added);   // the user adds the agent in Options, clicks OK
                return true;
            }, () =>
            {
                var panel = new AiChatPanel();
                var combo = FindPickerCombo(panel);

                combo.SelectedIndex = combo.Items.Count - 1;   // "Add agent…"

                // FR-041: on save the new agent becomes the picker's selection (persisted,
                // as every picker selection is — via FeatureAgents.Chat, not ActiveAgentId).
                Assert.Equal(added.Id, settings.Ai.FeatureAgents.Chat);
                Assert.Equal(active.Id, settings.Ai.ActiveAgentId);
                Assert.Equal("Kimi", combo.SelectedItem);
            });
        }

        [StaFact]
        public void A_still_blank_agent_added_via_the_picker_does_not_become_the_selection()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            settings.Ai.Agents.Add(active);
            settings.Ai.ActiveAgentId = active.Id;
            var blank = new AiAgent   // added in Options but saved unconfigured
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Agent 1",
                Enabled = true,
            };

            WithSeams(settings, (page, agentId) =>
            {
                settings.Ai.Agents.Add(blank);
                return true;
            }, () =>
            {
                var panel = new AiChatPanel();
                var combo = FindPickerCombo(panel);

                combo.SelectedIndex = combo.Items.Count - 1;   // "Add agent…"

                // The new agent cannot answer — chat must NOT be pinned to it: the next
                // normalised load would clear the assignment and fire the "can no longer
                // answer" notice seconds after the add.
                Assert.Equal(string.Empty, settings.Ai.FeatureAgents.Chat);
                Assert.Equal(active.Id, settings.Ai.ActiveAgentId);
                // The picker re-syncs to the resolved agent and never lists the unusable one.
                Assert.Equal("Claude (work)", combo.SelectedItem);
                Assert.Equal(new[] { "Claude (work)", AiAgentPicker.AddAgentDisplayText }, ComboItems(panel));
                Assert.Empty(FindNotices(panel));
            });
        }

        // ── T095: FR-049 — a cleared assignment is stated once, in the panel ─

        [StaFact]
        public void Cleared_chat_assignment_is_stated_once_plainly_in_the_conversation()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();
                Assert.Empty(FindNotices(panel));

                // V16: the assigned agent was deleted elsewhere; the next read sees the
                // assignment cleared and the picker following the active agent.
                settings.Ai.Agents.Remove(other);
                settings.Ai.FeatureAgents.Chat = string.Empty;
                panel.RefreshConfiguration(forceRefresh: true);

                // FR-049: stated plainly, once — the refresh signature bounds it, no counter.
                var notice = Assert.Single(FindNotices(panel));
                Assert.Equal(
                    "The agent assigned to chat is no longer available — chat will use the active agent, Claude (work).",
                    notice.Text);

                panel.RefreshConfiguration(forceRefresh: true);
                Assert.Single(FindNotices(panel));

                // A configuration notice is not conversation content — the copy path is untouched.
                Assert.Empty(HistoryOf(panel));
            });
        }

        [StaFact]
        public void Cleared_assignment_names_the_agent_when_it_is_still_listed_but_unusable()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                // V16: the assigned agent was disabled elsewhere — still listed, no longer usable.
                other.Enabled = false;
                settings.Ai.FeatureAgents.Chat = string.Empty;
                panel.RefreshConfiguration(forceRefresh: true);

                var notice = Assert.Single(FindNotices(panel));
                Assert.Equal(
                    "Kimi can no longer answer — chat will use the active agent, Claude (work).",
                    notice.Text);
            });
        }

        [StaFact]
        public void Cleared_assignment_with_no_agent_left_says_so_without_naming_a_replacement()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                settings.Ai.Agents.Remove(other);
                active.Enabled = false;   // nothing usable left — the empty state card applies
                settings.Ai.FeatureAgents.Chat = string.Empty;
                panel.RefreshConfiguration(forceRefresh: true);

                var notice = Assert.Single(FindNotices(panel));
                Assert.Equal("The agent assigned to chat is no longer available.", notice.Text);
            });
        }

        [StaFact]
        public void Clearing_the_assignment_in_the_picker_adds_no_notice()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Claude (work)");
            var other = UsableAgent("Kimi");
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(other);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.FeatureAgents.Chat = other.Id;

            using var redirect = new AppDataRedirect();
            WithSettings(settings, () =>
            {
                var panel = new AiChatPanel();

                // The user picks the active agent, clearing the assignment themselves (S3/R7) —
                // a deliberate change, not a silent repair: FR-049 does not fire.
                FindPickerCombo(panel).SelectedItem = "Claude (work)";

                Assert.Equal(string.Empty, settings.Ai.FeatureAgents.Chat);
                Assert.Empty(FindNotices(panel));
            });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void WithSettings(AppSettings settings, Action body)
            => WithSeams(settings, null, body);

        private static void WithSeams(AppSettings settings,
            Func<string?, string?, bool>? showOptionsRoute, Action body)
        {
            var priorSettings = AiChatPanel.TestSettingsProvider;
            var priorRoute = AiChatPanel.ShowOptionsRoute;
            AiChatPanel.TestSettingsProvider = () => settings;
            if (showOptionsRoute != null)
                AiChatPanel.ShowOptionsRoute = showOptionsRoute;
            try
            {
                body();
            }
            finally
            {
                AiChatPanel.TestSettingsProvider = priorSettings;
                AiChatPanel.ShowOptionsRoute = priorRoute;
            }
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

        private static ComboBox FindPickerCombo(DependencyObject root)
        {
            foreach (var combo in FindAll<ComboBox>(root))
            {
                if (AutomationProperties.GetName(combo) == AiAgentPicker.ComboAutomationName)
                    return combo;
            }
            throw new InvalidOperationException("Agent picker ComboBox not found");
        }

        private static List<string> ComboItems(DependencyObject root)
        {
            var items = new List<string>();
            foreach (var item in FindPickerCombo(root).Items)
                items.Add(item?.ToString() ?? string.Empty);
            return items;
        }

        private static List<TextBlock> FindNotices(DependencyObject root)
            => FindAll<TextBlock>(root).FindAll(
                tb => AutomationProperties.GetName(tb) == AiChatPanel.ClearedAssignmentNoticeAutomationName);

        private static void InvokeAddAssistantMessage(AiChatPanel panel, string text,
            string? agentName = null, string? selectedAgentName = null)
        {
            var method = typeof(AiChatPanel).GetMethod("AddAssistantMessage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(panel, new object?[] { text, null, agentName, selectedAgentName });
        }

        private static void SeedHistory(AiChatPanel panel, params (string Role, string Content)[] turns)
        {
            var field = typeof(AiChatPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var history = (List<ChatTurnDto>)field!.GetValue(panel)!;
            foreach (var (role, content) in turns)
                history.Add(new ChatTurnDto { Role = role, Content = content });
        }

        private static List<ChatTurnDto> HistoryOf(AiChatPanel panel)
        {
            var field = typeof(AiChatPanel).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (List<ChatTurnDto>)field!.GetValue(panel)!;
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

        /// <summary>
        /// Redirects <c>AKML_APP_DATA_ROOT</c> to a fresh temp root so a picker selection's
        /// <c>ConfigManager.Save</c> never touches the real user config (the panel tests cannot
        /// derive from <see cref="AppDataIsolatedTest"/> — a class joins exactly one xunit
        /// collection, and this one must stay in "AkmlSql ThemeRegistry" to serialise the
        /// panel-constructing tests).
        /// </summary>
        private sealed class AppDataRedirect : IDisposable
        {
            private readonly string? _prior;

            internal AppDataRedirect()
            {
                // Same process-wide lock as AppDataIsolatedTest: the env var is process-global
                // and this class lives in a different collection, so without the shared lock a
                // racing Dispose could restore the real path mid-write (fixture agents once
                // landed in the developer's REAL config.json).
                AppDataIsolatedTest.EnvVarLock.Wait();
                _prior = Environment.GetEnvironmentVariable("AKML_APP_DATA_ROOT");
                Environment.SetEnvironmentVariable("AKML_APP_DATA_ROOT",
                    Path.Combine(Path.GetTempPath(), "akml-picker-tests-" + Guid.NewGuid().ToString("N")));
            }

            public void Dispose()
            {
                try
                {
                    Environment.SetEnvironmentVariable("AKML_APP_DATA_ROOT", _prior);
                }
                finally
                {
                    AppDataIsolatedTest.EnvVarLock.Release();
                }
            }
        }
    }
}
