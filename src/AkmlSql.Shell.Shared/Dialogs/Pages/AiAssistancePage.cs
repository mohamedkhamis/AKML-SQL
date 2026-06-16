#nullable enable
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class AiAssistancePage : IPageBuilder
    {
        public string Key     => "AI Assistance";
        public string Display => "AI Assistance";
        public string Title   => "AI Assistance";
        public string Help    => "Connect an AI provider (Anthropic, OpenAI, Gemini, Ollama, and more) and tune the model, privacy mode, and request parameters that power AI features. Enable assistance such as natural-language-to-SQL, query explanation, error fixes, optimization, index suggestions, the chat panel, and inline ghost-text completions.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Provider Configuration");

            var (rowProvider, cboProvider) = ctx.Rows.AddDropdown(panel,
                "AI Provider",
                new[] { "(None)", "Anthropic", "OpenAI", "Azure OpenAI", "Gemini", "Ollama", "LM Studio", "Custom" },
                "Select the AI provider for SQL assistance features");
            ctx.RegisterSearch("AI Provider", "Select the AI provider for SQL assistance features", "Dropdown", rowProvider);

            var (rowModel, txtModel) = ctx.Rows.AddTextInput(panel,
                "Model", "e.g. gpt-4o, claude-sonnet-4-20250514, gemini-pro");
            ctx.RegisterSearch("Model", "e.g. gpt-4o, claude-sonnet-4-20250514, gemini-pro", "Text", rowModel);

            var (rowApiKey, txtApiKey) = ctx.Rows.AddTextInput(panel,
                "API Key", "Your API key for the selected provider", isPassword: true);
            ctx.RegisterSearch("API Key", "Your API key for the selected provider", "Text", rowApiKey);

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
                        "  —  example model: gemini-2.0-flash\n" +
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
                chkTextToSql, chkExplain, chkFix, chkOptimize, chkIndex, chkChat, chkInline, chkAutoFix);
        }
    }

    internal sealed class AiAssistanceControls : IPageControls
    {
        private readonly ComboBox _provider;
        private readonly TextBox _model;
        private readonly TextBox _apiKey;
        private readonly TextBox _endpoint;
        private readonly ComboBox _privacy;
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

        public AiAssistanceControls(
            ComboBox provider, TextBox model, TextBox apiKey, TextBox endpoint, ComboBox privacy,
            Slider sldMax, TextBlock lblMax, Slider sldTemp, TextBlock lblTemp,
            Slider sldTimeout, TextBlock lblTimeout, Slider sldRetries, TextBlock lblRetries,
            CheckBox textToSql, CheckBox explain, CheckBox fix, CheckBox optimize,
            CheckBox idx, CheckBox chat, CheckBox inline, CheckBox autoFix)
        {
            _provider = provider;
            _model = model;
            _apiKey = apiKey;
            _endpoint = endpoint;
            _privacy = privacy;
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
        }

        public void Load(AppSettings settings)
        {
            var ai = settings.Ai;
            _provider.SelectedIndex = (ai.Provider?.ToLowerInvariant()) switch
            {
                "anthropic"   => 1,
                "openai"      => 2,
                "azureopenai" => 3,
                "gemini"      => 4,
                "ollama"      => 5,
                "lmstudio"    => 6,
                "custom"      => 7,
                _             => 0,
            };
            _model.Text = ai.Model ?? string.Empty;
            _apiKey.Text = ai.ApiKey ?? string.Empty;
            _endpoint.Text = ai.Endpoint ?? string.Empty;
            _privacy.SelectedIndex = (ai.PrivacyMode?.ToLowerInvariant()) switch
            {
                "full"      => 1,
                "anonymous" => 2,
                "offline"   => 3,
                "disabled"  => 4,
                _           => 0, // schemaOnly
            };
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
            settings.Ai.Provider = _provider.SelectedIndex switch
            {
                1 => "Anthropic",
                2 => "OpenAI",
                3 => "AzureOpenAI",
                4 => "Gemini",
                5 => "Ollama",
                6 => "LMStudio",
                7 => "Custom",
                _ => "",
            };
            settings.Ai.Model = _model.Text ?? string.Empty;
            settings.Ai.ApiKey = _apiKey.Text ?? string.Empty;
            settings.Ai.Endpoint = _endpoint.Text ?? string.Empty;
            settings.Ai.PrivacyMode = _privacy.SelectedIndex switch
            {
                1 => "full",
                2 => "anonymous",
                3 => "offline",
                4 => "disabled",
                _ => "schemaOnly",
            };
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

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
