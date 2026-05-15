# AKML SQL — IPC API Reference

All communication between the shell extension and the engine uses a named pipe with MessagePack-framed `RpcMessage` envelopes. This document describes every message type, its direction, request payload, and response payload.

---

## Transport Layer

```
Pipe name:    akmlsql-engine-{user-SID}-{shell-PID}
Encoding:     MessagePack
Max frame:    16 MB
Frame header: [4-byte big-endian length] [4-byte XOR-rotate checksum]
```

Every message is wrapped in an `RpcMessage`:

```csharp
[MessagePackObject]
class RpcMessage {
    [Key(0)] int    MessageType   // see MessageTypes constants
    [Key(1)] int    RequestId     // echoed in response; 0 for notifications
    [Key(2)] byte[] Payload       // MessagePack-serialized request/response object
}
```

**Notifications** (no response expected) have `RequestId = 0` and the engine returns `null`.
**Request-response** pairs share the same `RequestId`.

---

## Message Type Constants

| Group | Constant | Value |
|-------|----------|-------|
| Shell→Engine | `ConnectionChanged` | 1 |
| Shell→Engine | `DocumentChanged` | 2 |
| Shell→Engine | `RequestCompletion` | 3 |
| Shell→Engine | `RequestSignatureHelp` | 4 |
| Shell→Engine | `RequestQuickInfo` | 5 |
| Shell→Engine | `SchemaRefreshRequest` | 6 |
| Shell→Engine | `Ping` | 7 |
| Shell→Engine | `Shutdown` | 8 |
| Shell→Engine | `FormatDocument` | 10 |
| Shell→Engine | `FormatSelection` | 11 |
| Shell→Engine | `FormatPreview` | 12 |
| Shell→Engine | `FormatAction` | 13 |
| Shell→Engine | `ProfileList` | 14 |
| Shell→Engine | `ProfileSave` | 15 |
| Shell→Engine | `ProfileDelete` | 16 |
| Shell→Engine | `ProfileImport` | 17 |
| Shell→Engine | `BulkFormat` | 18 |
| Shell→Engine | `BulkFormatCancel` | 19 |
| Shell→Engine | `SnippetExpand` | 20 |
| Shell→Engine | `SnippetList` | 21 |
| Shell→Engine | `SnippetSave` | 22 |
| Shell→Engine | `SnippetDelete` | 23 |
| Shell→Engine | `SnippetImport` | 24 |
| Shell→Engine | `RequestAnalyze` | 25 |
| Shell→Engine | `AnalysisSettingsChanged` | 26 |
| Shell→Engine | `RequestStyleEditorSchema` | 28 |
| Shell→Engine | `RequestRefactorPreview` | 30 |
| Shell→Engine | `RequestRefactorApply` | 31 |
| Engine→Shell | `CompletionResult` | 101 |
| Engine→Shell | `SignatureHelpResult` | 102 |
| Engine→Shell | `QuickInfoResult` | 103 |
| Engine→Shell | `SchemaRefreshComplete` | 104 |
| Engine→Shell | `Pong` | 105 |
| Engine→Shell | `Error` | 106 |
| Engine→Shell | `FormatDocumentResult` | 110 |
| Engine→Shell | `FormatSelectionResult` | 111 |
| Engine→Shell | `FormatPreviewResult` | 112 |
| Engine→Shell | `FormatActionResult` | 113 |
| Engine→Shell | `ProfileListResult` | 114 |
| Engine→Shell | `ProfileSaveResult` | 115 |
| Engine→Shell | `ProfileDeleteResult` | 116 |
| Engine→Shell | `ProfileImportResult` | 117 |
| Engine→Shell | `BulkFormatResult` | 118 |
| Engine→Shell | `SnippetExpandResult` | 120 |
| Engine→Shell | `SnippetListResult` | 121 |
| Engine→Shell | `SnippetSaveResult` | 122 |
| Engine→Shell | `SnippetDeleteResult` | 123 |
| Engine→Shell | `SnippetImportResult` | 124 |
| Engine→Shell | `AnalysisResult` | 125 |
| Engine→Shell | `StyleEditorSchemaResult` | 128 |
| Engine→Shell | `RefactorPreviewResult` | 130 |
| Engine→Shell | `RefactorApplyResult` | 131 |

