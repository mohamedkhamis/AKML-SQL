# Data Model: SQL Prompt UI/UX parity for Options, SQL History and format styles

**Feature**: 040-sqlprompt-ui-parity | **Date**: 2026-09-27 | **Research**: [research.md](./research.md)

Everything here is **additive**. Existing `config.json` files, history databases and older
IPC peers keep working. MessagePack keys are appended, never renumbered. New config
properties have defaults that reproduce today's behaviour. Core stays `netstandard2.0`-safe:
plain `{ get; set; }`, no `init`.

---

## 1. Settings (`%AppData%/AKML SQL/config.json` → `AppSettings`)

### 1.1 Existing settings whose meaning changes

**Now honoured** (they had no reader before):

| Setting | Default | Now does |
|---|---|---|
| `IntelliSense.MaxSuggestions` | 50 | Caps the completion list (clamped 5–200) |
| `IntelliSense.TriggerDelayMs` | 100 | Delay before the automatic popup (0 = immediate) |
| `IntelliSense.FuzzyMatch` | true | Off = prefix-only matching |
| `IntelliSense.ShowDataTypes` / `ShowNullability` / `ShowPkFk` | true | Each switches its part of the completion detail text |
| `IntelliSense.AfterDot` | true | Off = typing `.` doesn't open the list |
| `CodeAnalysis.ShowInErrorList` | true | Off = no AKML entries in the Error List |
| `CodeAnalysis.RunOnType` ("Analyze while typing") | true | Off = analysis runs only on open and on demand |
| `Formatter.Enabled` | true | Off = Format Document / Selection / Bulk say "formatting is off" |
| `Formatter.CreateBackups` | true | Seeds the Bulk Format wizard's backup checkbox |
| `Snippets.Enabled` | true | Off = no snippet expansion and no snippet items |
| `Snippets.FormatOnExpand` ("Format after expansion") | true | Replaces the hard-coded true |

**Hidden from the UI, value kept:** every Appendix A property, plus the other properties
listed in research R1. Nothing reads them.

### 1.2 New properties

```text
HistorySettings
  MaxQuerySizeKb            int     default 1024   range 16–1024    (capture limit; 1 MB is today's hard cap)
  RestoreMaxQueries         int     default 20     range 1–100      (restore-on-start cap)
  ReconnectRestoredQueries  bool    default true
  RememberAdvancedSearch    bool    default false
  AdvancedSearch            HistoryAdvancedSearchState?  default null   (persisted only when Remember… is on)

HistoryAdvancedSearchState
  Period     string  "all" | "week" | "month" | "3months" | "custom"
  From, To   string? ISO date (yyyy-MM-dd), only for "custom"
  Server     string? null = all
  Database   string? null = all
  Starred    bool
  OpenOnly   bool

TabSettings
  Environments  List<TabEnvironment>  default = migrated (see 1.3)

TabEnvironment
  Name   string  required, unique (case-insensitive), 1–40 chars
  Color  string  "#RRGGBB"

ColoringRule
  Environment  string  name of a TabEnvironment (new). Label and Color stay, and are rewritten
                       from the environment on save, because Safety keys on Label.

FormatterSettings
  TeamStyleFolder   string  default ""  (absolute path or UNC; empty = off)
  FormatSqlActions  FormatSqlActions

FormatSqlActions                       (defaults = today's built-in profile FormatActions)
  ApplyLayout         bool  default true
  ApplyCasing         bool  default true
  Semicolons          string "insert" | "remove" | "leave"   default = built-in value
  SquareBrackets      string "add" | "remove" | "leave"      default = built-in value
  ExpandWildcards     bool  default false
  QualifyObjectNames  bool  default false
```

