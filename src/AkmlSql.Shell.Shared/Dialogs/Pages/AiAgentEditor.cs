#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Serilog;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// 2026-11 master–detail redesign (SSMS layout B): the form for ONE agent, hosted by
    /// <see cref="AiAgentEditorDialog"/> when the user clicks Add… or Edit… on the AI assistance
    /// page. It edits a <see cref="Draft"/> — a copy of the working-copy agent — so Cancel is
    /// free; the page copies the draft back on OK and the Options window's OK saves it.
    ///
    /// <para>Kept from the always-open editor it replaces: provider-switch model autofill,
    /// name rules V1–V3 on focus loss, the masked key with Show, the key help, the decrypt-failure
    /// notice and guard (PR #251 review finding 2), health reset on a connection edit (R10,
    /// T086), the two local test refusals (T087) and the test flow (FR-054, T084/T085).</para>
    ///
    /// <para>New: the stored key is never shown. The key box starts empty; left empty it keeps
    /// the stored key, unless the provider changed — a key belongs to its provider, so switching
    /// provider drops it.</para>
    /// </summary>
    internal sealed class AiAgentEditor
    {
        private readonly PageTheme _theme;
        private readonly IReadOnlyList<AiAgent> _siblings;
        private readonly string _originalProvider;
        private readonly string _storedKey;
        private readonly Func<int> _timeoutSeconds;
        private readonly SolidColorBrush _successBrush;
        private readonly SolidColorBrush _failureBrush;
        private readonly Brush _idleBrush;
        private bool _suppressEdits;
        private bool _suppressKeySync;

        // Field names match the old page editor's, so the reflection-based tests read the same.
        private readonly TextBox _name;
        private readonly TextBlock _nameError;
        private readonly CheckBox _agentEnabled;
        private readonly ComboBox _provider;
        private readonly TextBox _model;
        private readonly TextBox _apiKey;
        private readonly PasswordBox _keyMask;
        private readonly Button _showKey;
        private readonly Button _keyHelpLink;
        private readonly Border _keyHelp;
        private readonly Border _keyNotice;
        private readonly TextBlock _keyHint;
        private readonly FrameworkElement _keyRow;
        private readonly TextBlock _localKeyNote;
        private readonly TextBox _endpoint;
        private readonly TextBlock _endpointHint;
        private readonly Button _testButton;
        private readonly TextBlock _testResult;
        private readonly TextBlock _error;

        /// <summary>The agent being edited — a copy; the page copies it back on OK.</summary>
        internal AiAgent Draft { get; }

        /// <summary>True for Add… (a fresh agent), false for Edit….</summary>
        internal bool IsNew { get; }

        /// <summary>The form, for the dialog to host.</summary>
        internal StackPanel Root { get; }

        internal PageTheme Theme => _theme;

        internal string Title => IsNew
            ? "Add AI agent"
            : "Edit AI agent: " + ((Draft.Name ?? string.Empty).Trim().Length > 0 ? Draft.Name.Trim() : "(unnamed)");

        // Test seams (the shell test assembly compiles these sources in).
        internal TextBox NameBox => _name;
        internal CheckBox EnabledToggle => _agentEnabled;
        internal ComboBox ProviderCombo => _provider;
        internal TextBox ModelBox => _model;
        internal TextBox KeyBox => _apiKey;
        internal PasswordBox KeyMask => _keyMask;
        internal Button ShowKeyButton => _showKey;
        internal Button KeyHelpLink => _keyHelpLink;
        internal Border KeyHelp => _keyHelp;
        internal Border KeyNotice => _keyNotice;
        internal TextBlock KeyHint => _keyHint;
        internal FrameworkElement KeyRow => _keyRow;
        internal TextBox EndpointBox => _endpoint;
        internal Button TestButton => _testButton;
        internal TextBlock TestResult => _testResult;
        internal TextBlock NameError => _nameError;
        internal TextBlock ErrorLine => _error;

        /// <param name="theme">The Options window's page theme.</param>
        /// <param name="draft">A copy of the agent to edit (or a fresh agent for Add…).</param>
        /// <param name="siblings">Every OTHER agent in the working copy — the name rules compare against them.</param>
        /// <param name="isNew">Add… rather than Edit….</param>
        /// <param name="timeoutSeconds">The Options window's current timeout, the test's wait budget.</param>
        internal AiAgentEditor(PageTheme theme, AiAgent draft, IReadOnlyList<AiAgent> siblings, bool isNew,
            Func<int>? timeoutSeconds = null)
        {
            _theme = theme;
            Draft = draft;
            _siblings = siblings;
            IsNew = isNew;
            _timeoutSeconds = timeoutSeconds ?? (() => draft.Timeout);
            _originalProvider = AiProviderIds.Normalize(draft.Provider);
            _storedKey = draft.ApiKey ?? string.Empty;
            var dark = ReferenceEquals(theme, PageTheme.Dark);
            _successBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Ready, dark);
            _failureBrush = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Failed, dark);

            var rows = new RowFactory(theme);
            Root = new StackPanel { Margin = new Thickness(0) };

            // Name ─ Enabled on one row.
            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameColumn = new StackPanel();
            (_, _name) = rows.AddTextInput(nameColumn, "Name", string.Empty, stretch: true);
            _name.ToolTip = "A friendly name for this agent — shown in the chat picker and feature assignments";
            AutomationProperties.SetName(_name, "Agent name");
            nameRow.Children.Add(nameColumn);
            _agentEnabled = new CheckBox
            {
                Content = "Enabled",
                Foreground = theme.FgPrimary,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 0, 18),
                ToolTip = "Turn this agent on or off — a turned-off agent is hidden from the chat picker and skipped by feature assignments and the fallback order",
            };
            AutomationProperties.SetName(_agentEnabled, "Agent enabled");
            Grid.SetColumn(_agentEnabled, 1);
            nameRow.Children.Add(_agentEnabled);
            Root.Children.Add(nameRow);

            _nameError = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, -6, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                Foreground = _failureBrush,
                Visibility = Visibility.Collapsed,
            };
            Root.Children.Add(_nameError);

            // Provider ─ Model on one row.
            var providerNames = new string[AiAssistancePage.Providers.Length];
            for (var i = 0; i < providerNames.Length; i++) providerNames[i] = AiAssistancePage.Providers[i].Display;
            var pairRow = new Grid();
            pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var providerColumn = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            (_, _provider) = rows.AddDropdown(providerColumn, "Provider", providerNames, string.Empty, width: 180);
            _provider.ToolTip = "The AI provider this agent sends requests to";
            AutomationProperties.SetName(_provider, "Provider");
            var modelColumn = new StackPanel();
            (_, _model) = rows.AddTextInput(modelColumn, "Model", "e.g. claude-sonnet-4-6, gpt-4o, gemini-flash-latest, llama3.1:8b", stretch: true);
            AutomationProperties.SetName(_model, "Model");
            Grid.SetColumn(modelColumn, 1);
            pairRow.Children.Add(providerColumn);
            pairRow.Children.Add(modelColumn);
            Root.Children.Add(pairRow);

            // API key: a masked face over the TextBox every flow reads, Show/Hide, the help link.
            var keyRow = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var keyHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            _keyHelpLink = MakeLinkButton(theme, "Where do I get a key?");
            DockPanel.SetDock(_keyHelpLink, Dock.Right);
            keyHeader.Children.Add(_keyHelpLink);
            var keyLabel = rows.MakeFieldLabel("API key");
            keyLabel.Margin = new Thickness(0);
            keyLabel.VerticalAlignment = VerticalAlignment.Center;
            keyHeader.Children.Add(keyLabel);
            keyRow.Children.Add(keyHeader);

            var keyLine = new Grid();
            keyLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _apiKey = new TextBox
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
                ToolTip = "The API key for the selected provider",
            };
            _keyMask = new PasswordBox
            {
                Background = theme.Input,
                Foreground = theme.FgPrimary,
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = theme.Caret,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = "The API key for the selected provider",
            };
            AutomationProperties.SetName(_apiKey, "API key");
            AutomationProperties.SetName(_keyMask, "API key");
            keyLine.Children.Add(_keyMask);
            keyLine.Children.Add(_apiKey);
            _showKey = AiAgentListView.MakeButton("Show", "Show the API key", theme);
            _showKey.Margin = new Thickness(8, 0, 0, 0);
            _showKey.MinWidth = 60;
            Grid.SetColumn(_showKey, 1);
            keyLine.Children.Add(_showKey);
            keyRow.Children.Add(keyLine);

            _keyHint = rows.MakeDescription(string.Empty);
            _keyHint.Margin = new Thickness(0, 4, 0, 0);
            keyRow.Children.Add(_keyHint);
            Root.Children.Add(keyRow);
            _keyRow = keyRow;

            // A local provider needs no key: the row gives way to one line saying so.
            _localKeyNote = rows.MakeDescription(string.Empty);
            _localKeyNote.Margin = new Thickness(0, 0, 0, 12);
            _localKeyNote.Visibility = Visibility.Collapsed;
            Root.Children.Add(_localKeyNote);

            _keyNotice = new Border
            {
                BorderBrush = theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, -4, 0, 12),
                Background = theme.Panel,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "The stored API key could not be decrypted on this machine (it may come " +
                           "from a roamed profile or a restored backup). The stored value is kept " +
                           "until you type a new key.",
                    Foreground = theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            Root.Children.Add(_keyNotice);

            _keyHelp = new Border
            {
                BorderBrush = theme.FgAccent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, -4, 0, 12),
                Background = theme.Panel,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text =
                        "How to get your API key:\n" +
                        "  • Anthropic (Claude): console.anthropic.com → API Keys  —  example model: claude-sonnet-4-6\n" +
                        "  • Google (Gemini): aistudio.google.com → Get API Key  —  example model: gemini-flash-latest\n" +
                        "  • OpenAI: platform.openai.com → API Keys  —  example model: gpt-4o\n" +
                        "Keys are stored encrypted with Windows DPAPI and never written in plain text.",
                    Foreground = theme.FgSecondary,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                },
            };
            Root.Children.Add(_keyHelp);

            var endpointRow = new StackPanel();
            (_, _endpoint) = rows.AddTextInput(endpointRow, "Endpoint URL", string.Empty, stretch: true);
            AutomationProperties.SetName(_endpoint, "Endpoint URL");
            _endpointHint = rows.MakeDescription(string.Empty);
            _endpointHint.Margin = new Thickness(0, -8, 0, 12);
            endpointRow.Children.Add(_endpointHint);
            Root.Children.Add(endpointRow);

            // Test connection, under the fields it tests; the outcome on the same line.
            _testButton = new Button
            {
                Content = "Test connection",
                MinWidth = 130,
                Height = 28,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ThemedButton.ApplySecondary(_testButton, theme);
            AutomationProperties.SetName(_testButton, "Test AI provider connection");
            _testResult = new TextBlock
            {
                Text = "Sends one short prompt with the values above. Nothing is saved.",
                Foreground = theme.FgSecondary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            _idleBrush = _testResult.Foreground;
            var testGrid = new Grid();
            testGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            testGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            testGrid.Children.Add(_testButton);
            Grid.SetColumn(_testResult, 1);
            testGrid.Children.Add(_testResult);
            Root.Children.Add(new Border
            {
                BorderBrush = theme.ComboBorder,
                BorderThickness = new Thickness(1),
                Background = theme.Main,
                Margin = new Thickness(0, 2, 0, 10),
                Child = new Border
                {
                    BorderBrush = theme.FgAccent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 8, 12, 8),
                    Child = testGrid,
                },
            });

            // The OK refusal, in the form rather than a modal over a modal.
            _error = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _failureBrush,
                Visibility = Visibility.Collapsed,
            };
            AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
            Root.Children.Add(_error);

            WireEvents();
            Bind();
        }

        // ── Binding and committing ───────────────────────────────────────────────

        private void WireEvents()
        {
            // Provider switch: an obviously foreign model gets the new provider's default
            // ("claude-sonnet-5" left behind on an Anthropic → Gemini switch reached Google verbatim).
            _provider.SelectionChanged += (_, _) =>
            {
                if (!_suppressEdits)
                {
                    var suggested = AiModelFamily.DefaultModelFor(_provider.SelectedItem as string);
                    if (suggested != null)
                    {
                        var current = (_model.Text ?? string.Empty).Trim();
                        var family = AiModelFamily.Detect(current);
                        if (current.Length == 0 || (family != null && family != AiModelFamily.Detect(suggested)))
                            _model.Text = suggested;
                    }
                }
                UpdateProviderRows();
                ResetHealthOnEdit();
            };
            _model.TextChanged += (_, _) => ResetHealthOnEdit();
            _endpoint.TextChanged += (_, _) => ResetHealthOnEdit();
            _apiKey.TextChanged += (_, _) =>
            {
                if (!_suppressEdits && (_apiKey.Text ?? string.Empty).Length > 0)
                {
                    // A typed key replaces an undecryptable one: the notice no longer applies.
                    Draft.KeyDecryptFailed = false;
                    _keyNotice.Visibility = Visibility.Collapsed;
                }
                ResetHealthOnEdit();
            };

            // The masked face mirrors the TextBox both ways; every flow reads _apiKey.Text.
            _keyMask.PasswordChanged += (_, _) =>
            {
                if (_suppressKeySync) return;
                _suppressKeySync = true;
                try
                {
                    if (!string.Equals(_apiKey.Text ?? string.Empty, _keyMask.Password, StringComparison.Ordinal))
                        _apiKey.Text = _keyMask.Password;
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
                    if (!string.Equals(_keyMask.Password, text, StringComparison.Ordinal))
                        _keyMask.Password = text;
                }
                finally { _suppressKeySync = false; }
            };
            _showKey.Click += (_, _) => SetKeyShown(_apiKey.Visibility != Visibility.Visible);
            _keyHelpLink.Click += (_, _) => SetKeyHelpOpen(_keyHelp.Visibility != Visibility.Visible);
            _name.LostFocus += (_, _) => ValidateNameOnFocusLoss();
            _testButton.Click += async (_, _) => await RunProviderTestAsync();
        }

        /// <summary>Replays the draft into the boxes. A bind is not an edit: no health reset, no autofill.</summary>
        private void Bind()
        {
            _suppressEdits = true;
            try
            {
                _name.Text = Draft.Name ?? string.Empty;
                _agentEnabled.IsChecked = Draft.Enabled;
                var index = Array.FindIndex(AiAssistancePage.Providers, p => p.Id == _originalProvider);
                _provider.SelectedIndex = index >= 0 ? index : 0;
                _model.Text = Draft.Model ?? string.Empty;
                _apiKey.Text = string.Empty;   // the stored key is never shown
                _endpoint.Text = Draft.Endpoint ?? string.Empty;
                _keyNotice.Visibility = Draft.KeyDecryptFailed ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _suppressEdits = false; }
            UpdateProviderRows();
        }

        /// <summary>The canonical id of the provider in the box ("" for "(None)").</summary>
        internal string SelectedProviderId
        {
            get
            {
                var index = _provider.SelectedIndex;
                return index > 0 && index < AiAssistancePage.Providers.Length ? AiAssistancePage.Providers[index].Id : string.Empty;
            }
        }

        private string SelectedProviderDisplay
        {
            get
            {
                var index = _provider.SelectedIndex;
                return index > 0 && index < AiAssistancePage.Providers.Length ? AiAssistancePage.Providers[index].Display : string.Empty;
            }
        }

        /// <summary>A stored key exists and still belongs to the provider in the box.</summary>
        private bool StoredKeyApplies =>
            _storedKey.Length > 0 && string.Equals(SelectedProviderId, _originalProvider, StringComparison.Ordinal);

        /// <summary>Writes the boxes into <see cref="Draft"/>.</summary>
        internal void Commit()
        {
            Draft.Name = (_name.Text ?? string.Empty).Trim();
            Draft.Enabled = _agentEnabled.IsChecked == true;
            Draft.Provider = SelectedProviderId;
            Draft.Model = (_model.Text ?? string.Empty).Trim();
            var keyText = _apiKey.Text ?? string.Empty;
            if (keyText.Length > 0)
            {
                // FR-008: keys are DPAPI-wrapped at rest; an already-wrapped value is never re-wrapped.
                Draft.ApiKey = ApiKeyProtector.IsProtected(keyText) ? keyText : ApiKeyProtector.Protect(keyText);
                Draft.KeyDecryptFailed = false;
            }
            else if (StoredKeyApplies)
            {
                // Left empty: keep the stored key — even one this machine cannot decrypt
                // (review finding 2: a save must never blank a key the user did not touch).
                Draft.ApiKey = _storedKey;
            }
            else
            {
                Draft.ApiKey = string.Empty;
                Draft.KeyDecryptFailed = false;
            }
            Draft.Endpoint = (_endpoint.Text ?? string.Empty).Trim();
        }

        /// <summary>
        /// The dialog's OK: commit, then validate the draft against every other agent (V1–V11).
        /// Returns the message shown in the form, or null when the draft may be accepted.
        /// </summary>
        internal string? TryAccept()
        {
            Commit();
            string? error;
            if (SelectedProviderId.Length == 0)
            {
                error = "Choose a provider.";
            }
            else
            {
                var all = new List<AiAgent>(_siblings) { Draft };
                error = AiAgentResolver.Validate(Draft, all, Draft.KeyDecryptFailed);
            }
            _error.Text = error ?? string.Empty;
            _error.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
            return error;
        }

        private void UpdateProviderRows()
        {
            var providerId = SelectedProviderId;
            var display = SelectedProviderDisplay;
            var local = providerId == AiProviderIds.Ollama || providerId == AiProviderIds.LmStudio;
            _keyRow.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
            _localKeyNote.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            _localKeyNote.Text = local ? display + " runs locally and needs no API key." : string.Empty;
            if (local) _keyHelp.Visibility = Visibility.Collapsed;

            if (_storedKey.Length > 0 && !StoredKeyApplies && providerId.Length > 0)
                _keyHint.Text = "The stored key belongs to the previous provider and is removed. Paste a key for " + display + ".";
            else if (StoredKeyApplies && Draft.KeyDecryptFailed)
                _keyHint.Text = "Type the key again to replace the one this machine cannot read.";
            else if (StoredKeyApplies)
                _keyHint.Text = "A key is stored. Leave this empty to keep it.";
            else if (providerId == AiProviderIds.Custom)
                _keyHint.Text = "Optional for a custom endpoint.";
            else
                _keyHint.Text = "Paste the key from your provider's console. It is stored encrypted.";

            _endpointHint.Text = AiAgentResolver.RequiresEndpoint(providerId)
                ? "Required for " + display + "."
                : providerId.Length == 0
                    ? "Leave empty to use the provider's standard endpoint."
                    : "Leave empty to use " + display + "'s standard endpoint.";
        }

        private void SetKeyShown(bool shown)
        {
            _apiKey.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            _keyMask.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            _showKey.Content = shown ? "Hide" : "Show";
            AutomationProperties.SetName(_showKey, shown ? "Hide the API key" : "Show the API key");
        }

        private void SetKeyHelpOpen(bool open)
        {
            _keyHelp.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _keyHelpLink.Content = open ? "Hide help" : "Where do I get a key?";
        }

        /// <summary>FR-032: V1–V3 on focus loss, against every other agent.</summary>
        private void ValidateNameOnFocusLoss()
        {
            var name = (_name.Text ?? string.Empty).Trim();
            string? error = null;
            if (name.Length == 0) error = "Name is required.";
            else if (name.Length > 40) error = "Name must be 40 characters or fewer.";
            else
            {
                foreach (var other in _siblings)
                {
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

        /// <summary>
        /// R10 (T086): a provider/model/key/endpoint edit returns the draft's health to
        /// <c>unknown</c> from any state. Only the status: the last check honestly happened.
        /// </summary>
        private void ResetHealthOnEdit()
        {
            if (_suppressEdits) return;
            if (Draft.Health != null &&
                !string.Equals(Draft.Health.Status, AgentHealthStatus.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                Draft.Health.Status = AgentHealthStatus.Unknown;
            }
        }

        // ── Test connection ──────────────────────────────────────────────────────

        /// <summary>
        /// FR-054: tests what is in the boxes — the typed key, else the stored key when it still
        /// belongs to the provider. Records the outcome on the draft (it reaches the list on OK).
        /// </summary>
        internal async Task RunProviderTestAsync()
        {
            var providerId = SelectedProviderId;
            var refusal = AiAgentTestFlow.LocalRefusal(providerId, SelectedProviderDisplay, _model.Text, _endpoint.Text);
            if (refusal != null)
            {
                ShowTestOutcome(false, refusal);
                return;
            }

            var keyText = _apiKey.Text ?? string.Empty;
            if (keyText.Length == 0 && StoredKeyApplies)
                keyText = AiAgentTestFlow.UnwrapKeyForDisplay(_storedKey).Display;
            var request = AiProviderTestRunner.BuildRequest(_provider.SelectedItem as string, _model.Text, keyText, _endpoint.Text);

            _testButton.IsEnabled = false;
            _testButton.Content = "Testing…";
            _testResult.Foreground = _idleBrush;
            _testResult.Text = "Testing the connection…";
            try
            {
                var (success, message, latencyMs) = await AiAgentTestFlow.RunAsync(request, _timeoutSeconds());
                ShowTestOutcome(success, message);
                AiAgentTestFlow.RecordHealth(Draft, success, message, latencyMs);
            }
            finally
            {
                _testButton.IsEnabled = true;
                _testButton.Content = "Test connection";
            }
        }

        private void ShowTestOutcome(bool success, string message)
        {
            _testResult.Text = message;
            _testResult.Foreground = success ? _successBrush : _failureBrush;
        }

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
            AutomationProperties.SetName(button, text);
            return button;
        }
    }

    /// <summary>
    /// The test flow shared by the agent editor (the values being typed) and the page's Test
    /// button (the selected agent as it stands): the two local refusals (T087/FR-056), the IPC
    /// round trip with its stopwatch, the health record (FR-053/T085) and the key unwrap.
    /// </summary>
    internal static class AiAgentTestFlow
    {
        /// <summary>
        /// Test seam (US5, T084): when set, tests send through this accessor instead of
        /// <see cref="EngineRpcClientAccessor.Instance"/>. Production code never sets it.
        /// </summary>
        internal static IRpcClientAccessor? TestRpcAccessor { get; set; }

        /// <summary>
        /// T087/FR-056: the causes determinable without sending anything — a missing endpoint
        /// for a provider that requires one (V9), and a first-party model whose family
        /// contradicts the provider (V7). Null when nothing refuses locally.
        /// </summary>
        internal static string? LocalRefusal(string providerId, string providerDisplay, string? model, string? endpoint)
        {
            if (providerId.Length == 0) return null;   // the runner's own "Select an AI provider first."

            if (AiAgentResolver.RequiresEndpoint(providerId) && (endpoint ?? string.Empty).Trim().Length == 0)
            {
                return $"Endpoint is required for {providerDisplay} — fill in the Endpoint URL before testing.";
            }

            if (providerId == AiProviderIds.Anthropic || providerId == AiProviderIds.OpenAI ||
                providerId == AiProviderIds.Gemini || providerId == AiProviderIds.Kimi)
            {
                var trimmed = (model ?? string.Empty).Trim();
                var family = AiModelFamily.Detect(trimmed);
                if (family != null && family != providerId)
                    return $"Model \"{trimmed}\" is a {family} model, not a {providerId} model.";
            }
            return null;
        }

        /// <summary>Sends the request; the round trip is measured here. Never throws.</summary>
        internal static async Task<(bool Success, string Message, int LatencyMs)> RunAsync(
            AiProviderTestRequest request, int timeoutSeconds)
        {
            // The wait budget follows the dialog's CURRENT timeout, not the saved config.
            var waitSettings = new AppSettings();
            waitSettings.Ai.Timeout = timeoutSeconds;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (success, message) = await AiProviderTestRunner.RunAsync(
                TestRpcAccessor ?? EngineRpcClientAccessor.Instance, request, waitSettings);
            sw.Stop();
            return (success, message, (int)sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// FR-053 (T085): status, check time and the user-facing message capped at 500 chars
        /// (never built from the key — V24). LatencyMs records the last SUCCESSFUL round trip;
        /// a failure writes 0 (E2).
        /// </summary>
        internal static void RecordHealth(AiAgent? agent, bool success, string message, int latencyMs)
        {
            if (agent == null) return;
            agent.Health ??= new AgentHealth();
            agent.Health.Status = success ? AgentHealthStatus.Ready : AgentHealthStatus.Failed;
            agent.Health.CheckedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            agent.Health.LatencyMs = success ? Math.Max(latencyMs, 1) : 0;
            agent.Health.Message = message.Length <= 500 ? message : message.Substring(0, 500);
        }

        /// <summary>
        /// Legacy plaintext passes through; a wrapped blob this Windows user cannot decrypt
        /// returns <c>Decrypted = false</c> with an empty value. Nothing stored is not a failure.
        /// </summary>
        internal static (string Display, bool Decrypted) UnwrapKeyForDisplay(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return (string.Empty, true);
            if (!ApiKeyProtector.IsProtected(stored)) return (stored!, true);
            try
            {
                return (ApiKeyProtector.Unprotect(stored), true);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                Log.Warning(ex, "AI agents: a stored API key could not be decrypted; the user must re-enter it");
                return (string.Empty, false);
            }
        }
    }
}
