#nullable enable
using System;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Spec 037 (US1, FR-022) — the no-agent gate for the deliberately invoked AI surfaces
    /// (Explain, Fix, Optimize, Index Analysis, Text-to-SQL). The chat panel has its own card;
    /// ghost text stays silent by design (it fires on typing — the user never asked). These five
    /// report the chat card's reason (the FR-021 wordings) and offer the same route into Options
    /// instead of surfacing a raw provider error. The commands are normally hidden by
    /// <see cref="AiCommandVisibility"/> when no agent is usable, but a shortcut or a stale menu
    /// can still invoke them — and <see cref="AiSettings.Enabled"/> cannot see an undecryptable
    /// key (V23), which this check does see.
    /// </summary>
    internal static class AiNoAgentGate
    {
        /// <summary>
        /// Wording when the master AI toggle is off. Deliberately distinct from the FR-021
        /// no-agent wordings: the user's problem is a switch that is off, not a missing agent.
        /// </summary>
        internal const string AiTurnedOffText = "AI assistance is turned off.";

        /// <summary>
        /// Wording when <see cref="AiSettings.PrivacyMode"/> is "disabled" — that setting blocks
        /// every AI feature no matter how many usable agents are configured.
        /// </summary>
        internal const string PrivacyDisabledText =
            "AI assistance is blocked by the \"Disabled\" privacy mode.";

        /// <summary>
        /// Reports the no-agent state and offers the Options route when no agent can answer.
        /// Returns <c>true</c> when the caller must stop (nothing can answer the request).
        /// </summary>
        public static async System.Threading.Tasks.Task<bool> ShouldStopAsync(string featureLabel)
        {
            AppSettings settings;
            try
            {
                settings = ConfigManager.Load();
            }
            catch (Exception ex)
            {
                // A failed read must not turn into a false "no agent" claim — let the feature
                // proceed to its own error path.
                Log.Debug(ex, "AiNoAgentGate: settings read failed; letting the feature proceed");
                return false;
            }

            // The master switch and the privacy mode gate every AI surface and are invisible to
            // the CanAnswer sweep below: with a perfectly usable agent but AI switched off, the
            // request reaches the engine and comes back with a bare "AI assistance is disabled"
            // that AiIpcTimeouts.IsConfigurationCaused classifies as non-actionable — no
            // explanation, no route to the setting that caused it. The hidden-menu path cannot
            // cover keyboard shortcuts (and caches for 5s), so the same predicates are asked here,
            // from AiCommandVisibility, so both surfaces read one condition.
            if (!AiCommandVisibility.IsAiEnabled(settings))
            {
                return await AskAndOpenOptionsAsync(
                    AiTurnedOffText,
                    "Open AKML SQL Options → AI Assistance to turn it on?",
                    featureLabel,
                    null);
            }

            if (!AiCommandVisibility.IsPrivacyPermitting(settings))
            {
                return await AskAndOpenOptionsAsync(
                    PrivacyDisabledText,
                    "Open AKML SQL Options → AI Assistance to change the privacy mode?",
                    featureLabel,
                    null);
            }

            var agents = settings.Ai.Agents;
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    if (AiChatEmptyState.CanAnswer(agent))
                        return false;
                }
            }

            var (text, agentId) = AiChatEmptyState.DetectReason(settings.Ai);
            return await AskAndOpenOptionsAsync(
                text,
                "Open AKML SQL Options to set up an AI agent?",
                featureLabel,
                agentId);
        }

        /// <summary>
        /// The shared report-and-route step: always returns <c>true</c> (the caller must stop).
        /// <paramref name="agentId"/> deep-links Options at the offending agent, or is
        /// <c>null</c> when the fault is a global setting rather than one agent.
        /// </summary>
        private static async System.Threading.Tasks.Task<bool> AskAndOpenOptionsAsync(
            string text, string question, string featureLabel, string? agentId)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var openOptions = MessageBox.Show(
                text + "\n\n" + question,
                featureLabel,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (openOptions)
            {
                // The shared save-and-notify path — never a bare ConfigManager.Save, or the
                // engine keeps serving the old configuration (R8).
                OptionsCommand.ShowOptions("AI Assistance", agentId);
            }
            return true;
        }
    }
}
