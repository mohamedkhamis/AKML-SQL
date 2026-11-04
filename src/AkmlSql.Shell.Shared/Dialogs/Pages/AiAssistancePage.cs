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
            // 2026-11 master–detail redesign (SSMS layout B): a full-width grid of agents with a
            // toolbar, and a summary of the selected agent under it. Add… and Edit… open
            // AiAgentEditorDialog; the dialog's OK puts the change in the working copy and the
            // Options window's OK saves it.
            var agentListView = new AiAgentListView(theme);
            ctx.Rows.AddGroupHeader(agentsTab, "AI agents");
            var indent = ctx.Rows.GroupIndent;
            agentListView.ButtonRow.Margin = new Thickness(indent, 0, 0, 6);
            agentsTab.Children.Add(agentListView.ButtonRow);
            agentListView.GridBlock.Margin = new Thickness(indent, 0, 0, 4);
            agentsTab.Children.Add(agentListView.GridBlock);
            ctx.RegisterSearch("AI agents", "Add, edit, duplicate, delete and activate AI agents", "List", agentListView.List);
            ctx.RegisterSearch("Add AI agent",
                "Connect an AI provider and model: Anthropic, OpenAI, Azure OpenAI, Gemini, Kimi, Ollama, LM Studio or a custom endpoint",
                "Button", agentListView.ButtonRow);
            ctx.RegisterSearch("Edit AI agent",
                "Change an agent's name, provider, model, API key or endpoint URL, or turn it off",
                "Button", agentListView.ButtonRow);
            ctx.RegisterSearch("Test connection", "Verify the selected agent's provider, model, endpoint and key", "Button", agentListView.ButtonRow);

            var activeChip = MakeChip(theme, "active agent");
            var editLink = MakeLinkButton(theme, "Edit…");
            editLink.Margin = new Thickness(8, 0, 0, 0);
            ctx.Rows.AddGroupHeader(agentsTab, "Selected agent", afterLabel: activeChip, trailing: editLink);

            var summaryTitle = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = theme.FgPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var summaryDetail = new TextBlock { FontSize = 12, Foreground = theme.FgSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            var summaryUse = new TextBlock { FontSize = 12, Foreground = theme.FgSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            var testIdle = new TextBlock { FontSize = 12, Foreground = theme.FgSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            var testResult = new TextBlock
            {
                FontSize = 12,
                Foreground = theme.FgSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            System.Windows.Automation.AutomationProperties.SetLiveSetting(testResult, System.Windows.Automation.AutomationLiveSetting.Polite);
            var summaryBody = new StackPanel();
            summaryBody.Children.Add(summaryTitle);
            summaryBody.Children.Add(summaryDetail);
            summaryBody.Children.Add(summaryUse);
            summaryBody.Children.Add(testIdle);
            summaryBody.Children.Add(testResult);
            agentsTab.Children.Add(new Border
            {
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                Background = theme.Main,
                Margin = new Thickness(indent, 0, 0, 12),
                Child = new Border
                {
                    BorderBrush = theme.FgAccent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 8, 12, 9),
                    Child = summaryBody,
                },
            });

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
                Theme = theme,
                ActiveChip = activeChip,
                EditLink = editLink,
                SummaryTitle = summaryTitle,
                SummaryDetail = summaryDetail,
                SummaryUse = summaryUse,
                TestIdle = testIdle,
                Dark = dark,
            };

            return new AiAssistanceControls(cboPrivacy,
                numMax, sldTemp, lblTemp, numTimeout, sldRetries, lblRetries,
                chkTextToSql!, chkExplain!, chkFix!, chkOptimize!, chkIndex!, chkInline!, chkAutoFix!,
                chkConsent, testResult, agentListView, featureRows, chrome);
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
    /// The Agents tab chrome that is not the grid: the tab strip, the "active agent" chip, the
    /// Edit… link and the selected-agent summary (title, details, "used by", the last check).
    /// One bag so the controls constructor grows by one parameter (the
    /// <see cref="FeatureAssignmentRows"/> precedent).
    /// </summary>
    internal sealed class AiEditorChrome
    {
        internal OptionsTabStrip Tabs { get; set; } = null!;
        internal PageTheme Theme { get; set; } = null!;
        internal Border ActiveChip { get; set; } = null!;
        internal Button EditLink { get; set; } = null!;
        internal TextBlock SummaryTitle { get; set; } = null!;
        internal TextBlock SummaryDetail { get; set; } = null!;
        internal TextBlock SummaryUse { get; set; } = null!;
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
        private readonly TextBlock _testResult;      // the page Test's outcome, in the summary
        private readonly Brush _testIdleBrush;
        private readonly AiAgentListView _listView;
        private readonly FeatureAssignmentRows _featureRows;
        private readonly AiEditorChrome _chrome;
        // Semantic colours per theme — the list badges' own brushes (the documented hex exception).
        private readonly SolidColorBrush _successBrush;
        private readonly SolidColorBrush _failureBrush;
        // The request parameters as Load showed them: Save compares against this to tell a
        // still-blank placeholder agent (dropped) from one the user retuned (kept).
        private (int MaxTokens, int Temperature10, int Timeout, int Retries) _loadedParams;
        private readonly List<string> _assignmentIds = new();   // index-aligned with every assignment combo
        // Defect 11 fix: index-aligned with the fallback CANDIDATE combo, which skips agents
        // already in _fallback — the ids must skip them in lockstep or the alignment is wrong.
        private readonly List<string> _fallbackCandidateIds = new();
        private bool _suppressFeatureEvents;

        // Spec 037 (US2, research R13): the page edits a WORKING COPY of the agent list (plus the
        // assignments and fallback order), written back wholesale in Save. Cancel is correct for
        // free — the host only calls Save on OK, so an abandoned copy is an abandoned edit.
        // KeyDecryptFailed lives on each working-copy agent (V23).
        private List<AiAgent> _agents = new();
        private string _activeId = string.Empty;
        private string _selectedId = string.Empty;
        private FeatureAgentAssignments _assignments = new();
        private List<string> _fallback = new();
        private bool _suppressListEvents;

        // Defect 9 fix (FR-032 scope): the ids of the agents this dialog session actually
        // TOUCHED — accepted from the editor with a change, or added/duplicated here. The
        // Options window's OK refuses only on those, so one long-stale half-configured agent
        // cannot block a save made on an entirely different page.
        private readonly HashSet<string> _touchedAgentIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Spec 037 (FR-017): deep-link target handed over by SettingsWindow before Load. An
        /// agent id selects that agent (and <see cref="OpenDeepLinkedEditor"/> opens it for
        /// editing); <c>""</c> with no agent opens Add…; <c>null</c> selects the active agent.
        /// </summary>
        internal string? InitialAgentId { get; set; }

        // ── Seams for the shell test assembly (it compiles these sources in) ─────────────

        internal AiAgentListView ListView => _listView;
        internal IReadOnlyList<AiAgent> WorkingAgents => _agents;
        internal string SelectedAgentId => _selectedId;
        internal string ActiveAgentId => _activeId;
        internal ComboBox AssignmentComboFor(AiFeature feature) => _featureRows.ComboFor(feature);
        internal ListBox FallbackList => _featureRows.FallbackList;
        internal ComboBox FallbackCandidate => _featureRows.FallbackCandidate;
        internal Button FallbackAddButton => _featureRows.FallbackAdd;
        internal Button FallbackRemoveButton => _featureRows.FallbackRemove;
        internal Button FallbackMoveUpButton => _featureRows.FallbackMoveUp;
        internal Button FallbackMoveDownButton => _featureRows.FallbackMoveDown;
        internal OptionsTabStrip Tabs => _chrome.Tabs;
        internal Button EditLink => _chrome.EditLink;
        internal TextBlock SummaryTitle => _chrome.SummaryTitle;
        internal TextBlock SummaryDetail => _chrome.SummaryDetail;
        internal TextBlock SummaryUse => _chrome.SummaryUse;
        internal TextBlock TestResultLine => _testResult;

        /// <summary>
        /// Test seam for the Delete confirmation (FR-028): receives the confirmation text and
        /// returns whether the user confirmed. When null, the handler asks via MessageBox.
        /// </summary>
        internal Func<string, bool>? DeleteConfirmation { get; set; }

        /// <summary>
        /// Test seam for the agent dialog: receives the bound editor and returns true for OK
        /// (after driving the editor's controls, and calling <see cref="AiAgentEditor.TryAccept"/>
        /// when the test wants the dialog's validation). When null, <see cref="AiAgentEditorDialog"/>
        /// is shown modally.
        /// </summary>
        internal Func<AiAgentEditor, bool>? EditorHost { get; set; }

        /// <summary>The last editor this page opened (test diagnostics).</summary>
        internal AiAgentEditor? LastEditor { get; private set; }

        /// <summary>
        /// Test seam (US5, T084): tests send through this accessor instead of the engine's.
        /// Shared with the agent editor through <see cref="AiAgentTestFlow"/>.
        /// </summary>
        internal static IRpcClientAccessor? TestRpcAccessor
        {
            get => AiAgentTestFlow.TestRpcAccessor;
            set => AiAgentTestFlow.TestRpcAccessor = value;
        }

        public AiAssistanceControls(
            ComboBox privacy,
            TextBox maxTokens, Slider sldTemp, TextBlock lblTemp,
            TextBox timeout, Slider sldRetries, TextBlock lblRetries,
            CheckBox textToSql, CheckBox explain, CheckBox fix, CheckBox optimize,
            CheckBox idx, CheckBox inline, CheckBox autoFix,
            CheckBox cloudConsent, TextBlock testResult,
            AiAgentListView listView, FeatureAssignmentRows featureRows, AiEditorChrome chrome)
        {
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
            _testResult = testResult;
            _testIdleBrush = testResult.Foreground;
            _listView = listView;
            _featureRows = featureRows;
            _chrome = chrome;
            _successBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Ready, chrome.Dark);
            _failureBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Failed, chrome.Dark);

            _listView.List.SelectionChanged += OnAgentSelectionChanged;
            _listView.AddButton.Click += (_, _) => ShowRefusal(TryAddAgent());
            _listView.EditButton.Click += (_, _) => ShowRefusal(TryEditSelectedAgent());
            _listView.EditRequested += (_, _) => ShowRefusal(TryEditSelectedAgent());
            _chrome.EditLink.Click += (_, _) => ShowRefusal(TryEditSelectedAgent());
            _listView.DuplicateButton.Click += (_, _) => ShowRefusal(TryDuplicateSelectedAgent());
            _listView.SetActiveButton.Click += (_, _) => ShowRefusal(TrySetActiveSelectedAgent());
            _listView.DeleteButton.Click += (_, _) => OnDeleteRequested();
            _listView.DeleteRequested += (_, _) => OnDeleteRequested();
            _listView.TestButton.Click += async (_, _) => await RunProviderTestAsync();

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

        // ── CRUD ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// FR-026: opens "Add AI agent" with a uniquely named fresh agent. On OK the agent joins
        /// the list and is selected; the first usable agent of an empty or unusable list also
        /// becomes the active one. V12 refuses the 21st with a message naming the limit — a
        /// refusal is returned, not shown, so tests never meet a modal.
        /// </summary>
        internal string? TryAddAgent()
        {
            if (!AiAgentResolver.CanAddAgent(_agents.Count))
                return CeilingRefusal();
            var (maxTokens, temperature10, timeout, retries) = ReadSharedParams();
            var draft = new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = AiAgentResolver.SuggestName(_agents),
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                MaxTokens = maxTokens,
                Temperature = temperature10 / 10.0,
                Timeout = timeout,
                Retries = retries,
            };
            var editor = NewEditor(draft, new List<AiAgent>(_agents), isNew: true);
            if (!ShowEditor(editor)) return null;

            var added = editor.Draft;
            _agents.Add(added);
            _touchedAgentIds.Add(added.Id);
            var active = FindAgent(_activeId);
            if ((active == null || !AiAgentResolver.IsUsable(active)) && AiAgentResolver.IsUsable(added))
                _activeId = added.Id;
            SelectAgent(added);
            return null;
        }

        /// <summary>
        /// Opens "Edit AI agent" on a copy of the selected agent; on OK the copy's values replace
        /// the agent's (its id — and every reference to it — stays).
        /// </summary>
        internal string? TryEditSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return null;
            var siblings = _agents.Where(a => !ReferenceEquals(a, agent)).ToList();
            var editor = NewEditor(CopyAgent(agent), siblings, isNew: false);
            if (!ShowEditor(editor)) return null;

            var edited = editor.Draft;
            if (!SameConfiguration(agent, edited)) _touchedAgentIds.Add(agent.Id);
            agent.Name = edited.Name;
            agent.Enabled = edited.Enabled;
            agent.Provider = edited.Provider;
            agent.Model = edited.Model;
            agent.ApiKey = edited.ApiKey;
            agent.Endpoint = edited.Endpoint;
            agent.KeyDecryptFailed = edited.KeyDecryptFailed;
            agent.Health = edited.Health;
            _testResult.Text = string.Empty;   // an outcome line belongs to the values that were tested
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
            _touchedAgentIds.Add(copy.Id);
            SelectAgent(copy);
            return null;
        }

        /// <summary>
        /// FR-028: delete the selected agent, repair the active id (first usable survivor, else
        /// none), clear assignments naming it (V16), drop fallback entries naming it (V17), then
        /// select the nearest survivor. The confirmation is the handler's job — see
        /// <see cref="BuildDeleteConfirmationText"/>.
        /// </summary>
        internal string? TryDeleteSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return null;
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
                RenderList();
            }
            return null;
        }

        private void OnDeleteRequested()
        {
            if (SelectedAgent() == null) return;
            var text = BuildDeleteConfirmationText();
            var confirmed = DeleteConfirmation?.Invoke(text)
                ?? MessageBox.Show(text, Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            if (confirmed) TryDeleteSelectedAgent();
        }

        /// <summary>FR-028: the confirmation wording, naming the agent and what it answers.</summary>
        internal string BuildDeleteConfirmationText()
        {
            var agent = SelectedAgent();
            var name = (agent?.Name ?? string.Empty).Trim();
            var text = $"Delete the agent \"{(name.Length > 0 ? name : "(unnamed)")}\"?";
            if (agent != null)
            {
                var features = AssignedFeatureLabels(agent.Id);
                if (features.Count > 0)
                    text += " It answers " + string.Join(", ", features) + "; those features will use the active agent instead.";
                if (string.Equals(agent.Id, _activeId, StringComparison.Ordinal))
                    text += " It is the active agent: the first usable agent left becomes active.";
            }
            return text + " Its settings and stored key are removed when you click OK in the Options window.";
        }

        /// <summary>
        /// Mark the selected agent active. Refused, with the reason, when the agent cannot
        /// answer (S1 plus V23 — the same usability the chat empty state uses).
        /// </summary>
        internal string? TrySetActiveSelectedAgent()
        {
            var agent = SelectedAgent();
            if (agent == null) return null;
            if (!AiChatEmptyState.CanAnswer(agent))
                return $"\"{(agent.Name ?? string.Empty).Trim()}\" cannot be made the active agent — {UnusableReason(agent)}";
            _activeId = agent.Id;
            RenderList(); // move the ● marker
            return null;
        }

        /// <summary>
        /// Spec 037 (FR-017): the deep link's editor, opened once the Options window has loaded —
        /// Edit… for the named agent, or Add… when the link carries "" and there is no agent.
        /// Consumed once, so a reload (Reset all) does not reopen it.
        /// </summary>
        internal void OpenDeepLinkedEditor()
        {
            var id = InitialAgentId;
            InitialAgentId = null;
            if (id == null) return;
            _chrome.Tabs.Select(0);
            if (id.Length == 0)
            {
                if (_agents.Count == 0) ShowRefusal(TryAddAgent());
                return;
            }
            var agent = FindAgent(id);
            if (agent == null) return;
            SelectAgent(agent);
            ShowRefusal(TryEditSelectedAgent());
        }

        private AiAgentEditor NewEditor(AiAgent draft, IReadOnlyList<AiAgent> siblings, bool isNew)
        {
            var editor = new AiAgentEditor(_chrome.Theme, draft, siblings, isNew, () => RowFactory.GetNumber(_timeout));
            LastEditor = editor;
            return editor;
        }

        private bool ShowEditor(AiAgentEditor editor)
            => EditorHost?.Invoke(editor) ?? AiAgentEditorDialog.ShowFor(editor, _listView.List);

        private static bool SameConfiguration(AiAgent a, AiAgent b)
            => string.Equals(a.Name ?? string.Empty, b.Name ?? string.Empty, StringComparison.Ordinal)
               && a.Enabled == b.Enabled
               && string.Equals(AiProviderIds.Normalize(a.Provider), AiProviderIds.Normalize(b.Provider), StringComparison.Ordinal)
               && string.Equals(a.Model ?? string.Empty, b.Model ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(a.ApiKey ?? string.Empty, b.ApiKey ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(a.Endpoint ?? string.Empty, b.Endpoint ?? string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// The reference repair every removal path shares (Delete and the Save-time blank-agent
        /// drop): an active id pointing at the removed agent moves to the first usable survivor,
        /// assignments naming it revert to "" (V16), and fallback entries naming it drop out (V17).
        /// </summary>
        private void RepairReferencesToRemovedAgent(AiAgent agent)
        {
            if (string.Equals(_activeId, agent.Id, StringComparison.Ordinal))
                _activeId = FirstUsableAgentId() ?? string.Empty;
            foreach (var (_, _, feature) in AiAssistancePage.AssignmentFeatures)
            {
                if (string.Equals(GetAssignment(feature), agent.Id, StringComparison.Ordinal))
                    SetAssignment(feature, string.Empty);
            }
            _fallback.RemoveAll(id => string.Equals(id, agent.Id, StringComparison.Ordinal));
        }

        /// <summary>
        /// An agent that is STILL completely blank (no provider, model, key or endpoint, default
        /// parameters) is a placeholder, not a configuration: it never persists. Defence in
        /// depth — the dialog refuses to accept one, but a hand-edited config can carry one.
        /// </summary>
        private void DropStillBlankAgents(bool paramsEdited)
        {
            var dropped = false;
            foreach (var agent in _agents.Where(a => IsStillBlankAgent(a, paramsEdited)).ToList())
            {
                _agents.Remove(agent);
                RepairReferencesToRemovedAgent(agent);
                if (string.Equals(_selectedId, agent.Id, StringComparison.Ordinal))
                    _selectedId = _agents.Count > 0 ? _agents[0].Id : string.Empty;
                dropped = true;
            }
            if (dropped) RenderList();
        }

        /// <summary>
        /// FR-032: validate the WHOLE working copy (V1–V11 per agent, V12 at list level) and
        /// return a message naming the agent and the field, or null when every agent is valid.
        /// The offending agent is selected before the caller shows the message.
        /// </summary>
        internal string? ValidateWorkingCopy() => ValidateAgents(touchedOnly: false);

        /// <summary>
        /// Defect 9 fix — the gate the settings dialog's OK and Apply use: the same rules, but
        /// only over the agents this dialog session touched.
        /// </summary>
        internal string? ValidateTouchedAgents() => ValidateAgents(touchedOnly: true);

        private string? ValidateAgents(bool touchedOnly)
        {
            if (_agents.Count > AiAgentResolver.MaxAgents)
            {
                return $"The list holds {_agents.Count} agents; the maximum is {AiAgentResolver.MaxAgents}. " +
                       "Delete agents until the list fits.";
            }

            foreach (var agent in _agents)
            {
                if (touchedOnly && !_touchedAgentIds.Contains(agent.Id)) continue;
                var error = IsBlankAgent(agent)
                    ? ValidateBlankAgentName(agent)
                    : AiAgentResolver.Validate(agent, _agents, agent.KeyDecryptFailed);
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
               "Delete an agent before adding another.";

        private string? FirstUsableAgentId()
        {
            foreach (var agent in _agents)
            {
                if (AiAgentResolver.IsUsable(agent)) return agent.Id;
            }
            return null;
        }

        // The reason an agent cannot be made active, most actionable cause first.
        private static string UnusableReason(AiAgent agent)
        {
            var provider = agent.Provider ?? string.Empty;
            if (!agent.Enabled) return "it is disabled.";
            if (provider.Length == 0 || !AiProviderIds.CanonicalIds.Contains(provider))
                return "no provider is selected.";
            if (AiAgentResolver.RequiresApiKey(provider) && string.IsNullOrEmpty(agent.ApiKey))
                return "it needs an API key.";
            if (AiAgentResolver.RequiresApiKey(provider) && agent.KeyDecryptFailed)
                return "its stored API key could not be read on this machine — edit it and enter the key again.";
            if (string.IsNullOrWhiteSpace(agent.Model))
                return "it has no model selected.";
            if (AiAgentResolver.RequiresEndpoint(provider) && string.IsNullOrEmpty(agent.Endpoint))
                return "it needs an endpoint URL.";
            return "it is not fully configured.";
        }

        // ── Selection, rendering and the summary ───────────────────────────────────

        private void OnAgentSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressListEvents) return;
            var id = (_listView.List.SelectedItem as ListBoxItem)?.Tag as string;
            if (id == null || id == _selectedId) return;
            _selectedId = id;
            _testResult.Text = string.Empty;   // an outcome line belongs to the agent that was tested
            RefreshAgentChrome();
        }

        /// <summary>Selects an agent and refreshes the list and the summary.</summary>
        private void SelectAgent(AiAgent agent)
        {
            if (!string.Equals(_selectedId, agent.Id, StringComparison.Ordinal))
                _testResult.Text = string.Empty;
            _selectedId = agent.Id;
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
        /// The summary under the grid: the selected agent's name, its provider, model, endpoint
        /// and key state, what it answers, and its last check (or this session's test outcome).
        /// Also enables the actions that need a selection.
        /// </summary>
        private void RefreshAgentChrome()
        {
            var agent = SelectedAgent();
            var has = agent != null;
            _listView.EditButton.IsEnabled = has;
            _listView.DuplicateButton.IsEnabled = has;
            _listView.DeleteButton.IsEnabled = has;
            _listView.SetActiveButton.IsEnabled = has && !string.Equals(agent!.Id, _activeId, StringComparison.Ordinal);
            _listView.TestButton.IsEnabled = has;
            _chrome.EditLink.IsEnabled = has;
            _chrome.EditLink.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _chrome.ActiveChip.Visibility = has && string.Equals(agent!.Id, _activeId, StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;

            if (agent == null)
            {
                _chrome.SummaryTitle.Text = _agents.Count == 0 ? "No AI agent yet" : "No agent selected";
                _chrome.SummaryDetail.Text = _agents.Count == 0
                    ? "Click Add… to connect an AI provider. AI features stay off until an agent can answer."
                    : "Select an agent in the list to see its details.";
                _chrome.SummaryUse.Text = string.Empty;
                _chrome.SummaryUse.Visibility = Visibility.Collapsed;
                _chrome.TestIdle.Visibility = Visibility.Collapsed;
                _testResult.Visibility = Visibility.Collapsed;
                return;
            }

            var name = (agent.Name ?? string.Empty).Trim();
            _chrome.SummaryTitle.Text = (name.Length > 0 ? name : "(unnamed)") + (agent.Enabled ? string.Empty : " · off");
            _chrome.SummaryDetail.Text = DetailLine(agent);
            _chrome.SummaryUse.Text = UsedByLine(agent);
            _chrome.SummaryUse.Visibility = Visibility.Visible;

            var hasOutcome = !string.IsNullOrEmpty(_testResult.Text);
            _testResult.Visibility = hasOutcome ? Visibility.Visible : Visibility.Collapsed;
            _chrome.TestIdle.Visibility = hasOutcome ? Visibility.Collapsed : Visibility.Visible;
            if (hasOutcome) return;

            var health = agent.Health;
            if (health == null || string.IsNullOrEmpty(health.CheckedUtc) ||
                string.Equals(health.Status, AgentHealthStatus.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                _chrome.TestIdle.Text = string.Equals(health?.Status, AgentHealthStatus.NeedsKey, StringComparison.OrdinalIgnoreCase)
                    ? "Needs an API key — click Edit… to enter one."
                    : "Not tested yet — click Test to check the connection.";
                _chrome.TestIdle.Foreground = _testIdleBrush;
                return;
            }
            var latency = health.LatencyMs > 0 ? $" ({health.LatencyMs.ToString(CultureInfo.InvariantCulture)} ms)" : string.Empty;
            var message = string.IsNullOrEmpty(health.Message) || health.Status == AgentHealthStatus.Ready
                ? string.Empty
                : ": " + health.Message;
            _chrome.TestIdle.Text = "Last check " + AiAgentListView.HealthBadgeTextFor(agent) + latency + message;
            _chrome.TestIdle.Foreground = AiAgentListView.BadgeBrushFor(health.Status, _chrome.Dark);
        }

        private static string DetailLine(AiAgent agent)
        {
            var provider = AiProviderIds.Normalize(agent.Provider);
            var parts = new List<string> { AiAgentListView.ProviderLabel(agent) };
            parts.Add(string.IsNullOrWhiteSpace(agent.Model) ? "no model" : agent.Model.Trim());
            parts.Add(string.IsNullOrWhiteSpace(agent.Endpoint) ? "standard endpoint" : "endpoint " + agent.Endpoint.Trim());
            if (provider == AiProviderIds.Ollama || provider == AiProviderIds.LmStudio)
                parts.Add("no key needed");
            else if (agent.KeyDecryptFailed)
                parts.Add("stored key cannot be read on this machine");
            else if (string.IsNullOrEmpty(agent.ApiKey))
                parts.Add(AiAgentResolver.RequiresApiKey(provider) ? "no API key" : "no API key (optional)");
            else
                parts.Add("API key stored, encrypted");
            return string.Join(" · ", parts);
        }

        private string UsedByLine(AiAgent agent)
        {
            var parts = new List<string>();
            if (string.Equals(agent.Id, _activeId, StringComparison.Ordinal))
                parts.Add("Active agent: answers every feature set to “Use active agent”");
            var features = AssignedFeatureLabels(agent.Id);
            if (features.Count > 0)
                parts.Add("Answers " + string.Join(", ", features));
            var position = _fallback.IndexOf(agent.Id);
            if (position >= 0)
                parts.Add("Fallback #" + (position + 1).ToString(CultureInfo.InvariantCulture));
            return parts.Count > 0 ? string.Join(" · ", parts) : "Not assigned to any feature — set it on the Features tab, or make it the active agent.";
        }

        private List<string> AssignedFeatureLabels(string agentId)
        {
            var labels = new List<string>();
            foreach (var (feature, label) in FeatureShortLabels)
            {
                if (string.Equals(GetAssignment(feature), agentId, StringComparison.Ordinal))
                    labels.Add(label);
            }
            return labels;
        }

        private static readonly (AiFeature Feature, string Label)[] FeatureShortLabels =
        {
            (AiFeature.Chat, "Chat panel"),
            (AiFeature.TextToSql, "Text-to-SQL"),
            (AiFeature.Explain, "Explain SQL"),
            (AiFeature.Fix, "Fix errors"),
            (AiFeature.Optimize, "Optimize queries"),
            (AiFeature.IndexSuggestions, "Index suggestions"),
            (AiFeature.GhostText, "Inline ghost text"),
        };

        /// <summary>The shared request parameters as the Privacy &amp; data tab shows them.</summary>
        private (int MaxTokens, int Temperature10, int Timeout, int Retries) ReadSharedParams()
            => (RowFactory.GetNumber(_maxTokens), (int)_temperature.Value, RowFactory.GetNumber(_timeout), (int)_retries.Value);

        /// <summary>
        /// The request parameters are ONE set for every agent (the user's 2026-10-13 decision):
        /// Save writes them to each working-copy agent.
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

        /// <summary>
        /// FR-055 (US5, T083): <c>needsKey</c> is computed LOCALLY — the provider requires a key
        /// and the agent has none, or its stored key will not decrypt on this machine (V23) —
        /// with no request. Forced from ANY state while the condition holds, cleared back to
        /// <c>unknown</c> the moment it stops holding. Runs at the head of every render.
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
        /// Called from <see cref="RenderList"/> so adds, removes and renames propagate. A
        /// dangling assignment or fallback entry is dropped here so the working copy never
        /// persists what the UI shows as "Use active agent".
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
                // necessarily non-empty).
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
            RefreshAgentChrome();   // the summary's "Answers …" line follows the assignment
        }

        private void OnFallbackAdd()
        {
            // Defect 11 fix: resolve the pick by INDEX against _fallbackCandidateIds, never by name.
            var index = _featureRows.FallbackCandidate.SelectedIndex;
            if (index < 0 || index >= _fallbackCandidateIds.Count) return;
            var agent = FindAgent(_fallbackCandidateIds[index]);
            if (agent == null || _fallback.Contains(agent.Id)) return;
            _fallback.Add(agent.Id);
            RebuildFeatureRows();
            _featureRows.FallbackList.SelectedIndex = _featureRows.FallbackList.Items.Count - 1;
            RefreshAgentChrome();
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
            RefreshAgentChrome();
        }

        private void OnFallbackMove(int delta)
        {
            var index = _featureRows.FallbackList.SelectedIndex;
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _fallback.Count) return;
            (_fallback[index], _fallback[target]) = (_fallback[target], _fallback[index]);
            RebuildFeatureRows();
            _featureRows.FallbackList.SelectedIndex = target;
            RefreshAgentChrome();
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

        // ── Load / Save ───────────────────────────────────────────────────────────

        public void Load(AppSettings settings)
        {
            var ai = settings.Ai;

            // R13: deep-copy the agent list into the working copy. Cancel is correct for free.
            _agents = DeepCopyAgents(ai.Agents);
            _touchedAgentIds.Clear();
            _activeId = ai.ActiveAgentId ?? string.Empty;
            _assignments = CopyAssignments(ai.FeatureAgents);
            _fallback = new List<string>(ai.FallbackOrder ?? new List<string>());
            _testResult.Text = string.Empty;

            // No agent list but flat fields that name a provider (a hand-built or pre-migration
            // config V14 has not turned into an agent yet): keep them as an agent so nothing the
            // user had is lost. An empty config gets no placeholder — the grid says how to start.
            if (_agents.Count == 0 && !string.IsNullOrEmpty(AiProviderIds.Normalize(ai.Provider)))
            {
                var seed = new AiAgent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = AiAgentResolver.SuggestName(_agents),
                    Enabled = true,
                    CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    Provider = AiProviderIds.Normalize(ai.Provider),
                    Model = ai.Model ?? string.Empty,
                    ApiKey = ai.ApiKey ?? string.Empty,
                    Endpoint = ai.Endpoint ?? string.Empty,
                    MaxTokens = ai.MaxTokens,
                    Temperature = ai.Temperature,
                    Timeout = ai.Timeout,
                    Retries = ai.Retries,
                };
                _agents.Add(seed);
                _activeId = seed.Id;
            }

            // FR-053 (V23): derive the decrypt-failure flag for EVERY agent now, so an agent the
            // user never selects shows the right badge from the first render.
            foreach (var agent in _agents)
            {
                var (_, decrypted) = AiAgentTestFlow.UnwrapKeyForDisplay(agent.ApiKey);
                agent.KeyDecryptFailed = !decrypted;
            }

            // Selection: the deep-linked agent, else the active agent, else the first.
            AiAgent? selected = null;
            if (!string.IsNullOrEmpty(InitialAgentId)) selected = FindAgent(InitialAgentId!);
            selected ??= FindAgent(_activeId);
            selected ??= _agents.Count > 0 ? _agents[0] : null;
            if (FindAgent(_activeId) == null)
                _activeId = FirstUsableAgentId() ?? selected?.Id ?? string.Empty;
            _selectedId = selected?.Id ?? string.Empty;
            RenderList();

            // The request parameters are one set for every agent: shown from the active agent
            // (the source of the flat mirror), or from the flat fields when there is none.
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

            // Global concerns come from the settings, never from an agent (FR-033).
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

        public void Save(AppSettings settings)
        {
            var paramsEdited = ReadSharedParams() != _loadedParams;
            DropStillBlankAgents(paramsEdited);
            ApplySharedParamsToAgents();

            // The working copy is written back wholesale — as fresh lists and fresh agent
            // objects: the dialog can stay open after an Apply, and an edit made past that point
            // must not mutate the settings object the host already saved.
            settings.Ai.Agents = DeepCopyAgents(_agents);
            settings.Ai.ActiveAgentId = _activeId;
            settings.Ai.FeatureAgents = CopyAssignments(_assignments);
            settings.Ai.FallbackOrder = new List<string>(_fallback);

            // The flat fields mirror the ACTIVE agent, so every pre-agents consumer, and an
            // older build reading this config after a downgrade, sees what the active agent
            // carries. ConfigManager.Save re-asserts V18's usability-gated mirror on disk.
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
                // Every agent was deleted: blank the flat connection fields so the next load's
                // V14 migration cannot resurrect a deliberately deleted configuration.
                settings.Ai.Provider = settings.Ai.Model = settings.Ai.ApiKey = settings.Ai.Endpoint = string.Empty;
            }

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
            // V19: "AI enabled" is a property of the whole list — at least one usable agent.
            settings.Ai.Enabled = _agents.Any(AiAgentResolver.IsUsable);
            settings.Ai.TextToSql = _textToSql.IsChecked == true;
            settings.Ai.Explain = _explain.IsChecked == true;
            settings.Ai.Fix = _fix.IsChecked == true;
            settings.Ai.Optimize = _optimize.IsChecked == true;
            settings.Ai.IndexSuggestions = _indexSuggestions.IsChecked == true;
            settings.Ai.InlineCompletion = _inlineCompletion.IsChecked == true;
            settings.Ai.AutoFixOnError = _autoFixOnError.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);

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
                KeyDecryptFailed = agent.KeyDecryptFailed,
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

        // ── Test (the toolbar's: the selected agent as it stands) ─────────────────

        /// <summary>
        /// FR-009/FR-054: tests the SELECTED agent as it stands in the working copy (the editor
        /// dialog has its own Test for values being typed). The two local refusals fire before
        /// anything is sent; the outcome is written to the agent's Health (T085) and shown in
        /// the summary. Internal so the shell tests can await the flow; the button awaits it too.
        /// </summary>
        internal async System.Threading.Tasks.Task RunProviderTestAsync()
        {
            var agent = SelectedAgent();
            if (agent == null) return;

            var providerId = AiProviderIds.Normalize(agent.Provider);
            var refusal = AiAgentTestFlow.LocalRefusal(providerId, AiAgentListView.ProviderLabel(agent), agent.Model, agent.Endpoint);
            if (refusal != null)
            {
                _testResult.Foreground = _failureBrush;
                _testResult.Text = refusal;
                RefreshAgentChrome();
                return;
            }

            var (key, _) = AiAgentTestFlow.UnwrapKeyForDisplay(agent.ApiKey);
            var request = AiProviderTestRunner.BuildRequest(providerId, agent.Model, key, agent.Endpoint);

            _listView.TestButton.IsEnabled = false;
            _listView.TestButton.Content = "Testing…";
            _testResult.Foreground = _testIdleBrush;
            _testResult.Text = "Testing the connection…";
            RefreshAgentChrome();
            try
            {
                var (success, message, latencyMs) = await AiAgentTestFlow.RunAsync(request, RowFactory.GetNumber(_timeout));
                _testResult.Text = message;
                _testResult.Foreground = success ? _successBrush : _failureBrush;
                AiAgentTestFlow.RecordHealth(agent, success, message, latencyMs);
                RenderList();   // the tested agent's badge moves with its Health
            }
            finally
            {
                _listView.TestButton.Content = "Test";
                _listView.TestButton.IsEnabled = SelectedAgent() != null;
            }
        }
    }
}
