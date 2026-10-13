#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Serilog;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class AiAssistancePage : IPageBuilder
    {
        public string Key     => "AI Assistance";
        public string Display => "AI assistance";
        public string Title   => "AI assistance";
        public string HelpTopic => "topics/options#ai-assistance";
        public string Help    => "Connect an AI provider (Anthropic, OpenAI, Gemini, Kimi, Ollama, and more) and tune the model, privacy mode, and request parameters that power AI features. Enable assistance such as natural-language-to-SQL, query explanation, error fixes, optimization, index suggestions, the chat panel, and inline ghost-text completions.";

        /// <summary>
        /// Spec 036 (US2, FR-013): the provider list keyed by canonical id, in display order.
        /// <c>Save</c> writes the id (never the display name) and <c>Load</c> finds the entry by
        /// id after <see cref="AiProviderIds.Normalize"/> — the old positional index→string
        /// switches are how the Azure/LM Studio mismatch survived (research R8).
        /// </summary>
        internal static readonly (string Display, string Id)[] Providers =
        {
            ("(None)", ""),
            ("Anthropic", AiProviderIds.Anthropic),
            ("OpenAI", AiProviderIds.OpenAI),
            ("Azure OpenAI", AiProviderIds.Azure),
            ("Gemini", AiProviderIds.Gemini),
            ("Kimi (Moonshot)", AiProviderIds.Kimi),
            ("Ollama", AiProviderIds.Ollama),
            ("LM Studio", AiProviderIds.LmStudio),
            ("Custom", AiProviderIds.Custom),
        };

        /// <summary>Spec 037 (US4, T074): the first entry of every assignment dropdown — an
        /// empty assignment follows the active agent (S3).</summary>
        internal const string UseActiveAgentDisplay = "Use active agent";

        /// <summary>Spec 037 (US4, T074): the seven assignable features, in display order.</summary>
        internal static readonly (string Label, string Tip, AiFeature Feature)[] AssignmentFeatures =
        {
            ("Agent for the chat panel", "The agent that answers in the chat panel — or follow the active agent", AiFeature.Chat),
            ("Agent for text-to-SQL", "The agent that generates SQL from plain English — or follow the active agent", AiFeature.TextToSql),
            ("Agent for Explain SQL", "The agent that explains queries — or follow the active agent", AiFeature.Explain),
            ("Agent for Fix errors", "The agent that suggests fixes for failing queries — or follow the active agent", AiFeature.Fix),
            ("Agent for Optimize queries", "The agent that suggests optimizations — or follow the active agent", AiFeature.Optimize),
            ("Agent for index suggestions", "The agent that recommends indexes — or follow the active agent", AiFeature.IndexSuggestions),
            ("Agent for inline ghost text", "The agent that completes as you type — or follow the active agent", AiFeature.GhostText),
        };

        /// <summary>Width of the feature table's "On" column.</summary>
        private const double FeatureOnColumnWidth = 60;

        /// <summary>Width of the feature table's "Answered by" column.</summary>
        private const double FeatureAgentColumnWidth = 230;

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var theme = ctx.Theme;
            var dark = ReferenceEquals(theme, PageTheme.Dark);

            // 2026-10 redesign (design B of artifacts/ai-assistance-demo): three in-page tabs.
            //   Agents          the list, the selected agent's fitted editor, Test connection
            //   Features        one table of feature / on / answering agent, the fallback order
            //   Privacy & data  privacy mode, consent, the request parameters shared by every agent
            // Every row keeps its search label and automation name; only the arrangement moved.
            var tabs = new OptionsTabStrip(theme);
            var agentsTab = tabs.AddTab("Agents");
            var featuresTab = tabs.AddTab("Features");
            var privacyTab = tabs.AddTab("Privacy & data");
            tabs.AddTo(panel);

            // ── Agents tab ─────────────────────────────────────────────────────────────────
            var agentListView = new AiAgentListView(theme);
            // The list's four actions sit on the group-header line, so the agent row and the
            // fallback row no longer read as the same four buttons.
            agentListView.ButtonRow.Margin = new Thickness(8, 0, 0, 0);
            ctx.Rows.AddGroupHeader(agentsTab, "AI agents", afterLabel: null, trailing: agentListView.ButtonRow);
            var indent = ctx.Rows.GroupIndent;
            agentListView.List.Height = 124;   // four rows without a scrollbar
            agentListView.List.Margin = new Thickness(indent, 4, 0, 4);
            agentListView.AddListTo(agentsTab);
            ctx.RegisterSearch("AI agents", "Add, duplicate, remove, rename and activate AI agents", "List", agentListView.List);

            var activeChip = MakeChip(theme, "active agent");
            ctx.Rows.AddGroupHeader(agentsTab, "Selected agent", afterLabel: activeChip, trailing: null);

            // Name ─ Enabled on one row; the name box stretches to the column's right edge.
            const string nameTip = "A friendly name for this agent — shown in the chat picker and feature assignments";
            const string enabledTip = "Turn this agent on or off — a turned-off agent is hidden from the chat picker and skipped by feature assignments and the fallback order";
            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameColumn = new StackPanel();
            var (_, txtName) = ctx.Rows.AddTextInput(nameColumn, "Name", string.Empty, stretch: true);
            txtName.ToolTip = nameTip;
            nameRow.Children.Add(nameColumn);
            // Spec 037 (review finding): the per-agent on/off switch — the route back from the
            // "Every AI agent is turned off." empty state without deleting the agent. Committed
            // through CommitEditorToSelectedAgent like every other editor field, and NOT one of
            // the T086 health-reset fields, so it carries no change handler at all.
            var chkAgentEnabled = new CheckBox
            {
                Content = "Enabled",
                Foreground = theme.FgPrimary,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 0, 18),
                ToolTip = enabledTip,
            };
            chkAgentEnabled.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Agent enabled");
            Grid.SetColumn(chkAgentEnabled, 1);
            nameRow.Children.Add(chkAgentEnabled);
            agentsTab.Children.Add(nameRow);
            ctx.RegisterSearch("Name", nameTip, "Text", nameRow);
            ctx.RegisterSearch("Enabled", enabledTip, "Toggle", nameRow);

            // FR-032 (US2 scenario 5): name validation on focus loss, inline beside the field —
            // a modal here would fire while the user is still tabbing through the dialog. The
            // controls object recolours this to the semantic failure brush and drives it from
            // the name box's LostFocus.
            var nameError = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(indent, -6, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            agentsTab.Children.Add(nameError);

            // Provider ─ Model on one row.
            var providerNames = new string[Providers.Length];
            for (var i = 0; i < Providers.Length; i++) providerNames[i] = Providers[i].Display;

            var pairRow = new Grid();
            pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var providerColumn = new StackPanel();
            var (rowProvider, cboProvider) = ctx.Rows.AddDropdown(providerColumn, "Provider", providerNames, string.Empty, width: 180);
            cboProvider.ToolTip = "Select the AI provider for SQL assistance features";
            var modelColumn = new StackPanel();
            var (rowModel, txtModel) = ctx.Rows.AddTextInput(modelColumn, "Model", "e.g. gpt-4o, claude-sonnet-4-6, gemini-flash-latest", stretch: true);
            Grid.SetColumn(modelColumn, 1);
            pairRow.Children.Add(providerColumn);
            pairRow.Children.Add(modelColumn);
            agentsTab.Children.Add(pairRow);
            ctx.RegisterSearch("AI Provider", "Select the AI provider for SQL assistance features", "Dropdown", rowProvider);
            ctx.RegisterSearch("Model", "e.g. gpt-4o, claude-sonnet-4-6, gemini-flash-latest", "Text", rowModel);

            // Provider switch: auto-correct an obviously foreign model. "claude-sonnet-5" left
            // behind on an Anthropic → Gemini switch reached Google's API verbatim and died with
            // a raw 404 in the chat panel. Empty or foreign-family text gets the new provider's
            // default; custom/unrecognised names are the user's business and stay untouched.
            // (Safe during Load: the provider combo is set BEFORE the stored model overwrites
            // whatever this writes.)
            cboProvider.SelectionChanged += (_, _) =>
            {
                var suggested = AiModelFamily.DefaultModelFor(cboProvider.SelectedItem as string);
                if (suggested == null) return;
                var current = (txtModel.Text ?? string.Empty).Trim();
                var family = AiModelFamily.Detect(current);
                if (current.Length == 0 || (family != null && family != AiModelFamily.Detect(suggested)))
                    txtModel.Text = suggested;
            };

            // API key: masked by default (a PasswordBox face over the TextBox every flow reads),
            // a Show/Hide button, and the "where do I get a key" help behind a link instead of an
            // always-open block between the key and the endpoint.
            var keyRow = new StackPanel { Margin = new Thickness(indent, 0, 0, 12) };
            var keyHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var keyHelpLink = MakeLinkButton(theme, "Where do I get a key?");
            DockPanel.SetDock(keyHelpLink, Dock.Right);
            keyHeader.Children.Add(keyHelpLink);
            var keyLabel = ctx.Rows.MakeFieldLabel("API key");
            keyLabel.Margin = new Thickness(0);
            keyLabel.VerticalAlignment = VerticalAlignment.Center;
            keyHeader.Children.Add(keyLabel);
            keyRow.Children.Add(keyHeader);

            var keyLine = new Grid();
            keyLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var txtApiKey = new TextBox
            {
                Background = theme.Input,
                Foreground = theme.FgPrimary,
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = theme.Caret,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                Visibility = Visibility.Collapsed,
                Tag = "password",
                ToolTip = "Your API key for the selected provider",
            };
            var keyMask = new PasswordBox
            {
                Background = theme.Input,
                Foreground = theme.FgPrimary,
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = theme.Caret,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "Your API key for the selected provider",
            };
            System.Windows.Automation.AutomationProperties.SetName(txtApiKey, "API key");
            System.Windows.Automation.AutomationProperties.SetName(keyMask, "API key");
            keyLine.Children.Add(keyMask);
            keyLine.Children.Add(txtApiKey);
            var btnShowKey = AiAgentListView.MakeButton("Show", "Show the API key", theme);
            btnShowKey.Margin = new Thickness(8, 0, 0, 0);
            btnShowKey.MinWidth = 60;
            Grid.SetColumn(btnShowKey, 1);
            keyLine.Children.Add(btnShowKey);
            keyRow.Children.Add(keyLine);
            agentsTab.Children.Add(keyRow);
            ctx.RegisterSearch("API Key", "Your API key for the selected provider", "Text", keyRow);

            // PR #251 review finding 2: a stored key this Windows user cannot decrypt (roamed
            // profile, restored backup, different machine) must be VISIBLE, not silently dropped.
            // Shown by Load on decrypt failure; follows the inline-help idiom (keyHelp below).
            var keyNotice = new Border
            {
                BorderBrush = theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(indent, 0, 0, 10),
                Background = theme.Panel,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "The stored API key could not be decrypted on this machine (it may come " +
                           "from a roamed profile or a restored backup). The stored value was left " +
                           "untouched — re-enter the key to keep using the selected provider.",
                    Foreground = theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            agentsTab.Children.Add(keyNotice);

            // The key help (theme-aware), collapsed until its link is clicked.
            var keyHelp = new Border
            {
                BorderBrush = theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(indent, -6, 0, 12),
                Background = theme.Panel,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text =
                        "How to get your API key:\n" +
                        "  • Anthropic (Claude): console.anthropic.com → API Keys" +
                        "  —  example model: claude-sonnet-4-6\n" +
                        "  • Google (Gemini): aistudio.google.com → Get API Key" +
                        "  —  example model: gemini-flash-latest\n" +
                        "  • OpenAI: platform.openai.com → API Keys" +
                        "  —  example model: gpt-4o\n" +
                        "Keys are stored encrypted with Windows DPAPI and never written in plain text.",
                    Foreground = theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                }
            };
            agentsTab.Children.Add(keyHelp);

            var (rowEndpoint, txtEndpoint) = ctx.Rows.AddTextInput(agentsTab,
                "Endpoint URL", "Required for Azure OpenAI and custom providers; leave empty for the others.", stretch: true);
            ctx.RegisterSearch("Endpoint URL", "Custom endpoint (required for Azure OpenAI and custom providers)", "Text", rowEndpoint);

            // Spec 036 (US2, FR-009): in-dialog connection test — directly UNDER the four fields it
            // tests, outcome on the same line. Sends the CURRENT field values over the existing
            // AiProviderTest (77/177) IPC pair; nothing is saved until OK.
            var btnTest = new Button
            {
                Content = "Test connection",
                MinWidth = 130,
                Height = 28,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ThemedButton.ApplySecondary(btnTest, theme);
            btnTest.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Test AI provider connection");
            var testIdle = new TextBlock
            {
                Foreground = theme.FgSecondary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var testResult = new TextBlock
            {
                Foreground = theme.FgSecondary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            var testLine = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            testLine.Children.Add(testIdle);
            testLine.Children.Add(testResult);
            var testGrid = new Grid();
            testGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            testGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            testGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            testGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            testGrid.Children.Add(btnTest);
            Grid.SetColumn(testLine, 1);
            testGrid.Children.Add(testLine);
            var testSub = new TextBlock
            {
                Text = "Sends one short prompt with the provider, model, key and endpoint above. Nothing is saved until OK.",
                Foreground = theme.FgSecondary,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Grid.SetRow(testSub, 1);
            Grid.SetColumnSpan(testSub, 2);
            testGrid.Children.Add(testSub);
            var testBox = new Border
            {
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                Background = theme.Main,
                Margin = new Thickness(indent, 2, 0, 14),
                Child = new Border
                {
                    BorderBrush = theme.FgAccent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 10, 12, 10),
                    Child = testGrid,
                },
            };
            agentsTab.Children.Add(testBox);
            ctx.RegisterSearch("Test connection", "Verify the configured AI provider, model, endpoint and key", "Button", testBox);

            // ── Features tab ───────────────────────────────────────────────────────────────
            // Spec 037 (US4, T074): which agent serves each feature — "Use active agent" or a
            // pinned agent — in ONE table with the feature's on/off switch (the two seven-item
            // lists of the old page), and (T075) the ordered chain the engine walks when the
            // answering agent fails, before the offline provider (contracts/agent-resolution.md).
            ctx.Rows.AddGroupHeader(featuresTab, "Features");
            var table = new StackPanel { Margin = new Thickness(indent, 0, 0, 0) };
            table.Children.Add(MakeFeatureTableHeader(theme));
            featuresTab.Children.Add(table);
            var featureRows = new FeatureAssignmentRows();

            (Border Row, CheckBox? Toggle, ComboBox? Combo) FeatureRow(string label, string? toggleTip, AiFeature? assign, CheckBox? parent)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FeatureOnColumnWidth) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FeatureAgentColumnWidth) });

                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    Foreground = theme.FgPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    // The two right columns are fixed; at the window's 720 px minimum the label
                    // column is ~120 px, so a long feature name trims and keeps its full text in a tip.
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = label,
                };
                grid.Children.Add(labelText);

                CheckBox? toggle = null;
                if (toggleTip != null)
                {
                    toggle = new CheckBox
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Foreground = theme.FgPrimary,
                        ToolTip = toggleTip,
                    };
                    System.Windows.Automation.AutomationProperties.SetName(toggle, label);
                    Grid.SetColumn(toggle, 1);
                    grid.Children.Add(toggle);
                }
                else
                {
                    // Spec 040 (OPT-01): the chat window is always available from the menu; the
                    // saved "Chat panel" value is kept untouched.
                    var always = new TextBlock
                    {
                        Text = "always",
                        FontSize = 11,
                        Foreground = theme.FgSecondary,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "The chat window is always available from the AKML SQL menu",
                    };
                    Grid.SetColumn(always, 1);
                    grid.Children.Add(always);
                }

                ComboBox? combo = null;
                string? assignLabel = null, assignTip = null;
                if (assign is AiFeature feature)
                {
                    (assignLabel, assignTip, _) = Array.Find(AssignmentFeatures, f => f.Feature == feature);
                    combo = new ComboBox
                    {
                        FontSize = 13,
                        Height = 26,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        FocusVisualStyle = FocusVisualStyles.HighStakes,
                        ToolTip = assignTip,
                    };
                    ComboBoxTheming.Apply(combo, theme);
                    combo.Items.Add(UseActiveAgentDisplay);
                    combo.SelectedIndex = 0;
                    System.Windows.Automation.AutomationProperties.SetName(combo, assignLabel ?? label);
                    Grid.SetColumn(combo, 2);
                    grid.Children.Add(combo);
                    featureRows.SetCombo(feature, combo);
                }

                var row = ctx.Rows.WrapRow(grid, new Thickness(0, 4, 0, 4));
                table.Children.Add(row);
                ctx.Rows.ApplyParent(row, parent, labelText);
                if (toggleTip != null) ctx.RegisterSearch(label, toggleTip, "Toggle", row);
                if (assignLabel != null) ctx.RegisterSearch(assignLabel, assignTip ?? string.Empty, "Dropdown", row);
                return (row, toggle, combo);
            }

            FeatureRow("Chat panel", null, AiFeature.Chat, null);
            var (_, chkTextToSql, _) = FeatureRow("Natural language to SQL", "Generate SQL from plain English descriptions", AiFeature.TextToSql, null);
            var (_, chkExplain, _) = FeatureRow("Explain SQL", "Get AI-powered explanations of SQL queries", AiFeature.Explain, null);
            var (_, chkFix, _) = FeatureRow("Fix errors", "Suggest fixes when queries fail with errors", AiFeature.Fix, null);
            var (_, chkAutoFix, _) = FeatureRow("Auto-fix on error", "Automatically suggest fixes when query execution fails", null, chkFix);
            var (_, chkOptimize, _) = FeatureRow("Optimize queries", "Get AI-powered query optimization suggestions", AiFeature.Optimize, null);
            var (_, chkIndex, _) = FeatureRow("Index suggestions", "AI-powered index analysis and recommendations", AiFeature.IndexSuggestions, null);
            var (_, chkInline, _) = FeatureRow("Inline ghost text", "Show AI-powered inline completion suggestions as ghost text", AiFeature.GhostText, null);

            var tableNote = ctx.Rows.MakeDescription(
                "“Use active agent” follows the ● agent in the list. Switching a feature off hides it from the menus; its agent choice is kept.");
            tableNote.Margin = new Thickness(indent, 4, 0, 0);
            featuresTab.Children.Add(tableNote);

            ctx.Rows.AddGroupSeparator(featuresTab);
            ctx.Rows.AddGroupHeader(featuresTab, "Fallback order");
            const string fallbackTip = "When the answering agent fails, the chain below is tried in order — before the offline provider";
            var fallbackNote = ctx.Rows.MakeDescription("When the answering agent fails, these agents are tried in order before the offline provider.");
            fallbackNote.Margin = new Thickness(indent, 0, 0, 6);
            featuresTab.Children.Add(fallbackNote);

            var addLine = new Grid { Margin = new Thickness(indent, 0, 0, 4) };
            addLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cboFallback = new ComboBox
            {
                FontSize = 13,
                Height = 28,
                Width = 220,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                ToolTip = fallbackTip,
            };
            ComboBoxTheming.Apply(cboFallback, theme);
            System.Windows.Automation.AutomationProperties.SetName(cboFallback, "Add to fallback order");
            addLine.Children.Add(cboFallback);
            var btnFallbackAdd = AiAgentListView.MakeButton("Add", "Add the chosen agent to the fallback order", theme);
            btnFallbackAdd.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(btnFallbackAdd, 1);
            addLine.Children.Add(btnFallbackAdd);
            featuresTab.Children.Add(addLine);
            ctx.RegisterSearch("Add to fallback order", fallbackTip, "Dropdown", addLine);

            var fallbackList = new ListBox
            {
                Height = 64,
                Margin = new Thickness(indent, 4, 0, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = theme.ComboBorder,
                Background = theme.Input,
                FontSize = 13,
            };
            System.Windows.Automation.AutomationProperties.SetName(fallbackList, "Fallback order");
            ScrollViewer.SetHorizontalScrollBarVisibility(fallbackList, ScrollBarVisibility.Disabled);
            fallbackList.ItemContainerStyle = AiAgentListView.BuildItemStyle(theme);
            featuresTab.Children.Add(fallbackList);
            ctx.RegisterSearch("Fallback order", "Agents tried in order when the answering agent fails", "List", fallbackList);

            var btnFallbackRemove = AiAgentListView.MakeButton("Remove", "Remove the selected agent from the fallback order", theme);
            var btnFallbackUp = AiAgentListView.MakeButton("Move up", "Try the selected agent earlier in the fallback order", theme);
            var btnFallbackDown = AiAgentListView.MakeButton("Move down", "Try the selected agent later in the fallback order", theme);
            var fallbackButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(indent, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            fallbackButtons.Children.Add(btnFallbackRemove);
            fallbackButtons.Children.Add(btnFallbackUp);
            fallbackButtons.Children.Add(btnFallbackDown);
            featuresTab.Children.Add(fallbackButtons);

            featureRows.FallbackList = fallbackList;
            featureRows.FallbackCandidate = cboFallback;
            featureRows.FallbackAdd = btnFallbackAdd;
            featureRows.FallbackRemove = btnFallbackRemove;
            featureRows.FallbackMoveUp = btnFallbackUp;
            featureRows.FallbackMoveDown = btnFallbackDown;

            // ── Privacy & data tab ─────────────────────────────────────────────────────────
            ctx.Rows.AddGroupHeader(privacyTab, "Privacy & data");

            var (rowPrivacy, cboPrivacy) = ctx.Rows.AddDropdown(privacyTab,
                "Privacy mode",
                new[] { "Schema Only", "Full", "Anonymous", "Offline", "Disabled" },
                "Controls what data is sent to the AI provider",
                width: 180);
            ctx.RegisterSearch("Privacy mode", "Controls what data is sent to the AI provider", "Dropdown", rowPrivacy);

            // Cloud-provider consent gate. The engine refuses to send prompts/schema to a NON-LOCAL
            // provider (Anthropic, OpenAI, Gemini, …) until the user consents here — otherwise AI
            // Chat and every AI feature fail with "CONSENT_REQUIRED: Data will be sent to your AI
            // provider. Please confirm in settings." Local providers (Ollama, LM Studio) never need
            // this. Unchecked = consent withheld (privacy-first default).
            const string consentTip = "Required before a cloud provider (Anthropic, OpenAI, Gemini) receives your prompts and schema. Local providers (Ollama, LM Studio) never need this. Leave off to block cloud AI.";
            var (rowConsent, chkConsent) = ctx.Rows.AddToggle(privacyTab,
                "Consent to cloud AI data sharing", consentTip);
            ctx.RegisterSearch("Consent to cloud AI data sharing", consentTip, "Toggle", rowConsent);

            // The request parameters: ONE set for every agent (the user's 2026-10-13 decision),
            // a 2 × 2 grid. Save writes the same values to every agent and the flat mirror, so
            // the config format and the engine are untouched.
            ctx.Rows.AddGroupSeparator(privacyTab);
            ctx.Rows.AddGroupHeader(privacyTab, "Request parameters");
            var paramsNote = ctx.Rows.MakeDescription("Used for every agent.");
            paramsNote.Margin = new Thickness(indent, 0, 0, 8);
            privacyTab.Children.Add(paramsNote);

            var paramsGrid = new Grid();
            paramsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            paramsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var leftColumn = new StackPanel();
            var rightColumn = new StackPanel();
            Grid.SetColumn(rightColumn, 1);
            paramsGrid.Children.Add(leftColumn);
            paramsGrid.Children.Add(rightColumn);
            privacyTab.Children.Add(paramsGrid);

            // Spec 040 (OPT-06): number fields for the wide ranges; units after the box, not in the label.
            var (rowMax, numMax) = ctx.Rows.AddNumber(leftColumn,
                "Max response tokens", 128, 128000, 256, "tokens",
                "Maximum number of tokens in the AI response");
            ctx.RegisterSearch("Max response tokens", "Maximum number of tokens in the AI response", "Number", rowMax);

            var (rowTemp, sldTemp, lblTemp) = ctx.Rows.AddSlider(leftColumn,
                "Creativity (temperature)", 0, 20, 2,
                "0 gives the same answer every time; higher values give more varied answers (the temperature, ×10)");
            ctx.RegisterSearch("Creativity (temperature)", "0 gives the same answer every time; higher values give more varied answers", "Slider", rowTemp);

            var (rowTimeout, numTimeout) = ctx.Rows.AddNumber(rightColumn,
                "Timeout", 5, 300, 5, "seconds",
                "How long to wait for the AI provider to answer");
            ctx.RegisterSearch("Timeout", "How long to wait for the AI provider to answer", "Number", rowTimeout);

            var (rowRetries, sldRetries, lblRetries) = ctx.Rows.AddSlider(rightColumn,
                "Retries", 0, 10, 2,
                "Number of automatic retries on transient failures");
            ctx.RegisterSearch("Retries", "Number of automatic retries on transient failures", "Slider", rowRetries);

            var chrome = new AiEditorChrome
            {
                Tabs = tabs,
                KeyMask = keyMask,
                ShowKeyButton = btnShowKey,
                KeyHelp = keyHelp,
                KeyHelpLink = keyHelpLink,
                ActiveChip = activeChip,
                TestIdle = testIdle,
                Dark = dark,
            };

            return new AiAssistanceControls(cboProvider, txtModel, txtApiKey, txtEndpoint, cboPrivacy,
                numMax, sldTemp, lblTemp, numTimeout, sldRetries, lblRetries,
                chkTextToSql!, chkExplain!, chkFix!, chkOptimize!, chkIndex!, chkInline!, chkAutoFix!,
                chkConsent, btnTest, testResult, keyNotice, txtName, nameError, chkAgentEnabled, agentListView, featureRows, chrome);
        }

        /// <summary>A small rounded status chip for a group-header line ("active agent"), collapsed until shown.</summary>
        private static Border MakeChip(PageTheme theme, string text)
        {
            return new Border
            {
                Background = theme.Button,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = theme.FgSecondary,
                },
            };
        }

        /// <summary>A link-styled button (accent text, no chrome) for an inline help toggle.</summary>
        private static Button MakeLinkButton(PageTheme theme, string text)
        {
            var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            var button = new Button
            {
                Content = text,
                Foreground = theme.FgAccent,
                Background = theme.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                FontSize = 11.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Template = template,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            return button;
        }

        /// <summary>The feature table's column captions ("Feature", "On", "Answered by") over a hairline.</summary>
        private static Border MakeFeatureTableHeader(PageTheme theme)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FeatureOnColumnWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FeatureAgentColumnWidth) });
            var captions = new[] { "Feature", "On", "Answered by" };
            for (var i = 0; i < captions.Length; i++)
            {
                var caption = new TextBlock { Text = captions[i], FontSize = 11, Foreground = theme.FgSecondary };
                Grid.SetColumn(caption, i);
                grid.Children.Add(caption);
            }
            return new Border
            {
                BorderBrush = theme.Sep,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 2, 0, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Child = grid,
            };
        }
    }

    /// <summary>
    /// The 2026-10 page chrome that is not an editor field: the tab strip, the masked key face
    /// and its Show button, the key help and its link, the "active agent" chip and the idle
    /// Test-connection line. One bag so the already-wide controls constructor grows by one
    /// parameter (the <see cref="FeatureAssignmentRows"/> precedent).
    /// </summary>
    internal sealed class AiEditorChrome
    {
        internal OptionsTabStrip Tabs { get; set; } = null!;
        internal PasswordBox KeyMask { get; set; } = null!;
        internal Button ShowKeyButton { get; set; } = null!;
        internal Border KeyHelp { get; set; } = null!;
        internal Button KeyHelpLink { get; set; } = null!;
        internal Border ActiveChip { get; set; } = null!;
        internal TextBlock TestIdle { get; set; } = null!;
        internal bool Dark { get; set; }
    }

    /// <summary>
    /// Spec 037 (US4, T074/T075): the "Feature assignments" dropdowns (keyed by
    /// <see cref="AiFeature"/>) and the "Fallback order" editor, built by
    /// <see cref="AiAssistancePage.Build"/> and handed to <see cref="AiAssistanceControls"/> as
    /// one bag so the already-wide controls constructor grows by a single parameter.
    /// </summary>
    internal sealed class FeatureAssignmentRows
    {
        private readonly Dictionary<AiFeature, ComboBox> _combos = new();

        internal ListBox FallbackList { get; set; } = null!;
        internal ComboBox FallbackCandidate { get; set; } = null!;
        internal Button FallbackAdd { get; set; } = null!;
        internal Button FallbackRemove { get; set; } = null!;
        internal Button FallbackMoveUp { get; set; } = null!;
        internal Button FallbackMoveDown { get; set; } = null!;

        internal void SetCombo(AiFeature feature, ComboBox combo) => _combos[feature] = combo;
        internal ComboBox ComboFor(AiFeature feature) => _combos[feature];
    }

    internal sealed class AiAssistanceControls : IPageControls
    {
        private readonly ComboBox _provider;
        private readonly TextBox _model;
        private readonly TextBox _apiKey;
        private readonly TextBox _endpoint;
        private readonly ComboBox _privacy;
        private readonly CheckBox _cloudConsent;
        private readonly TextBox _maxTokens;
        private readonly Slider _temperature;
        private readonly TextBlock _temperatureLabel;
        private readonly TextBox _timeout;
        private readonly Slider _retries;
        private readonly TextBlock _retriesLabel;
        private readonly CheckBox _textToSql;
        private readonly CheckBox _explain;
        private readonly CheckBox _fix;
        private readonly CheckBox _optimize;
        private readonly CheckBox _indexSuggestions;
        private readonly CheckBox _inlineCompletion;
        private readonly CheckBox _autoFixOnError;
        private readonly Button _testButton;
        private readonly TextBlock _testResult;
        private readonly Brush _testIdleBrush;
        private readonly Border _keyNotice;
        private readonly TextBox _name;
        private readonly TextBlock _nameError;
        private readonly CheckBox _agentEnabled;
        private readonly AiAgentListView _listView;
        private readonly FeatureAssignmentRows _featureRows;
        private readonly AiEditorChrome _chrome;   // tabs, masked key, key help, active chip, idle test line
        // Semantic colours per theme — the list badges' own brushes (the documented hex exception).
        private readonly SolidColorBrush _successBrush;
        private readonly SolidColorBrush _failureBrush;
        // The request parameters as Load showed them: Save compares against this to tell a
        // still-blank placeholder agent (dropped) from one the user retuned (kept).
        private (int MaxTokens, int Temperature10, int Timeout, int Retries) _loadedParams;
        private bool _suppressKeySync;
        private readonly List<string> _assignmentIds = new();   // index-aligned with every assignment combo
        // Defect 11 fix: index-aligned with the fallback CANDIDATE combo, which skips agents
        // already in _fallback — the ids must skip them in lockstep or the alignment is wrong.
        // Resolving the picked candidate by name went to the first agent carrying that name,
        // which is the wrong agent as soon as two agents share one (and did nothing at all for
        // an unnamed one).
        private readonly List<string> _fallbackCandidateIds = new();
        private bool _suppressFeatureEvents;

        // Spec 037 (US2, research R13): the page edits a WORKING COPY of the agent list (plus the
        // assignments and fallback order), written back wholesale in Save. Cancel is correct for
        // free — the host only calls Save on OK, so an abandoned copy is an abandoned edit.
        // KeyDecryptFailed lives on each working-copy agent (V23): the user can switch between a
        // decryptable and an undecryptable agent without saving, so the flag cannot be page-level.
        private List<AiAgent> _agents = new();
        private string _activeId = string.Empty;
        private string _selectedId = string.Empty;
        private FeatureAgentAssignments _assignments = new();
        private List<string> _fallback = new();
        private bool _suppressListEvents;
        private bool _suppressHealthReset;

        // Defect 9 fix (FR-032 scope): the ids of the agents this dialog session actually
        // TOUCHED — an editor commit that differs from the bound agent, or an agent the user
        // added/duplicated here. Pages load eagerly, so the working copy always holds every
        // stored agent; gating OK on all of them let one long-stale half-configured agent block
        // a save the user came here to make on an entirely different page, with no way out but
        // repairing an agent they never opened. FR-032 still refuses the agents being edited.
        private readonly HashSet<string> _touchedAgentIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Spec 037 (FR-017): deep-link target handed over by SettingsWindow before Load. An
        /// agent id binds the editor to that agent; <c>""</c> with an empty list performs the
        /// implicit Add (a new, empty agent ready to edit); <c>null</c> selects the active agent.
        /// </summary>
        internal string? InitialAgentId { get; set; }

        // ── US2 seams (T038–T046) ────────────────────────────────────────────
        // The agent list view and the working copy, exposed to the shell test assembly (which
        // compiles these sources in) and to SettingsWindow's OK-refusal hook.

        internal AiAgentListView ListView => _listView;
        internal IReadOnlyList<AiAgent> WorkingAgents => _agents;
        internal string SelectedAgentId => _selectedId;
        internal string ActiveAgentId => _activeId;

        // US4 seams (T074/T075): the feature-assignment dropdowns and the fallback-order
        // editor, exposed to the shell test assembly.
        internal ComboBox AssignmentComboFor(AiFeature feature) => _featureRows.ComboFor(feature);
        internal ListBox FallbackList => _featureRows.FallbackList;
        internal ComboBox FallbackCandidate => _featureRows.FallbackCandidate;
        internal Button FallbackAddButton => _featureRows.FallbackAdd;
        internal Button FallbackRemoveButton => _featureRows.FallbackRemove;
        internal Button FallbackMoveUpButton => _featureRows.FallbackMoveUp;
        internal Button FallbackMoveDownButton => _featureRows.FallbackMoveDown;

        // 2026-10 layout seams (AiAssistanceTabsLayoutTests): the tab strip, the masked key face
        // and its Show/Hide button, the key help block and its link.
        internal OptionsTabStrip Tabs => _chrome.Tabs;
        internal PasswordBox KeyMask => _chrome.KeyMask;
        internal Button ShowKeyButton => _chrome.ShowKeyButton;
        internal Border KeyHelp => _chrome.KeyHelp;
        internal Button KeyHelpLink => _chrome.KeyHelpLink;

        /// <summary>
        /// Test seam for the Remove confirmation (FR-028): receives the confirmation text and
        /// returns whether the user confirmed. When null, the click handler asks via MessageBox.
        /// </summary>
        internal Func<string, bool>? RemoveConfirmation { get; set; }

        /// <summary>
        /// Test seam (US5, T084): when set, the Test-connection flow sends through this accessor
        /// instead of <see cref="EngineRpcClientAccessor.Instance"/>, so the flow is exercisable
        /// without a pipe or an engine. Production code never sets it.
        /// </summary>
        internal static IRpcClientAccessor? TestRpcAccessor { get; set; }

        /// <summary>
        /// FR-026: append a fresh agent with a unique suggested name and select it for editing.
        /// V12 refuses the 21st with a message naming the limit; a refusal is returned, not
        /// shown, so tests never meet a modal — the click handler shows it.
        /// </summary>
        internal string? TryAddAgent()
        {
            CommitEditorToSelectedAgent();
            if (!AiAgentResolver.CanAddAgent(_agents.Count))
                return CeilingRefusal();
            var agent = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = AiAgentResolver.SuggestName(_agents),
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };
            _agents.Add(agent);
            _touchedAgentIds.Add(agent.Id);   // defect 9 fix: an agent the user added here is touched
            SelectAgent(agent);
            return null;
        }

        /// <summary>
        /// FR-027: copy every field of the selected agent INCLUDING the key, regenerate the id,
        /// rename via <see cref="AiAgentResolver.SuggestCopyName"/>, reset Health to null, insert
        /// after the source and select the copy.
        /// </summary>
        internal string? TryDuplicateSelectedAgent()
        {
            CommitEditorToSelectedAgent();
            if (!AiAgentResolver.CanAddAgent(_agents.Count))
                return CeilingRefusal();
            var source = SelectedAgent();
            if (source == null) return null;
            var copy = CopyAgent(source);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = AiAgentResolver.SuggestCopyName(_agents, source.Name ?? string.Empty);
            copy.Health = null;
            copy.CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _agents.Insert(_agents.IndexOf(source) + 1, copy);
            _touchedAgentIds.Add(copy.Id);   // defect 9 fix: a copy made here is touched
            SelectAgent(copy);
            return null;
        }

        /// <summary>
        /// FR-028: delete the selected agent, repair the active id (V13-shaped: first usable
        /// survivor, else none), clear assignments naming it (V16) and drop fallback entries
        /// naming it (V17), then select the nearest survivor. The confirmation itself is the
        /// click handler's job — see <see cref="BuildRemoveConfirmationText"/>.
        /// </summary>
        internal string? TryRemoveSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return null;
            CommitEditorToSelectedAgent();
            var index = _agents.IndexOf(agent);
            _agents.RemoveAt(index);
            RepairReferencesToRemovedAgent(agent);

            if (_agents.Count > 0)
            {
                SelectAgent(_agents[Math.Min(index, _agents.Count - 1)]);
            }
            else
            {
                _selectedId = string.Empty;
                ClearEditor();
                RenderList();
            }
            return null;
        }

        /// <summary>
        /// The reference repair every removal path shares (FR-028's Remove and the Save-time
        /// blank-agent drop): an active id pointing at the removed agent moves to the first
        /// usable survivor (V13-shaped), assignments naming it revert to "" (V16), and fallback
        /// entries naming it drop out (V17).
        /// </summary>
        private void RepairReferencesToRemovedAgent(AiAgent agent)
        {
            if (string.Equals(_activeId, agent.Id, StringComparison.Ordinal))
                _activeId = FirstUsableAgentId() ?? string.Empty;
            if (string.Equals(_assignments.Chat, agent.Id, StringComparison.Ordinal)) _assignments.Chat = string.Empty;
            if (string.Equals(_assignments.TextToSql, agent.Id, StringComparison.Ordinal)) _assignments.TextToSql = string.Empty;
            if (string.Equals(_assignments.Explain, agent.Id, StringComparison.Ordinal)) _assignments.Explain = string.Empty;
            if (string.Equals(_assignments.Fix, agent.Id, StringComparison.Ordinal)) _assignments.Fix = string.Empty;
            if (string.Equals(_assignments.Optimize, agent.Id, StringComparison.Ordinal)) _assignments.Optimize = string.Empty;
            if (string.Equals(_assignments.IndexSuggestions, agent.Id, StringComparison.Ordinal)) _assignments.IndexSuggestions = string.Empty;
            if (string.Equals(_assignments.GhostText, agent.Id, StringComparison.Ordinal)) _assignments.GhostText = string.Empty;
            _fallback.RemoveAll(id => string.Equals(id, agent.Id, StringComparison.Ordinal));
        }

        /// <summary>
        /// The Load-time seed (and any agent the user added but left untouched) is a placeholder
        /// for the editor, not a configuration: pages load eagerly, so an OK from ANY page would
        /// otherwise persist the phantom. Drop every agent that is STILL completely blank,
        /// repairing references exactly as Remove does. The validation exemption
        /// (<see cref="IsBlankAgent"/>) stays: a blank agent never BLOCKS OK — it just never
        /// persists.
        /// </summary>
        private void DropStillBlankAgents(bool paramsEdited)
        {
            var droppedAny = false;
            var selectedDropped = false;
            // Materialise first: the working copy is mutated inside the loop.
            foreach (var agent in _agents.Where(a => IsStillBlankAgent(a, paramsEdited)).ToList())
            {
                _agents.Remove(agent);
                RepairReferencesToRemovedAgent(agent);
                selectedDropped |= string.Equals(_selectedId, agent.Id, StringComparison.Ordinal);
                droppedAny = true;
            }
            if (!droppedAny) return;

            // Mirror Remove's selection repair so the editor never stays bound to a phantom.
            if (selectedDropped)
            {
                if (_agents.Count > 0)
                {
                    SelectAgent(_agents[0]);
                }
                else
                {
                    _selectedId = string.Empty;
                    ClearEditor();
                    RenderList();
                }
            }
            else
            {
                RenderList();   // rows, assignment combos and fallback candidates lost an entry
            }
        }

        /// <summary>FR-028: the confirmation wording, naming the selected agent.</summary>
        internal string BuildRemoveConfirmationText()
        {
            var name = (SelectedAgent()?.Name ?? string.Empty).Trim();
            return $"Remove the agent \"{(name.Length > 0 ? name : "(unnamed)")}\"? " +
                   "Its settings and stored key are deleted. This cannot be undone.";
        }

        /// <summary>
        /// Mark the selected agent active. Refused, with the reason, when the agent cannot
        /// answer (S1 plus the shell-observable half of V23 — the same usability the chat
        /// empty state uses), so an unconfigured agent cannot silently become active.
        /// </summary>
        internal string? TrySetActiveSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return null;
            CommitEditorToSelectedAgent(); // usability must see the current boxes, not the last commit
            if (!AiChatEmptyState.CanAnswer(agent))
                return $"\"{(agent.Name ?? string.Empty).Trim()}\" cannot be made the active agent — {UnusableReason(agent)}";
            _activeId = agent.Id;
            RenderList(); // move the ● marker
            return null;
        }

        /// <summary>
        /// FR-032: validate the WHOLE working copy (V1–V11 per agent via the resolver, V12 at
        /// list level) and return a message naming the agent and the field, or null when every
        /// agent is valid. The offending agent is selected BEFORE the caller shows the message,
        /// so the user lands where the problem is. The leading commit is the Save moment
        /// arriving early: OK is validate-then-Save, and validation must see the same working
        /// copy Save would write — including edits still sitting in the boxes.
        /// </summary>
        internal string? ValidateWorkingCopy() => ValidateAgents(touchedOnly: false);

        /// <summary>
        /// Defect 9 fix — the gate the settings dialog's OK and Apply use. Same FR-032 rules and
        /// wordings as <see cref="ValidateWorkingCopy"/>, but only over the agents this dialog
        /// session touched (see <see cref="_touchedAgentIds"/>): a stale half-configured agent
        /// the user never opened must not be able to refuse a save made from another page.
        /// V12 (the list ceiling) stays list-level — it is about the list, not one agent.
        /// </summary>
        internal string? ValidateTouchedAgents() => ValidateAgents(touchedOnly: true);

        private string? ValidateAgents(bool touchedOnly)
        {
            CommitEditorToSelectedAgent();

            if (_agents.Count > AiAgentResolver.MaxAgents)
            {
                return $"The list holds {_agents.Count} agents; the maximum is {AiAgentResolver.MaxAgents}. " +
                       "Remove agents until the list fits.";
            }

            foreach (var agent in _agents)
            {
                // Untouched agents are skipped by the dialog's gate only — the name-uniqueness
                // rules still compare against the WHOLE list, so a rename onto an untouched
                // agent's name is still caught.
                if (touchedOnly && !_touchedAgentIds.Contains(agent.Id)) continue;

                // A completely unconfigured agent is a placeholder, not a validation failure:
                // the page seeds exactly one on every load when the list is empty, so strict V4
                // would trap every unconfigured user out of the dialog's OK — from any page,
                // not just this one. The connection rules (V4, V6–V11) bite the moment ANY
                // connection field is filled; the name rules (V1–V3) always apply.
                var blank = IsBlankAgent(agent);
                string? error;
                if (blank)
                {
                    error = ValidateBlankAgentName(agent);
                }
                else
                {
                    error = AiAgentResolver.Validate(agent, _agents, agent.KeyDecryptFailed);
                }
                if (error == null) continue;
                SelectAgent(agent);
                var name = (agent.Name ?? string.Empty).Trim();
                return (name.Length > 0 ? name : "(unnamed agent)") + ": " + error;
            }
            return null;
        }

        private static bool IsBlankAgent(AiAgent agent)
            => string.IsNullOrEmpty(agent.Provider)
               && string.IsNullOrEmpty(agent.Model)
               && string.IsNullOrEmpty(agent.ApiKey)
               && string.IsNullOrEmpty(agent.Endpoint);

        /// <summary>
        /// The Save-time counterpart of <see cref="IsBlankAgent"/>, one notch stricter: the
        /// request parameters must also sit at the <see cref="AiAgent"/> defaults and the
        /// shared parameter rows must be untouched this session (<paramref name="paramsEdited"/>).
        /// That is the shape of the Load-seeded placeholder and of an Add the user never edited —
        /// a configuration the user only retuned the parameters of is a real (if incomplete)
        /// configuration and is kept. The name is irrelevant to both predicates.
        /// </summary>
        private static bool IsStillBlankAgent(AiAgent agent, bool paramsEdited)
        {
            var defaults = new AiAgent();
            return IsBlankAgent(agent)
                && !paramsEdited
                && agent.MaxTokens == defaults.MaxTokens
                && agent.Temperature == defaults.Temperature
                && agent.Timeout == defaults.Timeout
                && agent.Retries == defaults.Retries;
        }

        /// <summary>V1–V3 for a blank agent — the resolver's name rules and wordings, no connection rules.</summary>
        private string? ValidateBlankAgentName(AiAgent agent)
        {
            var name = (agent.Name ?? string.Empty).Trim();
            if (name.Length == 0) return "Name is required.";
            if (name.Length > 40) return "Name must be 40 characters or fewer.";
            foreach (var other in _agents)
            {
                if (ReferenceEquals(other, agent)) continue;
                if (string.Equals((other.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return $"Another agent is already named \"{name}\".";
            }
            return null;
        }

        private static string CeilingRefusal()
            => $"The list already holds the maximum of {AiAgentResolver.MaxAgents} agents. " +
               "Remove an agent before adding another.";

        private string? FirstUsableAgentId()
        {
            foreach (var agent in _agents)
            {
                if (AiAgentResolver.IsUsable(agent)) return agent.Id;
            }
            return null;
        }

        // The reason an agent cannot be made active, in the FR-021 spirit (key, key-decrypt,
        // model, endpoint) — ordered so the most actionable cause is named first.
        private static string UnusableReason(AiAgent agent)
        {
            var provider = agent.Provider ?? string.Empty;
            if (!agent.Enabled) return "it is disabled.";
            if (provider.Length == 0 || !AiProviderIds.CanonicalIds.Contains(provider))
                return "no provider is selected.";
            if (AiAgentResolver.RequiresApiKey(provider) && string.IsNullOrEmpty(agent.ApiKey))
                return "it needs an API key.";
            if (AiAgentResolver.RequiresApiKey(provider) && agent.KeyDecryptFailed)
                return "its stored API key could not be read on this machine — re-enter it.";
            if (string.IsNullOrWhiteSpace(agent.Model))
                return "it has no model selected.";
            if (AiAgentResolver.RequiresEndpoint(provider) && string.IsNullOrEmpty(agent.Endpoint))
                return "it needs an endpoint URL.";
            return "it is not fully configured.";
        }

        // The semantic success/failure brushes come from AiAgentListView.BadgeBrushFor, per
        // theme (the light-theme hex read at ~2.4:1 on the dark panel) — see the constructor.

        public AiAssistanceControls(
            ComboBox provider, TextBox model, TextBox apiKey, TextBox endpoint, ComboBox privacy,
            TextBox maxTokens, Slider sldTemp, TextBlock lblTemp,
            TextBox timeout, Slider sldRetries, TextBlock lblRetries,
            CheckBox textToSql, CheckBox explain, CheckBox fix, CheckBox optimize,
            CheckBox idx, CheckBox inline, CheckBox autoFix,
            CheckBox cloudConsent, Button testButton, TextBlock testResult, Border keyNotice,
            TextBox name, TextBlock nameError, CheckBox agentEnabled, AiAgentListView listView, FeatureAssignmentRows featureRows,
            AiEditorChrome chrome)
        {
            _provider = provider;
            _model = model;
            _apiKey = apiKey;
            _endpoint = endpoint;
            _privacy = privacy;
            _cloudConsent = cloudConsent;
            _maxTokens = maxTokens;
            _temperature = sldTemp;
            _temperatureLabel = lblTemp;
            _timeout = timeout;
            _retries = sldRetries;
            _retriesLabel = lblRetries;
            _textToSql = textToSql;
            _explain = explain;
            _fix = fix;
            _optimize = optimize;
            _indexSuggestions = idx;
            _inlineCompletion = inline;
            _autoFixOnError = autoFix;
            _testButton = testButton;
            _testResult = testResult;
            _testIdleBrush = testResult.Foreground;
            _keyNotice = keyNotice;
            _name = name;
            _nameError = nameError;
            _agentEnabled = agentEnabled;
            _listView = listView;
            _featureRows = featureRows;
            _chrome = chrome;
            _successBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Ready, chrome.Dark);
            _failureBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Failed, chrome.Dark);
            _nameError.Foreground = _failureBrush;
            // Any edit means the user has taken control of the field — the decrypt-failure
            // guard no longer applies (a deliberate clear must stay possible). The notice goes
            // with it: once the user types, "the stored value was left untouched" is no longer
            // true, because Save will now overwrite it. The flag lives on the SELECTED
            // working-copy agent, which is why BindEditorTo sets _selectedId before assigning
            // the key text (the assignment fires this handler).
            _apiKey.TextChanged += (_, _) =>
            {
                var agent = SelectedAgent();
                if (agent != null) agent.KeyDecryptFailed = false;
                _keyNotice.Visibility = Visibility.Collapsed;
            };
            // R10 (US5, T086): any edit to provider, model, key or endpoint invalidates the
            // health a test recorded — a ready badge next to a key the user just replaced is
            // worse than no badge. The reset lands on the SELECTED working-copy agent
            // immediately (the commit moments come later) and is suppressed while
            // BindEditorTo / ClearEditor replay stored values into the boxes.
            _provider.SelectionChanged += (_, _) => ResetSelectedAgentHealthOnEdit();
            _model.TextChanged += (_, _) => ResetSelectedAgentHealthOnEdit();
            _apiKey.TextChanged += (_, _) => ResetSelectedAgentHealthOnEdit();
            _endpoint.TextChanged += (_, _) => ResetSelectedAgentHealthOnEdit();
            _testButton.Click += async (_, _) => await RunProviderTestAsync();

            // The masked face of the key box: the PasswordBox mirrors the TextBox both ways, so
            // every flow (Load, Save, the test, the key-protection rules) keeps reading and
            // writing _apiKey.Text. A user typing in the mask IS an edit — the TextChanged
            // handlers above still run; only the echo back into the mask is suppressed.
            _chrome.KeyMask.PasswordChanged += (_, _) =>
            {
                if (_suppressKeySync) return;
                _suppressKeySync = true;
                try
                {
                    if (!string.Equals(_apiKey.Text ?? string.Empty, _chrome.KeyMask.Password, StringComparison.Ordinal))
                        _apiKey.Text = _chrome.KeyMask.Password;
                }
                finally { _suppressKeySync = false; }
            };
            _apiKey.TextChanged += (_, _) =>
            {
                if (_suppressKeySync) return;
                _suppressKeySync = true;
                try
                {
                    var text = _apiKey.Text ?? string.Empty;
                    if (!string.Equals(_chrome.KeyMask.Password, text, StringComparison.Ordinal))
                        _chrome.KeyMask.Password = text;
                }
                finally { _suppressKeySync = false; }
            };
            _chrome.ShowKeyButton.Click += (_, _) => SetKeyShown(_apiKey.Visibility != Visibility.Visible);
            _chrome.KeyHelpLink.Click += (_, _) => SetKeyHelpOpen(_chrome.KeyHelp.Visibility != Visibility.Visible);

            // US2: the list drives the editor. Selection change is the FIRST of the
            // CommitEditorToSelectedAgent moments (research R13; the test flow is the fourth).
            _listView.List.SelectionChanged += OnAgentSelectionChanged;
            _listView.AddButton.Click += (_, _) => ShowRefusal(TryAddAgent());
            _listView.DuplicateButton.Click += (_, _) => ShowRefusal(TryDuplicateSelectedAgent());
            _listView.SetActiveButton.Click += (_, _) => ShowRefusal(TrySetActiveSelectedAgent());
            _listView.RemoveButton.Click += OnRemoveClick;
            _name.LostFocus += (_, _) => ValidateNameOnFocusLoss();

            // US4 (T074/T075): assignments write straight through to the working copy (an id,
            // never a name); the fallback editor mutates the ordered working-copy list.
            foreach (var (_, _, feature) in AiAssistancePage.AssignmentFeatures)
            {
                var combo = _featureRows.ComboFor(feature);
                combo.SelectionChanged += (_, _) => OnAssignmentChanged(feature, combo);
            }
            _featureRows.FallbackAdd.Click += (_, _) => OnFallbackAdd();
            _featureRows.FallbackRemove.Click += (_, _) => OnFallbackRemove();
            _featureRows.FallbackMoveUp.Click += (_, _) => OnFallbackMove(-1);
            _featureRows.FallbackMoveDown.Click += (_, _) => OnFallbackMove(+1);
        }

        private static void ShowRefusal(string? message)
        {
            if (message != null)
                MessageBox.Show(message, Constants.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>Swaps the key's masked face for the plain TextBox (and back).</summary>
        private void SetKeyShown(bool shown)
        {
            _apiKey.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            _chrome.KeyMask.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            _chrome.ShowKeyButton.Content = shown ? "Hide" : "Show";
            System.Windows.Automation.AutomationProperties.SetName(_chrome.ShowKeyButton,
                shown ? "Hide the API key" : "Show the API key");
        }

        private void SetKeyHelpOpen(bool open)
        {
            _chrome.KeyHelp.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _chrome.KeyHelpLink.Content = open ? "Hide help" : "Where do I get a key?";
        }

        /// <summary>
        /// The bits of the Agents tab that follow the selected agent but are not editor fields:
        /// the "active agent" chip and the idle Test-connection line, which shows the agent's
        /// last recorded check while no outcome from this session is on the line.
        /// </summary>
        private void RefreshAgentChrome()
        {
            var agent = SelectedAgent();
            _chrome.ActiveChip.Visibility =
                agent != null && string.Equals(agent.Id, _activeId, StringComparison.Ordinal)
                    ? Visibility.Visible : Visibility.Collapsed;

            var hasOutcome = !string.IsNullOrEmpty(_testResult.Text);
            _testResult.Visibility = hasOutcome ? Visibility.Visible : Visibility.Collapsed;
            _chrome.TestIdle.Visibility = hasOutcome ? Visibility.Collapsed : Visibility.Visible;
            if (hasOutcome) return;

            var health = agent?.Health;
            if (agent == null || health == null || string.IsNullOrEmpty(health.CheckedUtc) ||
                string.Equals(health.Status, AgentHealthStatus.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                _chrome.TestIdle.Text = "Not tested yet.";
                _chrome.TestIdle.Foreground = _testIdleBrush;
                return;
            }
            var latency = health.LatencyMs > 0 ? $" ({health.LatencyMs.ToString(CultureInfo.InvariantCulture)} ms)" : string.Empty;
            _chrome.TestIdle.Text = "Last check: " + AiAgentListView.HealthBadgeTextFor(agent) + latency;
            _chrome.TestIdle.Foreground = AiAgentListView.BadgeBrushFor(health.Status, _chrome.Dark);
        }

        /// <summary>The shared request parameters as the Privacy &amp; data tab shows them.</summary>
        private (int MaxTokens, int Temperature10, int Timeout, int Retries) ReadSharedParams()
            => (RowFactory.GetNumber(_maxTokens), (int)_temperature.Value, RowFactory.GetNumber(_timeout), (int)_retries.Value);

        /// <summary>
        /// The request parameters are ONE set for every agent (the user's 2026-10-13 decision):
        /// Save writes them to each working-copy agent, so the per-agent fields in the config
        /// and the engine's per-agent resolution are untouched.
        /// </summary>
        private void ApplySharedParamsToAgents()
        {
            var (maxTokens, temperature10, timeout, retries) = ReadSharedParams();
            foreach (var agent in _agents)
            {
                agent.MaxTokens = maxTokens;
                agent.Temperature = temperature10 / 10.0;
                agent.Timeout = timeout;
                agent.Retries = retries;
            }
        }

        private void OnRemoveClick(object? sender, RoutedEventArgs e)
        {
            if (SelectedAgent() == null) return;
            var text = BuildRemoveConfirmationText();
            var confirmed = RemoveConfirmation?.Invoke(text)
                ?? MessageBox.Show(text, Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (confirmed) TryRemoveSelectedAgent();
        }

        private void OnAgentSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressListEvents) return;
            var id = (_listView.List.SelectedItem as ListBoxItem)?.Tag as string;
            if (id == null || id == _selectedId) return;
            CommitEditorToSelectedAgent(); // FR-031 — unsaved edits survive the switch
            var agent = FindAgent(id);
            if (agent == null) return;
            _selectedId = id;
            BindEditorTo(agent);
            RenderList(); // the committed agent's row (name, marker, badge) may have changed
        }

        /// <summary>Selects an agent, binds the editor to it and refreshes the list.</summary>
        private void SelectAgent(AiAgent agent)
        {
            _selectedId = agent.Id;
            BindEditorTo(agent);
            RenderList();
        }

        private void RenderList()
        {
            ApplyLocalHealthState();
            _suppressListEvents = true;
            try { _listView.Render(_agents, _activeId, _selectedId); }
            finally { _suppressListEvents = false; }
            RebuildFeatureRows();
            RefreshAgentChrome();
        }

        /// <summary>
        /// FR-055 (US5, T083): <c>needsKey</c> is computed LOCALLY — the provider requires a key
        /// and the agent has none, or its stored key will not decrypt on this machine (V23) —
        /// with no request, so the badge is right the moment the list renders, even with 20
        /// agents. Forced from ANY state while the condition holds (a keyless cloud agent needs
        /// a key whatever a stale test once said), and cleared back to <c>unknown</c> the moment
        /// it stops holding (a key was supplied, or the provider switched to a local one). Runs
        /// at the head of every render so every repaint path re-derives it.
        /// </summary>
        private void ApplyLocalHealthState()
        {
            foreach (var agent in _agents)
            {
                if (agent == null) continue;
                var needsKey = AiAgentResolver.RequiresApiKey(agent.Provider ?? string.Empty) &&
                               (string.IsNullOrEmpty(agent.ApiKey) || agent.KeyDecryptFailed);
                if (needsKey)
                {
                    agent.Health ??= new AgentHealth();
                    agent.Health.Status = AgentHealthStatus.NeedsKey;
                }
                else if (string.Equals(agent.Health?.Status, AgentHealthStatus.NeedsKey,
                             StringComparison.OrdinalIgnoreCase))
                {
                    agent.Health!.Status = AgentHealthStatus.Unknown;
                }
            }
        }

        // ── US4: feature assignments + fallback order (T074/T075) ──────────

        /// <summary>
        /// Rebuilds the assignment dropdowns and the fallback editor from the working copy.
        /// Called from <see cref="RenderList"/> so adds, removes and renames propagate; the
        /// write-through handlers keep <see cref="_assignments"/> and <see cref="_fallback"/>
        /// the truth, so a rebuild never loses a selection. A dangling assignment or fallback
        /// entry (a hand-edited config; full V16/V17 repair is US6) is dropped here so the
        /// working copy never persists what the UI shows as "Use active agent".
        /// </summary>
        private void RebuildFeatureRows()
        {
            _suppressFeatureEvents = true;
            try
            {
                _assignmentIds.Clear();
                _assignmentIds.Add(string.Empty);
                foreach (var agent in _agents) _assignmentIds.Add(agent.Id);

                foreach (var (_, _, feature) in AiAssistancePage.AssignmentFeatures)
                {
                    var combo = _featureRows.ComboFor(feature);
                    combo.Items.Clear();
                    combo.Items.Add(AiAssistancePage.UseActiveAgentDisplay);
                    foreach (var agent in _agents) combo.Items.Add(agent.Name ?? string.Empty);

                    var assignedId = GetAssignment(feature);
                    var index = _assignmentIds.IndexOf(assignedId);
                    if (!string.IsNullOrEmpty(assignedId) && index < 0)
                    {
                        SetAssignment(feature, string.Empty);   // dangling — see the summary above
                        index = 0;
                    }
                    combo.SelectedIndex = Math.Max(index, 0);
                }

                _fallback.RemoveAll(id => FindAgent(id) == null);
                _featureRows.FallbackList.Items.Clear();
                foreach (var id in _fallback)
                {
                    var agent = FindAgent(id)!;
                    var item = new ListBoxItem { Content = agent.Name ?? string.Empty, Tag = id };
                    System.Windows.Automation.AutomationProperties.SetName(
                        item, "Fallback " + (agent.Name ?? string.Empty));
                    _featureRows.FallbackList.Items.Add(item);
                }

                var candidate = _featureRows.FallbackCandidate;
                candidate.Items.Clear();
                // Defect 11 fix: the candidate ids are filled in lockstep with the items —
                // including the skip of agents already in _fallback — so the combo's
                // SelectedIndex identifies the agent exactly (names are neither unique nor
                // necessarily non-empty), the way _assignmentIds backs the assignment combos.
                _fallbackCandidateIds.Clear();
                foreach (var agent in _agents)
                {
                    if (_fallback.Contains(agent.Id)) continue;
                    candidate.Items.Add(agent.Name ?? string.Empty);
                    _fallbackCandidateIds.Add(agent.Id);
                }
                if (candidate.Items.Count > 0) candidate.SelectedIndex = 0;
            }
            finally { _suppressFeatureEvents = false; }
        }

        private void OnAssignmentChanged(AiFeature feature, ComboBox combo)
        {
            if (_suppressFeatureEvents) return;
            var index = combo.SelectedIndex;
            SetAssignment(feature,
                index > 0 && index < _assignmentIds.Count ? _assignmentIds[index] : string.Empty);
        }

        private void OnFallbackAdd()
        {
            // Defect 11 fix: resolve the pick by INDEX against _fallbackCandidateIds, never by
            // name — two agents may carry one name within a session (the name box only renders
            // an inline error, it does not revert or block), which sent the FIRST match into the
            // order while the UI showed the second; and an agent that has no name yet was not
            // addable at all because the name guard swallowed the click silently.
            var index = _featureRows.FallbackCandidate.SelectedIndex;
            if (index < 0 || index >= _fallbackCandidateIds.Count) return;
            var agent = FindAgent(_fallbackCandidateIds[index]);
            if (agent == null || _fallback.Contains(agent.Id)) return;
            _fallback.Add(agent.Id);
            RebuildFeatureRows();
            _featureRows.FallbackList.SelectedIndex = _featureRows.FallbackList.Items.Count - 1;
        }

        private void OnFallbackRemove()
        {
            var index = _featureRows.FallbackList.SelectedIndex;
            if (index < 0 || index >= _fallback.Count) return;
            _fallback.RemoveAt(index);
            RebuildFeatureRows();
            if (_featureRows.FallbackList.Items.Count > 0)
                _featureRows.FallbackList.SelectedIndex =
                    Math.Min(index, _featureRows.FallbackList.Items.Count - 1);
        }

        private void OnFallbackMove(int delta)
        {
            var index = _featureRows.FallbackList.SelectedIndex;
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _fallback.Count) return;
            (_fallback[index], _fallback[target]) = (_fallback[target], _fallback[index]);
            RebuildFeatureRows();
            _featureRows.FallbackList.SelectedIndex = target;
        }

        private string GetAssignment(AiFeature feature) => feature switch
        {
            AiFeature.Chat => _assignments.Chat,
            AiFeature.TextToSql => _assignments.TextToSql,
            AiFeature.Explain => _assignments.Explain,
            AiFeature.Fix => _assignments.Fix,
            AiFeature.Optimize => _assignments.Optimize,
            AiFeature.IndexSuggestions => _assignments.IndexSuggestions,
            AiFeature.GhostText => _assignments.GhostText,
            _ => string.Empty,
        };

        private void SetAssignment(AiFeature feature, string id)
        {
            switch (feature)
            {
                case AiFeature.Chat: _assignments.Chat = id; break;
                case AiFeature.TextToSql: _assignments.TextToSql = id; break;
                case AiFeature.Explain: _assignments.Explain = id; break;
                case AiFeature.Fix: _assignments.Fix = id; break;
                case AiFeature.Optimize: _assignments.Optimize = id; break;
                case AiFeature.IndexSuggestions: _assignments.IndexSuggestions = id; break;
                case AiFeature.GhostText: _assignments.GhostText = id; break;
            }
        }

        /// <summary>
        /// FR-032 (US2 scenario 5): the name rules V1–V3 fire on focus loss so the user learns
        /// immediately rather than at OK. Same rules and wordings as
        /// <see cref="AiAgentResolver.Validate"/>, scoped to the name so leaving the field does
        /// not lecture about provider/model the user has not reached yet.
        /// </summary>
        private void ValidateNameOnFocusLoss()
        {
            var agent = SelectedAgent();
            if (agent == null) return;
            var name = (_name.Text ?? string.Empty).Trim();
            string? error = null;
            if (name.Length == 0)
            {
                error = "Name is required.";
            }
            else if (name.Length > 40)
            {
                error = "Name must be 40 characters or fewer.";
            }
            else
            {
                foreach (var other in _agents)
                {
                    if (ReferenceEquals(other, agent)) continue;
                    if (string.Equals((other.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Another agent is already named \"{name}\".";
                        break;
                    }
                }
            }
            _nameError.Text = error ?? string.Empty;
            _nameError.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Load(AppSettings settings)
        {
            var ai = settings.Ai;

            // R13: deep-copy the agent list into the working copy. Cancel is correct for free —
            // the host only calls Save on OK, so an abandoned copy is an abandoned edit.
            _agents = DeepCopyAgents(ai.Agents);
            // Defect 9 fix: a fresh working copy is a fresh session — nothing is touched yet
            // (Reset-all reloads through here too).
            _touchedAgentIds.Clear();
            _activeId = ai.ActiveAgentId ?? string.Empty;
            _assignments = CopyAssignments(ai.FeatureAgents);
            _fallback = new List<string>(ai.FallbackOrder ?? new List<string>());

            if (_agents.Count == 0)
            {
                // No agent list yet: a fresh install, or a hand-built/pre-migration config whose
                // flat fields V14 turns into an agent on the next ConfigManager.Load. The editor
                // needs one agent to bind to. FR-017's implicit Add (InitialAgentId == "") wants
                // it blank; anything else keeps the flat values so nothing the user had is lost.
                var seed = new AiAgent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = AiAgentResolver.SuggestName(_agents),
                    Enabled = true,
                    CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                };
                if (InitialAgentId != string.Empty)
                {
                    seed.Provider = AiProviderIds.Normalize(ai.Provider);
                    seed.Model = ai.Model ?? string.Empty;
                    seed.ApiKey = ai.ApiKey ?? string.Empty;
                    seed.Endpoint = ai.Endpoint ?? string.Empty;
                    seed.MaxTokens = ai.MaxTokens;
                    seed.Temperature = ai.Temperature;
                    seed.Timeout = ai.Timeout;
                    seed.Retries = ai.Retries;
                }
                _agents.Add(seed);
                _activeId = seed.Id;
            }

            // FR-053 (V23, review): derive the decrypt-failure flag for EVERY agent now, not
            // only for the one BindEditorTo happens to select — ApplyLocalHealthState reads the
            // flag for the needsKey badge, and an agent the user never selects must not show a
            // stale badge. A local DPAPI attempt per agent (≤ 20, no RPC), in the same try/catch
            // idiom the bind path uses. Runs before any bind: the TextChanged ordering contract
            // (flag set AFTER the key text assignment) belongs to BindEditorTo alone.
            foreach (var agent in _agents)
            {
                var (_, decrypted) = UnwrapKeyForDisplay(agent.ApiKey);
                agent.KeyDecryptFailed = !decrypted;
            }

            // Selection: the deep-linked agent when one was named, else the active agent, else
            // the first. A dangling active id follows the selection (full V13 repair is US6).
            AiAgent? selected = null;
            if (!string.IsNullOrEmpty(InitialAgentId))
            {
                foreach (var agent in _agents)
                {
                    if (string.Equals(agent.Id, InitialAgentId, StringComparison.Ordinal))
                    {
                        selected = agent;
                        break;
                    }
                }
            }
            if (selected == null)
            {
                foreach (var agent in _agents)
                {
                    if (string.Equals(agent.Id, _activeId, StringComparison.Ordinal))
                    {
                        selected = agent;
                        break;
                    }
                }
            }
            selected ??= _agents[0];
            if (_agents.TrueForAll(a => !string.Equals(a.Id, _activeId, StringComparison.Ordinal)))
                _activeId = selected.Id;

            SelectAgent(selected);

            // The request parameters are one set for every agent (2026-10 redesign): shown from
            // the active agent (the source of the flat mirror), or from the flat fields when the
            // active id matches no agent.
            var paramSource = FindAgent(_activeId);
            var maxTokens = paramSource?.MaxTokens ?? ai.MaxTokens;
            var temperature10 = (int)((paramSource?.Temperature ?? ai.Temperature) * 10);
            var timeout = paramSource?.Timeout ?? ai.Timeout;
            var retries = paramSource?.Retries ?? ai.Retries;
            RowFactory.SetNumber(_maxTokens, maxTokens);
            _temperature.Value = temperature10;
            _temperatureLabel.Text = temperature10.ToString(CultureInfo.InvariantCulture);
            RowFactory.SetNumber(_timeout, timeout);
            _retries.Value = retries;
            _retriesLabel.Text = retries.ToString(CultureInfo.InvariantCulture);
            _loadedParams = ReadSharedParams();

            // Global concerns below come from the settings, never from the agent (FR-033).
            _privacy.SelectedIndex = (ai.PrivacyMode?.ToLowerInvariant()) switch
            {
                "full"      => 1,
                "anonymous" => 2,
                "offline"   => 3,
                "disabled"  => 4,
                _           => 0, // schemaOnly
            };
            // Stored as "consent required?"; the checkbox shows "consent granted?" (the inverse).
            _cloudConsent.IsChecked = !ai.PrivacyConsentRequired;

            _textToSql.IsChecked = ai.TextToSql;
            _explain.IsChecked = ai.Explain;
            _fix.IsChecked = ai.Fix;
            _optimize.IsChecked = ai.Optimize;
            _indexSuggestions.IsChecked = ai.IndexSuggestions;
            _inlineCompletion.IsChecked = ai.InlineCompletion;
            _autoFixOnError.IsChecked = ai.AutoFixOnError;
        }

        /// <summary>
        /// Binds the editor rows to one working-copy agent. Preserves the PR #251 ordering
        /// contract (R11), per agent: the key text is assigned BEFORE the decrypt-failure flag
        /// is set, because the assignment fires TextChanged, which clears the flag on the
        /// selected agent. Callers set <see cref="_selectedId"/> first so that handler lands
        /// on the agent being bound.
        /// </summary>
        private void BindEditorTo(AiAgent agent)
        {
            // Replaying the stored values into the boxes fires the edit handlers — a bind is
            // not an edit, so the health reset (T086) is suppressed for the body.
            _suppressHealthReset = true;
            try
            {
                _name.Text = agent.Name ?? string.Empty;
                _nameError.Visibility = Visibility.Collapsed;
                _agentEnabled.IsChecked = agent.Enabled;
                // Normalise BEFORE matching (FR-013): configs written by earlier builds ("AzureOpenAI",
                // "LMStudio") resolve to their canonical ids and select correctly with no migration.
                var providerId = AiProviderIds.Normalize(agent.Provider);
                var providerIndex = Array.FindIndex(AiAssistancePage.Providers, p => p.Id == providerId);
                _provider.SelectedIndex = providerIndex >= 0 ? providerIndex : 0;
                _model.Text = agent.Model ?? string.Empty;
                var (keyDisplay, keyDecrypted) = UnwrapKeyForDisplay(agent.ApiKey);
                _apiKey.Text = keyDisplay;
                // AFTER the text assignment (which fires TextChanged and clears the flag): an empty
                // field here means the stored value failed to decrypt, not that the user cleared it.
                agent.KeyDecryptFailed = !keyDecrypted;
                _keyNotice.Visibility = agent.KeyDecryptFailed ? Visibility.Visible : Visibility.Collapsed;
                _endpoint.Text = agent.Endpoint ?? string.Empty;
                // The request parameters are shared by every agent (Privacy & data tab), not bound here.
                _testResult.Text = string.Empty;   // an outcome line belongs to the agent that was tested
            }
            finally { _suppressHealthReset = false; }
        }

        /// <summary>Blanks the editor when the list holds no agent at all (the last one was removed).</summary>
        private void ClearEditor()
        {
            _suppressHealthReset = true;
            try
            {
                _name.Text = string.Empty;
                _nameError.Visibility = Visibility.Collapsed;
                _agentEnabled.IsChecked = true;   // a fresh agent starts enabled (AiAgent default)
                _provider.SelectedIndex = 0;
                _model.Text = string.Empty;
                _apiKey.Text = string.Empty;
                _keyNotice.Visibility = Visibility.Collapsed;
                _endpoint.Text = string.Empty;
                _testResult.Text = string.Empty;
            }
            finally { _suppressHealthReset = false; }
        }

        /// <summary>
        /// Writes the visible editor fields into the selected working-copy agent. Called at
        /// exactly four moments: the three of research R13 — on selection change, before any
        /// CRUD action, and in Save — plus before a Test connection (US5, FR-054: the working
        /// copy must hold the values being tested). (<see cref="ValidateWorkingCopy"/> commits
        /// as the Save moment arriving early — OK is validate-then-Save.)
        /// </summary>
        private void CommitEditorToSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return;

            // Defect 9 fix: a commit that writes something other than what BindEditorTo put in
            // the boxes is the user having edited THIS agent — the signal the dialog's gate
            // (ValidateTouchedAgents) scopes itself to. Computed before the write, while the
            // agent still holds its pre-edit values.
            if (!EditorMatchesBoundAgent(agent)) _touchedAgentIds.Add(agent.Id);

            agent.Name = _name.Text ?? string.Empty;
            agent.Enabled = _agentEnabled.IsChecked == true;
            // Key off the canonical id, never the index (FR-013) — the factory rejects anything else.
            var index = _provider.SelectedIndex;
            agent.Provider = index > 0 ? AiAssistancePage.Providers[index].Id : string.Empty;
            agent.Model = _model.Text ?? string.Empty;
            // FR-008: keys are DPAPI-wrapped at rest. An already-wrapped value is never re-wrapped.
            var keyText = _apiKey.Text ?? string.Empty;
            if (!(agent.KeyDecryptFailed && string.IsNullOrEmpty(keyText)))
            {
                agent.ApiKey = ApiKeyProtector.IsProtected(keyText) ? keyText : ApiKeyProtector.Protect(keyText);
            }
            // else (review finding 2): the field is empty because decryption failed — never let a
            // Save the user never touched the key in overwrite a non-empty stored key with "".
            agent.Endpoint = _endpoint.Text ?? string.Empty;
            // The request parameters are shared: Save applies them to every agent at once.
        }

        /// <summary>
        /// Defect 9 fix: true when every editor row still holds exactly what
        /// <see cref="BindEditorTo"/> projected from this agent — i.e. nothing has been edited
        /// since the bind. Deliberately compares the BOUND projection rather than the raw stored
        /// values: the provider falls back to "(None)" when a stored id does not match, and the
        /// key is shown unwrapped (DPAPI ciphertext never round-trips byte for byte), so a raw
        /// comparison would report a phantom edit on an agent the user never opened.
        /// </summary>
        private bool EditorMatchesBoundAgent(AiAgent agent)
        {
            var providerId = AiProviderIds.Normalize(agent.Provider);
            var providerIndex = Array.FindIndex(AiAssistancePage.Providers, p => p.Id == providerId);
            if (providerIndex < 0) providerIndex = 0;
            var (keyDisplay, _) = UnwrapKeyForDisplay(agent.ApiKey);

            return string.Equals(_name.Text ?? string.Empty, agent.Name ?? string.Empty, StringComparison.Ordinal)
                && (_agentEnabled.IsChecked == true) == agent.Enabled
                && _provider.SelectedIndex == providerIndex
                && string.Equals(_model.Text ?? string.Empty, agent.Model ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(_apiKey.Text ?? string.Empty, keyDisplay, StringComparison.Ordinal)
                && string.Equals(_endpoint.Text ?? string.Empty, agent.Endpoint ?? string.Empty, StringComparison.Ordinal);
        }

        private AiAgent? SelectedAgent() => FindAgent(_selectedId);

        private AiAgent? FindAgent(string id)
        {
            foreach (var agent in _agents)
            {
                if (string.Equals(agent.Id, id, StringComparison.Ordinal))
                    return agent;
            }
            return null;
        }

        private static FeatureAgentAssignments CopyAssignments(FeatureAgentAssignments? source)
        {
            return new FeatureAgentAssignments
            {
                Chat = source?.Chat ?? string.Empty,
                TextToSql = source?.TextToSql ?? string.Empty,
                Explain = source?.Explain ?? string.Empty,
                Fix = source?.Fix ?? string.Empty,
                Optimize = source?.Optimize ?? string.Empty,
                IndexSuggestions = source?.IndexSuggestions ?? string.Empty,
                GhostText = source?.GhostText ?? string.Empty,
            };
        }

        private static AiAgent CopyAgent(AiAgent agent)
        {
            return new AiAgent
            {
                Id = agent.Id,
                Name = agent.Name,
                Provider = agent.Provider,
                Model = agent.Model,
                ApiKey = agent.ApiKey,
                Endpoint = agent.Endpoint,
                MaxTokens = agent.MaxTokens,
                Temperature = agent.Temperature,
                Timeout = agent.Timeout,
                Retries = agent.Retries,
                Enabled = agent.Enabled,
                CreatedUtc = agent.CreatedUtc,
                Health = agent.Health == null
                    ? null
                    : new AgentHealth
                    {
                        Status = agent.Health.Status,
                        CheckedUtc = agent.Health.CheckedUtc,
                        LatencyMs = agent.Health.LatencyMs,
                        Message = agent.Health.Message,
                    },
            };
        }

        private static List<AiAgent> DeepCopyAgents(List<AiAgent>? agents)
        {
            var copy = new List<AiAgent>();
            if (agents == null) return copy;
            foreach (var agent in agents)
            {
                if (agent == null) continue;
                copy.Add(CopyAgent(agent));
            }
            return copy;
        }

        public void Save(AppSettings settings)
        {
            CommitEditorToSelectedAgent(); // the third of the three commit moments (R13)
            var paramsEdited = ReadSharedParams() != _loadedParams;
            DropStillBlankAgents(paramsEdited);
            ApplySharedParamsToAgents();

            // The working copy is written back wholesale — as fresh lists and fresh agent
            // objects, never the page's own: the dialog can stay open after an Apply, and an
            // edit made past that point must not mutate the settings object the host already
            // saved through the aliased reference.
            settings.Ai.Agents = DeepCopyAgents(_agents);
            settings.Ai.ActiveAgentId = _activeId;
            settings.Ai.FeatureAgents = CopyAssignments(_assignments);
            settings.Ai.FallbackOrder = new List<string>(_fallback);

            // The flat fields mirror the ACTIVE working-copy agent — the editor may be showing a
            // different (selected) agent — so every pre-agents consumer, and an older build
            // reading this config after a downgrade, sees the values the active agent carries.
            // This mirror deliberately ignores usability (the dialog persists what the user
            // configured); ConfigManager.Save re-asserts V18's usability-gated mirror on disk.
            var active = FindAgent(_activeId);
            if (active != null)
            {
                settings.Ai.Provider = active.Provider ?? string.Empty;
                settings.Ai.Model = active.Model ?? string.Empty;
                settings.Ai.ApiKey = active.ApiKey ?? string.Empty;
                settings.Ai.Endpoint = active.Endpoint ?? string.Empty;
            }
            else if (_agents.Count == 0)
            {
                // The user removed every agent: blank the flat connection fields so the next
                // load's V14 migration cannot resurrect a deliberately deleted configuration.
                settings.Ai.Provider = settings.Ai.Model = settings.Ai.ApiKey = settings.Ai.Endpoint = string.Empty;
            }

            // The shared request parameters mirror as shown, with or without an active agent.
            var (sharedMaxTokens, sharedTemperature10, sharedTimeout, sharedRetries) = ReadSharedParams();
            settings.Ai.MaxTokens = sharedMaxTokens;
            settings.Ai.Temperature = sharedTemperature10 / 10.0;
            settings.Ai.Timeout = sharedTimeout;
            settings.Ai.Retries = sharedRetries;

            settings.Ai.PrivacyMode = _privacy.SelectedIndex switch
            {
                1 => "full",
                2 => "anonymous",
                3 => "offline",
                4 => "disabled",
                _ => "schemaOnly",
            };
            // Unchecked → consent withheld → the engine keeps requiring it (privacy-first default).
            settings.Ai.PrivacyConsentRequired = _cloudConsent.IsChecked != true;
            // V19 on the write path: "AI enabled" is a property of the whole list — at least one
            // usable agent — never of whichever agent happens to be selected in the editor.
            settings.Ai.Enabled = _agents.Any(AiAgentResolver.IsUsable);
            settings.Ai.TextToSql = _textToSql.IsChecked == true;
            settings.Ai.Explain = _explain.IsChecked == true;
            settings.Ai.Fix = _fix.IsChecked == true;
            settings.Ai.Optimize = _optimize.IsChecked == true;
            settings.Ai.IndexSuggestions = _indexSuggestions.IsChecked == true;
            settings.Ai.InlineCompletion = _inlineCompletion.IsChecked == true;
            settings.Ai.AutoFixOnError = _autoFixOnError.IsChecked == true;
        }

        /// <summary>
        /// Reads accept legacy plaintext for free (<see cref="ApiKeyProtector.Unprotect"/> passes
        /// unprefixed values through). A wrapped blob this Windows user cannot decrypt (a roamed
        /// profile, a restored backup, a different machine) returns <c>Decrypted = false</c> with
        /// an empty display value — the caller surfaces a notice and never lets a Save blank the
        /// stored key (review finding 2). Nothing to decrypt (null/empty) is not a failure.
        /// </summary>
        private static (string Display, bool Decrypted) UnwrapKeyForDisplay(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return (string.Empty, true);
            if (!ApiKeyProtector.IsProtected(stored)) return (stored!, true); // legacy plaintext
            try
            {
                return (ApiKeyProtector.Unprotect(stored), true);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                Log.Warning(ex, "AiAssistancePage: stored API key could not be decrypted; user must re-enter it");
                return (string.Empty, false);
            }
        }

        /// <summary>
        /// FR-009/FR-054: sends the CURRENT dialog values to the engine's AiProviderTest handler
        /// — the user can verify a key before committing it. Busy state, never blocks the UI
        /// thread, re-enables in a finally; the key is never logged. US5: the outcome is also
        /// written to the tested working-copy agent's <see cref="AiAgent.Health"/> (T085) and
        /// the two locally determinable refusals (T087) fire before anything is sent. Internal
        /// so the shell tests can await the flow directly; the button click awaits it too.
        /// </summary>
        internal async System.Threading.Tasks.Task RunProviderTestAsync()
        {
            // What is tested is what is in the boxes (FR-054) — commit first so the working
            // copy (and the needsKey sweep after the outcome) sees exactly the values tested.
            CommitEditorToSelectedAgent();
            var testedAgent = SelectedAgent();

            // T087/FR-056: the two causes the page can determine LOCALLY, before anything
            // leaves the machine. A refusal is a status-line outcome only: no request, and
            // Health is untouched because nothing was checked.
            var refusal = LocalTestRefusal();
            if (refusal != null)
            {
                _testResult.Foreground = _failureBrush;
                _testResult.Text = refusal;
                RefreshAgentChrome();
                return;
            }

            var request = AiProviderTestRunner.BuildRequest(
                _provider.SelectedItem as string, _model.Text, _apiKey.Text, _endpoint.Text);

            // The wait budget follows the dialog's CURRENT timeout, not the saved config.
            var waitSettings = new AppSettings();
            waitSettings.Ai.Timeout = RowFactory.GetNumber(_timeout);

            _testButton.IsEnabled = false;
            _testButton.Content = "Testing…";
            _testResult.Foreground = _testIdleBrush;
            _testResult.Text = "Testing the connection…";
            RefreshAgentChrome();
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (success, message) = await AiProviderTestRunner.RunAsync(
                    TestRpcAccessor ?? EngineRpcClientAccessor.Instance, request, waitSettings);
                sw.Stop();
                _testResult.Text = message;
                _testResult.Foreground = success ? _successBrush : _failureBrush;
                RecordHealth(testedAgent, success, message, (int)sw.ElapsedMilliseconds);
                RenderList();   // the tested agent's badge moves with its Health
            }
            finally
            {
                _testButton.IsEnabled = true;
                _testButton.Content = "Test connection";
            }
        }

        /// <summary>
        /// T087/FR-056: the two failure causes the page can determine without sending anything —
        /// a missing endpoint for a provider that requires one (V9), and a first-party model
        /// whose detected family contradicts the provider (V7, in the resolver's own wording).
        /// Null when nothing locally refuses; the "(None)" selection is left to the runner's
        /// own refusal.
        /// </summary>
        private string? LocalTestRefusal()
        {
            var index = _provider.SelectedIndex;
            if (index <= 0) return null;
            var providerId = AiAssistancePage.Providers[index].Id;

            var endpoint = (_endpoint.Text ?? string.Empty).Trim();
            if (AiAgentResolver.RequiresEndpoint(providerId) && endpoint.Length == 0)
            {
                return $"Endpoint is required for {AiAssistancePage.Providers[index].Display} — " +
                       "fill in the Endpoint URL before testing.";
            }

            if (providerId == AiProviderIds.Anthropic || providerId == AiProviderIds.OpenAI ||
                providerId == AiProviderIds.Gemini || providerId == AiProviderIds.Kimi)
            {
                var model = (_model.Text ?? string.Empty).Trim();
                var family = AiModelFamily.Detect(model);
                if (family != null && family != providerId)
                    return $"Model \"{model}\" is a {family} model, not a {providerId} model.";
            }

            return null;
        }

        /// <summary>
        /// FR-053 (US5, T085): the test outcome lands on the TESTED working-copy agent — the
        /// status, the check time, and the user-facing message capped at 500 chars (data-model
        /// E2; never built from the key — V24). <see cref="AgentHealth.LatencyMs"/> records the
        /// round trip of the last SUCCESSFUL check: a failed check writes 0 (E2 — "0 when the
        /// last check did not succeed"), never leaves an earlier success's number standing next
        /// to a failure.
        /// </summary>
        private static void RecordHealth(AiAgent? agent, bool success, string message, int latencyMs)
        {
            if (agent == null) return;
            agent.Health ??= new AgentHealth();
            agent.Health.Status = success ? AgentHealthStatus.Ready : AgentHealthStatus.Failed;
            agent.Health.CheckedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            agent.Health.LatencyMs = success ? latencyMs : 0;
            agent.Health.Message = message.Length <= 500 ? message : message.Substring(0, 500);
        }

        /// <summary>
        /// R10 (US5, T086): a provider/model/key/endpoint edit returns the selected agent's
        /// health to <c>unknown</c> from ANY state — only the status resets; the last check
        /// honestly happened, so its time, latency and message stay recorded against it.
        /// </summary>
        private void ResetSelectedAgentHealthOnEdit()
        {
            if (_suppressHealthReset) return;
            var agent = SelectedAgent();
            if (agent?.Health != null &&
                !string.Equals(agent.Health.Status, AgentHealthStatus.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                agent.Health.Status = AgentHealthStatus.Unknown;
            }
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
