using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
// ReSharper disable UnusedMember.Global

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Root configuration POCO for AKML SQL, persisted to <c>%AppData%\AKML SQL\config.json</c>.
    /// Loaded and saved via <see cref="ConfigManager"/>. All settings have safe defaults so the
    /// file can be missing or partially populated.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class AppSettings
    {
        /// <summary>Schema version for future migrations. Currently <c>1</c>.</summary>
        public int ConfigVersion { get; set; } = 1;

        /// <summary>When <c>true</c>, the shell checks for updates on startup.</summary>
        public bool AutoUpdateEnabled { get; set; } = true;

        /// <summary>Reserved for future telemetry opt-in. Defaults to <c>false</c>.</summary>
        public bool TelemetryEnabled { get; set; }

        /// <summary>ISO 8601 timestamp of the last successful update check. <c>null</c> if never checked.</summary>
        public DateTimeOffset? LastUpdateCheck { get; set; }

        /// <summary>Anonymous installation GUID. Generated once on first run.</summary>
        public string InstallId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Records which IDE targets AKML SQL has been installed to.</summary>
        public List<InstalledTarget> InstalledTargets { get; set; } = [];

        /// <summary>IntelliSense feature configuration.</summary>
        public IntelliSenseSettings IntelliSense { get; set; } = new();

        /// <summary>Schema cache configuration.</summary>
        public CacheSettings Cache { get; set; } = new();

        [JsonPropertyName("formatter")]
        public FormatterSettings Formatter { get; set; } = new();

        [JsonPropertyName("snippets")]
        public SnippetSettings Snippets { get; set; } = new();

        [JsonPropertyName("codeAnalysis")]
        public CodeAnalysisSettings CodeAnalysis { get; set; } = new();

        [JsonPropertyName("refactoring")]
        public RefactoringSettings Refactoring { get; set; } = new();

        /// <summary>SQL History recording and storage configuration (Phase 7).</summary>
        [JsonPropertyName("history")]
        public HistorySettings History { get; set; } = new();

        /// <summary>Tab management, coloring, and session recovery configuration (Phase 7).</summary>
        [JsonPropertyName("tabs")]
        public TabSettings Tabs { get; set; } = new();

        /// <summary>Execution safety warnings and transaction reminders (Phase 7).</summary>
        [JsonPropertyName("safety")]
        public SafetySettings Safety { get; set; } = new();

        /// <summary>
        /// T093-T095: Whether the user has been prompted about native IntelliSense conflict.
        /// </summary>
        public bool NativeIntelliSensePrompted { get; set; }

        /// <summary>
        /// T093-T095: Whether AKML SQL disabled native SSMS IntelliSense (for restore on uninstall).
        /// </summary>
        public bool DisabledNativeIntelliSense { get; set; }

        /// <summary>
        /// Minimum log level for the rolling file sink.
        /// Valid values: Verbose, Debug, Information, Warning, Error, Fatal.
        /// Defaults to Debug.
        /// </summary>
        [JsonPropertyName("logMinimumLevel")]
        public string LogMinimumLevel { get; set; } = "Debug";
    }

    /// <summary>Keyword casing mode for IntelliSense completions and the formatter.</summary>
    public enum KeywordCaseOption
    {
        /// <summary>ALL CAPS (e.g. <c>SELECT</c>).</summary>
        Upper,
        /// <summary>all lowercase (e.g. <c>select</c>).</summary>
        Lower,
        /// <summary>First letter capitalised (e.g. <c>Select</c>).</summary>
        PascalCase,
        /// <summary>Preserve the original casing from the source.</summary>
        AsIs
    }

    /// <summary>Settings for the IntelliSense completion engine.</summary>
    public class IntelliSenseSettings
    {
        /// <summary>Master switch — disabling this suppresses all IntelliSense features.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Show completion list automatically while typing (no Ctrl+Space required).</summary>
        public bool AutoTrigger { get; set; } = true;
        /// <summary>Debounce delay in milliseconds before triggering auto-completion.</summary>
        public int TriggerDelayMs { get; set; } = 100;
        /// <summary>Auto-trigger after typing <c>.</c> for table.column completion.</summary>
        public bool AfterDot { get; set; } = true;
        /// <summary>Maximum number of items in the completion list.</summary>
        public int MaxSuggestions { get; set; } = 50;
        /// <summary>Enable fuzzy/substring matching in addition to prefix matching.</summary>
        public bool FuzzyMatch { get; set; } = true;
        /// <summary>Show column data types in completion details.</summary>
        public bool ShowDataTypes { get; set; } = true;
        /// <summary>Show NOT NULL / NULL nullability in completion details.</summary>
        public bool ShowNullability { get; set; } = true;
        /// <summary>Show PK/FK badge indicators in completion details.</summary>
        public bool ShowPkFk { get; set; } = true;
        /// <summary>Suggest automatic table aliases when completing table names.</summary>
        public bool AutoAlias { get; set; } = true;
        /// <summary>Suggest JOIN conditions based on foreign key relationships.</summary>
        public bool JoinAssist { get; set; } = true;
        /// <summary>Keyword casing applied to completions inserted into the editor.</summary>
        public KeywordCaseOption KeywordCase { get; set; } = KeywordCaseOption.Upper;
        /// <summary>Whether to disable native SSMS IntelliSense to avoid conflicts.</summary>
        public bool DisableNativeIntelliSense { get; set; } = true;
    }

    /// <summary>Settings for the in-memory schema cache.</summary>
    public class CacheSettings
    {
        /// <summary>Periodically check for schema changes.</summary>
        public bool AutoRefresh { get; set; } = true;
        /// <summary>Interval in seconds between change-detection checks (shell-side timer; engine uses 60 s internally).</summary>
        public int RefreshIntervalSeconds { get; set; } = 300;
        /// <summary>Trigger an immediate Phase A refresh when a DDL statement is executed.</summary>
        public bool DetectDdl { get; set; } = true;
        /// <summary>Maximum number of server:database caches kept in memory; LRU eviction applies beyond this limit.</summary>
        public int MaxDatabases { get; set; } = 10;
        /// <summary>Load columns and FKs in Phase B (background) rather than blocking Phase A.</summary>
        public bool LazyLoadColumns { get; set; } = true;
        /// <summary>Persist schema cache to disk across sessions.</summary>
        public bool PersistToDisk { get; set; } = true;
        /// <summary>Override the cache directory path. Empty string = <c>%LocalAppData%\AKML SQL\cache</c>.</summary>
        public string PersistPath { get; set; } = string.Empty;
    }

    /// <summary>Records an IDE target that AKML SQL has been deployed to.</summary>
    public class InstalledTarget
    {
        /// <summary>IDE type identifier (e.g. <c>"SSMS20"</c>, <c>"VS2022"</c>).</summary>
        public string IdeType { get; set; } = string.Empty;
        /// <summary>IDE version string.</summary>
        public string Version { get; set; } = string.Empty;
        /// <summary>Platform architecture (<c>"x86"</c> or <c>"x64"</c>).</summary>
        public string Architecture { get; set; } = string.Empty;
        /// <summary>Absolute path to the extensions directory where files were deployed.</summary>
        public string ExtensionsPath { get; set; } = string.Empty;
        /// <summary>Timestamp when this target was installed.</summary>
        public DateTimeOffset InstalledAt { get; set; }
    }

    /// <summary>Settings for the SQL formatter feature.</summary>
    public class FormatterSettings
    {
        /// <summary>Master switch — disabling this suppresses all formatting triggers.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Name of the active formatting profile (<c>.akmlstyle</c> file).</summary>
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

    /// <summary>Settings for the SQL snippet feature.</summary>
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

    /// <summary>Settings for the static code analysis engine.</summary>
    public class CodeAnalysisSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("runOnType")]
        public bool RunOnType { get; set; } = true;

        [JsonPropertyName("runOnSave")]
        public bool RunOnSave { get; set; } = true;

        [JsonPropertyName("autoFixOnFormat")]
        public bool AutoFixOnFormat { get; set; }

        [JsonPropertyName("squiggleStyle")]
        public string SquiggleStyle { get; set; } = "underline";

        [JsonPropertyName("showInErrorList")]
        public bool ShowInErrorList { get; set; } = true;
    }

    /// <summary>Settings for the refactoring engine.</summary>
    public class RefactoringSettings
    {
        [JsonPropertyName("previewBeforeApply")]
        public bool PreviewBeforeApply { get; set; } = true;

        [JsonPropertyName("createBackups")]
        public bool CreateBackups { get; set; } = true;

        [JsonPropertyName("formatAfterRefactor")]
        public bool FormatAfterRefactor { get; set; } = true;

        /// <summary>
        /// Default scope for Safe Rename. Valid values: "currentScript", "projectDirectory".
        /// </summary>
        [JsonPropertyName("renameScope")]
        public string RenameScope { get; set; } = "currentScript";

        [JsonPropertyName("includeCommentsInRename")]
        public bool IncludeCommentsInRename { get; set; } = true;

        [JsonPropertyName("includeStringLiteralsInRename")]
        public bool IncludeStringLiteralsInRename { get; set; }
    }

    /// <summary>Settings for the SQL History feature (Phase 7).</summary>
    public class HistorySettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("retentionDays")]
        public int RetentionDays { get; set; } = 90;

        [JsonPropertyName("maxEntries")]
        public int MaxEntries { get; set; } = 100_000;

        [JsonPropertyName("encryptAtRest")]
        public bool EncryptAtRest { get; set; }

        [JsonPropertyName("recordFailures")]
        public bool RecordFailures { get; set; } = true;

        [JsonPropertyName("deduplication")]
        public bool Deduplication { get; set; } = true;

        [JsonPropertyName("shortcut")]
        public string Shortcut { get; set; } = "Ctrl+Alt+H";
    }

    /// <summary>Settings for tab management and session recovery (Phase 7).</summary>
    public class TabSettings
    {
        [JsonPropertyName("coloringEnabled")]
        public bool ColoringEnabled { get; set; } = true;

        [JsonPropertyName("coloringRules")]
        public List<ColoringRule> ColoringRules { get; set; } =
        [
            new() { Order = 0, Pattern = "*PROD*,*LIVE*", MatchTarget = "serverName", Color = "#FF4444", Label = "PRODUCTION" },
            new() { Order = 1, Pattern = "*STG*,*UAT*,*STAGING*", MatchTarget = "serverName", Color = "#FFB800", Label = "STAGING" },
            new() { Order = 2, Pattern = "*DEV*,*LOCAL*,localhost,(local)", MatchTarget = "serverName", Color = "#44BB44", Label = "DEV" },
            new() { Order = 3, Pattern = "*.database.windows.net", MatchTarget = "serverName", Color = "#4488FF", Label = "AZURE" }
        ];

        [JsonPropertyName("sessionRecovery")]
        public bool SessionRecovery { get; set; } = true;

        [JsonPropertyName("autoSaveInterval")]
        public int AutoSaveInterval { get; set; } = 60;

        [JsonPropertyName("restoreOnStartup")]
        public string RestoreOnStartup { get; set; } = "prompt";

        [JsonPropertyName("maxClosedTabs")]
        public int MaxClosedTabs { get; set; } = 20;

        [JsonPropertyName("customWindowTitle")]
        public string CustomWindowTitle { get; set; } = "{server} - {database} - SSMS";
    }

    /// <summary>Configuration for a single tab coloring environment rule.</summary>
    public class ColoringRule
    {
        [JsonPropertyName("order")]
        public int Order { get; set; }

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("matchTarget")]
        public string MatchTarget { get; set; } = "serverName";

        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Settings for execution safety warnings (Phase 7).</summary>
    public class SafetySettings
    {
        [JsonPropertyName("productionWarning")]
        public bool ProductionWarning { get; set; } = true;

        [JsonPropertyName("deleteWithoutWhere")]
        public bool DeleteWithoutWhere { get; set; } = true;

        [JsonPropertyName("updateWithoutWhere")]
        public bool UpdateWithoutWhere { get; set; } = true;

        [JsonPropertyName("dropConfirmation")]
        public bool DropConfirmation { get; set; } = true;

        [JsonPropertyName("truncateConfirmation")]
        public bool TruncateConfirmation { get; set; } = true;

        [JsonPropertyName("transactionReminder")]
        public bool TransactionReminder { get; set; } = true;

        [JsonPropertyName("transactionReminderInterval")]
        public int TransactionReminderInterval { get; set; } = 300;
    }
}