---

## Session & Document Messages

### `ConnectionChanged` (notification, no response)

Sent whenever the user connects to a different SQL Server or switches databases.

**Request** (`ConnectionInfo`):
```
SessionId         string   Unique editor session identifier
ConnectionString  string   ADO.NET connection string for schema queries
ServerVersion     int      SQL Server major version (e.g. 16)
EngineEdition     int      SERVERPROPERTY('EngineEdition') value
DatabaseName      string   Currently connected database
```

**Effect**: Updates `SessionManager`; triggers background Phase A schema population if cache is cold.

---

### `DocumentChanged` (notification, no response)

Sent on every editor keystroke (debounced by the shell).

**Request** (`DocumentChange`):
```
SessionId    string   Editor session identifier
ChangeType   int      0 = full replacement, 1 = incremental (incremental not yet used)
FullText     string   Entire document text (max 10 MB)
Version      int      Monotonically increasing document version
```

---

### `Ping` → `Pong`

Health check. Shell sends periodically to detect engine crashes.

**Request**: empty payload (send `EngineStatusInfo` with all zeros).

**Response** (`EngineStatusInfo`):
```
MemoryUsageMb   int   GC heap in MB
CachedDatabases int   Number of schema caches
ActiveSessions  int   Number of active editor sessions
UptimeSeconds   int   Engine uptime (not currently populated)
```

---

### `Shutdown` (notification, no response)

Signals the engine to exit cleanly. Throws `OperationCanceledException` inside the server loop.

---

## IntelliSense Messages

### `RequestCompletion` → `CompletionResult`

**Request** (`CompletionRequest`):
```
SessionId     string   Editor session identifier
CursorOffset  int      Zero-based character offset in the document
TriggerChar   string?  Character that triggered completion (e.g. ".")
```

**Response** (`CompletionResponse`):
```
Items  CompletionItem[]
    Label         string   Display text
    InsertText    string   Text to insert
    Kind          int      1=Keyword, 2=Table, 3=Column, 4=Procedure, 5=Function,
                           6=View, 7=Alias, 8=Snippet, 9=Parameter
    Detail        string?  Type info / row count / description
    SortText      string   Used for ordering within the list
    FilterText    string   Used for fuzzy matching
    Documentation string?  Extended hover documentation
```

---

### `RequestSignatureHelp` → `SignatureHelpResult`

**Request** (`SignatureRequest`):
```
SessionId     string
CursorOffset  int
```

**Response** (`SignatureResponse`):
```
FunctionName      string
ActiveParameter   int      Zero-based index of the parameter at cursor
Signatures        SignatureInfo[]
    Label         string   Full signature text, e.g. "dbo.usp_Get(@Id INT, @Name NVARCHAR(100))"
    Documentation string?
    Parameters    ParameterInfo[]
        Label     string   Parameter name with type
        Documentation string?
```

---

### `RequestQuickInfo` → `QuickInfoResult`

**Request** (`QuickInfoRequest`):
```
SessionId     string
CursorOffset  int
```

**Response** (`QuickInfoResponse`):
```
HasContent    bool
Content       string?   Markdown-formatted hover content
ObjectType    string?   "Table", "View", "Procedure", etc.
SchemaName    string?
ObjectName    string?
```

---

### `SchemaRefreshRequest` → `SchemaRefreshComplete`

Forces a schema cache invalidation.

**Request** (`RefreshRequest`):
```
SessionId  string   If empty, refreshes all cached databases
Force      bool     If true, triggers immediate Phase A repopulation
```

**Response** (`RefreshResponse`):
```
Success      bool
ObjectCount  int   Number of objects previously cached (now invalidated)
```

---

## Formatter Messages

### `FormatDocument` → `FormatDocumentResult`

Formats an entire SQL document.