**Moved in the UI only (same property):** `Tabs.SessionRecovery` (re-labelled "Restore open
queries when SSMS starts"), `Tabs.RestoreOnStartup`, `Tabs.AutoSaveInterval` and
`Tabs.MaxClosedTabs` move to Options › Queries › History.

**Validation** (applies on OK and on number-field commit):

- **Numbers:** clamped to their range. Invalid text keeps the last valid value and gets a red
  border.
- **`TeamStyleFolder`:** must be rooted (`Path.IsPathRooted`). The canonical check uses
  `Path.GetFullPath` (Constitution security). An unreachable folder is allowed; it is reported
  in the style list, not refused.
- **Environments:**
  - names are unique;
  - colours must match `^#[0-9A-Fa-f]{6}$`;
  - a rule must reference an existing environment;
  - deleting an environment that rules use is refused, with the rules named.

### 1.3 Environment migration (in `ConfigManager.Load`, idempotent)

1. **When it runs:** only when `Tabs.Environments` is null or empty **and**
   `Tabs.ColoringRules` is non-empty.
2. **Build the environments:** one per distinct `(Label, Color)` pair, in rule order.
   - A pair whose label repeats with a different colour gets `Label (2)`.
   - Stock configs produce PRODUCTION `#FF4444`, STAGING `#FFB800`, DEV `#44BB44` and
     AZURE `#4488FF`.
3. **Link the rules:** set each rule's `Environment` to its environment's name.
4. **When there are no rules:** seed the four defaults.

### 1.4 Install-state fields preserved by Import and "Restore all defaults"

`InstallId`, `InstalledTargets`, `LastUpdateCheck`, `NativeIntelliSensePrompted`,
`DisabledNativeIntelliSense`, `CommandPalette.UsageCounts`, `CommandPalette.RecentItems` and
`ConfigVersion`, through one helper, `PreserveInstallState(from, to)`.

---

## 2. History store (`%AppData%/AKML SQL/history/sqlhistory.db`, SQLite + FTS5)

### 2.1 Schema v3 (additive, run by `InitializeCoreAsync`, shared with the web engine)

```sql
ALTER TABLE history ADD COLUMN open_pid INTEGER NULL;          -- try/catch "duplicate column", like v2
CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE OF sql_text ON history BEGIN
  INSERT INTO history_fts(history_fts, rowid, sql_text) VALUES ('delete', old.id, old.sql_text);
  INSERT INTO history_fts(rowid, sql_text) VALUES (new.id, new.sql_text);
END;
```

**One-time repair.** It runs under `BEGIN IMMEDIATE`, guarded by `metadata` key
`history_v3 = 1`, written in the same transaction:

1. Normalise `executed_at` and `history_versions.saved_at` from `YYYY-MM-DD HH:MM:SS` to the
   insert's ISO "o" form (`YYYY-MM-DDTHH:MM:SS.0000000Z`). The only rows affected are those
   matching `' '` at position 11.
2. Delete orphan `history_versions` whose `history_id` has no `history` row.
3. `INSERT INTO history_fts(history_fts) VALUES('rebuild')`.

**`SchemaVersion`** becomes 3, and `metadata.schema_version` is upserted as today.

**Status values.** `ExecutionStatus` gains `NotExecuted = 3`, used for **drafts**: unsaved
text of a query tab that was never run (HIS-14). Drafts:

- are listed like other entries, with "Not executed" on the row;
- are not counted as runs (`exec_count` counts `status <> 3`);
- are replaced by the real run when the session is executed later: same session, and the
  draft row is kept as a version.

### 2.2 Query-session (grouped row) semantics

`GroupKey = COALESCE(CAST(h.session_id AS TEXT), 'hash:' || h.content_hash)`, unchanged.

| Property | Definition |
|---|---|
| Group starred | `MAX(is_favorite)` over the group. Toggling a group sets every row to `1 - MAX`. |
| Group open | `MAX(is_open)` over the group, with `open_pid` = the owning shell. |
| Versions | Distinct `content_hash` across the group's runs and snapshots, ordered by time. The row's "M versions" equals the panel's count. |
| Filters on starred / open | Applied to the group values (outer `WHERE`), so "closed" never returns a group that has an open run. |
| Delete group | One transaction: the group's versions, then its rows, then its `query_sessions` row. |

### 2.3 Open state transitions (per query session)

```text
            execute / activate (shell PID p)                 close (not shutting down)
 closed ─────────────────────────────────────▶ open(p) ───────────────────────────────▶ closed
   ▲                                              │
   │   ReconcileOpen(p', keys):                   │ SSMS exits or crashes with the tab open
   │   p' == p and key not in keys → closed       ▼
   └──────────────────────────────────────── open(p, p dead)
             restore declined, or a later ReconcileOpen after restore
```

- **Restore on start** reads the `open(p dead)` groups: the queries open at the last exit.
- **Web rows** never enter `open`.

### 2.4 Search semantics (engine)

The match is made across FTS and several text columns:

```text
(FTS MATCH text)
OR name LIKE %t%
OR source LIKE %t%
OR server LIKE %t%
OR database LIKE %t%
```

- Every word must match somewhere.
- Prefixes (`HistorySearchParser`): `name:` `path:` (new) `sql:` `server:` `database:`/`db:`
  `starred:` `open:` `date:[yyyyMMdd TO yyyyMMdd]` (new), plus `"phrase"`, `OR`, `NOT` and
  trailing `*`.

---

## 3. IPC contracts (MessagePack, additive keys)

Full shapes are in [contracts/ipc.md](./contracts/ipc.md).

| Type | New keys | Purpose |
|---|---|---|
| `HistoryActionRequest` | 9 `bool? GroupScope`, 10 `string? SessionKey`, 11 `int? OwnerPid`, 12 `string[]? OpenSessionKeys` | Group-scoped actions, open state, reconcile |
| `HistoryActions` codes | 11 `ReconcileOpen`, 12 `GetFilterValues` | New actions |
| `HistoryActionResponse` | 8 `bool? IsFavorite`, 9 `string[]? Servers`, 10 `string[]? Databases`, 11 `long[]? RestorableEntryIds` | Toggle result, filter lists, restore on start |
| `HistoryEntryDto` | 17 `string? SessionKey` | "Opened from History" adopts the session |
| `HistoryRecordRequest` | 12 `bool IsDraft` | Draft capture |
| `HistorySearchRequest` | 13 `string? PathFilter` | `path:` prefix |
| `FormatRequest` | 5 `FormatSqlActionsDto? Actions` | Interactive Format SQL actions |
| `FormatSelectionRequest` | 5 `FormatSqlActionsDto? Actions` | Same, for Format Selection |
| `FormatSelectionResponse` | 7 `string? ProfileFallbackWarning` | Selection warns like Document |
| `ProfileInfo` | 9 `string? Source` ("builtIn"/"user"/"team"), 10 `bool IsReadOnly` | Team styles |
| `ExecutionStatus` (enum) | `NotExecuted = 3` | Drafts |

`FormatRequest.IncludeActions` (Key 4, never read) stays unread. Key 5 carries the typed
actions instead, because `ApplyLayout`/`ApplyCasing` aren't `FormatActionType` values.

---

## 4. Shell view models and models

| Entity | Where | Key fields | Rules |
|---|---|---|---|
| `OptionsCatalogEntry` | `Dialogs/` (new) | PageKey, PageDisplay, Label, Description, Kind, HelpTopic | Built from the Options search index; cached per session; the AI page is excluded from palette toggles |
| `OptionPaletteEntry` | `Productivity/CommandPalette/` (new, : `CommandEntry`, INotifyPropertyChanged) | Id `opt:{pageKey}:{label}`, IsOn, StateText | Toggle kinds flip in place; other kinds open Options focused on the row |
| `IPageBuilder.HelpTopic` | `Dialogs/Pages/IPageBuilder.cs` | `string` docs slug + anchor | Must resolve in `DocsCatalog` (test) |
| `RowFactory` rows | `Dialogs/Pages/RowFactory.cs` | + `parent` (CheckBox?) | The child's IsEnabled follows the parent; label TextDisabled when off |
| `NumberRow` | `RowFactory.AddNumber` (new) | min, max, step, unit | Clamp; red border on invalid; last valid value kept |
| `HistoryViewModel` | `History/` | + `LastPageCount`, `AdvancedSearch`, preview cache, `SelectedVersion` | `HasMoreEntries = LastPageCount == PageSize && Entries.Count < TotalCount`; row actions take the row, not `SelectedEntries` |
| `HistoryDateGroups` | `Core/Models/History/` (new) | Today, Yesterday, ThisWeek, LastWeek, ThisMonth, Older | `HistoryDateBucket.Of` unchanged (web) |
| `LineDiff` | `Core/Text/` (new) | `Diff(a, b) → [(kind: same/added/removed/changed, leftLine, rightLine)]` | LCS over lines; tested |
| `SqlPreviewView` | `Ui/SqlPreview/` (new control) | Text, TabSize, HighlightTerms, HighlightLines, ShowLineNumbers | Read-only, selectable; colours via theme tokens |
| `ActiveStyleCache` | `Formatting/` (new) | Styles (name, source, isActive), LastRefreshed | Refreshed asynchronously; read synchronously by the menu slots |
| `FormatStylesEditorViewModel` | `Formatting/` | + `SchemaModel`, `ChangedCount(groupId)`, `IsChanged(id)`, `ResetOption(id)`, `PreviousPreviewLines`, `MovedLines` | Changed = working value ≠ catalog Default; `IsDirty` recomputed against the saved values |
| `StyleNameDialog` | `Formatting/` | + existing names, current name | Live validation; OK disabled until valid |

### Format style list states

```text
selected style: clean ──edit option──▶ dirty ──save──▶ clean
                  ▲                     │
                  │                     ├─revert──▶ clean
                  │                     ├─switch style / close / import / export ──▶ prompt: Save / Discard / Cancel
                  └──────── dirty back to its saved values (recomputed) ─────────┘
active flag: exactly one style; changing it updates the list, header chip, button, status bar and Active Style menu together
team style (read-only): edit disabled; "Copy" allowed
```
