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
using Serilog;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class AiAssistancePage : IPageBuilder
    {
        public string Key     => "AI Assistance";
        public string Display => "AI Assistance";
        public string Title   => "AI Assistance";
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

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            // Spec 037 (US2, R12): the agent list + its CRUD row sit above the provider rows,
            // which are the editor for the selected agent — inline, not a modal (an agent has
            // ten fields; the rows already exist on this page).
            ctx.Rows.AddGroupHeader(panel, "Agents");
            var agentListView = new AiAgentListView(ctx.Theme);
            agentListView.AddTo(panel);
            ctx.RegisterSearch("AI agents", "Add, duplicate, remove, rename and activate AI agents", "List", agentListView.List);

            ctx.Rows.AddGroupHeader(panel, "Selected agent");

            var (rowName, txtName) = ctx.Rows.AddTextInput(panel,
                "Name", "A friendly name for this agent — shown in the chat picker and feature assignments");
            ctx.RegisterSearch("Name", "A friendly name for this agent — shown in the chat picker and feature assignments", "Text", rowName);

            // FR-032 (US2 scenario 5): name validation on focus loss, inline beside the field —
            // a modal here would fire while the user is still tabbing through the dialog. The
            // controls object recolours this to the semantic failure brush and drives it from
            // the name box's LostFocus.
            var nameError = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(20, -6, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            panel.Children.Add(nameError);

            // Spec 037 (review finding): the per-agent on/off switch — the route back from the
            // "Every AI agent is turned off." empty state without deleting the agent. Committed
            // through CommitEditorToSelectedAgent like every other editor field, and NOT one of
            // the T086 health-reset fields (only provider/model/key/endpoint edits invalidate a
            // recorded check), so it carries no change handler at all.
            const string enabledTip = "Turn this agent on or off — a turned-off agent is hidden from the chat picker and skipped by feature assignments and the fallback order";
            var (rowEnabled, chkAgentEnabled) = ctx.Rows.AddToggle(panel, "Enabled", enabledTip);
            ctx.RegisterSearch("Enabled", enabledTip, "Toggle", rowEnabled);
            chkAgentEnabled.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Agent enabled");

            var providerNames = new string[Providers.Length];
            for (var i = 0; i < Providers.Length; i++) providerNames[i] = Providers[i].Display;

            var (rowProvider, cboProvider) = ctx.Rows.AddDropdown(panel,
                "AI Provider",
                providerNames,
                "Select the AI provider for SQL assistance features");
            ctx.RegisterSearch("AI Provider", "Select the AI provider for SQL assistance features", "Dropdown", rowProvider);

            var (rowModel, txtModel) = ctx.Rows.AddTextInput(panel,
                "Model", "e.g. gpt-4o, claude-sonnet-4-6, gemini-flash-latest");
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

            var (rowApiKey, txtApiKey) = ctx.Rows.AddTextInput(panel,
                "API Key", "Your API key for the selected provider", isPassword: true);
            ctx.RegisterSearch("API Key", "Your API key for the selected provider", "Text", rowApiKey);

            // PR #251 review finding 2: a stored key this Windows user cannot decrypt (roamed
            // profile, restored backup, different machine) must be VISIBLE, not silently dropped.
            // Shown by Load on decrypt failure; follows the inline-help idiom (helpBorder below).
            var keyNotice = new Border
            {
                BorderBrush = ctx.Theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Background = ctx.Theme.Panel,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "The stored API key could not be decrypted on this machine (it may come " +
                           "from a roamed profile or a restored backup). The stored value was left " +
                           "untouched — re-enter the key to keep using the selected provider.",
                    Foreground = ctx.Theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            panel.Children.Add(keyNotice);

            // Spec 036 (US2, FR-009): in-dialog connection test. Sends the CURRENT field values
            // over the existing AiProviderTest (77/177) IPC pair — nothing is saved until OK.
            var (rowTest, btnTest) = ctx.Rows.AddButton(panel,
                "Test connection",
                "Test connection",
                "Verify the provider, model, endpoint and key above with a one-line test prompt. Uses the current field values; nothing is saved.");
            ctx.RegisterSearch("Test connection", "Verify the configured AI provider, model, endpoint and key", "Button", rowTest);
            btnTest.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Test AI provider connection");

            var testResult = new TextBlock
            {
                Foreground = ctx.Theme.FgSecondary,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(ctx.Rows.WrapZebraRow(testResult));

            // Inline help block (theme-aware) — preserved from the legacy build
            var helpBorder = new Border
            {
                BorderBrush = ctx.Theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Background = ctx.Theme.Panel,
                Child = new TextBlock
                {
                    Text =
                        "How to get your API key:\n" +
                        "  • Anthropic (Claude): console.anthropic.com → API Keys" +
                        "  —  example model: claude-sonnet-4-6\n" +
                        "  • Google (Gemini): aistudio.google.com → Get API Key" +
                        "  —  example model: gemini-flash-latest\n" +
                        "  • OpenAI: platform.openai.com → API Keys" +
                        "  —  example model: gpt-4o\n\n" +
                        "Keys are stored encrypted with Windows DPAPI and never written in plain text.",
                    Foreground = ctx.Theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                }
            };
            panel.Children.Add(helpBorder);

            var (rowEndpoint, txtEndpoint) = ctx.Rows.AddTextInput(panel,
                "Endpoint URL", "Custom endpoint (required for Azure OpenAI and custom providers)");
            ctx.RegisterSearch("Endpoint URL", "Custom endpoint (required for Azure OpenAI and custom providers)", "Text", rowEndpoint);

            // Spec 037 (US4, T074): which agent serves each feature — "Use active agent" or a
            // pinned agent — and (T075) the ordered chain the engine walks when the answering
            // agent fails, before the offline provider (contracts/agent-resolution.md).
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Feature assignments");

            var featureRows = new FeatureAssignmentRows();
            foreach (var (label, tip, feature) in AssignmentFeatures)
            {
                var (rowAssign, comboAssign) = ctx.Rows.AddDropdown(panel,
                    label, new[] { UseActiveAgentDisplay }, tip);
                ctx.RegisterSearch(label, tip, "Dropdown", rowAssign);
                featureRows.SetCombo(feature, comboAssign);
            }

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Fallback order");

            var (rowFallbackAdd, cboFallback) = ctx.Rows.AddDropdown(panel,
                "Add to fallback order",
                new string[0],
                "When the answering agent fails, the chain below is tried in order — before the offline provider");
            ctx.RegisterSearch("Add to fallback order", "When the answering agent fails, the chain below is tried in order — before the offline provider", "Dropdown", rowFallbackAdd);

            var fallbackList = new ListBox
            {
                Height = 84,
                Margin = new Thickness(20, 4, 20, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = ctx.Theme.ComboBorder,
                Background = ctx.Theme.Input,
                FontSize = 13,
            };
            System.Windows.Automation.AutomationProperties.SetName(fallbackList, "Fallback order");
            fallbackList.ItemContainerStyle = AiAgentListView.BuildItemStyle(ctx.Theme);
            panel.Children.Add(fallbackList);
            ctx.RegisterSearch("Fallback order", "Agents tried in order when the answering agent fails", "List", fallbackList);

            var btnFallbackAdd = AiAgentListView.MakeButton("Add", "Add the chosen agent to the fallback order");
            var btnFallbackRemove = AiAgentListView.MakeButton("Remove", "Remove the selected agent from the fallback order");
            var btnFallbackUp = AiAgentListView.MakeButton("Move up", "Try the selected agent earlier in the fallback order");
            var btnFallbackDown = AiAgentListView.MakeButton("Move down", "Try the selected agent later in the fallback order");
            var fallbackButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 4, 20, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            fallbackButtons.Children.Add(btnFallbackAdd);
            fallbackButtons.Children.Add(btnFallbackRemove);
            fallbackButtons.Children.Add(btnFallbackUp);
            fallbackButtons.Children.Add(btnFallbackDown);
            panel.Children.Add(fallbackButtons);

            featureRows.FallbackList = fallbackList;
            featureRows.FallbackCandidate = cboFallback;
            featureRows.FallbackAdd = btnFallbackAdd;
            featureRows.FallbackRemove = btnFallbackRemove;
            featureRows.FallbackMoveUp = btnFallbackUp;
            featureRows.FallbackMoveDown = btnFallbackDown;

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Privacy & Data");

            var (rowPrivacy, cboPrivacy) = ctx.Rows.AddDropdown(panel,
                "Privacy mode",
                new[] { "Schema Only", "Full", "Anonymous", "Offline", "Disabled" },
                "Controls what data is sent to the AI provider");
            ctx.RegisterSearch("Privacy mode", "Controls what data is sent to the AI provider", "Dropdown", rowPrivacy);

            // Cloud-provider consent gate. The engine refuses to send prompts/schema to a NON-LOCAL
            // provider (Anthropic, OpenAI, Gemini, …) until the user consents here — otherwise AI
            // Chat and every AI feature fail with "CONSENT_REQUIRED: Data will be sent to your AI
            // provider. Please confirm in settings." Local providers (Ollama, LM Studio) never need
            // this. Unchecked = consent withheld (privacy-first default).
            const string consentTip = "Required before a cloud provider (Anthropic, OpenAI, Gemini) receives your prompts and schema. Local providers (Ollama, LM Studio) never need this. Leave off to block cloud AI.";
            var (rowConsent, chkConsent) = ctx.Rows.AddToggle(panel,
                "Consent to cloud AI data sharing", consentTip);
            ctx.RegisterSearch("Consent to cloud AI data sharing", consentTip, "Toggle", rowConsent);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Parameters");

            var (rowMax, sldMax, lblMax) = ctx.Rows.AddSlider(panel,
                "Max response tokens", 128, 128000, 4096,
                "Maximum number of tokens in the AI response", largeRange: true);
            ctx.RegisterSearch("Max response tokens", "Maximum number of tokens in the AI response", "Slider", rowMax);

            var (rowTemp, sldTemp, lblTemp) = ctx.Rows.AddSlider(panel,
                "Temperature (x10)", 0, 20, 2,
                "Sampling temperature: 0 = deterministic, 20 = creative");
            ctx.RegisterSearch("Temperature (x10)", "Sampling temperature: 0 = deterministic, 20 = creative", "Slider", rowTemp);

            var (rowTimeout, sldTimeout, lblTimeout) = ctx.Rows.AddSlider(panel,
                "Timeout (seconds)", 5, 300, 30,
                "Request timeout for AI API calls");
            ctx.RegisterSearch("Timeout (seconds)", "Request timeout for AI API calls", "Slider", rowTimeout);

            var (rowRetries, sldRetries, lblRetries) = ctx.Rows.AddSlider(panel,
                "Retries", 0, 10, 2,
                "Number of automatic retries on transient failures");
            ctx.RegisterSearch("Retries", "Number of automatic retries on transient failures", "Slider", rowRetries);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Features");

            var (rowTextToSql, chkTextToSql) = ctx.Rows.AddToggle(panel,
                "Natural language to SQL", "Generate SQL from plain English descriptions");
            ctx.RegisterSearch("Natural language to SQL", "Generate SQL from plain English descriptions", "Toggle", rowTextToSql);

            var (rowExplain, chkExplain) = ctx.Rows.AddToggle(panel,
                "Explain SQL", "Get AI-powered explanations of SQL queries");
            ctx.RegisterSearch("Explain SQL", "Get AI-powered explanations of SQL queries", "Toggle", rowExplain);

            var (rowFix, chkFix) = ctx.Rows.AddToggle(panel,
                "Fix errors", "Suggest fixes when queries fail with errors");
            ctx.RegisterSearch("Fix errors", "Suggest fixes when queries fail with errors", "Toggle", rowFix);

            var (rowOptimize, chkOptimize) = ctx.Rows.AddToggle(panel,
                "Optimize queries", "Get AI-powered query optimization suggestions");
            ctx.RegisterSearch("Optimize queries", "Get AI-powered query optimization suggestions", "Toggle", rowOptimize);

            var (rowIdx, chkIndex) = ctx.Rows.AddToggle(panel,
                "Index suggestions", "AI-powered index analysis and recommendations");
            ctx.RegisterSearch("Index suggestions", "AI-powered index analysis and recommendations", "Toggle", rowIdx);

            var (rowChat, chkChat) = ctx.Rows.AddToggle(panel,
                "Chat panel", "Enable the AI chat side panel for interactive assistance");
            ctx.RegisterSearch("Chat panel", "Enable the AI chat side panel for interactive assistance", "Toggle", rowChat);

            var (rowInline, chkInline) = ctx.Rows.AddToggle(panel,
                "Inline ghost text", "Show AI-powered inline completion suggestions as ghost text");
            ctx.RegisterSearch("Inline ghost text", "Show AI-powered inline completion suggestions as ghost text", "Toggle", rowInline);

            var (rowAutoFix, chkAutoFix) = ctx.Rows.AddToggle(panel,
                "Auto-fix on error", "Automatically suggest fixes when query execution fails");
            ctx.RegisterSearch("Auto-fix on error", "Automatically suggest fixes when query execution fails", "Toggle", rowAutoFix);

            return new AiAssistanceControls(cboProvider, txtModel, txtApiKey, txtEndpoint, cboPrivacy,
                sldMax, lblMax, sldTemp, lblTemp, sldTimeout, lblTimeout, sldRetries, lblRetries,
                chkTextToSql, chkExplain, chkFix, chkOptimize, chkIndex, chkChat, chkInline, chkAutoFix,
                chkConsent, btnTest, testResult, keyNotice, txtName, nameError, chkAgentEnabled, agentListView, featureRows);
        }
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
        private readonly Slider _maxTokens;
        private readonly TextBlock _maxTokensLabel;
        private readonly Slider _temperature;
        private readonly TextBlock _temperatureLabel;
        private readonly Slider _timeout;
        private readonly TextBlock _timeoutLabel;
        private readonly Slider _retries;
        private readonly TextBlock _retriesLabel;
        private readonly CheckBox _textToSql;
        private readonly CheckBox _explain;
        private readonly CheckBox _fix;
        private readonly CheckBox _optimize;
        private readonly CheckBox _indexSuggestions;
        private readonly CheckBox _chatPanel;
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
        private readonly List<string> _assignmentIds = new();   // index-aligned with every assignment combo
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
        private void DropStillBlankAgents()
        {
            var droppedAny = false;
            var selectedDropped = false;
            // Materialise first: the working copy is mutated inside the loop.
            foreach (var agent in _agents.Where(IsStillBlankAgent).ToList())
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
        internal string? ValidateWorkingCopy()
        {
            CommitEditorToSelectedAgent();

            if (_agents.Count > AiAgentResolver.MaxAgents)
            {
                return $"The list holds {_agents.Count} agents; the maximum is {AiAgentResolver.MaxAgents}. " +
                       "Remove agents until the list fits.";
            }

            foreach (var agent in _agents)
            {
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
        /// request parameters must also sit at the <see cref="AiAgent"/> defaults. That is the
        /// shape of the Load-seeded placeholder and of an Add the user never edited — an agent
        /// the user only retuned the sliders on is a real (if incomplete) configuration and is
        /// kept. The name is irrelevant to both predicates.
        /// </summary>
        private static bool IsStillBlankAgent(AiAgent agent)
        {
            var defaults = new AiAgent();
            return IsBlankAgent(agent)
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

        // Semantic colours are the only acceptable hardcoded hex (CLAUDE.md WPF conventions).
        private static readonly SolidColorBrush SuccessBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)));
        private static readonly SolidColorBrush FailureBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public AiAssistanceControls(
            ComboBox provider, TextBox model, TextBox apiKey, TextBox endpoint, ComboBox privacy,
            Slider sldMax, TextBlock lblMax, Slider sldTemp, TextBlock lblTemp,
            Slider sldTimeout, TextBlock lblTimeout, Slider sldRetries, TextBlock lblRetries,
            CheckBox textToSql, CheckBox explain, CheckBox fix, CheckBox optimize,
            CheckBox idx, CheckBox chat, CheckBox inline, CheckBox autoFix,
            CheckBox cloudConsent, Button testButton, TextBlock testResult, Border keyNotice,
            TextBox name, TextBlock nameError, CheckBox agentEnabled, AiAgentListView listView, FeatureAssignmentRows featureRows)
        {
            _provider = provider;
            _model = model;
            _apiKey = apiKey;
            _endpoint = endpoint;
            _privacy = privacy;
            _cloudConsent = cloudConsent;
            _maxTokens = sldMax;
            _maxTokensLabel = lblMax;
            _temperature = sldTemp;
            _temperatureLabel = lblTemp;
            _timeout = sldTimeout;
            _timeoutLabel = lblTimeout;
            _retries = sldRetries;
            _retriesLabel = lblRetries;
            _textToSql = textToSql;
            _explain = explain;
            _fix = fix;
            _optimize = optimize;
            _indexSuggestions = idx;
            _chatPanel = chat;
            _inlineCompletion = inline;
            _autoFixOnError = autoFix;
            _testButton = testButton;
            _testResult = testResult;
            _testIdleBrush = testResult.Foreground;
            _keyNotice = keyNotice;
            _name = name;
            _nameError = nameError;
            _nameError.Foreground = FailureBrush;
            _agentEnabled = agentEnabled;
            _listView = listView;
            _featureRows = featureRows;
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
                foreach (var agent in _agents)
                {
                    if (_fallback.Contains(agent.Id)) continue;
                    candidate.Items.Add(agent.Name ?? string.Empty);
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
            var name = _featureRows.FallbackCandidate.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            var agent = _agents.Find(a => string.Equals(a.Name ?? string.Empty, name, StringComparison.Ordinal));
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
            _chatPanel.IsChecked = ai.ChatPanel;
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
                _maxTokens.Value = agent.MaxTokens;
                _maxTokensLabel.Text = agent.MaxTokens.ToString(CultureInfo.InvariantCulture);
                _temperature.Value = (int)(agent.Temperature * 10);
                _temperatureLabel.Text = ((int)(agent.Temperature * 10)).ToString(CultureInfo.InvariantCulture);
                _timeout.Value = agent.Timeout;
                _timeoutLabel.Text = agent.Timeout.ToString(CultureInfo.InvariantCulture);
                _retries.Value = agent.Retries;
                _retriesLabel.Text = agent.Retries.ToString(CultureInfo.InvariantCulture);
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
                _maxTokens.Value = 4096;
                _maxTokensLabel.Text = 4096.ToString(CultureInfo.InvariantCulture);
                _temperature.Value = 2;
                _temperatureLabel.Text = 2.ToString(CultureInfo.InvariantCulture);
                _timeout.Value = 30;
                _timeoutLabel.Text = 30.ToString(CultureInfo.InvariantCulture);
                _retries.Value = 2;
                _retriesLabel.Text = 2.ToString(CultureInfo.InvariantCulture);
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
            agent.MaxTokens = (int)_maxTokens.Value;
            agent.Temperature = (int)_temperature.Value / 10.0;
            agent.Timeout = (int)_timeout.Value;
            agent.Retries = (int)_retries.Value;
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
            DropStillBlankAgents();

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
                settings.Ai.MaxTokens = active.MaxTokens;
                settings.Ai.Temperature = active.Temperature;
                settings.Ai.Timeout = active.Timeout;
                settings.Ai.Retries = active.Retries;
            }
            else if (_agents.Count == 0)
            {
                // The user removed every agent: blank the flat connection fields so the next
                // load's V14 migration cannot resurrect a deliberately deleted configuration.
                settings.Ai.Provider = settings.Ai.Model = settings.Ai.ApiKey = settings.Ai.Endpoint = string.Empty;
            }

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
            settings.Ai.ChatPanel = _chatPanel.IsChecked == true;
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
                _testResult.Foreground = FailureBrush;
                _testResult.Text = refusal;
                return;
            }

            var request = AiProviderTestRunner.BuildRequest(
                _provider.SelectedItem as string, _model.Text, _apiKey.Text, _endpoint.Text);

            // The wait budget follows the dialog's CURRENT timeout slider, not the saved config.
            var waitSettings = new AppSettings();
            waitSettings.Ai.Timeout = (int)_timeout.Value;

            _testButton.IsEnabled = false;
            _testButton.Content = "Testing…";
            _testResult.Foreground = _testIdleBrush;
            _testResult.Text = "Testing the connection…";
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (success, message) = await AiProviderTestRunner.RunAsync(
                    TestRpcAccessor ?? EngineRpcClientAccessor.Instance, request, waitSettings);
                sw.Stop();
                _testResult.Text = message;
                _testResult.Foreground = success ? SuccessBrush : FailureBrush;
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