**Request** (`FormatRequest`):
```
Text         string    SQL text to format
ProfileName  string?   Name of the .akmlstyle profile (null = default)
```

**Response** (`FormatResponse`):
```
Success            bool
FormattedText      string
WasModified        bool
ValidationPassed   bool
ElapsedMs          long
Diagnostics        FormatDiagnosticInfo[]
    Severity  int     0=Info, 1=Warning, 2=Error
    Message   string
    Line      int
    Offset    int
```

---

### `FormatSelection` → `FormatSelectionResult`

Formats a selected text range within a document.

**Request** (`FormatSelectionRequest`):
```
Text            string    Full document text
SelectionStart  int       Zero-based start character offset
SelectionEnd    int       Zero-based end character offset
ProfileName     string?
```

**Response** (`FormatSelectionResponse`):
```
Success           bool
FormattedText     string   Replacement text for the selection
OriginalStart     int      Adjusted selection start (may shift after formatting)
OriginalEnd       int      Adjusted selection end
WasModified       bool
ValidationPassed  bool
ElapsedMs         long
```

---

### `FormatPreview` → `FormatPreviewResult`

Formats a sample SQL string against an unsaved profile (used in the Options dialog preview pane).

**Request** (`FormatPreviewRequest`):
```
SampleText   string   SQL to format
ProfileJson  string   Full JSON of the profile to preview (not yet saved)
```

**Response** (`FormatPreviewResponse`):
```
FormattedText  string
ElapsedMs      long
```

---

### `FormatAction` → `FormatActionResult`

Applies a specific formatting action (e.g. casing-only, expand wildcards).

**Request** (`FormatActionRequest`):
```
Text             string
ActionType       int      See FormatActionType enum (0–15)
ProfileName      string?
SelectionStart   int
SelectionLength  int
SessionId        string?
```

**FormatActionType values:**
```
0  CasingOnly                Apply keyword/identifier casing only
1  ExpandWildcards           SELECT * → explicit column list
2  InsertSemicolons          Add statement-terminating semicolons
3  QualifyNames              Add schema prefix to unqualified names
4  ToggleAs                  Add/remove AS keyword in column aliases
5  ToggleBrackets            Add/remove bracket quoting on identifiers
9  ExpandInsertColumns       INSERT INTO t VALUES → explicit column list
10 ExpandExecParameters      EXEC proc 1,'x' → EXEC proc @p1=1, @p2='x'
11 ExpandUpdateColumns       UPDATE SET col1=1 → multi-line SET
12 ConvertOldStyleJoins      FROM a,b WHERE a.id=b.id → INNER JOIN
13 AddGroupByColumns         Add non-aggregate SELECT columns to GROUP BY
14 EncapsulateBeginEnd       Wrap IF/WHILE body in BEGIN...END
15 ReplaceDeprecatedSyntax   Modernize deprecated T-SQL patterns
```

**Response** (`FormatActionResponse`):
```
Success        bool
FormattedText  string
WasModified    bool
ElapsedMs      long
ErrorMessage   string?
Warnings       string[]?
```

---

### `BulkFormat` → `BulkFormatResult`

Formats multiple SQL files on disk.

**Request** (`BulkFormatRequest`):
```
SessionId      string
FilePaths      string[]   Absolute paths only; no traversal sequences
ProfileName    string?
CreateBackups  bool       If true, saves .bak copies before overwriting
DryRun         bool       If true, returns results without writing files
```

**Response** (`BulkFormatReportResponse`):
```
SessionId      string
TotalFiles     int
SuccessCount   int
FailedCount    int
SkippedCount   int
ElapsedMs      long
Results        FileResult[]
    FilePath      string
    Status        int    0=Formatted, 1=AlreadyFormatted, 2=ParseError,
                         3=Error, 4=Skipped, 5=Backup
    LinesChanged  int
    ErrorMessage  string?
```

---

### `BulkFormatCancel` (notification, no response)

Cancels an in-progress bulk format.

**Request** (`BulkFormatCancelRequest`):
```
SessionId  string   Must match the SessionId used in BulkFormat
```

---

## Format Styles Editor Messages

