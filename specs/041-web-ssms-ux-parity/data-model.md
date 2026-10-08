# Data Model: SSMS-grade workspace for the web edition

**Feature**: 041-web-ssms-ux-parity | **Date**: 2026-10-08 | **Research**: [research.md](./research.md)

Everything here is **additive**. Existing browser records, the shared history database and
older IPC peers keep working. MessagePack keys are appended, never renumbered. New JSON fields
have defaults that reproduce today's behaviour. Core stays `netstandard2.0`-safe
(`{ get; set; }`, no `init`).

---

## 1. Browser-side records (IndexedDB `AkmlSqlWeb`, version 3 → 4)

### 1.1 `analysisSettings` store (existing; three keys)

| Key | Record | Change |
|---|---|---|
| `current` | `WebAnalysisSettings` | + `ProblemsFilter { ShowInfo=true, ShowWarning=true, ShowError=true }` (FR-055). `Enabled` is now read (R50). `RuleOverrides` keys exported verbatim; `off` entries are the "suppressed everywhere" list. |
| `executionSettings` | `ExecutionSettings` | + `ShowColumnTypes=false` (FR-011), + `RetainLineBreaksOnCopy=false` (FR-052). Saved **defaults** only; the toolbar's quick control is an in-memory `SessionOverride { MaxRows, CommandTimeoutSeconds }` on the singleton store, never persisted (FR-052). `GetEffectiveAsync()` = override ?? saved; saving defaults clears the override. |
| `editorSettings` | `EditorSettings` (new) | `FontSize=13` (px, 9–32), `WordWrap=false`, `TabSize=4` (1–8) (FR-053). |

Reset = delete the record by key (never `clear` the shared store). `ReloadAsync()` drops the
per-tab cache and re-reads (FR-064).

### 1.2 `workspaceLayout` store (new; one record per field)

| Field key | Type | Default | Notes |
|---|---|---|---|
| `layoutVersion` | int | 1 | Any other value → the whole layout is discarded (FR-047) |
| `navVisible` | bool | true | Editor route only (R42) |
| `schemaVisible` | bool | true | |
| `schemaWidth` | int px | 260 | Clamped to [180, window − editor minimum] on load |
| `aiVisible` | bool | false | Replaces the unpersisted `_showAiPanel` |
| `aiWidth` | int px | 380 | Clamped to [300, …] |
| `aiTab` | `"actions"` \| `"chat"` | `"actions"` | |
| `resultsVisible` | bool | true | A hidden pane reappears on the next execution (FR-032) |
| `resultsHeight` | int px | 38 % of the workspace | Clamped to [120, workspace − 320] |
| `resultsMaximised` | bool | false | |
| `resultsTab` | `"results"` \| `"messages"` \| `"problems"` | `"results"` | Restored until the next execution (FR-020/FR-045) |

