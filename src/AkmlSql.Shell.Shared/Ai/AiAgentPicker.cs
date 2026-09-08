#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Spec 037 (US3, FR-037/FR-038/FR-041/FR-044) — the chat header's agent picker: a
    /// non-editable ComboBox listing the usable agents by name (S1 — disabled and otherwise
    /// unusable agents are hidden) plus a trailing <see cref="AddAgentDisplayText"/> entry that
    /// routes into Options. The selection is the RESOLVED chat agent (S3): the chat assignment
    /// when set, otherwise the active agent — so a user who never touches the picker still sees
    /// who answers. Selecting an entry raises <see cref="AgentSelected"/> with the agent's id;
    /// the owning panel decides what to persist (research R7). Shown even with exactly one
    /// agent — the user must always know who is answering.
    ///
    /// <para>The ComboBox is themed via <see cref="ComboBoxTheming"/> (the stock template paints
    /// a hardcoded light face). Unlike the Options dialogs, which rebuild on a theme switch, the
    /// chat panel lives across one — so the template is re-applied on
    /// <see cref="ThemeRegistry.VariantChanged"/>, subscribed only while loaded. Items are plain
    /// strings per the <see cref="ComboBoxTheming"/> contract.</para>
    /// </summary>
    internal sealed class AiAgentPicker : UserControl
    {
        /// <summary>The trailing entry that opens Options to add an agent (FR-041).</summary>
        internal const string AddAgentDisplayText = "Add agent…";

        /// <summary>Accessible name of the picker's ComboBox.</summary>
        internal const string ComboAutomationName = "Answering agent";

        private readonly ComboBox _combo;

        /// <summary>Agent ids parallel to <see cref="_combo"/>'s items, minus the Add entry.</summary>
        private readonly List<string> _agentIds = new();
        private bool _suppressSelection;

        /// <summary>Raised when the user picks an agent entry; the argument is the agent's id.</summary>
        public event EventHandler<string>? AgentSelected;

        /// <summary>Raised when the user picks the trailing <see cref="AddAgentDisplayText"/> entry.</summary>
        public event EventHandler? AddAgentRequested;

        public AiAgentPicker()
        {
            _combo = new ComboBox
            {
                FontSize = Typography.Small,
                MinWidth = 110,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Choose which agent answers your chat messages",
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            System.Windows.Automation.AutomationProperties.SetName(_combo, ComboAutomationName);
            ComboBoxTheming.Apply(_combo);
            _combo.SelectionChanged += OnSelectionChanged;

            Content = _combo;

            Loaded += (_, _) =>
            {
                ComboBoxTheming.Apply(_combo);
                ThemeRegistry.Instance.VariantChanged += OnThemeVariantChanged;
            };
            Unloaded += (_, _) => ThemeRegistry.Instance.VariantChanged -= OnThemeVariantChanged;
        }

        /// <summary>
        /// Repopulates the picker from <paramref name="agents"/> — usable ones only, in list
        /// order — plus the Add entry, and selects <paramref name="resolvedAgentId"/> (no
        /// selection when null or not listed). Programmatic; never raises
        /// <see cref="AgentSelected"/>.
        /// </summary>
        internal void SetAgents(IReadOnlyList<AiAgent>? agents, string? resolvedAgentId)
        {
            _suppressSelection = true;
            try
            {
                _agentIds.Clear();
                _combo.Items.Clear();
                if (agents != null)
                {
                    foreach (var agent in agents)
                    {
                        // S1: disabled and otherwise unusable agents are hidden from the picker.
                        if (agent == null || !AiAgentResolver.IsUsable(agent)) continue;
                        _agentIds.Add(agent.Id);
                        _combo.Items.Add(agent.Name);
                    }
                }
                _combo.Items.Add(AddAgentDisplayText);
                _combo.SelectedIndex = resolvedAgentId == null ? -1 : _agentIds.IndexOf(resolvedAgentId);
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            var index = _combo.SelectedIndex;
            if (index < 0) return;

            if (index == _agentIds.Count)
            {
                // The Add entry is an action, not a selection — the panel re-syncs the picker
                // to the resolved agent once Options returns (or is cancelled).
                AddAgentRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            AgentSelected?.Invoke(this, _agentIds[index]);
        }

        private void OnThemeVariantChanged(object? sender, EventArgs e) => ComboBoxTheming.Apply(_combo);
    }
}