> Introduced by spec 020 US3 (T049–T051). Full contract:
> `specs/020-sqlprompt-visual-parity/contracts/ipc-style-editor-schema.md`.

### `RequestStyleEditorSchema` → `StyleEditorSchemaResult`

Returns the canonical descriptor of every formatting setting (groups + settings + types + defaults + SQL Prompt aliases) so the Format Styles editor UI can build its tree from one source of truth.

**Request** (`StyleEditorSchemaRequest`):
```
ClientSchemaVersion  int?   (optional) Shell's cached version; engine short-circuits if it matches.
IncludeUnsupported   bool   When true (default), unsupported / AKML-only settings are returned
                            so the editor can render them disabled-with-value per FR-023.
```

**Response** (`StyleEditorSchemaResponse`):
```
SchemaVersion   int     Engine's current schema version.
SchemaJson      string?  Full FormatSettingSchema serialised as System.Text.Json.
                        Null when Cached = true.
Cached          bool    True when ClientSchemaVersion matched and the engine returned no body.
ErrorMessage    string?  Populated only on failure.
```

The JSON-string payload (rather than a typed MessagePack object) keeps the wire contract decoupled from `AkmlSql.Formatting` types, which `AkmlSql.Core`'s netstandard2.0 surface cannot reference.

**Effect**: Engine builds the schema once (lazy, via reflection over `FormattingProfile`) and caches it for the process lifetime. Short-circuit path returns within ~5 ms; full-payload path is ~30 ms p95 including IPC.

---

## Profile Management Messages

### `ProfileList` → `ProfileListResult`

**Request**: no payload required.

**Response** (`ProfileListResponse`):
```
Profiles  ProfileInfo[]
    Name         string
    Description  string
    Author       string
    IsBuiltIn    bool
    BasedOn      string?   Name of the parent profile this derives from
    Modified     string    ISO 8601 datetime string
```

---

### `ProfileSave` → `ProfileSaveResult`

**Request** (`ProfileSaveRequest`):
```
ProfileJson  string   Full JSON serialization of the FormattingProfile
```

**Response** (`ProfileSaveResponse`):
```
Success       bool
ErrorMessage  string?
```

---

### `ProfileDelete` → `ProfileDeleteResult`

**Request** (`ProfileDeleteRequest`):
```
Name  string   Profile name to delete (built-in profiles cannot be deleted)
```

**Response** (`ProfileDeleteResponse`):
```
Success       bool
ErrorMessage  string?
```

---

### `ProfileImport` → `ProfileImportResult`

**Request** (`ProfileImportRequest`):
```
SourceFormat       string    "sqlprompt" | "sqlpromptstylev2" | "akmlstyle" | "akml"
FileContent        byte[]    Raw file bytes (UTF-8)
TargetProfileName  string?   Override name for the imported profile
```

**Response** (`ProfileImportResponse`):
```
Success              bool
MappedOptionsCount   int      Number of options successfully mapped (-1 for native format)
UnmappedOptionsCount int      Number of options not mappable
UnmappedOptions      string[] Names of options that could not be mapped
ErrorMessage         string?
```

---

## Snippet Messages

### `SnippetExpand` → `SnippetExpandResult`

**Request** (`SnippetExpandRequest`):
```
SessionId      string
Shortcode      string    e.g. "ssf", "cte"
ClipboardText  string?   Passed to $CLIPBOARD$ variable
SelectedText   string?   Passed to $SELECTION$ variable
```

**Response** (`SnippetExpandResponse`):
```
Success       bool
ExpandedText  string
CursorOffset  int         Position for the caret after insertion
ErrorMessage  string?
Placeholders  PlaceholderInfo[]
    Name         string   Placeholder identifier
    DisplayName  string
    DefaultValue string
    Offset       int      Character offset in ExpandedText
    Length       int
```

---

### `SnippetList` → `SnippetListResult`

