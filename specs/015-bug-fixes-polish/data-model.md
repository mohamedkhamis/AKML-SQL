# Data Model: Multi-Area Bug Fixes and UI Polish (015)

**Branch**: `015-bug-fixes-polish`  
**Date**: 2026-04-14

This document captures the data entities touched or introduced by this feature, their fields, validation rules, and state transitions.

---

## 1. History Entry (`HistoryEntryDto`)

**File**: `src/AkmlSql.Core/Ipc/Messages/HistoryEntryDto.cs`

| Field | Type | Key | Notes |
|---|---|---|---|
| `Id` | `int` | PK | Auto-increment, unique per entry |
| `SessionId` | `string` | FK | Links to the shell session that ran the query |
| `ConnectionString` | `string` | — | Connection used at execution time |
| `DatabaseName` | `string` | — | Active database at execution time |
| `SqlText` | `string` | — | Full SQL text (up to session doc limit) |
| `TabTitle` | `string?` | — | User-assigned name; nullable (empty = auto-label) |
| `ExecutedAt` | `DateTime` | — | UTC timestamp of execution |
| `IsFavorite` | `bool` | — | Starred flag; default `false` |
| `RowCount` | `int?` | — | Result row count if available |
| `DurationMs` | `int?` | — | Execution duration in milliseconds |

**Validation rules**:
- Maximum 1,000 entries retained; oldest (by `ExecutedAt`) evicted on overflow.
- Starred entries (`IsFavorite = true`) are subject to the same eviction policy.
- `TabTitle` max length: 200 characters.
- `SqlText` max length: 10 MB (matching `MaxDocumentSizeChars` in SessionManager).

**State transitions**:
```
New entry added → [IsFavorite = false, TabTitle = null]
    ↓ User stars         → [IsFavorite = true]
    ↓ User un-stars      → [IsFavorite = false]
    ↓ User renames       → [TabTitle = "<user text>"]
    ↓ Eviction (1001st)  → [deleted, oldest ExecutedAt wins]
```

**Computed / derived**:
- `StarredCount` (ViewModel): `COUNT WHERE IsFavorite = true` — recalculated on every toggle. Displayed as badge on the Starred filter button.
- Display label (`QueryNameConverter`): `TabTitle ?? truncate(SqlText, 80)`.

---

## 2. AI Provider Configuration (`AiSettings`)

**File**: `src/AkmlSql.Core/Config/AppSettings.cs` (nested class `AiSettings`)

| Field | Stored in | Notes |
|---|---|---|
| `Provider` | `config.json → ai.provider` | Enum string: `"Claude"`, `"Gemini"`, `"None"` |
| `ModelName` | `config.json → ai.modelName` | Optional override (e.g., `"claude-sonnet-4-6"`) |
| `Endpoint` | `config.json → ai.endpoint` | Optional base URL override |
| `ApiKey` | **Windows Credential Manager** | Never written to `config.json`; stored as DPAPI-encrypted blob with `dpapi:` prefix |

**DPAPI key management** (`src/AkmlSql.Engine/Ai/Security/CredentialManager.cs`):
- Write path: `Encrypt(plaintext)` → `DataProtectionScope.CurrentUser` + app-specific entropy (`SHA256("AkmlSql-ApiKey-v1")`) → base64 blob prefixed with `"dpapi:"` → stored in Credential Manager.
- Read path: `Decrypt(blob)` → strip prefix → DPAPI decrypt → held in memory only for request duration → `CryptographicOperations.ZeroMemory()` after use.
- Legacy fallback: if stored value lacks `"dpapi:"` prefix, treated as plaintext (migration path).

**Validation rules**:
- `ApiKey` must be non-empty before saving (FR-031).
- `ModelName` may be empty (provider default used).
- `Provider = "None"` disables AI features without deleting the stored key.

---

## 3. Completion Context (`CursorContext` / `ClauseType`)

**Files**:  
- `src/AkmlSql.Engine/Completion/CursorContextAnalyzer.cs`  
- `src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs`

