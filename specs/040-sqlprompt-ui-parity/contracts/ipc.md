# Contract: IPC changes (shell ↔ engine)

**Feature**: 040-sqlprompt-ui-parity

**Transport:** the existing named pipe with MessagePack frames. **No new message types.**

**Rules for this contract:**
- Every change here is an additive key or a new action code on an existing message.
- Keys are appended in order and never renumbered. Optional fields are nullable.
- An **older engine** ignores unknown keys. Shell and engine ship together, so that only
  happens in a dev mismatch, where actions degrade to today's per-row behaviour.
- An **older shell** never sends the new keys, so the engine keeps today's behaviour.
- The **web edition** never sets the new keys (FR-070).

**Tests:** round-trip and legacy-shape tests for each changed DTO live in
`tests/AkmlSql.Core.Tests/Ipc/`, using the `HistoryRecordRequestTests` pattern.

---

## History (message types 41/141 HistorySearch, 42/142 HistoryAction, 40/140 HistoryRecord)

### `HistoryActionRequest` (`AkmlSql.Core/Ipc/Messages/HistoryActionRequest.cs`)

| Key | Field | Type | Used by | Meaning |
|---|---|---|---|---|
| 0–8 | *(unchanged)* | | | Action, EntryIds, ExportFormat, ExportPath, Filter, NewName, IsOpen, SqlText, KeepFavorites |
| **9** | `GroupScope` | `bool?` | Delete, ToggleFavorite, GetVersions | `true` = act on the whole query session of `EntryIds[0]`. `null`/`false` = per id, as today |
| **10** | `SessionKey` | `string?` | SetOpenStatus, SaveVersion | Shell document session key; identifies every row of the session |
| **11** | `OwnerPid` | `int?` | SetOpenStatus, ReconcileOpen | Shell process id that owns the open state |
| **12** | `OpenSessionKeys` | `string[]?` | ReconcileOpen | Session keys of documents open in that shell right now |

### `HistoryActions` codes