**Request** (`SnippetListRequest`):
```
Query           string?   Free-text search (searches shortcode, name, description, tags, body)
Context         string?   Clause context, e.g. "SELECT", "FROM" → filters by snippet context
HasSelection    bool      True when editor has selected text (shows surround-with snippets only)
SourceFilter    int       0=All, 1=Personal, 2=Team, 3=BuiltIn
CategoryFilter  string?   Filter by snippet category
```

**Response** (`SnippetListResponse`):
```
Snippets  SnippetInfo[]
    Id           string
    Shortcode    string
    Name         string
    Description  string
    Category     string
    Source       int      1=Personal, 2=Team, 3=BuiltIn
    SurroundsWith bool
    UsageCount   int
    Tags         string[]
```

---

### `SnippetSave` → `SnippetSaveResult`

**Request** (`SnippetSaveRequest`):
```
SnippetJson  string   JSON of the Snippet model (max 1 MB)
IsNew        bool     If true, sets Created timestamp; otherwise only sets Modified
```

**Response** (`SnippetSaveResponse`):
```
Success       bool
ErrorMessage  string?
```

---

### `SnippetDelete` → `SnippetDeleteResult`

**Request** (`SnippetDeleteRequest`):
```
SnippetId  string   Snippet GUID (built-in snippets cannot be deleted)
```

**Response** (`SnippetDeleteResponse`):
```
Success       bool
ErrorMessage  string?
```

---

## Code Analysis Messages

### `RequestAnalyze` → `AnalysisResult`

**Request** (`CodeAnalysisRequest`):
```
SessionId     string
DocumentText  string   Full SQL text to analyze
FilePath      string?  Used to load per-project .casettings overrides
```

**Response** (`CodeAnalysisResponse`):
```
Issues  CodeIssueInfo[]
    RuleId        string     e.g. "PE001"
    Severity      int        0=Info, 1=Warning, 2=Error
    Message       string
    Line          int        1-based
    Column        int        1-based
    EndLine       int
    EndColumn     int
    RuleCategory  string     "Performance", "BestPractices", "Security", etc.
    FixActions    FixActionInfo[]
        Label       string
        FixType     int      0=None, 1=ReplaceRange, 2=InsertBefore, 3=InsertAfter
        Replacement string?
        StartOffset int
        EndOffset   int
```

---

### `AnalysisSettingsChanged` (notification, no response)

Signals that `.casettings` files have changed. Engine invalidates the settings loader cache and the AppSettings cache.

---

## Refactoring Messages

### `RequestRefactorPreview` → `RefactorPreviewResult`

**Request** (`RefactorPreviewRequest`):
```
SessionId        string
OperationType    string   "ExtractToProc" | "EncapsulateAsView" | "ExtractToCte" |
                          "ConvertTempTable" | "SafeRename" | "ParameterizeValues" |
                          "GenerateTests" | "DocumentProc"
SelectionStart   int
SelectionLength  int
Options          Dictionary<string,string>?   Operation-specific options
```

**Response** (`RefactorPreviewResponse`):
```
Success        bool
Changes        RefactorChangeInfo[]
    FilePath      string
    StartOffset   int
    EndOffset     int
    NewText       string
    Description   string
ErrorMessage   string?
Warnings       string[]?
```

---

### `RequestRefactorApply` → `RefactorApplyResult`

Applies the changes produced by a previous preview.

**Request** (`RefactorApplyRequest`):
```
SessionId  string
Changes    RefactorChangeInfo[]   Same changes returned from preview
```

**Response** (`RefactorApplyResponse`):
```
Success       bool
ErrorMessage  string?
```

---

## Error Response

Any request may receive an `Error` response (type 106) if an unhandled exception occurs:

```
ErrorInfo
    Code     int      Always -1
    Message  string   Exception message
```

---

## Example Exchange

```
Shell sends:
  RpcMessage {
    MessageType: 3 (RequestCompletion),
    RequestId: 42,
    Payload: MessagePack({ SessionId: "abc", CursorOffset: 150, TriggerChar: "." })
  }

Engine replies:
  RpcMessage {
    MessageType: 101 (CompletionResult),
    RequestId: 42,
    Payload: MessagePack({ Items: [...] })
  }
```
