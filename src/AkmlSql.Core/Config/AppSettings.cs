using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AkmlSql.Core.Config
{
    public class AppSettings
    {
        public int ConfigVersion { get; set; } = 1;
        public bool AutoUpdateEnabled { get; set; } = true;
        public bool TelemetryEnabled { get; set; }
        public DateTimeOffset? LastUpdateCheck { get; set; }
        public string InstallId { get; set; } = Guid.NewGuid().ToString();
        public List<InstalledTarget> InstalledTargets { get; set; } = [];
        public IntelliSenseSettings IntelliSense { get; set; } = new IntelliSenseSettings();
        public CacheSettings Cache { get; set; } = new CacheSettings();

        [JsonPropertyName("formatter")]
        public FormatterSettings Formatter { get; set; } = new FormatterSettings();

        [JsonPropertyName("snippets")]
        public SnippetSettings Snippets { get; set; } = new SnippetSettings();

        /// <summary>
        /// T093-T095: Whether the user has been prompted about native IntelliSense conflict.
        /// </summary>
        public bool NativeIntelliSensePrompted { get; set; }

        /// <summary>
        /// T093-T095: Whether AKML SQL disabled native SSMS IntelliSense (for restore on uninstall).
        /// </summary>
        public bool DisabledNativeIntelliSense { get; set; }
    }

    public enum KeywordCaseOption
    {
        Upper,
        Lower,
        PascalCase,
        AsIs
    }

    public class IntelliSenseSettings
    {
        public bool Enabled { get; set; } = true;
        public bool AutoTrigger { get; set; } = true;
        public int TriggerDelayMs { get; set; } = 100;
        public bool AfterDot { get; set; } = true;
        public int MaxSuggestions { get; set; } = 50;
        public bool FuzzyMatch { get; set; } = true;
        public bool ShowDataTypes { get; set; } = true;
        public bool ShowNullability { get; set; } = true;
        public bool ShowPkFk { get; set; } = true;
        public bool AutoAlias { get; set; } = true;
        public bool JoinAssist { get; set; } = true;
        public KeywordCaseOption KeywordCase { get; set; } = KeywordCaseOption.Upper;
        public bool DisableNativeIntelliSense { get; set; } = true;
    }

    public class CacheSettings
    {
        public bool AutoRefresh { get; set; } = true;
        public int RefreshIntervalSeconds { get; set; } = 300;
        public bool DetectDdl { get; set; } = true;
        public int MaxDatabases { get; set; } = 10;
        public bool LazyLoadColumns { get; set; } = true;
        public bool PersistToDisk { get; set; } = true;
        public string PersistPath { get; set; } = string.Empty;
    }

    public class InstalledTarget
    {
        public string IdeType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string ExtensionsPath { get; set; } = string.Empty;
        public DateTimeOffset InstalledAt { get; set; }
    }

    public class FormatterSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("activeProfile")]
        public string ActiveProfile { get; set; } = "Default";

        [JsonPropertyName("formatOnPaste")]
        public bool FormatOnPaste { get; set; }

        [JsonPropertyName("formatOnSave")]
        public bool FormatOnSave { get; set; }

        [JsonPropertyName("formatOnDelimiter")]
        public bool FormatOnDelimiter { get; set; }

        [JsonPropertyName("shortcutKey")]
        public string ShortcutKey { get; set; } = "Ctrl+K, Y";

        [JsonPropertyName("showProfileInStatusBar")]
        public bool ShowProfileInStatusBar { get; set; } = true;

        [JsonPropertyName("confirmBulkFormat")]
        public bool ConfirmBulkFormat { get; set; } = true;

        [JsonPropertyName("createBackups")]
        public bool CreateBackups { get; set; } = true;

        [JsonPropertyName("respectNoformat")]
        public bool RespectNoformat { get; set; } = true;

        [JsonPropertyName("handleParseErrors")]
        public bool HandleParseErrors { get; set; } = true;

        [JsonPropertyName("semanticValidation")]
        public bool SemanticValidation { get; set; } = true;
    }

    public class SnippetSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("showInCompletion")]
        public bool ShowInCompletion { get; set; } = true;

        [JsonPropertyName("triggerKey")]
        public string TriggerKey { get; set; } = "Tab";

        [JsonPropertyName("formatOnExpand")]
        public bool FormatOnExpand { get; set; } = true;

        [JsonPropertyName("personalFolder")]
        public string PersonalFolder { get; set; } = string.Empty;

        [JsonPropertyName("teamFolder")]
        public string TeamFolder { get; set; } = string.Empty;

        [JsonPropertyName("contextFilter")]
        public bool ContextFilter { get; set; } = true;

        [JsonPropertyName("surroundShortcut")]
        public string SurroundShortcut { get; set; } = "Ctrl+K, Ctrl+S";

        [JsonPropertyName("trackUsage")]
        public bool TrackUsage { get; set; } = true;
    }
}