Values are JSON-encoded scalars (the adapter's `list` stringifies values). Writes happen only
on a user action and only for the changed field (FR-045/FR-064). `Fit(layout, width, height,
limits)` is pure and is applied in memory on load and on resize; it also un-hides every region
when all would be hidden. Fold state (panels folded because the window is narrow) is derived,
never stored (FR-046).

### 1.3 `editorSession` store (existing; key `current`)

`EditorSessionRecord` + `DocumentName` (string?, default null → `SQLQuery{NextDefaultNumber}`),
`IsModified` (bool), `NextDefaultNumber` (int, default 1). Every writer carries every field
(the store overwrites the whole record). `StartDocumentAsync(name, text)` writes a fresh record
with a **new `SessionKey`** (the New / Open boundary, FR-072); "Reset editor session" keeps
`ClearAsync`.

### 1.4 `localStorage['akml.theme']`

A mirror of the saved theme mode written when Blazor applies it, read by the synchronous boot
script before first paint (FR-092); cleared by the theme reset. IndexedDB `themePreference`
remains the source of truth.

### 1.5 Settings export envelope (file, FR-059)

```text
{
  "format": "akmlsql-web-settings",
  "version": 1,
  "exportedAt": "<UTC ISO-8601>",
  "appVersion": "1.26.1007.0613",
  "sections": {
    "general":      { "theme": "system|light|dark|highContrast" },
    "editor":       { "fontSize": 13, "wordWrap": false, "tabSize": 4 },
    "format":       { "activeStyleId": "builtin.khamis" },
    "queries":      { "maxRows": 1000, "commandTimeoutSeconds": 30, "showColumnTypes": false, "retainLineBreaksOnCopy": false },
    "codeAnalysis": { "enabled": true, "autoAnalyseOnFormat": true, "problemsFilter": {...}, "ruleOverrides": { "PE001": "off" } },
    "aiAssistance": { "providers": [ { "providerId", "displayName", "model", "endpoint" } ], "activeProviderId": "...",
                      "privacy": { "globalDefaultMode", "featureModeOverrides" }, "ghostText": { "enabled", "delayMs", "maxRequestsPer3s" } },
    "layout":       { ...workspaceLayout fields... },
    "grid":         { "maxColumnWidthPx": 480 }
  }
}
```

Never present: API keys, passwords, pairing tokens, engine connections, SQL Server connections,
schema cache, snippets, history, chat, the document, diagnostics (SC-013). Import applies the
sections present, leaves absent sections unchanged, counts unknown properties (every DTO and
nested object carries `[JsonExtensionData]`) and unknown sections, and marks imported providers
`HasKey=false` ("Needs key").

---

## 2. Grid state (per result set, in memory)

| Entity | Fields | Rules |
|---|---|---|
| `GridColumnLayout` | `int[] WidthsPx` (row-number track first), `bool[] UserSet` | `MinColumnPx=48`, `MaxColumnPx=480`, `DefaultColumnPx=120`; header sets the floor up to the maximum; discarded when a new result replaces the set (FR-006); `ToCssTracks()` emits `--akml-grid-cols` |
| `GridSelection` | `List<GridRect> Rects`, `HashSet<(r,c)> Deselected`, `(r,c) Anchor`, `(r,c) Focus` | Display coordinates (row = position in `_rowIndexes`); `c = -1` row-number column, `r = -1` header; cleared on new result; remapped through the permutation on sort |
| Sort | `SortColumn` (-1 none), `SortDirection` | Permutes `_rowIndexes` only; cycle asc → desc → none; cleared on new result; re-applied after a bake (N23) |
| Pending edits | `_edits[fetchedRow][col] = string?`, `_deletes`, `_inserts` | Unchanged representation: `null` = SQL NULL, `""` = empty string; keyed by **fetched** row index |
| Validity | derived predicate | `""` on a non-character type → invalid; unset NOT NULL non-identity column of a new row → invalid; Apply disabled while any cell is invalid (FR-014) |
| `DisplayValue` | `Text`, `IsEngineCut`, `IsPreview` | Produced by `SqlValueDisplay.Format(wire, SqlTypeName, hint)`; the editor and Apply use the wire text |

Row glyphs: ✎ edited, ＋ new, ✕ deleted in the row-number cell, plus the edge-bar colour
(FR-016).

### 2.1 Display formatting (FR-026)

| Declared type | Shown as |
|---|---|
| `date` | `yyyy-MM-dd` |
| `smalldatetime` | `yyyy-MM-dd HH:mm:ss` |
| `datetime` | `yyyy-MM-dd HH:mm:ss.fff` |
| `datetime2(n)`, `time(n)` | n fractional digits (0 → none); unknown n → 7 |
| `datetimeoffset(n)` | as `datetime2(n)` + ` +02:00` (space, SSMS spacing) |
| binary types | `0x` + upper-case hex; preview after 1,024 bytes |
| `uniqueidentifier` | upper case |
| `decimal`/`numeric` | wire text (declared scale) |
| `money`/`smallmoney` | four decimals |
| `bit` | `1` / `0` |
| NULL | `NULL` (muted) |
| engine-cut | `[text N chars]` / `[binary N bytes]` verbatim, muted, counted on copy |

### 2.2 Validity by base type (FR-014)

Character types (`char nchar varchar nvarchar text ntext xml sql_variant`) accept the empty
string; every other type marks an emptied cell invalid until a value is typed or NULL is set.

---

## 3. IPC data (shell/web ↔ engine) — see `contracts/ipc.md` for keys

### 3.1 `ExecuteQueryResult` (213 / 220)

| Field | Key | Meaning |
|---|---|---|
| *(existing 0–7)* | | `QueryId`, `Status`, `ErrorMessage` (first error's text), `ResultSets` (always returned), `Messages string[]` (SSMS-formatted lines), `ElapsedMs` (total), `TotalRowsAffected` (sum of DML counts), `ConnectionWasReset` |
| `MessageDetails` | 8 | `ExecuteMessageDto[]` in stream order |
| `Batches` | 9 | `ExecuteBatchDto[]` |
| `CompletedAtUnixMs` | 10 | UTC, stamped by the engine at the end of the script; 0 from older engines |
| `MessagesOmitted` | 11 | count beyond `MaxMessages = 10,000` |
| `CurrentDatabase` | 12 | `conn.Database` after the reader is disposed; null from older engines |

`ExecuteStatus`: 0 Ok, 1 Error, 2 Cancelled, 3 TimedOut, 4 NoConnection, **5 Skipped**
(batch-level), **6 Disconnected**, **7 NoReply** (6 and 7 are client-only).

### 3.2 `ExecuteResultSet`

Existing keys 0–10 (`ColumnSqlTypes` now carries the **declared** type: `datetime2(3)`,
`decimal(10,2)`, `nvarchar(max)`, bare otherwise); + key 11 `BatchIndex`.
`ColumnProvenanceDto.Precision/Scale` (keys 9/10) are **null** when SqlClient reports the 255
sentinel.

### 3.3 `ExecuteMessageDto` (new)

| Key | Field | Notes |
|---|---|---|
| 0 | `Kind` | 0 Info (PRINT, severity ≤ 10), 1 RowCount, 2 Error (severity ≥ 11), 3 System (engine-authored) |
| 1 | `Text` | message text; for RowCount `(N rows affected)` or `(N rows returned; more exist)` |
| 2 | `BatchIndex` | |
| 3 | `Number` | `SqlError.Number` (0 for PRINT) |
| 4 | `Severity` | `SqlError.Class` |
| 5 | `State` | |
| 6 | `Line` | batch-relative; 0 = none |
| 7 | `Procedure` | null when empty |
| 8 | `ResultSetIndex` | the set a SELECT count belongs to; -1 none |
| 9 | `RowCount` | server count; -1 none |

### 3.4 `ExecuteBatchDto` (new)

`Index`, `StartLine` (1-based line of the batch text within the submitted SQL, verbatim),
`StartOffset`, `EndOffset`, `Status` (`ExecuteStatus`), `ElapsedMs`, `RepeatCount`,
`ErrorCount`.

### 3.5 `ChangeDatabaseRequest` (217) / `ChangeDatabaseResponse` (218)

Request: `SessionId`, `DatabaseName` (identifier-safe: no `;`, `=`, quotes, control chars).
Response: `Ok`, `DatabaseName` (server-reported), `ErrorMessage`, `ConnectionWasReset`.

### 3.6 `ListDatabasesRequest` (94)

+ key 1 `SessionId` (when set and `ConnectionString` empty, the engine uses the session's
stored connection string; guard re-run).

### 3.7 `HandshakeResponse` (201)

+ key 8 `HostKind` (`ide` | `service` | `console` | null), + key 9 `RunsAs` (Windows account
name; hover/Diagnostics only).

### 3.8 Capabilities

`session.database.v1` — ListDatabases by session, ChangeDatabase, `CurrentDatabase`.
`execute.v2` — `MessageDetails`, `Batches`, `CompletedAtUnixMs`, `MessagesOmitted`, Parse.

---

## 4. Engine session state

| Item | Change |
|---|---|
| `SessionState.DatabaseName`, `ConnectionString` | Updated by `SessionManager.SetDatabase` after a switch or a reported `USE` **without** the `ConnectionChanged` dispose path; `SessionConnectionRegistry.GetOrCreate` picks the new string up on the next call |
| `SessionConnection` | `FireInfoMessageEventOnUserErrors` toggled only inside the execute gate; `ChangeDatabase` executed under the gate; parse mode closes the connection if `SET PARSEONLY OFF` fails |
| Schema cache `(sessionId, database)` | A new cache is populated by `SchemaRefreshService.Refresh` after a switch; the old one lingers until LRU eviction |
| `EngineIdentity` | Static `HostKind` + `RunsAs` set once at start-up |

---

## 5. State machines

### 5.1 Execution (web, `IWorkspaceStatus.Phase`)

```text
Idle ──Execute/Parse──▶ Running ──reply──▶ Succeeded (Status Ok)
                           │                 Failed (Error / TimedOut / Cancelled / NoConnection)
                           ├──bridge lost──▶ Failed (Disconnected: "the statement may still have run")
                           └──deadline─────▶ Failed (NoReply: "no result was received within the timeout")
Succeeded/Failed ──next Execute──▶ Running
```

Deadline = `timeout × max(1, batchCount incl. repeat counts) + 15 s`; the deadline token is
distinguished from the caller's token before mapping. Execute is not offered while Running
(FR-027).

### 5.2 Results tab selection (FR-020 / FR-034 / FR-045)

```text
after Execute:  sets > 0 && no error → Results;  else → Messages
after Analyse:  findings > 0 → Problems (pane shown if hidden);  else unchanged
on load:        restored resultsTab (until the next execution)
```

### 5.3 Batch status (engine)

```text
Pending → Running → Ok | Error (continue) | TimedOut (stop; rest Skipped) | Cancelled (stop) | Skipped
connection closed after a batch → remaining Skipped + one System message
separator error → whole script refused (Status Error, no batch ran)
```

### 5.4 Document (web)

```text
New (name SQLQueryN, clean) ──typing──▶ Modified ──Save/Save as──▶ Clean (named)
Modified ──New/Open──▶ confirm discard ──▶ New/Opened (new SessionKey)
Open file ──▶ Clean (name = file name without extension, sanitised; new SessionKey)
```

`IsModified` = text ≠ baseline (baseline set on New, Open, Save). A programmatic `setText` is
suppressed from marking the document modified.

### 5.5 Panel visibility (web)

```text
visible ──hide (menu/key/strip)──▶ hidden (edge strip)  ──show──▶ visible (previous size)
visible ──editor column would drop below its minimum──▶ folded (AI first, then Schema; edge strip; derived)  ──wider──▶ visible
results hidden ──Execute──▶ visible
```

---

## 6. Derived rules

- **Document line of an error**: `(selectionStartLine − 1) + (Batches[b].StartLine − 1) + Line`,
  only when `Line > 0` and `Procedure` is empty; a whitespace-only selection runs the whole
  document (selection start = 1).
- **History naming**: `TabTitle` = document name when it is not the default, else null (the
  engine then names the session `query-NN`); a new `SessionKey` on New and Open.
- **"Shared with SSMS" label**: only when `HostKind == "ide"`, else "On this engine".
- **Version display**: `StripBuildMetadata(version)` (everything from `+`); mismatch marker when
  web and engine differ after stripping.
- **Maximum column width**: 480 px (planning constant), header floor, user override by drag.
