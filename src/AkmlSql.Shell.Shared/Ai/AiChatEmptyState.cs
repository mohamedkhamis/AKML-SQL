#nullable enable
using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Spec 037 (US1, FR-015/FR-016/FR-021) — the chat panel's onboarding card, shown in place of
    /// the greeting when no agent can answer: the panel must never invite a question it cannot
    /// answer. One primary action ("Add AI agent"); the reason text names the specific problem
    /// and the offending agent (FR-021) so the button can open Options on that agent rather
    /// than on a blank one. Theme tokens only (SetResourceReference), the shared hoisted
    /// <see cref="Typography.UiFont"/>, no brushes created (so nothing to freeze).
    /// </summary>
    internal sealed class AiChatEmptyState : UserControl
    {
        /// <summary>Accessible name of the card's single primary action (FR-016).</summary>
        internal const string AddAgentAutomationName = "Add AI agent";

        /// <summary>FR-021 wording when the agent list is empty.</summary>
        internal const string NoAgentsText = "No AI agent is set up.";

        /// <summary>FR-021 wording when every agent in the list is disabled.</summary>
        internal const string AllDisabledText = "Every AI agent is turned off.";

        /// <summary>Raised by the card's single primary action.</summary>
        public event EventHandler? AddAgentRequested;

        public AiChatEmptyState(string reasonText)
        {
            var titleBlock = new TextBlock
            {
                Text = reasonText,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

            var bodyBlock = new TextBlock
            {
                Text = "AKML SQL needs an AI agent before it can answer questions about your database.",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, Spacing.Sm, 0, 0)
            };
            bodyBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var addButton = new Button
            {
                Content = AddAgentAutomationName,
                MinWidth = 120,
                Padding = new Thickness(Spacing.Md, 6, Spacing.Md, 6),
                Margin = new Thickness(0, Spacing.Md, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            ThemedButton.ApplyPrimary(addButton);
            System.Windows.Automation.AutomationProperties.SetName(addButton, AddAgentAutomationName);
            addButton.Click += (_, _) => AddAgentRequested?.Invoke(this, EventArgs.Empty);

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(titleBlock);
            stack.Children.Add(bodyBlock);
            stack.Children.Add(addButton);

            var card = new Border
            {
                Child = stack,
                Padding = new Thickness(Spacing.Lg, Spacing.Lg, Spacing.Lg, Spacing.Lg),
                Margin = new Thickness(Spacing.Md, Spacing.Xl, Spacing.Md, Spacing.Sm),
                CornerRadius = new CornerRadius(Spacing.Sm),
                BorderThickness = new Thickness(1),
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            card.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            Content = card;
        }

        // ── Reason detection (FR-021) ────────────────────────────────────────

        /// <summary>
        /// The empty-state reason: the FR-021 wording and the id of the offending agent (the
        /// first unusable one in list order), or <c>""</c> when there is no agent to blame —
        /// the card's button then opens Options with an implicit Add instead of on an agent.
        /// </summary>
        internal static (string Text, string OffendingAgentId) DetectReason(AiSettings? ai)
        {
            var agents = ai?.Agents;
            if (agents == null || agents.Count == 0)
                return (NoAgentsText, string.Empty);

            // "Turned off" is a global state, not a per-agent fault — say so only when it is
            // true of every agent; a disabled agent in a mixed list is skipped below so the
            // first fixable one is named instead.
            AiAgent? firstEnabled = null;
            foreach (var agent in agents)
            {
                if (agent != null && agent.Enabled)
                {
                    firstEnabled = agent;
                    break;
                }
            }
            if (firstEnabled == null)
                return (AllDisabledText, agents[0]?.Id ?? string.Empty);

            foreach (var agent in agents)
            {
                if (agent == null || !agent.Enabled) continue;
                if (CanAnswer(agent)) continue;
                return (ReasonTextFor(agent), agent.Id ?? string.Empty);
            }

            // Defensive: the caller only asks when no agent can answer.
            return (NoAgentsText, string.Empty);
        }

        /// <summary>The FR-021 wording for one unusable agent, naming it.</summary>
        private static string ReasonTextFor(AiAgent agent)
        {
            var name = agent.Name ?? string.Empty;
            var provider = agent.Provider ?? string.Empty;
            if (AiAgentResolver.RequiresApiKey(provider) && string.IsNullOrEmpty(agent.ApiKey))
                return $"{name} needs an API key before it can answer.";
            if (AiAgentResolver.RequiresApiKey(provider) && KeyWillNotDecrypt(agent.ApiKey))
                return $"{name}'s stored API key could not be read on this machine — re-enter it.";
            if (string.IsNullOrWhiteSpace(agent.Model))
                return $"{name} has no model selected.";
            if (AiAgentResolver.RequiresEndpoint(provider) && string.IsNullOrEmpty(agent.Endpoint))
                return $"{name} needs an endpoint URL.";
            // Provider blank/unrecognised: FR-021's closed set has no provider wording, and an
            // agent with no provider cannot have a usable model — the needs-model wording is
            // this case's bucket. What it must NOT say is NoAgentsText: the deep link opens
            // THIS agent, so "No AI agent is set up." would contradict the dialog it lands on.
            return $"{name} has no model selected.";
        }

        /// <summary>
        /// S1 (<see cref="AiAgentResolver.IsUsable"/>) plus the shell-observable half of V23: an
        /// agent whose stored key cannot be unwrapped on this machine cannot answer — the card
        /// reports it ("could not be read") instead of letting the first question fail with a
        /// provider error. The Core predicate deliberately does not attempt DPAPI; the shell can.
        /// </summary>
        internal static bool CanAnswer(AiAgent? agent)
        {
            if (agent == null || !AiAgentResolver.IsUsable(agent)) return false;
            if (AiAgentResolver.RequiresApiKey(agent.Provider ?? string.Empty) &&
                KeyWillNotDecrypt(agent.ApiKey))
                return false;
            return true;
        }

        /// <summary>True when the value is <c>dpapi:</c>-wrapped and this Windows user cannot unwrap it.</summary>
        private static bool KeyWillNotDecrypt(string? stored)
        {
            if (!ApiKeyProtector.IsProtected(stored)) return false;
            try
            {
                ApiKeyProtector.Unprotect(stored);
                return false;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return true;
            }
        }
    }
}
