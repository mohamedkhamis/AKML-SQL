using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AKML.SQL.SSMS.UI.Settings
{
    /// <summary>
    /// Options page for AKML-SQL settings in VS Tools > Options.
    /// Implements Story 3.3: Settings Framework & Options Page.
    /// </summary>
    [ComVisible(true)]
    [Guid("B8A9C4D5-E6F7-4890-ABCD-123456789ABC")]
    public class AkmlOptionsPage : DialogPage
    {
        private static readonly ILogger _logger = Log.Logger;
        private AkmlSettings? _workingSettings;

        public AkmlOptionsPage()
        {
            _logger.Debug("[AkmlOptionsPage] Created");
        }

        #region IntelliSense Settings

        [Category("IntelliSense")]
        [DisplayName("Enable IntelliSense")]
        [Description("Enable or disable IntelliSense suggestions.")]
        [DefaultValue(true)]
        public bool EnableIntelliSense
        {
            get => GetWorkingSettings().EnableIntelliSense;
            set => GetWorkingSettings().EnableIntelliSense = value;
        }

        [Category("IntelliSense")]
        [DisplayName("Enable Auto-Complete")]
        [Description("Automatically show completion suggestions while typing.")]
        [DefaultValue(true)]
        public bool EnableAutoComplete
        {
            get => GetWorkingSettings().EnableAutoComplete;
            set => GetWorkingSettings().EnableAutoComplete = value;
        }

        [Category("IntelliSense")]
        [DisplayName("Auto-Complete Delay (ms)")]
        [Description("Delay in milliseconds before showing auto-completion. Range: 50-1000.")]
        [DefaultValue(150)]
        public int AutoCompleteDelay
        {
            get => GetWorkingSettings().AutoCompleteDelay;
            set => GetWorkingSettings().AutoCompleteDelay = value;
        }

        [Category("IntelliSense")]
        [DisplayName("Enable Fuzzy Matching")]
        [Description("Allow fuzzy matching in completion filtering (e.g., 'slct' matches 'SELECT').")]
        [DefaultValue(true)]
        public bool EnableFuzzyMatching
        {
            get => GetWorkingSettings().EnableFuzzyMatching;
            set => GetWorkingSettings().EnableFuzzyMatching = value;
        }

        [Category("IntelliSense")]
        [DisplayName("Max Completion Items")]
        [Description("Maximum number of items to show in the completion list. Range: 10-200.")]
        [DefaultValue(50)]
        public int MaxCompletionItems
        {
            get => GetWorkingSettings().MaxCompletionItems;
            set => GetWorkingSettings().MaxCompletionItems = value;
        }

        [Category("IntelliSense")]
        [DisplayName("Show Completion Icons")]
        [Description("Show icons next to completion items indicating their type.")]
        [DefaultValue(true)]
        public bool ShowCompletionIcons
        {
            get => GetWorkingSettings().ShowCompletionIcons;
            set => GetWorkingSettings().ShowCompletionIcons = value;
        }

        #endregion

        #region Formatting Settings

        [Category("Formatting")]
        [DisplayName("Format On Save")]
        [Description("Automatically format SQL code when saving the document.")]
        [DefaultValue(false)]
        public bool FormatOnSave
        {
            get => GetWorkingSettings().FormatOnSave;
            set => GetWorkingSettings().FormatOnSave = value;
        }

        [Category("Formatting")]
        [DisplayName("Auto Uppercase Keywords")]
        [Description("Automatically convert SQL keywords to uppercase.")]
        [DefaultValue(true)]
        public bool AutoUppercaseKeywords
        {
            get => GetWorkingSettings().AutoUppercaseKeywords;
            set => GetWorkingSettings().AutoUppercaseKeywords = value;
        }

        [Category("Formatting")]
        [DisplayName("Indent Size")]
        [Description("Number of spaces for indentation. Range: 1-8.")]
        [DefaultValue(4)]
        public int IndentSize
        {
            get => GetWorkingSettings().IndentSize;
            set => GetWorkingSettings().IndentSize = value;
        }

        #endregion

        #region UI Settings

        [Category("UI")]
        [DisplayName("Enable Tab Coloring")]
        [Description("Color-code editor tabs based on connection/database.")]
        [DefaultValue(true)]
        public bool EnableTabColoring
        {
            get => GetWorkingSettings().EnableTabColoring;
            set => GetWorkingSettings().EnableTabColoring = value;
        }

        [Category("UI")]
        [DisplayName("Popup Max Height")]
        [Description("Maximum height of the completion popup in pixels. Range: 150-600.")]
        [DefaultValue(300)]
        public int PopupMaxHeight
        {
            get => GetWorkingSettings().PopupMaxHeight;
            set => GetWorkingSettings().PopupMaxHeight = value;
        }

        [Category("UI")]
        [DisplayName("Popup Width")]
        [Description("Width of the completion popup in pixels. Range: 250-600.")]
        [DefaultValue(350)]
        public int PopupWidth
        {
            get => GetWorkingSettings().PopupWidth;
            set => GetWorkingSettings().PopupWidth = value;
        }

        #endregion

        #region Query Settings

        [Category("Query")]
        [DisplayName("Enable Query History")]
        [Description("Keep track of recently executed queries.")]
        [DefaultValue(true)]
        public bool EnableQueryHistory
        {
            get => GetWorkingSettings().EnableQueryHistory;
            set => GetWorkingSettings().EnableQueryHistory = value;
        }

        [Category("Query")]
        [DisplayName("Max History Items")]
        [Description("Maximum number of queries to keep in history. Range: 10-500.")]
        [DefaultValue(100)]
        public int MaxHistoryItems
        {
            get => GetWorkingSettings().MaxHistoryItems;
            set => GetWorkingSettings().MaxHistoryItems = value;
        }

        #endregion

        #region Connection Settings

        [Category("Connection")]
        [DisplayName("Connection Timeout (seconds)")]
        [Description("Timeout for database connections in seconds. Range: 5-120.")]
        [DefaultValue(30)]
        public int ConnectionTimeout
        {
            get => GetWorkingSettings().ConnectionTimeout;
            set => GetWorkingSettings().ConnectionTimeout = value;
        }

        [Category("Connection")]
        [DisplayName("Auto Refresh Metadata")]
        [Description("Automatically refresh database metadata periodically.")]
        [DefaultValue(true)]
        public bool AutoRefreshMetadata
        {
            get => GetWorkingSettings().AutoRefreshMetadata;
            set => GetWorkingSettings().AutoRefreshMetadata = value;
        }

        #endregion

        #region Logging Settings

        [Category("Logging")]
        [DisplayName("Log Level")]
        [Description("Minimum log level: Verbose, Debug, Information, Warning, Error.")]
        [DefaultValue("Information")]
        [TypeConverter(typeof(LogLevelTypeConverter))]
        public string LogLevel
        {
            get => GetWorkingSettings().LogLevel;
            set => GetWorkingSettings().LogLevel = value;
        }

        [Category("Logging")]
        [DisplayName("Log Retention (days)")]
        [Description("Number of days to keep log files. Range: 1-30.")]
        [DefaultValue(7)]
        public int LogRetentionDays
        {
            get => GetWorkingSettings().LogRetentionDays;
            set => GetWorkingSettings().LogRetentionDays = value;
        }

        #endregion

        #region DialogPage Overrides

        protected override void OnActivate(CancelEventArgs e)
        {
            base.OnActivate(e);

            // Create a working copy of settings when the page is activated
            _workingSettings = SettingsService.Instance.Settings.Clone();
            _logger.Debug("[AkmlOptionsPage] Activated, created working copy of settings");
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);

            if (e.ApplyBehavior == ApplyKind.Apply && _workingSettings != null)
            {
                // Copy working settings to actual settings
                CopySettings(_workingSettings, SettingsService.Instance.Settings);
                SettingsService.Instance.SaveSettings();
                _logger.Information("[AkmlOptionsPage] Settings applied and saved");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _workingSettings = null;
            _logger.Debug("[AkmlOptionsPage] Closed");
        }

        public override void ResetSettings()
        {
            base.ResetSettings();

            _workingSettings = new AkmlSettings();
            _logger.Information("[AkmlOptionsPage] Settings reset to defaults");
        }

        #endregion

        #region Helper Methods

        private AkmlSettings GetWorkingSettings()
        {
            return _workingSettings ?? SettingsService.Instance.Settings;
        }

        private static void CopySettings(AkmlSettings source, AkmlSettings target)
        {
            // IntelliSense
            target.EnableIntelliSense = source.EnableIntelliSense;
            target.EnableAutoComplete = source.EnableAutoComplete;
            target.AutoCompleteDelay = source.AutoCompleteDelay;
            target.EnableFuzzyMatching = source.EnableFuzzyMatching;
            target.MaxCompletionItems = source.MaxCompletionItems;
            target.ShowCompletionIcons = source.ShowCompletionIcons;
            target.PreSelectBestMatch = source.PreSelectBestMatch;

            // Formatting
            target.FormatOnSave = source.FormatOnSave;
            target.FormatOnPaste = source.FormatOnPaste;
            target.AutoUppercaseKeywords = source.AutoUppercaseKeywords;
            target.IndentSize = source.IndentSize;
            target.UseSpacesForTabs = source.UseSpacesForTabs;

            // UI
            target.EnableTabColoring = source.EnableTabColoring;
            target.ShowStatusBarInfo = source.ShowStatusBarInfo;
            target.ThemeName = source.ThemeName;
            target.PopupOpacity = source.PopupOpacity;
            target.PopupMaxHeight = source.PopupMaxHeight;
            target.PopupWidth = source.PopupWidth;

            // Query
            target.EnableQueryHistory = source.EnableQueryHistory;
            target.MaxHistoryItems = source.MaxHistoryItems;
            target.EnableParameterSnippets = source.EnableParameterSnippets;

            // Connection
            target.ConnectionTimeout = source.ConnectionTimeout;
            target.MetadataRefreshInterval = source.MetadataRefreshInterval;
            target.AutoRefreshMetadata = source.AutoRefreshMetadata;

            // Logging
            target.LogLevel = source.LogLevel;
            target.LogRetentionDays = source.LogRetentionDays;
        }

        #endregion
    }

    /// <summary>
    /// Type converter for log level dropdown in options page.
    /// </summary>
    public class LogLevelTypeConverter : StringConverter
    {
        private static readonly string[] _logLevels = { "Verbose", "Debug", "Information", "Warning", "Error" };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            return new StandardValuesCollection(_logLevels);
        }
    }

    /// <summary>
    /// Advanced options page for AKML-SQL.
    /// </summary>
    [ComVisible(true)]
    [Guid("C9BAD5E6-F7A8-4901-BCDE-234567890ABC")]
    public class AkmlAdvancedOptionsPage : DialogPage
    {
        private static readonly ILogger _logger = Log.Logger;

        [Category("Advanced")]
        [DisplayName("Metadata Refresh Interval (seconds)")]
        [Description("How often to refresh database metadata in seconds. Range: 60-3600.")]
        [DefaultValue(300)]
        public int MetadataRefreshInterval
        {
            get => SettingsService.Instance.Settings.MetadataRefreshInterval;
            set => SettingsService.Instance.Settings.MetadataRefreshInterval = value;
        }

        [Category("Advanced")]
        [DisplayName("Format On Paste")]
        [Description("Automatically format pasted SQL code.")]
        [DefaultValue(false)]
        public bool FormatOnPaste
        {
            get => SettingsService.Instance.Settings.FormatOnPaste;
            set => SettingsService.Instance.Settings.FormatOnPaste = value;
        }

        [Category("Advanced")]
        [DisplayName("Enable Parameter Snippets")]
        [Description("Enable code snippets with parameter placeholders.")]
        [DefaultValue(true)]
        public bool EnableParameterSnippets
        {
            get => SettingsService.Instance.Settings.EnableParameterSnippets;
            set => SettingsService.Instance.Settings.EnableParameterSnippets = value;
        }

        [Category("Advanced")]
        [DisplayName("Pre-Select Best Match")]
        [Description("Automatically select the most relevant completion item.")]
        [DefaultValue(true)]
        public bool PreSelectBestMatch
        {
            get => SettingsService.Instance.Settings.PreSelectBestMatch;
            set => SettingsService.Instance.Settings.PreSelectBestMatch = value;
        }

        [Category("Advanced")]
        [DisplayName("Use Spaces For Tabs")]
        [Description("Use spaces instead of tab characters for indentation.")]
        [DefaultValue(true)]
        public bool UseSpacesForTabs
        {
            get => SettingsService.Instance.Settings.UseSpacesForTabs;
            set => SettingsService.Instance.Settings.UseSpacesForTabs = value;
        }

        [Category("Advanced")]
        [DisplayName("Theme")]
        [Description("UI theme: Auto (follow VS), Light, or Dark.")]
        [DefaultValue("Auto")]
        [TypeConverter(typeof(ThemeTypeConverter))]
        public string ThemeName
        {
            get => SettingsService.Instance.Settings.ThemeName;
            set => SettingsService.Instance.Settings.ThemeName = value;
        }

        [Category("Advanced")]
        [DisplayName("Popup Opacity")]
        [Description("Opacity of the completion popup. Range: 0.5-1.0.")]
        [DefaultValue(1.0)]
        public double PopupOpacity
        {
            get => SettingsService.Instance.Settings.PopupOpacity;
            set => SettingsService.Instance.Settings.PopupOpacity = value;
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);

            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                SettingsService.Instance.SaveSettings();
                _logger.Information("[AkmlAdvancedOptionsPage] Settings applied and saved");
            }
        }
    }

    /// <summary>
    /// Type converter for theme dropdown in options page.
    /// </summary>
    public class ThemeTypeConverter : StringConverter
    {
        private static readonly string[] _themes = { "Auto", "Light", "Dark" };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            return new StandardValuesCollection(_themes);
        }
    }
}
