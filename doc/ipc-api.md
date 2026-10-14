# AKML SQL — IPC API Reference

All communication between the shell extension and the engine uses MessagePack-framed `RpcMessage` envelopes. The named pipe is the **default** transport (used by all SSMS/VS shell extensions today); spec 021 (web edition) introduced an `IRpcTransport` abstraction so the same handlers also serve in-process and (future M3) WebSocket transports. This document describes every message type, its direction, request payload, and response payload.

---

## Transport Plurality (spec 021 M0)

The engine supports three transports, all of which carry the same `RpcMessage` envelope and the same message-type integer codes:

| Transport | Class | Wire | Consumers |
|-----------|-------|------|-----------|
| **Named pipe** (default) | `Transports/NamedPipeTransport` | `[length][CRC][MessagePack(RpcMessage)]` over `\\.\pipe\akmlsql-engine-{SID}-{PID}` | SSMS 22 (the extension) |
| **In-process** | `Transports/InProcessTransport` | Method calls; no serialisation | Blazor WASM running engine logic in the browser tab (spec 021 M2+); engine unit tests |
| **WebSocket** (M3, future) | `Transports/WebSocketTransport` | One WebSocket binary message = one `RpcMessage` MessagePack payload | Browser ↔ engine bridge (spec 021 M3+) |

All three transports raise a `RequestReceived` event carrying the inbound `RpcMessage`; the per-process `RpcRouter` resolves the message-type integer code to an `IRpcRequestHandler<TRequest, TResponse>` and dispatches. **Wire format and message-type integer codes are unchanged** from the original (pre-M0) named-pipe contract, so existing shell extensions need zero updates.

Routing details inside the engine (post-M0):

1. Inbound frame arrives at a transport.
2. Transport raises `RequestReceived` with the decoded `RpcMessage`.
3. `RpcRouter.RouteAsync(msg, ctx, ct)` resolves the message-type integer code against the router's registered handlers — the registry is populated at engine start by `EngineHandlerRegistry.RegisterAllHandlers()`, called from `EngineComposition.Build()`.
4. Typed handlers (`IRpcRequestHandler<TRequest, TResponse>`, registered via `RpcRouter.Register`) receive the deserialised request; delegating handlers (registered via `RpcRouter.RegisterRaw`, e.g. AI, history, navigation) receive the raw `RpcMessage` and produce the response envelope themselves.
5. A typed handler returns a `TResponse` that the router serialises into the response `RpcMessage`; a delegating handler returns the `RpcMessage` directly. Notifications return `null`.
6. The transport writes the response back over the same channel.

Every dispatch flows through the `RpcRouter`; the pre-M0 53-case `switch` is gone entirely.

---

## Transport Layer — Named Pipe

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
| Shell→Engine | `ProfileGet` | 34 |
| Shell→Engine | `ProfileRename` | 35 |
| Shell→Engine | `SessionSuppression` | 36 |
| Shell→Engine | `AiProviderTest` | 77 |
| Engine→Shell | `AiProviderTestResult` | 177 |
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
| Engine→Shell | `ProfileGetResult` | 134 |
| Engine→Shell | `ProfileRenameResult` | 135 |
| Engine→Shell | `SessionSuppressionResult` | 136 |
| Shell→Engine | `HandshakeRequest` | 200 |
| Engine→Shell | `HandshakeResponse` | 201 |
| Shell→Engine | `SchemaIdentifyRequest` | 202 |
| Engine→Shell | `SchemaIdentifyResponse` | 203 |
| Shell→Engine | `ExecuteQuery` | 212 |
| Engine→Shell | `ExecuteQueryResult` | 213 |
| Shell→Engine | `ExecuteCancel` (notification) | 214 |
| Shell→Engine | `ApplyChanges` | 215 |
| Engine→Shell | `ApplyChangesResult` | 216 |
| Shell→Engine | `ChangeDatabase` | 217 |
| Engine→Shell | `ChangeDatabaseResult` | 218 |
| Shell→Engine | `ExecuteParse` | 219 |
| Engine→Shell | `ExecuteParseResult` | 220 |

Note: codes 200–203 are part of the spec 021 web-edition bridge. They carry over named-pipe transports unchanged, but their primary purpose is the browser ↔ engine WebSocket bridge introduced in M3.

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

