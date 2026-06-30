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

        /// <summary>
        /// UI theme for AKML SQL dialogs. Valid values: "dark", "light", "system".
        /// </summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "light";

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

        /// <summary>Results grid productivity settings (Phase 8).</summary>
        [JsonPropertyName("grid")]
        public GridSettings Grid { get; set; } = new();

        /// <summary>Editor productivity settings (Phase 8).</summary>
        [JsonPropertyName("editorProductivity")]
        public EditorProductivitySettings EditorProductivity { get; set; } = new();

        /// <summary>Execution productivity settings (Phase 8).</summary>
        [JsonPropertyName("executionProductivity")]
        public ExecutionProductivitySettings ExecutionProductivity { get; set; } = new();

        /// <summary>Navigation settings (Phase 8).</summary>
        [JsonPropertyName("navigation")]
        public NavigationSettings Navigation { get; set; } = new();

        /// <summary>Command Palette usage tracking (Phase 8).</summary>
        [JsonPropertyName("commandPalette")]
        public CommandPaletteSettings CommandPalette { get; set; } = new();

        /// <summary>AI assistance settings (Phase 9).</summary>
        [JsonPropertyName("ai")]
        public AiSettings Ai { get; set; } = new();

        /// <summary>
        /// Completion popup polish (spec 014, US19, US2, US8): MS_Description tooltips,
        /// parameter highlighting, encrypted-object decryption, temp-table IntelliSense,
        /// custom ALTER/INSERT templates, object definition box size, column picker
        /// sort default. Existing fields like SpaceCommits / DotCommits live in
        /// <see cref="IntelliSenseSettings"/> and are unchanged.
        /// </summary>
        [JsonPropertyName("completionPolish")]
        public CompletionPolishSettings CompletionPolish { get; set; } = new();

        /// <summary>
        /// T093-T095: Whether the user has been prompted about native IntelliSense conflict.
        /// </summary>
        public bool NativeIntelliSensePrompted { get; set; }

        /// <summary>
        /// T093-T095: Whether AKML SQL disabled native SSMS IntelliSense (for restore on uninstall).
        /// </summary>
        public bool DisabledNativeIntelliSense { get; set; }

        [JsonPropertyName("labs")]
        public LabsSettings Labs { get; set; } = new();

        /// <summary>
        /// Spec 025 (M3 bridge closure) FR-027: WebSocket-bridge transport composition.
        /// When <see cref="BridgeOptions.Enabled"/> is <c>true</c>, the engine host starts
        /// a <c>WebSocketTransport</c> alongside the existing <c>NamedPipeTransport</c>;
        /// both share the same <c>RpcRouter</c> so SSMS plugin and web edition serve
        /// identical handler chains. When the section is absent or disabled, only the
        /// named pipe runs (IDE-plugin-only behaviour unchanged).
        /// </summary>
        [JsonPropertyName("bridge")]
        public BridgeOptions Bridge { get; set; } = new();

        /// <summary>
        /// Minimum log level for the rolling file sink.
        /// Valid values: Verbose, Debug, Information, Warning, Error, Fatal.
        /// Defaults to Debug.
        /// </summary>
        [JsonPropertyName("logMinimumLevel")]
        public string LogMinimumLevel { get; set; } = "Debug";
    }

    /// <summary>
    /// Spec 025 (M3 bridge closure) FR-027: configuration for the engine's WebSocket
    /// bridge. When <see cref="Enabled"/> is <c>true</c>, the engine host composes a
    /// <c>WebSocketTransport</c> from this section alongside the existing named-pipe
    /// transport — both share the same <c>RpcRouter</c> so the SSMS plugin and the web
    /// edition serve identical handler chains. Field shapes mirror
    /// <c>WebSocketTransportOptions</c> in <c>AkmlSql.Engine.Transports</c>.
    /// </summary>
    public class BridgeOptions
    {
        /// <summary>
        /// Master switch. When <c>false</c> (the default), the engine runs the named-pipe
        /// transport only — byte-for-byte identical to the IDE-plugin-only deployment.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// IP address to bind. <c>"127.0.0.1"</c> for localhost mode (no TLS required);
        /// <c>"0.0.0.0"</c> for LAN mode (TLS REQUIRED — see <see cref="TlsCertPath"/>).
        /// </summary>
        [JsonPropertyName("bindAddress")]
        public string BindAddress { get; set; } = "127.0.0.1";

        /// <summary>TCP port. Default <c>47291</c>.</summary>
        [JsonPropertyName("port")]
        public int Port { get; set; } = 47291;

        /// <summary>
        /// Absolute path to the LAN-mode self-signed TLS certificate. Accepts either:
        /// <list type="bullet">
        ///   <item><c>.cer</c> — the installer default at
        ///         <c>%ProgramData%/AKML SQL Web/certs/bridge.cer</c>. The installer's
        ///         <c>web-tls-setup.ps1</c> generates only this public-part file
        ///         because the LocalMachine\My private key is NonExportable.</item>
        ///   <item><c>.pfx</c> — user-supplied path with embedded private key.</item>
        /// </list>
        /// Only the thumbprint is read; the private key is held by the LocalMachine
        /// cert store referenced via the netsh sslcert binding. Required when
        /// <see cref="BindAddress"/> is non-loopback.
        /// </summary>
        [JsonPropertyName("tlsCertPath")]
        public string TlsCertPath { get; set; } = string.Empty;

        /// <summary>
        /// Environment-variable name carrying the PFX password. Kept out of
        /// <c>config.json</c> so the password is never on disk in plain.
        /// </summary>
        [JsonPropertyName("tlsCertPasswordRef")]
        public string? TlsCertPasswordRef { get; set; }

        /// <summary>
        /// Absolute path to the bearer-token store. Default
        /// <c>%CommonAppData%/AKML SQL Web/tokens.json</c>.
        /// </summary>
        [JsonPropertyName("tokenStorePath")]
        public string TokenStorePath { get; set; } = string.Empty;

        /// <summary>Bearer-token TTL in days. Default <c>90</c>.</summary>
        [JsonPropertyName("tokenTtlDays")]
        public int TokenTtlDays { get; set; } = 90;

        /// <summary>True if the bind address is a loopback IP.</summary>
        [JsonIgnore]
        public bool IsLoopback =>
            BindAddress == "127.0.0.1" || BindAddress == "::1" || BindAddress == "localhost";
    }

    /// <summary>
    /// Experimental / preview feature flags. Per-feature opt-ins for in-flight work.
    /// Labs entries may change or be removed without notice.
    /// </summary>
    public class LabsSettings
    {
        [JsonPropertyName("ghostTextCompletion")]
        public bool GhostTextCompletion { get; set; }

        [JsonPropertyName("parallelSchemaCache")]
        public bool ParallelSchemaCache { get; set; }

        [JsonPropertyName("sharedSnippetSync")]
        public bool SharedSnippetSync { get; set; }
    }

    /// <summary>Default focus button for the pre-execution safety warning dialog (US1 / FR-005).</summary>
    public enum SafetyDefaultButton
    {
        /// <summary>Focus Cancel so accidental Enter does not run unsafe SQL.</summary>
        Cancel,
        /// <summary>Focus Execute (for advanced users who prefer speed over safety).</summary>
        Execute
    }

    /// <summary>Sort mode for the Column Picker inside the completion popup (US2 / FR-011).</summary>
    public enum ColumnPickerSortMode
    {
        /// <summary>Columns in the order they are defined in the table.</summary>
        TableOrder,
        /// <summary>Columns sorted alphabetically by name.</summary>
        Alphabetical
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
        [JsonPropertyName("suggestionTypes")]
        public SuggestionTypesSettings SuggestionTypes { get; set; } = new();

        [JsonPropertyName("qualification")]
        public QualificationSettings Qualification { get; set; } = new();

        [JsonPropertyName("insertOptions")]
        public InsertOptionsSettings InsertOptions { get; set; } = new();

        [JsonPropertyName("joinOptions")]
        public JoinOptionsSettings JoinOptions { get; set; } = new();

        [JsonPropertyName("aliasOptions")]
        public AliasOptionsSettings AliasOptions { get; set; } = new();

        [JsonPropertyName("connectionScope")]
        public ConnectionScopeSettings ConnectionScope { get; set; } = new();

        /// <summary>
        /// Spec 030 T077 / FR-043 — special-character handling for the editor (auto-close
        /// matching characters, auto-add parentheses after functions). Surfaced in Options by T080.
        /// </summary>
        [JsonPropertyName("specialCharOptions")]
        public SpecialCharacterSettings SpecialCharOptions { get; set; } = new();

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
        /// <summary>
        /// Controls whether a NEW alias is generated for tables inserted via completion.
        /// When enabled:
        /// - After typing a table name in FROM, alias candidates ("o", "od", ...) are suggested.
        /// - FK-assisted JOIN suggestions (see <see cref="JoinAssist"/>) insert
        ///   <c>Orders o ON o.CustomerId = c.Id</c> with an alias prefix.
        /// When disabled, FK-assisted JOIN suggestions still fire but the target table is
        /// referenced by its unaliased name: <c>Orders ON Orders.CustomerId = c.Id</c>.
        /// Default disabled — most users prefer explicit aliases.
        /// </summary>
        public bool AutoAlias { get; set; } = false;
        /// <summary>
        /// Master switch for FK-assisted JOIN completion. When enabled:
        /// - Typing <c>SELECT * FROM Customers JOIN </c> shows FK-related tables at the
        ///   top of the suggestion list with the full <c>ON</c> clause in the insert text.
        /// - Typing <c>JOIN Orders ON </c> surfaces ready-made FK equality predicates
        ///   (e.g. <c>Orders.CustomerId = Customers.Id</c>) as atomic suggestions.
        /// Orthogonal to <see cref="AutoAlias"/>, which only controls whether a new alias
        /// is generated for the inserted table. Default enabled — this is the single
        /// biggest productivity win in the completion pipeline.
        /// </summary>
        public bool JoinAssist { get; set; } = true;
        /// <summary>Keyword casing applied to completions inserted into the editor.</summary>
        public KeywordCaseOption KeywordCase { get; set; } = KeywordCaseOption.Upper;
        /// <summary>Whether to disable native SSMS IntelliSense to avoid conflicts.</summary>
        public bool DisableNativeIntelliSense { get; set; } = true;
        /// <summary>Use Space key to commit the selected completion item (SQL Prompt style).
        /// Default OFF: with it on, typing a prefix then pressing space replaced the typed text with
        /// the highlighted item — surprising, since SSMS users expect space to insert a literal space.
        /// Opt in for SQL-Prompt-style space-commit. (Dot-commit + Tab/Enter-commit are unaffected.)</summary>
        public bool SpaceCommits { get; set; } = false;
        /// <summary>Use Dot key to commit the selected completion item.</summary>
        public bool DotCommits { get; set; } = true;
        /// <summary>Show snippet shortcuts (sel, ssf, ins, etc.) in the completion popup. Default disabled.</summary>
        public bool SnippetsInCompletion { get; set; } = false;

        /// <summary>Spec 029. When true (default), AKML offers to store a SQL Server-auth password
        /// (DPAPI-encrypted, per server+login) so the out-of-process engine can load schema/IntelliSense
        /// for SQL-auth connections. Set false to disable the prompt and storage entirely.</summary>
        public bool EnableSqlAuthCredentials { get; set; } = true;
    }

    public enum ColumnSuggestionScope { All, ReferencedOnly }
    public enum SchemaQualifyMode { Always, NonDefaultOnly, Never }
    public enum BracketMode { Always, WhenRequired, Never }

    /// <summary>Which categories of database objects appear in the suggestion list.</summary>
    public class SuggestionTypesSettings
    {
        [JsonPropertyName("includeSystemObjects")]
        public bool IncludeSystemObjects { get; set; }

        [JsonPropertyName("suggestAllColumnsAfterSelect")]
        public bool SuggestAllColumnsAfterSelect { get; set; }

        [JsonPropertyName("columnScope")]
        public ColumnSuggestionScope ColumnScope { get; set; } = ColumnSuggestionScope.ReferencedOnly;

        [JsonPropertyName("includeKeywords")]
        public bool IncludeKeywords { get; set; } = true;
    }

    /// <summary>How object names are formatted when inserted from the suggestion list.</summary>
    public class QualificationSettings
    {
        // Always = SQL Prompt's default: committing a table from the suggestion list inserts
        // the owner-qualified name ("dbo.Customers") so the user never types the schema.
        [JsonPropertyName("schemaMode")]
        public SchemaQualifyMode SchemaMode { get; set; } = SchemaQualifyMode.Always;

        [JsonPropertyName("bracketMode")]
        public BracketMode BracketMode { get; set; } = BracketMode.WhenRequired;

        [JsonPropertyName("qualifyColumnsWithTableOrAlias")]
        public bool QualifyColumnsWithTableOrAlias { get; set; } = true;
    }

    /// <summary>What metadata is inserted when writing INSERT INTO statements.</summary>
    public class InsertOptionsSettings
    {
        [JsonPropertyName("includeColumns")]
        public bool IncludeColumns { get; set; } = true;

        [JsonPropertyName("includeDefaultsAsComments")]
        public bool IncludeDefaultsAsComments { get; set; } = true;

        [JsonPropertyName("includeProcParamInfo")]
        public bool IncludeProcParamInfo { get; set; } = true;
    }

    /// <summary>JOIN completion behavior.</summary>
    public class JoinOptionsSettings
    {
        [JsonPropertyName("matchByColumnName")]
        public bool MatchByColumnName { get; set; } = true;
    }

    /// <summary>
    /// Spec 030 T077 / FR-043 — special-character handling in the editor. Exposed via
    /// <see cref="IntelliSenseSettings.SpecialCharOptions"/> and surfaced in Options by T080.
    /// </summary>
    public class SpecialCharacterSettings
    {
        /// <summary>
        /// Auto-close matching characters: typing an opening <c>(</c>, <c>[</c>, <c>'</c>, etc.
        /// inserts the matching close character. SQL Prompt default: on.
        /// </summary>
        [JsonPropertyName("autoCloseCharacters")]
        public bool AutoCloseCharacters { get; set; } = true;

        /// <summary>
        /// Add parentheses automatically after inserting a function from the completion list.
        /// SQL Prompt default: on.
        /// </summary>
        [JsonPropertyName("addParentheses")]
        public bool AddParentheses { get; set; } = true;
    }

    /// <summary>
    /// Spec 030 T035 / FR-015 — automatic alias generation policy. Controls how aliases suggested
    /// after a table name in FROM/JOIN (and FK-assisted JOIN inserts) are formed and rendered.
    /// </summary>
    public class AliasOptionsSettings
    {
        /// <summary>Insert the <c>AS</c> keyword (<c>Orders AS o</c> vs <c>Orders o</c>). SQL Prompt default: on.</summary>
        [JsonPropertyName("includeAs")]
        public bool IncludeAs { get; set; } = true;

        /// <summary>
        /// User-defined object→alias overrides, keyed by (bare) object name, case-insensitive —
        /// e.g. <c>{"Orders":"ord"}</c>. When the table matches, its alias is offered first.
        /// </summary>
        [JsonPropertyName("objectAliasMap")]
        public Dictionary<string, string> ObjectAliasMap { get; set; } = new();

        /// <summary>
        /// Prefixes stripped from a table name before generating an alias — e.g. <c>["tbl_","tb_"]</c>
        /// so <c>tbl_Orders</c> generates <c>o</c>/<c>od</c> rather than <c>t</c>/<c>to</c>.
        /// </summary>
        [JsonPropertyName("prefixesToIgnore")]
        public string[] PrefixesToIgnore { get; set; } = [];
    }

    /// <summary>
    /// Spec 030 T036 / FR-016 — suggestion connection scope. Limits the object suggestion list to
    /// chosen databases/schemas and (forward-looking) toggles linked-server objects. Empty lists mean
    /// "no restriction" so the default has zero behavioural impact (matches the AliasOptions pattern).
    /// Pushed onto the engine per request via <c>CompletionHandler</c>; the Options UI pairs with T082.
    /// </summary>
    public class ConnectionScopeSettings
    {
        /// <summary>
        /// Databases the suggestion list is limited to (bare names, case-insensitive). Empty = all.
        /// The schema cache is single-database, so the only honest effect is: when the connected
        /// database is NOT in a non-empty list, its object/schema suggestions are suppressed.
        /// </summary>
        [JsonPropertyName("databases")]
        public string[] Databases { get; set; } = [];

        /// <summary>Schemas the object suggestion list is limited to (case-insensitive). Empty = all.</summary>
        [JsonPropertyName("schemas")]
        public string[] Schemas { get; set; } = [];

        /// <summary>
        /// Include linked-server objects in suggestions. Forward-looking: the schema cache does not
        /// load linked-server objects today, so this is honored only where such loading exists (none
        /// yet) — it is threaded through the completion path but currently has no observable effect.
        /// Default off.
        /// </summary>
        [JsonPropertyName("includeLinkedServers")]
        public bool IncludeLinkedServers { get; set; }

        /// <summary>
        /// True when the supplied connected-database name is in scope: the allow-list is empty
        /// (no restriction), the name is unknown (don't suppress), or the list contains it
        /// (case-insensitive). Used by <c>CompletionHandler</c> to decide whether to suppress
        /// the connected database's object suggestions.
        /// </summary>
        public bool IncludesDatabase(string? databaseName)
        {
            if (Databases is null || Databases.Length == 0) return true;
            if (string.IsNullOrEmpty(databaseName)) return true;
            foreach (var d in Databases)
                if (string.Equals(d, databaseName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True when the schema is in scope: an empty allow-list (all) or a case-insensitive match.</summary>
        public bool IncludesSchema(string schemaName)
        {
            if (Schemas is null || Schemas.Length == 0) return true;
            foreach (var s in Schemas)
                if (string.Equals(s, schemaName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
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
        /// <summary>IDE type identifier (e.g. <c>"SSMS22"</c>, <c>"VS2026"</c>).</summary>
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

        /// <summary>Include column data types and nullability as inline comments in INSERT column expansions.</summary>
        [JsonPropertyName("insertColumnsIncludeTypes")]
        public bool InsertColumnsIncludeTypes { get; set; } = true;
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

        // ── Spec 014, US17: Lightbulb quick-fixes and Issue Details popup ──

        /// <summary>US17 / FR-079 — render lightbulb gutter icons for analysis violations.</summary>
        [JsonPropertyName("lightbulbsEnabled")]
        public bool LightbulbsEnabled { get; set; } = true;

        /// <summary>
        /// US17 / FR-079 — show blue lightbulbs for advisory-only rules (no auto-fix).
        /// When false, only auto-fixable rules (orange) get a lightbulb.
        /// </summary>
        [JsonPropertyName("showAdvisoryHints")]
        public bool ShowAdvisoryHints { get; set; } = true;

        /// <summary>
        /// US17 — modifier key combination that, when held while clicking Apply Fix,
        /// applies the same fix to every occurrence in the document.
        /// </summary>
        [JsonPropertyName("applyFixOnAllOccurrencesShortcut")]
        public string ApplyFixOnAllOccurrencesShortcut { get; set; } = "Shift+Click";

        /// <summary>
        /// Spec 030 T053 — user-level per-rule overrides set from the Manage Rules dialog.
        /// Keyed by rule id (e.g. "PE001"). Applied by <c>CaSettingsLoader</c> over the built-in
        /// rule defaults and BELOW any project <c>.casettings</c> (project-local wins). Empty by
        /// default, so the global baseline is the rules' own defaults.
        /// The setter normalises the comparer to <see cref="StringComparer.OrdinalIgnoreCase"/> so that
        /// hand-edited config.json entries with lowercase rule ids (e.g. "pe001") are treated identically
        /// to the engine's canonical uppercase ids ("PE001"). System.Text.Json always calls the setter
        /// with a freshly-constructed ordinal dictionary, so the normalisation happens on every deserialise.
        /// </summary>
        [JsonPropertyName("ruleOverrides")]
        public Dictionary<string, RuleOverride> RuleOverrides
        {
            get => _ruleOverrides;
            set => _ruleOverrides = value == null
                ? new Dictionary<string, RuleOverride>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, RuleOverride>(value, StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, RuleOverride> _ruleOverrides = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Spec 030 T053 — a single global per-rule override (enable/severity), persisted in
    /// <c>config.json</c> under <c>codeAnalysis.ruleOverrides</c>. Severity is a string matching the
    /// <c>.casettings</c> convention: "error", "warning", "information", "hint", or "ignore"
    /// (empty = leave the rule's default severity, only the enable flag applies).
    /// </summary>
    public class RuleOverride
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;
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

        /// <summary>
        /// Spec 030 T075 / FR-040 — when <c>true</c>, history retention purging is disabled: all
        /// entries and version snapshots are kept regardless of <see cref="RetentionDays"/> /
        /// <see cref="MaxEntries"/>, and the engine's <c>HistoryRetentionService</c> skips every purge
        /// and does not schedule its timer. Default <c>false</c> (auto-trim on, the prior behaviour).
        /// </summary>
        [JsonPropertyName("disableAutoTrim")]
        public bool DisableAutoTrim { get; set; }

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

        /// <summary>Use gradient colors on tab header bars (lighter top, base color bottom).</summary>
        [JsonPropertyName("gradientColors")]
        public bool GradientColors { get; set; }

        /// <summary>Propagate environment color to the SSMS/VS status bar (T027).</summary>
        [JsonPropertyName("statusBarColorEnabled")]
        public bool StatusBarColorEnabled { get; set; } = true;

        /// <summary>Propagate environment color as a border on floating (undocked) query windows (T028).</summary>
        [JsonPropertyName("floatingWindowBorderEnabled")]
        public bool FloatingWindowBorderEnabled { get; set; } = true;
    }

    /// <summary>Configuration for a single tab coloring environment rule.</summary>
    public class ColoringRule
    {
        [JsonPropertyName("order")]
        public int Order { get; set; }

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("matchTarget")]
        public string MatchTarget { get; set; } = Models.Tabs.EnvironmentMatcher.MatchTargetServerName;

        /// <summary>
        /// Spec 030 T077 / FR-043 — database name this rule matches against when
        /// <see cref="MatchTarget"/> targets the database. Empty string = no database
        /// restriction (server-name matching only, the prior behaviour). Additive and
        /// backward-compatible: absent in existing configs and ignored by the server-name matcher.
        /// </summary>
        [JsonPropertyName("databaseName")]
        public string DatabaseName { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Settings for execution safety warnings (Phase 7).</summary>
    public class SafetySettings
    {
        /// <summary>
        /// Emergency kill-switch. When <c>true</c>, <c>ExecutionInterceptor.OnBeforeExecute</c>
        /// returns immediately without loading any safety config, calling the engine, or
        /// showing any dialog. Intended as a diagnostic lever for users who hit a hang in
        /// the execution-guard path — set this to <c>true</c> in
        /// <c>%AppData%\AKML SQL\config.json</c> (under <c>"safety"</c>) and F5 will go
        /// straight through with no AKML involvement. Default <c>false</c>.
        /// </summary>
        [JsonPropertyName("temporarilyDisabled")]
        public bool TemporarilyDisabled { get; set; }

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

        /// <summary>
        /// Maps environment label (e.g. "PRODUCTION", "STAGING", "DEV") to confirmation
        /// severity: "TypeServerName" (must type server name), "SimpleConfirm" (Yes/No dialog),
        /// or "Disabled" (no guard for that environment).
        /// </summary>
        [JsonPropertyName("environmentSeverity")]
        public Dictionary<string, string> EnvironmentSeverity { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRODUCTION"] = "TypeServerName",
            ["STAGING"] = "SimpleConfirm",
            // DEV used to default to "Disabled" but that silently swallowed every
            // safety warning on local boxes (most users' main work environment).
            // Users who really want zero warnings on DEV can still opt in by
            // editing config.json to set this back to "Disabled".
            ["DEV"] = "SimpleConfirm"
        };

        // ── Spec 014, US1: Pre-execution safety extensions ──

        /// <summary>
        /// US1 / FR-002 — warn when a MERGE statement has no WHEN MATCHED filter
        /// (equivalent to UPDATE / DELETE without WHERE for the target table).
        /// </summary>
        [JsonPropertyName("mergeNoFilter")]
        public bool MergeNoFilter { get; set; } = true;

        /// <summary>
        /// US1 / FR-002 — warn when a DELETE / UPDATE wraps an INNER JOIN with no
        /// WHERE clause; the join is not a row filter.
        /// </summary>
        [JsonPropertyName("insideJoin")]
        public bool InsideJoin { get; set; } = true;

        /// <summary>
        /// US1 / FR-003 — warn when a CREATE / ALTER PROCEDURE or CREATE / ALTER
        /// TRIGGER body contains DELETE / UPDATE / MERGE without a row filter.
        /// </summary>
        [JsonPropertyName("insideProcOrTrigger")]
        public bool InsideProcOrTrigger { get; set; } = true;

        /// <summary>
        /// US1 / FR-005 — default focus button on the warning dialog.
        /// Cancel is the safe default so accidental Enter presses do not run unsafe SQL.
        /// </summary>
        [JsonPropertyName("defaultButton")]
        public SafetyDefaultButton DefaultButton { get; set; } = SafetyDefaultButton.Cancel;

        /// <summary>
        /// US1 / FR-008 — when the target server is tagged Production via tab
        /// coloring, render the safety dialog header in that environment color.
        /// </summary>
        [JsonPropertyName("showEnvironmentColorInHeader")]
        public bool ShowEnvironmentColorInHeader { get; set; } = true;
    }

    /// <summary>Results grid productivity settings (Phase 8).</summary>
    public class GridSettings
    {
        [JsonPropertyName("aggregates")]
        public bool Aggregates { get; set; } = true;

        [JsonPropertyName("nullHighlight")]
        public bool NullHighlight { get; set; } = true;

        [JsonPropertyName("rowNumbers")]
        public bool RowNumbers { get; set; }

        [JsonPropertyName("freezeHeaders")]
        public bool FreezeHeaders { get; set; } = true;

        /// <summary>Format 15+ digit numbers as text in Excel exports to prevent rounding.</summary>
        [JsonPropertyName("excelLargeNumberAsText")]
        public bool ExcelLargeNumberAsText { get; set; } = true;

        // ── Spec 014, US16: Result-grid productivity ──

        /// <summary>US16 / FR-074 — surface "Copy as IN Clause" on the result grid right-click menu.</summary>
        [JsonPropertyName("enableCopyAsInClause")]
        public bool EnableCopyAsInClause { get; set; } = true;

        /// <summary>US16 / FR-074 — surface "Script as INSERT" on the result grid right-click menu.</summary>
        [JsonPropertyName("enableScriptAsInsert")]
        public bool EnableScriptAsInsert { get; set; } = true;

        /// <summary>US16 / FR-074 — surface "Open in Excel" on the result grid right-click menu.</summary>
        [JsonPropertyName("enableOpenInExcel")]
        public bool EnableOpenInExcel { get; set; } = true;

        /// <summary>
        /// US16 — when emitting INSERT statements for a table with an IDENTITY column,
        /// wrap with <c>SET IDENTITY_INSERT &lt;table&gt; ON / OFF</c>. Opt-in by default
        /// because IDENTITY_INSERT can fail if the user lacks ALTER permission.
        /// </summary>
        [JsonPropertyName("scriptAsInsertIncludesIdentity")]
        public bool ScriptAsInsertIncludesIdentity { get; set; } = false;
    }

    /// <summary>Editor productivity settings (Phase 8).</summary>
    public class EditorProductivitySettings
    {
        [JsonPropertyName("highlightOccurrences")]
        public bool HighlightOccurrences { get; set; } = true;

        [JsonPropertyName("bracketMatching")]
        public bool BracketMatching { get; set; } = true;

        [JsonPropertyName("namedRegions")]
        public bool NamedRegions { get; set; } = true;

        [JsonPropertyName("stickyScroll")]
        public bool StickyScroll { get; set; } = true;

        [JsonPropertyName("minimap")]
        public bool Minimap { get; set; }

        [JsonPropertyName("documentOutline")]
        public bool DocumentOutline { get; set; } = true;
    }

    /// <summary>Execution productivity settings (Phase 8).</summary>
    public class ExecutionProductivitySettings
    {
        [JsonPropertyName("notificationThreshold")]
        public int NotificationThreshold { get; set; } = 30;

        [JsonPropertyName("showExecutionTimer")]
        public bool ShowExecutionTimer { get; set; } = true;

        [JsonPropertyName("multiDatabase")]
        public bool MultiDatabase { get; set; } = true;
    }

    /// <summary>Navigation settings (Phase 8 + spec 014 US13/US20).</summary>
    public class NavigationSettings
    {
        [JsonPropertyName("goToDefinition")]
        public bool GoToDefinition { get; set; } = true;

        [JsonPropertyName("peekDefinition")]
        public bool PeekDefinition { get; set; } = true;

        [JsonPropertyName("findReferences")]
        public bool FindReferences { get; set; } = true;

        [JsonPropertyName("objectSearch")]
        public bool ObjectSearch { get; set; } = true;

        [JsonPropertyName("connectionAliases")]
        public List<ConnectionAliasEntry> ConnectionAliases { get; set; } = [];

        // ── Spec 014, US13: Script navigation chords ──

        /// <summary>US13 / FR-062 — bind <c>F12</c> to "Script Object as ALTER".</summary>
        [JsonPropertyName("enableF12ScriptAsAlter")]
        public bool EnableF12ScriptAsAlter { get; set; } = true;

        /// <summary>US13 / FR-063 — bind <c>Ctrl+F12</c> to "Select in Object Explorer" (SSMS only).</summary>
        [JsonPropertyName("enableCtrlF12SelectInOe")]
        public bool EnableCtrlF12SelectInOe { get; set; } = true;

        /// <summary>US13 / FR-061 — bind <c>Ctrl+B, Ctrl+S</c> to "Summarize Script".</summary>
        [JsonPropertyName("enableSummarizeScript")]
        public bool EnableSummarizeScript { get; set; } = true;

        /// <summary>US13 / FR-064 — bind <c>Ctrl+B, Ctrl+F</c> to "Find Unused Variables and Parameters".</summary>
        [JsonPropertyName("enableFindUnused")]
        public bool EnableFindUnused { get; set; } = true;

        // ── Spec 014, US20: New execution shortcuts and Browse Open Tabs ──

        /// <summary>US20 / FR-101 — bind <c>Alt+Shift+F5</c> to "Execute Current Batch".</summary>
        [JsonPropertyName("enableExecuteCurrentBatch")]
        public bool EnableExecuteCurrentBatch { get; set; } = true;

        /// <summary>US20 / FR-102 — bind <c>Ctrl+Shift+F5</c> to "Execute To Cursor".</summary>
        [JsonPropertyName("enableExecuteToCursor")]
        public bool EnableExecuteToCursor { get; set; } = true;

        /// <summary>
        /// US20 / FR-105 — bind <c>Ctrl+Q</c> to a fuzzy "Browse Open Tabs" popup.
        /// SSMS-only by default; in VS hosts <c>Ctrl+Q</c> is the host's Quick Launch.
        /// </summary>
        [JsonPropertyName("enableBrowseOpenTabs")]
        public bool EnableBrowseOpenTabs { get; set; } = true;

        /// <summary>US20 — keystroke for the Browse Open Tabs popup.</summary>
        [JsonPropertyName("browseOpenTabsShortcut")]
        public string BrowseOpenTabsShortcut { get; set; } = "Ctrl+Q";
    }

    /// <summary>A server name to friendly alias mapping.</summary>
    public class ConnectionAliasEntry
    {
        [JsonPropertyName("serverName")]
        public string ServerName { get; set; } = string.Empty;

        [JsonPropertyName("alias")]
        public string Alias { get; set; } = string.Empty;
    }

    /// <summary>Command Palette usage tracking (Phase 8) and aggregation toggles (spec 014, US4).</summary>
    public class CommandPaletteSettings
    {
        [JsonPropertyName("usageCounts")]
        public Dictionary<string, int> UsageCounts { get; set; } = new();

        // ── Spec 014, US4: unified Command Palette ──

        /// <summary>US4 / FR-047 — master switch for the Command Palette.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>US4 / FR-048 — include AKML SQL commands as a result source.</summary>
        [JsonPropertyName("includeAkmlCommands")]
        public bool IncludeAkmlCommands { get; set; } = true;

        /// <summary>US4 / FR-048 — include AKML SQL Options entries as a result source.</summary>
        [JsonPropertyName("includeAkmlOptions")]
        public bool IncludeAkmlOptions { get; set; } = true;

        /// <summary>US4 / FR-048 — include the SSMS / VS host's built-in commands as a result source.</summary>
        [JsonPropertyName("includeHostCommands")]
        public bool IncludeHostCommands { get; set; } = true;

        /// <summary>
        /// US4 / FR-048 — include database objects from the active connection as a result
        /// source. SSMS only; ignored in VS hosts.
        /// </summary>
        [JsonPropertyName("includeDbObjects")]
        public bool IncludeDbObjects { get; set; } = true;

        /// <summary>US4 / FR-052 — number of recent items the palette remembers per host.</summary>
        [JsonPropertyName("maxRecentItems")]
        public int MaxRecentItems { get; set; } = 10;

        /// <summary>
        /// US4 / FR-052 — recent palette selections, most recent first. Per machine
        /// only — not synced across installations.
        /// </summary>
        [JsonPropertyName("recentItems")]
        public List<string> RecentItems { get; set; } = new();
    }

    /// <summary>AI assistance settings (Phase 9).</summary>
    public class AiSettings
    {
        /// <summary>Master switch for AI assistance features.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>AI provider name (e.g. "openai", "anthropic", "gemini", "ollama").</summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "";

        /// <summary>Model identifier (e.g. "gpt-4o", "claude-sonnet-4-20250514").</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        /// <summary>API key for the configured provider.</summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        /// <summary>Custom endpoint URL (for Azure OpenAI, local proxies, etc.).</summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "";

        /// <summary>Maximum tokens in the AI response.</summary>
        [JsonPropertyName("maxTokens")]
        public int MaxTokens { get; set; } = 4096;

        /// <summary>Sampling temperature (0.0–2.0). Lower = more deterministic.</summary>
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.2;

        /// <summary>Request timeout in seconds.</summary>
        [JsonPropertyName("timeout")]
        public int Timeout { get; set; } = 30;

        /// <summary>Number of automatic retries on transient failures.</summary>
        [JsonPropertyName("retries")]
        public int Retries { get; set; } = 2;

        /// <summary>Privacy mode: "schemaOnly" sends only metadata, "full" sends query text.</summary>
        [JsonPropertyName("privacyMode")]
        public string PrivacyMode { get; set; } = "schemaOnly";

        /// <summary>Provider name for offline/local AI (e.g. "ollama").</summary>
        [JsonPropertyName("offlineProvider")]
        public string OfflineProvider { get; set; } = "";

        /// <summary>Model name for offline/local AI.</summary>
        [JsonPropertyName("offlineModel")]
        public string OfflineModel { get; set; } = "";

        /// <summary>Endpoint URL for offline/local AI.</summary>
        [JsonPropertyName("offlineEndpoint")]
        public string OfflineEndpoint { get; set; } = "";

        /// <summary>Enable natural-language to SQL generation.</summary>
        [JsonPropertyName("textToSql")]
        public bool TextToSql { get; set; } = true;

        /// <summary>Enable AI-powered SQL explanation.</summary>
        [JsonPropertyName("explain")]
        public bool Explain { get; set; } = true;

        /// <summary>Enable AI-powered error fix suggestions.</summary>
        [JsonPropertyName("fix")]
        public bool Fix { get; set; } = true;

        /// <summary>Automatically suggest fixes when a query execution fails.</summary>
        [JsonPropertyName("autoFixOnError")]
        public bool AutoFixOnError { get; set; }

        /// <summary>Enable AI-powered query optimization suggestions.</summary>
        [JsonPropertyName("optimize")]
        public bool Optimize { get; set; } = true;

        /// <summary>Enable AI-powered index suggestions.</summary>
        [JsonPropertyName("indexSuggestions")]
        public bool IndexSuggestions { get; set; } = true;

        /// <summary>Enable inline ghost-text completions.</summary>
        [JsonPropertyName("inlineCompletion")]
        public bool InlineCompletion { get; set; }

        /// <summary>Enable the AI chat side panel.</summary>
        [JsonPropertyName("chatPanel")]
        public bool ChatPanel { get; set; } = true;

        /// <summary>
        /// Whether privacy consent is still required before sending data to a cloud AI provider.
        /// Defaults to <c>true</c> (consent not yet given). Set to <c>false</c> after user confirms.
        /// Not required for local/offline providers (ollama, lmstudio).
        /// </summary>
        [JsonPropertyName("privacyConsentRequired")]
        public bool PrivacyConsentRequired { get; set; } = true;

        // ── Spec 014, US10 + US18: AI shortcuts and reach ──

        /// <summary>US10 / FR-053 — keystroke for "Open AI chat panel". Default <c>Alt+Z</c>.</summary>
        [JsonPropertyName("openChatShortcut")]
        public string OpenChatShortcut { get; set; } = "Alt+Z";

        /// <summary>US10 / FR-054 — keystroke for "AI Fix Selection". Default <c>Shift+Alt+R</c>.</summary>
        [JsonPropertyName("fixShortcut")]
        public string FixShortcut { get; set; } = "Shift+Alt+R";

        /// <summary>US10 / FR-055 — keystroke for "AI Optimize Selection". Default <c>Ctrl+Alt+Z</c>.</summary>
        [JsonPropertyName("optimizeShortcut")]
        public string OptimizeShortcut { get; set; } = "Ctrl+Alt+Z";

        /// <summary>US10 / FR-056 — keystroke for "AI Manual Ghost Text". Default <c>Ctrl+Alt+Up</c>.</summary>
        [JsonPropertyName("ghostTextShortcut")]
        public string GhostTextShortcut { get; set; } = "Ctrl+Alt+Up";

        /// <summary>US18 / FR-089 — render the floating AI icon at the right edge of any non-empty selection.</summary>
        [JsonPropertyName("showEditorIcon")]
        public bool ShowEditorIcon { get; set; } = true;

        /// <summary>US18 / FR-090 — render 1–3 follow-up suggestion buttons after every AI answer.</summary>
        [JsonPropertyName("showFollowupSuggestions")]
        public bool ShowFollowupSuggestions { get; set; } = true;

        /// <summary>
        /// US18 / FR-087 — comment line prefix that triggers comment-to-SQL when followed
        /// by Tab. Default <c>-- generate:</c>.
        /// </summary>
        [JsonPropertyName("commentTriggerPrefix")]
        public string CommentTriggerPrefix { get; set; } = "-- generate:";

        /// <summary>US18 — debounce delay before AI ghost-text auto-suggest fires (milliseconds).</summary>
        [JsonPropertyName("ghostTextDelayMs")]
        public int GhostTextDelayMs { get; set; } = 500;
    }

    /// <summary>
    /// Spec 014, US19 (and US2/US8): completion popup polish settings that did not
    /// fit into the existing <see cref="IntelliSenseSettings"/> class. Lives as a
    /// separate top-level section so the Options dialog can present it on its own page.
    /// Persisted under <c>completionPolish</c> in <c>config.json</c>.
    /// </summary>
    public class CompletionPolishSettings
    {
        // ── Tooltips and parameter help ──

        /// <summary>
        /// US19 / FR-096 — surface the <c>MS_Description</c> extended property in
        /// object tooltips, with clickable cross-references to other objects.
        /// </summary>
        [JsonPropertyName("enableMsDescription")]
        public bool EnableMsDescription { get; set; } = true;

        /// <summary>US19 / FR-097 — bold the next-expected parameter in function-signature popups.</summary>
        [JsonPropertyName("enableParameterHighlight")]
        public bool EnableParameterHighlight { get; set; } = true;

        // ── Encrypted object decryption ──

        /// <summary>
        /// US19 / FR-098 — when the user has DAC permission, attempt to decrypt
        /// encrypted procedures and functions and render the plaintext in the
        /// Object Definition Box's Script tab with a "decrypted" badge.
        /// </summary>
        [JsonPropertyName("enableEncryptedDecryption")]
        public bool EnableEncryptedDecryption { get; set; } = true;

        // ── Temp-table IntelliSense ──

        /// <summary>
        /// US19 / FR-100 — parse <c>CREATE TABLE #x</c> and <c>SELECT … INTO #x</c>
        /// statements in the active script and offer column completions for the
        /// resulting temp tables in later statements within the same script.
        /// </summary>
        [JsonPropertyName("enableTempTableIntellisense")]
        public bool EnableTempTableIntellisense { get; set; } = true;

        // ── Customisable insertion templates ──

        /// <summary>
        /// US19 / FR-099 — user-customised template for the <c>ALTER TABLE</c>
        /// statement that completion inserts. <c>null</c> = use the built-in default.
        /// </summary>
        [JsonPropertyName("alterTableTemplate")]
        public string? AlterTableTemplate { get; set; }

        /// <summary>
        /// US19 / FR-099 — user-customised template for the <c>INSERT INTO</c>
        /// statement that completion inserts. <c>null</c> = use the built-in default.
        /// </summary>
        [JsonPropertyName("insertIntoTemplate")]
        public string? InsertIntoTemplate { get; set; }

        // ── Object Definition Box (US8) ──

        /// <summary>US8 / FR-023 — persisted width of the Object Definition Box. Default 400.</summary>
        [JsonPropertyName("objectDefinitionBoxWidth")]
        public double ObjectDefinitionBoxWidth { get; set; } = 400;

        /// <summary>US8 / FR-023 — persisted height of the Object Definition Box. Default 300.</summary>
        [JsonPropertyName("objectDefinitionBoxHeight")]
        public double ObjectDefinitionBoxHeight { get; set; } = 300;

        // ── Column Picker (US2) ──

        /// <summary>
        /// US2 / FR-011 — default sort mode for the Column Picker.
        /// </summary>
        [JsonPropertyName("columnPickerDefaultSort")]
        public ColumnPickerSortMode ColumnPickerDefaultSort { get; set; } = ColumnPickerSortMode.TableOrder;
    }
}
