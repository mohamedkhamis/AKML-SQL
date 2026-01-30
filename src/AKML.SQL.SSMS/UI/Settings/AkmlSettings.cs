using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AKML.SQL.SSMS.UI.Settings
{
    /// <summary>
    /// AKML-SQL settings model with defaults and validation.
    /// Implements Story 3.3: Settings Framework & Options Page.
    /// </summary>
    public class AkmlSettings : INotifyPropertyChanged
    {
        // IntelliSense settings
        private bool _enableIntelliSense = true;
        private bool _enableAutoComplete = true;
        private int _autoCompleteDelay = 150;
        private bool _enableFuzzyMatching = true;
        private int _maxCompletionItems = 50;
        private bool _showCompletionIcons = true;
        private bool _preSelectBestMatch = true;

        // Formatting settings
        private bool _formatOnSave = false;
        private bool _formatOnPaste = false;
        private bool _autoUppercaseKeywords = true;
        private int _indentSize = 4;
        private bool _useSpacesForTabs = true;

        // UI settings
        private bool _enableTabColoring = true;
        private bool _showStatusBarInfo = true;
        private string _themeName = "Auto";
        private double _popupOpacity = 1.0;
        private int _popupMaxHeight = 300;
        private int _popupWidth = 350;

        // Query features
        private bool _enableQueryHistory = true;
        private int _maxHistoryItems = 100;
        private bool _enableParameterSnippets = true;

        // Connection settings
        private int _connectionTimeout = 30;
        private int _metadataRefreshInterval = 300;
        private bool _autoRefreshMetadata = true;

        // Logging settings
        private string _logLevel = "Information";
        private int _logRetentionDays = 7;

        #region IntelliSense Settings

        /// <summary>
        /// Gets or sets whether IntelliSense is enabled.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Enable IntelliSense")]
        [Description("Enable or disable IntelliSense suggestions.")]
        [DefaultValue(true)]
        public bool EnableIntelliSense
        {
            get => _enableIntelliSense;
            set => SetProperty(ref _enableIntelliSense, value);
        }

        /// <summary>
        /// Gets or sets whether auto-completion is enabled.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Enable Auto-Complete")]
        [Description("Automatically show completion suggestions while typing.")]
        [DefaultValue(true)]
        public bool EnableAutoComplete
        {
            get => _enableAutoComplete;
            set => SetProperty(ref _enableAutoComplete, value);
        }

        /// <summary>
        /// Gets or sets the delay before showing auto-completion (in milliseconds).
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Auto-Complete Delay (ms)")]
        [Description("Delay in milliseconds before showing auto-completion. Range: 50-1000.")]
        [DefaultValue(150)]
        public int AutoCompleteDelay
        {
            get => _autoCompleteDelay;
            set => SetProperty(ref _autoCompleteDelay, Math.Max(50, Math.Min(1000, value)));
        }

        /// <summary>
        /// Gets or sets whether fuzzy matching is enabled for completions.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Enable Fuzzy Matching")]
        [Description("Allow fuzzy matching in completion filtering (e.g., 'slct' matches 'SELECT').")]
        [DefaultValue(true)]
        public bool EnableFuzzyMatching
        {
            get => _enableFuzzyMatching;
            set => SetProperty(ref _enableFuzzyMatching, value);
        }

        /// <summary>
        /// Gets or sets the maximum number of completion items to display.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Max Completion Items")]
        [Description("Maximum number of items to show in the completion list. Range: 10-200.")]
        [DefaultValue(50)]
        public int MaxCompletionItems
        {
            get => _maxCompletionItems;
            set => SetProperty(ref _maxCompletionItems, Math.Max(10, Math.Min(200, value)));
        }

        /// <summary>
        /// Gets or sets whether to show icons in the completion list.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Show Completion Icons")]
        [Description("Show icons next to completion items indicating their type.")]
        [DefaultValue(true)]
        public bool ShowCompletionIcons
        {
            get => _showCompletionIcons;
            set => SetProperty(ref _showCompletionIcons, value);
        }

        /// <summary>
        /// Gets or sets whether to pre-select the best matching item.
        /// </summary>
        [Category("IntelliSense")]
        [DisplayName("Pre-Select Best Match")]
        [Description("Automatically select the most relevant completion item.")]
        [DefaultValue(true)]
        public bool PreSelectBestMatch
        {
            get => _preSelectBestMatch;
            set => SetProperty(ref _preSelectBestMatch, value);
        }

        #endregion

        #region Formatting Settings

        /// <summary>
        /// Gets or sets whether to format SQL on save.
        /// </summary>
        [Category("Formatting")]
        [DisplayName("Format On Save")]
        [Description("Automatically format SQL code when saving the document.")]
        [DefaultValue(false)]
        public bool FormatOnSave
        {
            get => _formatOnSave;
            set => SetProperty(ref _formatOnSave, value);
        }

        /// <summary>
        /// Gets or sets whether to format SQL on paste.
        /// </summary>
        [Category("Formatting")]
        [DisplayName("Format On Paste")]
        [Description("Automatically format pasted SQL code.")]
        [DefaultValue(false)]
        public bool FormatOnPaste
        {
            get => _formatOnPaste;
            set => SetProperty(ref _formatOnPaste, value);
        }

        /// <summary>
        /// Gets or sets whether to auto-uppercase SQL keywords.
        /// </summary>
        [Category("Formatting")]
        [DisplayName("Auto Uppercase Keywords")]
        [Description("Automatically convert SQL keywords to uppercase.")]
        [DefaultValue(true)]
        public bool AutoUppercaseKeywords
        {
            get => _autoUppercaseKeywords;
            set => SetProperty(ref _autoUppercaseKeywords, value);
        }

        /// <summary>
        /// Gets or sets the indent size in spaces.
        /// </summary>
        [Category("Formatting")]
        [DisplayName("Indent Size")]
        [Description("Number of spaces for indentation. Range: 1-8.")]
        [DefaultValue(4)]
        public int IndentSize
        {
            get => _indentSize;
            set => SetProperty(ref _indentSize, Math.Max(1, Math.Min(8, value)));
        }

        /// <summary>
        /// Gets or sets whether to use spaces instead of tabs.
        /// </summary>
        [Category("Formatting")]
        [DisplayName("Use Spaces For Tabs")]
        [Description("Use spaces instead of tab characters for indentation.")]
        [DefaultValue(true)]
        public bool UseSpacesForTabs
        {
            get => _useSpacesForTabs;
            set => SetProperty(ref _useSpacesForTabs, value);
        }

        #endregion

        #region UI Settings

        /// <summary>
        /// Gets or sets whether to enable tab coloring.
        /// </summary>
        [Category("UI")]
        [DisplayName("Enable Tab Coloring")]
        [Description("Color-code editor tabs based on connection/database.")]
        [DefaultValue(true)]
        public bool EnableTabColoring
        {
            get => _enableTabColoring;
            set => SetProperty(ref _enableTabColoring, value);
        }

        /// <summary>
        /// Gets or sets whether to show status bar information.
        /// </summary>
        [Category("UI")]
        [DisplayName("Show Status Bar Info")]
        [Description("Show AKML-SQL status information in the status bar.")]
        [DefaultValue(true)]
        public bool ShowStatusBarInfo
        {
            get => _showStatusBarInfo;
            set => SetProperty(ref _showStatusBarInfo, value);
        }

        /// <summary>
        /// Gets or sets the theme name (Auto, Light, Dark).
        /// </summary>
        [Category("UI")]
        [DisplayName("Theme")]
        [Description("UI theme: Auto (follow VS), Light, or Dark.")]
        [DefaultValue("Auto")]
        public string ThemeName
        {
            get => _themeName;
            set
            {
                var validThemes = new[] { "Auto", "Light", "Dark" };
                var theme = Array.IndexOf(validThemes, value) >= 0 ? value : "Auto";
                SetProperty(ref _themeName, theme);
            }
        }

        /// <summary>
        /// Gets or sets the popup opacity.
        /// </summary>
        [Category("UI")]
        [DisplayName("Popup Opacity")]
        [Description("Opacity of the completion popup. Range: 0.5-1.0.")]
        [DefaultValue(1.0)]
        public double PopupOpacity
        {
            get => _popupOpacity;
            set => SetProperty(ref _popupOpacity, Math.Max(0.5, Math.Min(1.0, value)));
        }

        /// <summary>
        /// Gets or sets the popup max height.
        /// </summary>
        [Category("UI")]
        [DisplayName("Popup Max Height")]
        [Description("Maximum height of the completion popup in pixels. Range: 150-600.")]
        [DefaultValue(300)]
        public int PopupMaxHeight
        {
            get => _popupMaxHeight;
            set => SetProperty(ref _popupMaxHeight, Math.Max(150, Math.Min(600, value)));
        }

        /// <summary>
        /// Gets or sets the popup width.
        /// </summary>
        [Category("UI")]
        [DisplayName("Popup Width")]
        [Description("Width of the completion popup in pixels. Range: 250-600.")]
        [DefaultValue(350)]
        public int PopupWidth
        {
            get => _popupWidth;
            set => SetProperty(ref _popupWidth, Math.Max(250, Math.Min(600, value)));
        }

        #endregion

        #region Query Features

        /// <summary>
        /// Gets or sets whether to enable query history.
        /// </summary>
        [Category("Query")]
        [DisplayName("Enable Query History")]
        [Description("Keep track of recently executed queries.")]
        [DefaultValue(true)]
        public bool EnableQueryHistory
        {
            get => _enableQueryHistory;
            set => SetProperty(ref _enableQueryHistory, value);
        }

        /// <summary>
        /// Gets or sets the maximum number of history items.
        /// </summary>
        [Category("Query")]
        [DisplayName("Max History Items")]
        [Description("Maximum number of queries to keep in history. Range: 10-500.")]
        [DefaultValue(100)]
        public int MaxHistoryItems
        {
            get => _maxHistoryItems;
            set => SetProperty(ref _maxHistoryItems, Math.Max(10, Math.Min(500, value)));
        }

        /// <summary>
        /// Gets or sets whether to enable parameter snippets.
        /// </summary>
        [Category("Query")]
        [DisplayName("Enable Parameter Snippets")]
        [Description("Enable code snippets with parameter placeholders.")]
        [DefaultValue(true)]
        public bool EnableParameterSnippets
        {
            get => _enableParameterSnippets;
            set => SetProperty(ref _enableParameterSnippets, value);
        }

        #endregion

        #region Connection Settings

        /// <summary>
        /// Gets or sets the connection timeout in seconds.
        /// </summary>
        [Category("Connection")]
        [DisplayName("Connection Timeout (seconds)")]
        [Description("Timeout for database connections in seconds. Range: 5-120.")]
        [DefaultValue(30)]
        public int ConnectionTimeout
        {
            get => _connectionTimeout;
            set => SetProperty(ref _connectionTimeout, Math.Max(5, Math.Min(120, value)));
        }

        /// <summary>
        /// Gets or sets the metadata refresh interval in seconds.
        /// </summary>
        [Category("Connection")]
        [DisplayName("Metadata Refresh Interval (seconds)")]
        [Description("How often to refresh database metadata in seconds. Range: 60-3600.")]
        [DefaultValue(300)]
        public int MetadataRefreshInterval
        {
            get => _metadataRefreshInterval;
            set => SetProperty(ref _metadataRefreshInterval, Math.Max(60, Math.Min(3600, value)));
        }

        /// <summary>
        /// Gets or sets whether to auto-refresh metadata.
        /// </summary>
        [Category("Connection")]
        [DisplayName("Auto Refresh Metadata")]
        [Description("Automatically refresh database metadata periodically.")]
        [DefaultValue(true)]
        public bool AutoRefreshMetadata
        {
            get => _autoRefreshMetadata;
            set => SetProperty(ref _autoRefreshMetadata, value);
        }

        #endregion

        #region Logging Settings

        /// <summary>
        /// Gets or sets the log level.
        /// </summary>
        [Category("Logging")]
        [DisplayName("Log Level")]
        [Description("Minimum log level: Verbose, Debug, Information, Warning, Error.")]
        [DefaultValue("Information")]
        public string LogLevel
        {
            get => _logLevel;
            set
            {
                var validLevels = new[] { "Verbose", "Debug", "Information", "Warning", "Error" };
                var level = Array.IndexOf(validLevels, value) >= 0 ? value : "Information";
                SetProperty(ref _logLevel, level);
            }
        }

        /// <summary>
        /// Gets or sets the log retention period in days.
        /// </summary>
        [Category("Logging")]
        [DisplayName("Log Retention (days)")]
        [Description("Number of days to keep log files. Range: 1-30.")]
        [DefaultValue(7)]
        public int LogRetentionDays
        {
            get => _logRetentionDays;
            set => SetProperty(ref _logRetentionDays, Math.Max(1, Math.Min(30, value)));
        }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a deep copy of the settings.
        /// </summary>
        public AkmlSettings Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<AkmlSettings>(json) ?? new AkmlSettings();
        }

        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public void ResetToDefaults()
        {
            EnableIntelliSense = true;
            EnableAutoComplete = true;
            AutoCompleteDelay = 150;
            EnableFuzzyMatching = true;
            MaxCompletionItems = 50;
            ShowCompletionIcons = true;
            PreSelectBestMatch = true;

            FormatOnSave = false;
            FormatOnPaste = false;
            AutoUppercaseKeywords = true;
            IndentSize = 4;
            UseSpacesForTabs = true;

            EnableTabColoring = true;
            ShowStatusBarInfo = true;
            ThemeName = "Auto";
            PopupOpacity = 1.0;
            PopupMaxHeight = 300;
            PopupWidth = 350;

            EnableQueryHistory = true;
            MaxHistoryItems = 100;
            EnableParameterSnippets = true;

            ConnectionTimeout = 30;
            MetadataRefreshInterval = 300;
            AutoRefreshMetadata = true;

            LogLevel = "Information";
            LogRetentionDays = 7;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Event raised when any setting changes.
        /// </summary>
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;

            var oldValue = field;
            field = value;
            OnPropertyChanged(propertyName);
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(propertyName ?? string.Empty, oldValue, value));
            return true;
        }

        #endregion
    }

    /// <summary>
    /// Event args for setting changes.
    /// </summary>
    public class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(string settingName, object? oldValue, object? newValue)
        {
            SettingName = settingName;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public string SettingName { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }
    }
}