| Code | Name | Status | Request → response |
|---|---|---|---|
| 0–10 | *(unchanged)* | existing | |
| 1 | `ToggleFavorite` | changed | With `GroupScope`: sets all rows to `1 - MAX(group)`. Response `IsFavorite` = the new state |
| 2 | `Delete` | changed | With `GroupScope`: one transaction deleting the group's versions, rows and session. Response `DeletedCount` = rows removed. `DeletedCount` is now also set for per-id Delete |
| 7 | `GetVersions` | changed | With `GroupScope`: runs and snapshots of the group, de-duplicated by content hash, newest first, id tiebreak |
| 8 | `SetOpenStatus` | changed | With `SessionKey` + `OwnerPid`: sets `is_open` and `open_pid` on every row of that session (open) or clears both (close) |
| **11** | **`ReconcileOpen`** | new | `OwnerPid`, `OpenSessionKeys`. Closes:<br>- rows with `open_pid = OwnerPid` whose session isn't in the list;<br>- rows whose `open_pid` isn't a running process.<br>Response:<br>- `Success`;<br>- **`RestorableEntryIds`**: the representative entry id of every group closed *because its owner process was gone*, i.e. the queries open when SSMS last exited or crashed. Newest first.<br>The shell sends it once after the engine connects; the restore service reads `RestorableEntryIds`. |
| **12** | **`GetFilterValues`** | new | No input. Response `Servers`, `Databases` (distinct, non-empty, sorted, capped at 500 each) |
| **13** | **`GetEntries`** | new (T142) | `EntryIds`. Response **`Entries`**: those entries with their full text, query name (`TabTitle`), server, database and `SessionKey`, in the order asked; unknown ids left out; at most 500. Restore on start reads the queries it reopens with it |
| 7 | `GetVersions` | changed (T137) | With `GroupScope`, each version also carries the `Server` and `Database` it ran on (a snapshot: its run's) |

### `HistoryActionResponse`

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0–7 | *(unchanged)* | | Success, FullSqlText, DiffLeft, DiffRight, ExportPath, Error, Versions, DeletedCount |
| **8** | `IsFavorite` | `bool?` | State after ToggleFavorite |
| **9** | `Servers` | `string[]?` | GetFilterValues |
| **10** | `Databases` | `string[]?` | GetFilterValues |
| **11** | `RestorableEntryIds` | `long[]?` | ReconcileOpen: entries that were open at the last exit (restore on start) |
| **12** | `Entries` | `HistoryEntryDto[]?` | GetEntries |

### `HistoryVersionDto`

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0–2 | *(unchanged)* | | Id, SqlText, SavedAt |
| **3** | `Server` | `string?` | GetVersions with `GroupScope`: where the version ran. Null from older engines |
| **4** | `Database` | `string?` | Same |

### Retired: session recovery (50 `SessionSave`, 51 `SessionRestore`, 52 `SessionDelete`)

Removed in T147. No shell ever sent them (the shell's session-recovery classes were never compiled); restore on start reopens queries from SQL History instead. The numbers stay reserved.

### `HistoryEntryDto`

| Key | Field | Type | Meaning |
|---|---|---|---|
| **17** | `SessionKey` | `string?` | The query session's key. When History opens an entry, the new document adopts it, so re-running continues the same session. It isn't adopted when another open tab already holds it, so two tabs never share a key |

### `HistoryRecordRequest`

| Key | Field | Type | Meaning |
|---|---|---|---|
| **12** | `IsDraft` | `bool` | The text was never executed. It is stored with `status = NotExecuted (3)` and not counted as a run |

**Shell behaviour change:** recording becomes a request (RequestId ≠ 0) instead of a
notification, so the shell knows the row exists before it sends `SetOpenStatus` and raises
`HistoryRecorded`. The engine handler already answers when RequestId ≠ 0.

### `HistorySearchRequest`

| Key | Field | Type | Meaning |
|---|---|---|---|
| **13** | `PathFilter` | `string?` | `path:` prefix, matched with `h.source LIKE %value%` |

**Semantic changes (no new keys):**
- Free text matches FTS **or** name / source / server / database with `LIKE`.
- `FavoritesOnly` and `IsOpen` apply at the group level.
- `DateFrom` / `DateTo` compare with `datetime()`.

### `ExecutionStatus` enum (`AkmlSql.Core/Models/History/ExecutionStatus.cs`)

`Success = 0`, `Error = 1`, `Cancelled = 2`, **`NotExecuted = 3`** (new).

---

## Formatting

### `FormatRequest` / `FormatSelectionRequest`

| Message | Key | Field | Type | Meaning |
|---|---|---|---|---|
| `FormatRequest` | **5** | `Actions` | `FormatSqlActionsDto?` | Interactive Format SQL actions. `null` = use `profile.FormatActions`, as today (CLI, bulk, tests) |
| `FormatSelectionRequest` | **5** | `Actions` | `FormatSqlActionsDto?` | Same |

`FormatRequest.IncludeActions` (Key 4) stays in place and unread.

The shell also sends the editor's real `SessionId` in `FormatRequest` (today a random GUID),
so the schema-aware operations can resolve the document.

### `FormatSqlActionsDto` (new, `[MessagePackObject]`)

| Key | Field | Type | Values |
|---|---|---|---|
| 0 | `ApplyLayout` | `bool` | false = keep the original whitespace; run only the actions below |
| 1 | `ApplyCasing` | `bool` | |
| 2 | `Semicolons` | `int` | 0 leave, 1 insert, 2 remove |
| 3 | `SquareBrackets` | `int` | 0 leave, 1 add, 2 remove |
| 4 | `ExpandWildcards` | `bool` | Needs the schema cache and session |
| 5 | `QualifyObjectNames` | `bool` | Needs the schema cache and session |

### `FormatSelectionResponse`

| Key | Field | Type | Meaning |
|---|---|---|---|
| **7** | `ProfileFallbackWarning` | `string?` | Same text and meaning as `FormatResponse` Key 6 |

### `ProfileInfo` (ProfileList response items)

| Key | Field | Type | Meaning |
|---|---|---|---|
| **9** | `Source` | `string?` | `"builtIn"`, `"user"` or `"team"` |
| **10** | `IsReadOnly` | `bool` | Team style in a folder that can't be written to |

### `ProfileListResponse`

| Key | Field | Type | Meaning |
|---|---|---|---|
| **1** | `TeamFolderUnavailable` | `bool` | The team style folder is set but couldn't be reached (or took longer than 2 s); the shell shows *Team styles unavailable* |

**Engine rule:** Save, Rename, Delete and ResetToBuiltIn on a read-only team style return
`Success = false`, `Error = "'X' is a team style and can't be changed here — copy it to edit."`.