**New enum variant added by this feature**:

| Variant | Trigger pattern | Provider |
|---|---|---|
| `AlterTableColumn` *(new)* | `ALTER TABLE <table> ALTER COLUMN ` | `ColumnProvider` |

**Existing variants (unchanged)**:

| Variant | Trigger | Provider |
|---|---|---|
| `UpdateSet` | `UPDATE <table> SET ` | `ColumnProvider` (alias fix required) |
| `Select` | `SELECT ` | `ColumnProvider` |
| `Where` | `WHERE ` | `ColumnProvider` |
| `Alter` | `ALTER ` | `ObjectProvider` |
| (others) | … | … |

**UPDATE SET alias fix** — Implicit alias injection:  
When `ClauseType = UpdateSet` and `context.AvailableAliases` is empty, the alias resolver (token scan fallback, `CompletionEngine.cs:101-109`) must detect the UPDATE target table and inject it as `{ "" → "<schema.table>" }` so `ColumnProvider` can find its columns.

**Token detection pattern for `AlterTableColumn`** (backward scan from cursor):
```
[<partial>] ← COLUMN ← ALTER ← <table_ref> ← TABLE ← ALTER
```
Extracted `<table_ref>` is injected into context for column lookup.

---

## 4. Schema Progress State Machine

**File**: `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs` (to be refactored)

**States** (unchanged):

| State | Notification box visible | Spinner | Status text |
|---|---|---|---|
| `Idle` | No | — | — |
| `Loading` | Yes | Spinning | "Loading schema…" |
| `Ready` | Fade-out in progress | Stopped | "Schema ready" (brief) |
| `Error` | Yes (brief) | Stopped | Error message |

**Positioning change**:
- **Before**: IWpfTextViewMargin — full-width strip, top of editor.
- **After**: AdornmentLayer overlay — fixed 280×56px box, bottom-right corner (`Canvas.Right = 12, Canvas.Bottom = 12`), respects viewport resize events.

---

## 5. Safety Check Configuration (`SafetySettings`)

**File**: `src/AkmlSql.Core/Config/AppSettings.cs` (nested `SafetySettings`)

| Field | Type | Default | Notes |
|---|---|---|---|
| `DropConfirmation` | `bool` | `true` | Controls DROP TABLE/DATABASE warning |
| `TruncateConfirmation` | `bool` | `true` | Controls TRUNCATE TABLE warning |
| `EnvironmentSeverity` | `Dictionary<string,string>` | `{}` | Per-environment override; `"Disabled"` suppresses all warnings |

**Fix**: Ensure `DropConfirmation` defaults to `true` in `AppSettings` constructor. Add a suppression log entry (WARNING level) whenever `ExecutionInterceptor` bypasses a safety check due to config or environment setting.

---

## 6. Product Version

**Format**: `Major.YY.MMDDHHmm`

| Segment | Source | Example |
|---|---|---|
| `Major` | Hardcoded `1` | `1` |
| `YY` | `DateTime.UtcNow.AddHours(2).ToString("yy")` | `26` (for 2026) |
| `MMDDHHmm` | `DateTime.UtcNow.AddHours(2).ToString("MMddHHmm")` | `04140511` |

**Files to update**:

| File | Change |
|---|---|
| `src/Directory.Build.props` | Replace `GitCommitCount` segment with `YY`; keep `MMddHHmm` stamp |
| `src/AkmlSql.Installer/AkmlSqlSetup.iss` | Inject version via `/DMyAppVersion=...` from `build.ps1` |
| 7× `source.extension.vsixmanifest` / `extension.vsixmanifest` | Patched by `build.ps1` using `$(Version)` MSBuild property |

**Monotonicity**: `1.26.04140511` < `1.26.04141200` (same day, later time) < `1.27.01010000` (next year). Numerically monotonic within a year; year rollover intentionally breaks sequence (acceptable — major version bump in that case).