**Response** (`CompletionResponse`) — actual wire DTO (`AkmlSql.Core/Ipc/Messages/CompletionResponse.cs`, MessagePack keys in parentheses):
```
Items         CompletionItem[] (0)
IsIncomplete  bool             (1)   true when the list was truncated at the suggestion cap

CompletionItem
    DisplayText    string  (0)   Display text (may be alias/schema-qualified, e.g. "o.OrderID")
    InsertText     string  (1)   Text to insert on accept
    ObjectType     int     (2)   CompletionObjectType: 0=Table 1=View 2=Column 3=Keyword 4=Snippet
                                 5=Function 6=Procedure 7=Schema 8=Database 9=Variable 10=Alias
                                 11=Parameter 12=SmartAction
    SecondaryText  string  (3)   Type info / row count / description
    SourceObject   string  (4)   Owning object's full name
    SortPriority   int     (5)   Ascending — lower ranks first
    IsLinkedServer bool    (6)   Pins linked-server items past the suggestion cap
    FilterText     string? (7)   Spec 032 (FR-026): the text fuzzy matching scores against when
                                 it differs from DisplayText (e.g. the bare column name of a
                                 qualified item). Null → filter on DisplayText. Additive field;
                                 pre-032 peers omit it and deserialize to null.
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
ProfileFallbackWarning string?  Key 7 (spec 040): set when the style could not be loaded and defaults were used
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
SqlPromptModel       bool   Key(2) — spec 039. When true, the engine answers with SQL Prompt's
                            option model instead of AKML's settings schema (see below).
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

**SQL Prompt model (spec 039)**: with `SqlPromptModel = true` the body is
`SqlPromptOptionCatalog.ToEditorSchemaJson()` — the same `groups` / `settings` shape plus
`"model": "sqlPrompt"`, `SchemaVersion = 2002` (2002 added each setting's `example` and the top-level `exampleQueries` / `selectExamples`). Groups are SQL Prompt's 14 pages (`parentId` =
`global` / `statements` / `clauses` / `expressions`, `sample` = the page's preview SQL); setting ids
are `sqlPrompt.<SQL Prompt path>` with typed `default`, `allowedEnumValues` + `enumLabels`,
`min` / `max`, `subgroup` (page sub-heading), `note`, and `enabledWhen: { id, value }`. An engine
without spec 039 ignores the flag and returns the AKML schema, which the shell detects by the
missing `model` field.

**Schema v2 (spec 033)**: `SchemaVersion` is now `2`. The JSON body additionally populates `parentId` on every group row (5-category hierarchy: `global` / `statements` / `clauses` / `expressions` / `other` — category ids travel ONLY as `parentId` values, never as group rows), plus per-setting `description`, `allowedEnumValues` (exact stored spellings, default included) and `min`/`max` for ranged ints, all sourced from `[SettingMeta]` attributes on the profile POCOs. The previously-opaque `insertStatements.columns`/`values` blobs are flattened into six multi-segment setting ids. All v2 fields are optional for clients: a v1 consumer renders flat with free-text enum boxes. Contract: `specs/033-format-styles-window/contracts/style-editor-schema-v2.md`.

---

## Profile Management Messages

### `ProfileGet` (34) → `ProfileGetResult` (134) — spec 033

Returns one stored profile's `.akmlstyle` file text **verbatim** (never re-serialized: serialization bumps `metadata.modified` and drops unknown fields nested inside option groups). This is the Format Styles editor's load-on-select read and the merge base for its edit-saves.

**Request** (`ProfileGetRequest`):
```
Name  string  Key(0)  Display name; resolved custom-first then built-in, OrdinalIgnoreCase.
```

**Response** (`ProfileGetResponse`):
```
Success       bool     Key(0)  False when the name resolves to no file (nothing is ever created).
ErrorMessage  string?  Key(1)  Populated iff Success == false.
Name          string?  Key(2)  Resolved display name.
ProfileJson   string?  Key(3)  Raw stored file text, verbatim.
IsBuiltIn     bool     Key(4)  True iff resolved from the built-in dir with no custom shadow
                               (directory-derived — the JSON's own isBuiltIn field is untrusted).
HasBuiltIn          bool     Key(5)  A shipped style of this name exists (edited or not).
IsCustomizedBuiltIn bool     Key(6)  A shipped style the user has edited.
SqlPromptJson       string?  Key(7)  Spec 039 — the style as a SQL Prompt document with every option
                                     written out (its own document, the spec-031 import source, or
                                     the SQL Prompt reading of an AKML-model style).
IsSqlPromptStyle    bool     Key(8)  Spec 039 — the stored style already is a SQL Prompt style.
```

### `ProfileRename` (35) → `ProfileRenameResult` (135) — spec 033

Atomically renames a **custom** profile engine-side: rewrites `metadata.name` (+`modified`) via a raw JsonNode edit, writes the new file atomically, deletes the old one, and moves the `<name>.source.json` import sidecar. Built-in sources, and collisions with any custom or built-in name (OrdinalIgnoreCase), are rejected; case-only renames are allowed. **Never touches config.json** — after renaming the active style, the shell updates `Formatter.ActiveProfile` itself.

**Request** (`ProfileRenameRequest`):
```
OldName  string  Key(0)
NewName  string  Key(1)  Engine-side sanitized.
```

**Response** (`ProfileRenameResponse`):
```
Success       bool     Key(0)
ErrorMessage  string?  Key(1)
NewName       string?  Key(2)  Final (sanitized/trimmed) name actually persisted.
```

**Behavior fixes shipped with spec 033**: `ProfileDelete` (16) now returns `Success=false` when the named profile does not exist (it previously discarded the delete result and always reported success), and `ProfileSave` (15) rejects `ProfileJson` payloads over 1 MB (mirroring the import cap).

### `SessionSuppression` (36) → `SessionSuppressionResult` (136)

Adds, removes, clears or lists the rules suppressed for the current session — the "Disable RULE for this session" quick fix, and the Restore button on the Manage Code Analysis Rules dialog. Backed by `SessionSuppressionStore`, which lives only in engine memory; because the engine runs one process per shell instance, the scope is exactly the IDE session, and nothing is written to the script or to `config.json`.

`AnalysisEngine` applies it as a post-filter over the finished diagnostics rather than by dropping the rule from the run set, so no batch-cache invalidation is needed and lifting a suppression takes effect on the very next pass.

**Request** (`SessionSuppressionRequest`):
```
RuleId  string  Key(0)  Ignored by Clear and List.
Action  int     Key(1)  0=Add, 1=Remove, 2=Clear, 3=List. Defaults to List.
```

**Response** (`SessionSuppressionResponse`):
```
Success          bool      Key(0)
SuppressedRules  string[]  Key(1)  The full list after the change, sorted. Returned by every action.
Error            string?   Key(2)
```

Empty payloads are allowed and are treated as List. An unrecognised `Action` returns `Success=false` and changes nothing.

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
    IsCustomizedBuiltIn  bool  Key(7)
    IsSqlPromptStyle     bool  Key(8)  Spec 039 — written in SQL Prompt's model
```

---

### `ProfileSave` → `ProfileSaveResult`

**Request** (`ProfileSaveRequest`):
```
Name         string   Key(0)  The style's name (the JSON's metadata.name is what is saved)
ProfileJson  string   Key(1)  Full JSON serialization of the FormattingProfile
Description  string?  Key(2)
BasedOn      string?  Key(3)
CreateOnly   bool     Key(4)  Spec 039 — the save creates a NEW style: the engine refuses a built-in's
                              name and a name already taken (file name or stored name, ignoring case
                              and surrounding spaces) instead of overwriting (ProfileManager.SaveNew).
                              False (an edit) overwrites, as before. Older engines ignore the key.
```

The stored name is trimmed, like the file name it is saved under.

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
FileContent        byte[]    Raw file bytes (UTF-8; a leading BOM is tolerated)
TargetProfileName  string?   Override name for the imported profile
```

For the `"sqlprompt"` / `"sqlpromptstylev2"` formats the engine sniffs the content by its
first non-whitespace character (BOM-tolerant): `{` routes to the Redgate JSON style importer
(modern SQL Prompt 10.5+ one-file-per-style format, spec 031); `<` routes to the legacy XML
importer (AKML's spec-020 export shape). Anything else fails with a clear error.

**Failure semantics**: on any parse failure the response is `Success = false` and **nothing
is saved**. Importing under a built-in profile name also fails (`Success = false`, error
message mentions "built-in") — in every format (JSON, XML `.sqlpromptstylev2` and `.akmlstyle`),
whether or not the built-in has been edited: `ProfileSave` accepts a built-in's name (that is how a
built-in is edited), so an import landing on it would silently become an edit of the built-in.
`DuplicateProfile` likewise refuses a built-in's name or an existing style's name. When `TargetProfileName` is set it overrides the style's
internal `metadata.name` (JSON) or names the profile (XML, which has no internal name).
Successful JSON imports additionally preserve a verbatim `<name>.source.json` copy beside
the saved profile for lossless re-export.

**Spec 039**: a JSON import now saves a **SQL Prompt style** — the document is kept whole in the
profile's `sqlPrompt` object (id included) and formats with the SQL Prompt layout. Every SQL Prompt
option in the file is reported `mapped`; keys this build does not know are reported `unknown` and
kept in the style unchanged.

**Export (`ProfileExportSqlPrompt`, 29/129)**: when `DestinationPath` ends in `.json`, the engine
writes the style's SQL Prompt document (SQL Prompt's minimal form, UTF-8 without BOM) — for an
AKML-model style, its SQL Prompt reading. Any other extension writes the spec-020
`.sqlpromptstylev2` XML as before.

**Response** (`ProfileImportResponse`):
```
Success              bool
MappedOptionsCount   int      Number of options successfully mapped (-1 for native format)
UnmappedOptionsCount int      Number of options not mappable
UnmappedOptions      string[] Names of options that could not be mapped (legacy XML path)
ErrorMessage         string?
OptionReports        ProfileImportOptionReport[]?   Key(5) — per-option classification
                                                    (spec 031, JSON path; null from pre-031
                                                    engines and on the XML path)
    Path    string   Redgate option path, e.g. "lists.placeCommasBeforeItems"
    Value   string   The file's value for that option, as text
    Status  string   "mapped" | "mapped-pending-render" | "unsupported" | "unknown"
    Reason  string?  Why the option is not (fully) honoured; null for "mapped"
ProfileName          string?  Key(6) — final saved profile name (post TargetProfileName
                              override); null on failure or from pre-031 engines
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
Kind            int       Spec 042 (key 5): DatabaseKind of the snippets wanted, 0=SQL Server (default), 1=PostgreSQL
```

A snippet file carries `metadata.dialect` (`sqlserver`, the default when absent, or `postgresql`). The list is cut to the requested
kind, and `SnippetExpand` uses the dialect of the session's kind, so a shortcode such as `sel` is never the other dialect's text.

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

## AI Messages

### `AiChat` (75) → `AiChatResult` (175)

Sends one chat-panel message (with the conversation history for multi-turn context) and returns
the assistant's reply plus optional code actions. The pair predates this reference; it is
documented at spec 037 because the response gained a key.

**Request** (`AiChatRequest`):
```
SessionId  string         Key(0)  Active editor session — binds the schema context
Message    string         Key(1)  The user's chat message
History    ChatTurnDto[]  Key(2)  Previous turns, oldest first
    Role     string  Key(0)  "user" | "assistant"
    Content  string  Key(1)
```

**Response** (`AiChatResponse`):
```
Success      bool              Key(0)
Response     string?           Key(1)  The assistant's reply text
CodeActions  CodeActionDto[]?  Key(2)  Optional actionable suggestions the user can apply
    Label      string  Key(0)  e.g. "Apply to Editor"
    ActionType string  Key(1)  "applyToEditor" | "copyToClipboard" | "runQuery"
    Code       string  Key(2)
ErrorMessage string?           Key(3)  Set when Success is false
TokensUsed   int               Key(4)
LatencyMs    int               Key(5)
AgentName    string?           Key(6)  Spec 037 (US3/US4, FR-042/FR-052): name of the agent that
                                     produced the answer — a fallback-chain agent's name, or the
                                     offline provider's display name when the fallback answered.
                                     Additive field: a peer that does not know key 6 ignores it,
                                     and a peer expecting it from an older engine deserializes
                                     null, so the shell renders no attribution rather than a
                                     guessed one.
```

### `AiProviderTest` (77) → `AiProviderTestResult` (177) — spec 036

Verifies an AI provider configuration (provider, model, endpoint, key) with a one-line test
prompt, so the Options dialog can validate a configuration before the user saves it. The pair
existed engine-side since the AI feature landed but had no caller; spec 036 (US2, FR-009)
wired it to the "Test connection" button on the AI Assistance page. The full caller contract is
`specs/036-kimi-chat-updater-fixes/contracts/ai-provider-test.md`.

**Request** (`AiProviderTestRequest`):
```
Provider   string   Canonical provider id (anthropic | openai | azure | gemini | kimi |
                    ollama | lmstudio | custom) — normalise through AiProviderIds first
ApiKey     string   The key as the field holds it (dpapi:-wrapped or plaintext — the engine
                    runs it through the KeyDecryptor hook, which passes plaintext through)
Endpoint   string?  Optional; required for azure, defaulted for kimi/ollama
Model      string   Required
```

**Response** (`AiProviderTestResponse`):
```
Success          bool
ModelName        string?   Echo of the tested model
ProviderVersion  string?   Echo of the provider
ErrorMessage     string?   Set when Success is false — one of the five FR-014 causes
                           (missing/invalid key, unknown model, unreachable endpoint,
                           quota/rate-limit, timeout); never a raw provider payload
LatencyMs        int
```

Caller obligations: test the dialog's CURRENT field values (not saved settings); use
`AiIpcTimeouts.ForAiRequestMs` for the wait budget; never log the key; "engine not connected"
is a distinct pre-IPC outcome. The other AI messages (70–74, 76, 78 / 170–174, 176, 178–179) are
implemented but not yet documented in this reference.

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

---

## Spec 021 — Web Edition Bridge Messages

Spec 021 (web edition) adds two message-type pairs the browser uses on the WebSocket bridge. Both pairs are MessagePack-typed like everything else and are also transport-agnostic — they work over named pipes too, but the IDE plugins do not currently send them.

### Handshake (200 / 201) — `contracts/rpc-handshake.md`

First MessagePack frame on every freshly opened WebSocket. Validates the protocol-version range, optional pairing PIN or bearer token, and returns the engine's advertised capability list.

```
HandshakeRequest (Shell→Engine, type 200)
    PairingPin           string?    One-time PIN for first-time LAN pairing. Mutually exclusive with BearerToken.
    BearerToken          string?    Long-lived bearer token from a prior successful pairing.
    WebVersion           string     Web-edition version (semver).
    ProtocolVersionMax   int        Highest protocol version the client supports.
    ProtocolVersionMin   int        Lowest protocol version the client supports.
    BrowserLabel         string?    Human-readable identifier of the browser (shown in the engine's Pairing UI).

HandshakeResponse (Engine→Shell, type 201)
    Status                     string    One of: "ok" | "pin_invalid" | "pin_required" | "protocol_mismatch" | "server_busy".
    EngineVersion              string    Engine semver.
    ChosenProtocolVersion      int       Always within the intersection of client min/max and engine min/max.
    EngineCapabilities         string[]  Stable capability identifiers (see "Capabilities" below).
    NewBearerToken             string?   Set ONLY on a successful PairingPin handshake. The browser stores it (wrapped) and uses it on future connections.
    ServerCanonicalIdentity    string?   The engine's canonical identity for any SQL Server currently selected by this session — used as the schema-cache key. Null if engine has no DB connection.
    ErrorMessage               string?   Human-readable detail on error; null on success.
    ServerTlsThumbprint        string?   (key 7) LAN-mode certificate thumbprint for pinning.
    HostKind                   string?   (key 8, spec 041) "ide" (started by SSMS over the pipe, runs as the signed-in user),
                                         "service" (the Windows service) or "console" (an interactive --web); null from older
                                         engines. The web calls the engine's styles "Shared with SSMS" only for "ide".
    RunsAs                     string?   (key 9, spec 041) the account the engine runs as; shown on hover and in Diagnostics.
```

#### Capabilities (current advertised list)

The engine advertises a list of stable capability identifiers in `EngineCapabilities`. The browser tracks them and renders an inline "feature requires engine ≥ X" notice (NOT a full-page blocker) when a feature's required capability is missing.

| ID | Constant | Meaning |
|----|----------|---------|
| `core.format.v1` | `Capabilities.CoreFormatV1` | Formatter pipeline available (always present). |
| `core.analysis.v1` | `Capabilities.CoreAnalysisV1` | Analyser rules available (always present). |
| `schema.v2` | `Capabilities.SchemaV2` | Live schema and IntelliSense (M3). |
| `schema.cache.v1` | `Capabilities.SchemaCacheV1` | Schema-cache identity protocol — engine reports `ServerCanonicalIdentity` and serves `SchemaIdentifyRequest` (M5). |
| `snippets.write` (planned) | `Capabilities.SnippetsWrite` | Snippet save/delete via the bridge. Added when T115 lands. |
| `styles.sqlprompt.v1` | `Capabilities.StylesSqlPromptV1` | Spec 039 — SQL Prompt styles via the bridge: `ProfileList` / `ProfileGet` (with `SqlPromptJson`) / `ProfileSave` / `ProfileDelete` / `ProfileRename` / `ProfileReset`. The web edition stores styles on the engine (the styles SSMS uses) when this is advertised. |
| `refactoring.heavy` (planned) | `Capabilities.RefactoringHeavy` | Heavyweight schema-aware refactorings. Added when T117 lands. |
| `ai.text-to-sql.v1` (reserved) | `Capabilities.AiTextToSqlV1` | AI Text-to-SQL via the bridge. AI invocation in the web edition normally goes direct-to-provider (FR-030); this capability covers any engine-hosted helpers a future M6 design adds. |
| `diagnostics.engine-log-tail.v1` (planned) | `Capabilities.DiagnosticsEngineLogTailV1` | Engine log-tail request used by the diagnostics export bundle. |
| `session.database.v1` | `Capabilities.SessionDatabaseV1` | Spec 041 — the database selector: `ListDatabasesRequest.SessionId` (key 1), `ChangeDatabase` (217 → 218) and `ExecuteQueryResult.CurrentDatabase` (key 12). |
| `execute.v2` | `Capabilities.ExecuteV2` | Spec 041 — scripts split on GO and keep running after a failing batch; the execute reply carries structured messages, batches, the completion time and the omitted-message count (keys 8–11), sets carry their batch (key 11), and `ExecuteParse` (219 → 220) is understood. |

### SchemaIdentify (202 / 203) — `contracts/schema-cache-shape.md`

Used by the browser to resolve the canonical `(serverCanonicalIdentity, databaseName)` pair that keys its IndexedDB schema cache. The pair is stable across host-string variations: two distinct DNS aliases pointing at the same SQL Server resolve to the same identity, so they share one cache entry.

```
SchemaIdentifyRequest (Shell→Engine, type 202)
    SessionId                  string    The session whose connection we are identifying.

SchemaIdentifyResponse (Engine→Shell, type 203)
    SessionId                  string    Echoed from the request.
    ServerCanonicalIdentity    string    Stable identifier for the SQL Server instance (resolved from @@SERVERNAME → SERVERPROPERTY('ServerName')). Empty when no live connection.
    DatabaseName               string    Current database name on the session. Empty when no live connection.
    HasConnection              bool      True only when the engine successfully resolved an identity. Browser MUST NOT cache against a response where this is false.
    ErrorMessage               string?   Human-readable detail when HasConnection is false (e.g. "Engine has no live connection for this session.").
```

The handshake's `ServerCanonicalIdentity` covers the single-DB case (engine has exactly one connection); `SchemaIdentify` covers the multi-session case where the browser needs to resolve identity per `SessionId`.

## Query Execution (212–216, 219/220) — web edition

Spec 030 added query execution with inline editing for the web edition; spec 041 made a script run as SSMS
runs it (`specs/041-web-ssms-ux-parity/contracts/ipc.md`). Every DTO is MessagePack with contiguous
`[Key(n)]` and **append-only** keys: an older web bundle ignores the trailing keys, and a newer bundle on an
older engine sees them at their defaults (empty arrays, 0, null) and falls back to the legacy fields.

### `ExecuteQuery` (212) → `ExecuteQueryResult` (213)

Runs the script on the session's persistent connection (temp tables, `SET` options and `USE` persist between
runs). The engine clamps the caps (rows ≤ 100,000, timeout ≤ 600 s) and always answers — never null.

```
ExecuteQueryRequest (type 212; also the payload of 219)
    0  SessionId              string    The web session (ISqlConnectionService.SessionId).
    1  Sql                    string    The script (the selection, or the whole document).
    2  MaxRows                int       Rows kept per result set (default 1,000).
    3  CommandTimeoutSeconds  int       Per command (default 30).
    4  QueryId                string    App-level id ExecuteCancel refers to.
    5  IncludeProvenance      bool      Read base-table provenance (KeyInfo) so a set can be edited.

ExecuteQueryResult (type 213; also the payload of 220)
    0  QueryId                string
    1  Status                 int       ExecuteStatus: 0 Ok · 1 Error · 2 Cancelled · 3 TimedOut · 4 NoConnection ·
                                        5 Skipped · 6 Disconnected (client-only) · 7 NoReply (client-only).
                                        Ok when no batch reported an error; Error when any did (the script still ran
                                        on); TimedOut / Cancelled when the script stopped for that reason.
    2  ErrorMessage           string?   The first error's text.
    3  ResultSets             ExecuteResultSet[]   Always returned, also when a later batch failed.
    4  Messages               string[]  Every message worded as SSMS words it: "Msg 208, Level 16, State 1, Line 4"
                                        + newline + text ("Procedure p, " before "Line" when set); "(N rows
                                        affected)"; "(N rows returned; more exist)" for a capped set; PRINT text;
                                        engine lines. Kept for older bundles; MessageDetails carries the same.
    5  ElapsedMs              long      The whole script.
    6  TotalRowsAffected      int       Sum of the DML row counts; -1 when none.
    7  ConnectionWasReset     bool      The connection had broken and was reopened (temp tables and SET state lost).
    8  MessageDetails         ExecuteMessageDto[]  execute.v2 — every message in stream order.
    9  Batches                ExecuteBatchDto[]    execute.v2 — one per batch (a GO n loop is one entry).
    10 CompletedAtUnixMs      long      execute.v2 — end of the script, UTC Unix ms; 0 from older engines.
    11 MessagesOmitted        int       execute.v2 — messages dropped beyond 10,000 (one System line says so).
    12 CurrentDatabase        string?   session.database.v1 — the connection's database after the script.

ExecuteResultSet
    0  ColumnNames            string[]
    1  ColumnSqlTypes         string[]  The DECLARED type: nvarchar(50), nvarchar(max), decimal(10,2), datetime2(3),
                                        time(0); bare for int, date, money, uniqueidentifier, UDTs. Older engines
                                        send bare names; SqlTypeName.Parse reads both.
    2  ClrTypeHints           int[]     ClrTypeHint per column (how to parse the text back).
    3  Rows                   string?[][]  Round-trip text (ISO-8601 "o" dates, TimeSpan "c", Base64 binary,
                                        invariant numbers); null = SQL NULL; very large values are sent as
                                        "[text N chars]" / "[binary N bytes]".
    4  Truncated              bool      The row cap or the reply budget stopped the set.
    5  RowsOmitted            int
    6  Provenance             ColumnProvenanceDto[]  Base column, key, identity, read-only, nullability,
                                        provider type, size, precision and scale (null when not applicable —
                                        SqlClient's 255 sentinel is never sent, so Apply never binds it).
    7  IsEditable             bool      One base table with a key.
    8  BaseSchema / 9 BaseTable / 10 BaseCatalog   string?   BaseCatalog is the database the set was read
                                        from when the name was two-part (a script's USE can move between sets),
                                        so Apply writes where the rows came from.
    11 BatchIndex             int       execute.v2 — the batch that produced the set.
    12 ReadOnlyReason         string?   Spec 042 (PostgreSQL) — why IsEditable is false when provenance was asked for: not a
                                        table, more than one table, a view, no primary key, a column twice, part of
                                        the key missing. Null for SQL Server and for an editable set.

ExecuteMessageDto (execute.v2)
    0 Kind (0 Info · 1 RowCount · 2 Error · 3 System) · 1 Text · 2 BatchIndex · 3 Number (0 for PRINT) ·
    4 Severity · 5 State · 6 Line (batch-relative, 0 = none) · 7 Procedure? · 8 ResultSetIndex (-1 = none) ·
    9 RowCount (-1 = none)

ExecuteBatchDto (execute.v2)
    0 Index · 1 StartLine (1-based line of the submitted SQL where the batch starts; the batch is sent
    verbatim, so the server's line 1 is this line) · 2 StartOffset · 3 EndOffset · 4 Status · 5 ElapsedMs ·
    6 RepeatCount (≥ 1, at most 10,000) · 7 ErrorCount
```

How a script runs (execute.v2):

- **Batches** — split on `GO` by ScriptDom's lexer (`TsqlParserService.SplitBatches`): a `GO` at the start of
  a line, never inside a string, a comment or `[GO]`. `GO n` runs the batch n times ("Beginning execution
  loop" … "Batch execution completed n times."). Anything else on a GO line (`GO SELECT 2`, `GO;`, `GO 0`)
  refuses the whole script with one error and runs nothing; so does a script of more than 20,000 batches.
- **Per batch** — its own command and timeout. `CommandBehavior.KeyInfo` (for provenance) only when the batch
  selects something and does not start with `CREATE`/`ALTER`/`CREATE OR ALTER` of a procedure, view,
  function, trigger, schema, default or rule (KeyInfo's `SET NO_BROWSETABLE ON` prefix would break those).
- **Script time limit** — the whole script, start to last row, may run the command timeout once for each
  command it runs (each batch × its `GO n`), at most one hour (`ExecuteLimits.ScriptTimeLimit`, shared with
  the web, which waits for the reply that long plus 15 s). Reaching it stops the script as `TimedOut` with an
  error saying so: the engine connection serves one request at a time, so an endless script would hold up
  completion, schema refresh and Cancel.
- **Errors** — severity ≤ 16 arrive as info messages for the duration of the execute
  (`FireInfoMessageEventOnUserErrors`, restored afterwards) and the reader carries on, so the sets before and
  after an error are kept; a failing batch does not stop the script. A closed connection (severity ≥ 20)
  skips the remaining batches ("The remaining N batch(es) did not run because the connection was closed.");
  a timeout or a cancellation stops the script and the remaining batches are `Skipped`.
- **Counts** — one `RowCount` message per statement (`StatementCompleted`; none under `SET NOCOUNT ON`); a
  SELECT's count names its set.
- **Budget** — one 15 MB budget for the batches, sets and messages of the reply (a message's text is counted
  twice; the batches are charged up front); at most 10,000 messages and 1,000 sets. A set past the cap is
  drained and its count is still a SELECT's, never DML. An error is never lost to a cap: it always sets
  `Status` and `ErrorMessage`, and up to 100 errors past the caps are kept, from a 256 KB reserve above the
  budget, so the reply stays under the 16 MB frame.

### `ExecuteCancel` (214) — notification

`ExecuteCancelRequest { 0 SessionId, 1 QueryId }`. Cancels an execute that is still queued behind another on
the session's connection; it does not interrupt a statement that is already running.

### `ApplyChanges` (215) → `ApplyChangesResult` (216)

Writes a result set's pending edits (parameterized UPDATE / INSERT / DELETE, each keyed by the fetched key
values) in one transaction on the same persistent connection. Any failed edit rolls the whole batch back; the
failed edit carries the error and every other edit the note "Rolled back — edit N failed.". `Status`,
`ErrorMessage`, `Results` (`CrudEditResult { Index, Ok, Error, RowsAffected, NewIdentity, Row }`) and
`ConnectionWasReset`.

Spec 042 adds, append-only, for a PostgreSQL session (SQL Server ignores them): `ApplyChangesRequest.ReturnColumns` (key 5, the set's
base columns in result order) and `CrudCellDto.TypeName` (key 6, the column's type as the server prints it, which the write casts
to), and `CrudEditResult.Row` (key 5, the written row as the server stored it, in `ReturnColumns` order, from `RETURNING`; null for
a delete). On PostgreSQL the batch runs in one transaction (`BEGIN` ... `COMMIT`), or under a savepoint inside the user's open
transaction, which is joined and left open; an aborted transaction is refused with a message to roll back. Every edit must touch
exactly one row.

### `ListDatabases` (94) → `ListDatabasesResult` (194) — `SessionId`

`ListDatabasesRequest { 0 ConnectionString, 1 SessionId }`. Spec 041 (session.database.v1): with `SessionId` set
and `ConnectionString` empty, the engine lists the databases of the session's server with the connection string
it already holds (the browser never resends a password); alias resolution and the bridge target guard run as
for any other request; an unknown or disconnected session gets `Ok = false` with an error.

### `ChangeDatabase` (217) → `ChangeDatabaseResult` (218) — session.database.v1

`ChangeDatabaseRequest { 0 SessionId, 1 DatabaseName }` → `ChangeDatabaseResponse { 0 Ok, 1 DatabaseName,
2 ErrorMessage, 3 ConnectionWasReset }`. The toolbar's database selector: the name is refused when it is blank, longer than 128 characters or holds
control characters, and is used exactly as listed (no trimming). Any other character is legal and safe: the
connection-string builder quotes the value and `ChangeDatabase` brackets the identifier. The session's connection string is pointed at it and judged by the bridge
guard; then, under the session gate, the persistent connection (opened first if it never was) runs the
synchronous `SqlConnection.ChangeDatabase` — the same physical connection, so #temp tables and SET options
survive. The session is re-keyed to the new database (`SessionManager.SetDatabase`, no dispose) and its schema
cache is created and populated (Phase A, then B) under a claimed populate. A script's `USE` re-keys the session
the same way after the execute (`ExecuteQueryResult.CurrentDatabase`).

### `ExecuteParse` (219) → `ExecuteParseResult` (220) — execute.v2

The same request and reply classes as `ExecuteQuery`. Under the session gate: `SET PARSEONLY ON` as its own
command, each batch once (results ignored, no row counts), then `SET PARSEONLY OFF` — if that fails the
connection is closed so the next execute reopens it and reports `ConnectionWasReset`. `Status` Ok means the
server found no syntax error ("Commands completed successfully."); errors carry batch-relative lines like an
execute's. Sent only when the engine advertises `execute.v2`: an engine that does not know 219 gives no reply.

## PostgreSQL Messages (221–224) — spec 042, `dialect.postgresql.v1`

Registered only when `postgreSql.enabled` is true in `config.json` (the default). A PostgreSQL session is an ordinary session whose
`ConnectionInfo.Kind` is 1; the handlers that take a `SessionId` branch on it (see `specs/042-postgresql-support/contracts/ipc.md`).

### `CredentialRequest` (221) → `CredentialResponse` (222)

`CredentialRequest { 0 Action, 1 Kind, 2 Server, 3 Port, 4 Login }` → `CredentialResponse { 0 Ok, 1 Has, 2 Error }`. `Action` 0 asks whether the
engine holds a remembered password for this server and login, 1 forgets it. The reply says yes or no and never carries a password.
Only PostgreSQL is served: any other `Kind`, or a missing login, finds nothing and touches nothing. The password lives in the engine's
protected credential store under `pg://{server}:{port}` and the login, and is filled into a connection that arrives without one.

### `RedactRequest` (223) → `RedactResponse` (224)

`RedactRequest { 0 Dialect, 1 Text }` → `RedactResponse { 0 Ok, 1 Text, 2 Error, 3 Placeholders }`. The web edition sends PostgreSQL text here before it
goes to a cloud AI provider. `Dialect` must be `postgresql` (or an alias such as `pg`); anything else is refused, never passed through. The
engine replaces each string, numeric and dollar-quoted literal with a placeholder and keeps identifiers, keywords, operators, comments and
layout. Placeholders are `'__STR_n__'` for strings, `__NUM_n__` for numbers, and the dollar tag around `__STR_n__` for a dollar-quoted
string; a dollar-quoted body after `AS` or `DO` that reads as PostgreSQL text is redacted inside instead. A number after `LIMIT`, `OFFSET` or
`FETCH FIRST|NEXT` is a row count and stays.

`Placeholders` (key 3, appended after the first release) maps each placeholder, as it appears in `Text`, to the literal it replaced, so the
client can restore the literals in the provider's answer; it goes back only to the client that sent the text. When the text ends inside a
string, quoted name, dollar-quoted string or block comment, or exceeds 10 MB, `Ok` is false, `Text` is empty and `Error` says why without
quoting the text: the client must not send the original without the user's say-so.
