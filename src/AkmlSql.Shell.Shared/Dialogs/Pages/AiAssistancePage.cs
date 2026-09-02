#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Provider Configuration");

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
                chkConsent, btnTest, testResult);
        }
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
            CheckBox cloudConsent, Button testButton, TextBlock testResult)
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
            _testButton.Click += async (_, _) => await RunProviderTestAsync();
        }

        public void Load(AppSettings settings)
        {
            var ai = settings.Ai;
            // Normalise BEFORE matching (FR-013): configs written by earlier builds ("AzureOpenAI",
            // "LMStudio") resolve to their canonical ids and select correctly with no migration.
            var providerId = AiProviderIds.Normalize(ai.Provider);
            var providerIndex = Array.FindIndex(AiAssistancePage.Providers, p => p.Id == providerId);
            _provider.SelectedIndex = providerIndex >= 0 ? providerIndex : 0;
            _model.Text = ai.Model ?? string.Empty;
            _apiKey.Text = UnwrapKeyForDisplay(ai.ApiKey);
            _endpoint.Text = ai.Endpoint ?? string.Empty;
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
            _maxTokens.Value = ai.MaxTokens;
            _maxTokensLabel.Text = ai.MaxTokens.ToString(CultureInfo.InvariantCulture);
            _temperature.Value = (int)(ai.Temperature * 10);
            _temperatureLabel.Text = ((int)(ai.Temperature * 10)).ToString(CultureInfo.InvariantCulture);
            _timeout.Value = ai.Timeout;
            _timeoutLabel.Text = ai.Timeout.ToString(CultureInfo.InvariantCulture);
            _retries.Value = ai.Retries;
            _retriesLabel.Text = ai.Retries.ToString(CultureInfo.InvariantCulture);

            _textToSql.IsChecked = ai.TextToSql;
            _explain.IsChecked = ai.Explain;
            _fix.IsChecked = ai.Fix;
            _optimize.IsChecked = ai.Optimize;
            _indexSuggestions.IsChecked = ai.IndexSuggestions;
            _chatPanel.IsChecked = ai.ChatPanel;
            _inlineCompletion.IsChecked = ai.InlineCompletion;
            _autoFixOnError.IsChecked = ai.AutoFixOnError;
        }

        public void Save(AppSettings settings)
        {
            // Key off the canonical id, never the index (FR-013) — the factory rejects anything else.
            var index = _provider.SelectedIndex;
            settings.Ai.Provider = index > 0 ? AiAssistancePage.Providers[index].Id : string.Empty;
            settings.Ai.Model = _model.Text ?? string.Empty;
            // FR-008: keys are DPAPI-wrapped at rest. An already-wrapped value is never re-wrapped.
            var keyText = _apiKey.Text ?? string.Empty;
            settings.Ai.ApiKey = ApiKeyProtector.IsProtected(keyText) ? keyText : ApiKeyProtector.Protect(keyText);
            settings.Ai.Endpoint = _endpoint.Text ?? string.Empty;
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
            settings.Ai.MaxTokens = (int)_maxTokens.Value;
            settings.Ai.Temperature = (int)_temperature.Value / 10.0;
            settings.Ai.Timeout = (int)_timeout.Value;
            settings.Ai.Retries = (int)_retries.Value;
            settings.Ai.Enabled = _provider.SelectedIndex > 0;
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
        /// unprefixed values through); a corrupt wrapped blob (e.g. a roamed profile) is dropped
        /// to empty rather than blocking the dialog — the same drop-and-continue rule as
        /// <c>SqlCredentialStore</c>.
        /// </summary>
        private static string UnwrapKeyForDisplay(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            try
            {
                return ApiKeyProtector.Unprotect(stored);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                Log.Warning(ex, "AiAssistancePage: stored API key could not be decrypted; clearing the field");
                return string.Empty;
            }
        }

        /// <summary>
        /// FR-009: sends the CURRENT dialog values to the engine's AiProviderTest handler — the
        /// user can verify a key before committing it. Busy state, never blocks the UI thread,
        /// re-enables in a finally; the key is never logged.
        /// </summary>
        private async System.Threading.Tasks.Task RunProviderTestAsync()
        {
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
                var (success, message) = await AiProviderTestRunner.RunAsync(
                    EngineRpcClientAccessor.Instance, request, waitSettings);
                _testResult.Text = message;
                _testResult.Foreground = success ? SuccessBrush : FailureBrush;
            }
            finally
            {
                _testButton.IsEnabled = true;
                _testButton.Content = "Test connection";
            }
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
