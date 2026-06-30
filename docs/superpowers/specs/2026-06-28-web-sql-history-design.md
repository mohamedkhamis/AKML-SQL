# Web SQL History — Design

**Date:** 2026-06-28
**Branch:** 030-sqlprompt-parity-closure
**Status:** Approved (design); implementation pending. NOT committed (per project git rule).

## Goal

Bring the desktop SQL History feature to the Blazor **web edition** (`AkmlSql.Web`) at full
parity, then verify it end-to-end in the browser. The desktop control
(`AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`) was just redesigned to match Red Gate
SQL Prompt; this design ports the same feature to the web.

## Key architectural finding (why this is mostly wrapper work)

The engine's history IPC is **fully reachable over the web WebSocket bridge** — the transport
forwards every message type to the shared `RpcRouter` after handshake (no per-type allowlist;
`src/AkmlSql.Engine/Transports/WebSocketTransport.cs:229-243`). The history handlers are registered
unconditionally on that same router (`src/AkmlSql.Engine/EngineHandlerRegistry.cs:309-311`), backed
by a single SQLite store at a **global per-Windows-user path**
(`%APPDATA%\AKML SQL\history\sqlhistory.db`, WAL mode, per-call connection;
`src/AkmlSql.Engine/History/HistoryDatabase.cs:25-34`).

Consequences:
- The web client sends the **same** message types/DTOs the desktop uses; it hits the **same**
  handler and the **same** store.
- If the web pairs with an engine running as the same Windows user, web history and desktop history
  are **one shared store** — web instantly shows real history captured in SSMS/VS, and web-captured
  executions appear in the desktop.
- **No new engine code.** Reuse FTS5 search, filters, pagination, versions, favorites, rename,
  delete, remove-older-than, retention.

## IPC contract (reused as-is from `AkmlSql.Core`)

Message types (`src/AkmlSql.Core/Ipc/RpcMessage.cs`):

| Type | Code | Direction | DTO |
|------|------|-----------|-----|
| `HistoryRecord` | 40 | write (notification) | `HistoryRecordRequest` → `HistoryRecordResponse` (140) |
| `HistorySearch` | 41 | read | `HistorySearchRequest` → `HistorySearchResponse` (141) |
| `HistoryAction` | 42 | read+mutate | `HistoryActionRequest` → `HistoryActionResponse` (142) |

`HistoryActions`: `GetFullSql=0, ToggleFavorite=1, Delete=2, Export=3, GetDiff=4, DeleteAll=5,
Rename=6, GetVersions=7, SetOpenStatus=8, SaveVersion=9, RemoveOlderThan=10`.

`HistorySearchRequest` fields: SearchText, Server, Database, Status, DateFrom, DateTo,
FavoritesOnly, Deduplicate, Offset, Limit(=100), IsOpen, NameFilter, CamelCaseTokens.
`HistorySearchResponse`: Success, Entries[`HistoryEntryDto`], TotalCount, Error.
`HistoryEntryDto`: Id, SqlText(≤500), Server, Database, Username, ExecutedAt(ISO), DurationMs,
RowCount, Status, ErrorMessage, Source, TabTitle, IsFavorite, ExecutionCount, ContentHash, IsOpen.
`HistoryRecordRequest`: SqlText, Truncated, Server, Database, Username, DurationMs, RowCount,
Status, ErrorMessage, Source, TabTitle. (Engine stamps `executed_at` + `content_hash`.)
`HistoryActionResponse`: Success, FullSqlText, DiffLeftSql, DiffRightSql, ExportPath, Error,
Versions[`HistoryVersionDto`{Id,SqlText,SavedAt}], DeletedCount.

## Components

### 1. `IHistoryService` / `HistoryService` (new, `src/AkmlSql.Web/Services/`)
Modeled on `QueryExecutionService` (bridge round-trips) + `SnippetStore` (notification writes).
Methods:
- `Task<HistorySearchResponse> SearchAsync(HistorySearchRequest, CancellationToken)`
- `Task RecordAsync(HistoryRecordRequest, CancellationToken)` (notification; fire-and-forget)
- `Task<bool> ToggleFavoriteAsync(long id, CancellationToken)`
- `Task<bool> RenameAsync(long id, string newName, CancellationToken)`
- `Task<int> DeleteAsync(long[] ids, CancellationToken)`
- `Task<int> RemoveOlderThanAsync(long id, bool keepFavorites, CancellationToken)`
- `Task<string?> GetFullSqlAsync(long id, CancellationToken)`
- `Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken)`
- `Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken)`

All guard `_bridge.State == BridgeState.Open`; return empty/`false`/`null` when not paired.
DI: `builder.Services.AddSingleton<IHistoryService, HistoryService>();` in `Program.cs`.

### 2. Execution capture (modify `Pages/Editor.razor`)
After `QueryExec.ExecuteAsync` returns in the **user-initiated** `ExecuteAsync` path (NOT
`OnAppliedReExecuteAsync`), call `HistoryService.RecordAsync` with a `HistoryRecordRequest` built
from the result + `ISqlConnectionService.Server/Database`. Row count = `ResultSets.Sum(rs =>
rs.Rows.Length)` for reads, else `TotalRowsAffected`. `Source = "web"`. Best-effort; swallow failures.

### 3. `Pages/History.razor` (new, `@page "/history"`)
Ports the desktop 2-region layout to Blazor + AKML-Blue CSS tokens (`--akml-*`; `History*` tokens
already exist in `ThemeTokens.cs`). NavMenu link added.
- **Top bar:** search box (Enter → search), favorites toggle, source/server menu (derived from
  entries), refresh.
- **Left:** date-grouped collapsible list (Today / This Week / Two Months Ago / Older — porting the
  `DateBucketConverter` ranges: Today=same day, This Week=prior 6 days, Two Months Ago=7–59 days,
  Older=60+); 2-line rows (name [TabTitle or 60-char SQL], relative-time · exec-count,
  ● server\db). Below: a "History for <name>" **version sub-panel** (GetVersions).
- **Right:** read-only **CodeMirror** preview (SQL syntax highlight + search-term highlight), header
  (name + timestamp), metadata line (● server · db · v N of M), primary **Open** button.

### 4. Actions
Per-row context actions: Copy SQL, Open in editor, Re-execute, Rename, Toggle favorite, Delete,
Remove older than, Compare(2), Export.
- **Open in editor** — load full SQL (`GetFullSql`) + connection into the web editor
  (`IEditorSessionStore` / navigation to `/`).
- **Re-execute** — load + run via `QueryExecutionService`.
- **Compare** — `GetDiff` → side-by-side read-only diff modal (web-native; no DTE diff window).
- **Export** — **web-native**: fetch entries' SQL and generate CSV/JSON/SQL **client-side → browser
  download** (Blob + anchor via a small JS helper). The engine `Export` action writes a server-side
  absolute path and is NOT used.

### 5. Offline / degradation / theming
`BridgeState` gates everything; an explicit "Connect an engine to view history" empty state when not
paired. `IThemeService.InitializeAsync()` for light/dark/HC. All chrome via `var(--akml-*)` tokens.

## Decisions (confirmed with user)
1. Surface = dedicated `/history` Page (desktop tool window → web Page).
2. Capture = user-initiated executions only (matches desktop F5).
3. Compare + Export are web-native (side-by-side diff modal; client-side download).

## Files
**New:** `Services/IHistoryService.cs`, `Services/HistoryService.cs`, `Pages/History.razor`,
client-side export-download JS helper. **Modified:** `Program.cs` (DI), `Shared/NavMenu.razor`
(link), `Pages/Editor.razor` (capture hook), `wwwroot/js/akml-editor.js` (read-only preview
instance, if needed). **Reused:** all `AkmlSql.Core` history DTOs + the entire engine backend.

## Testable units (engine/library-style, unit-testable without a browser)
- Date-bucket classification (Today/This Week/Two Months Ago/Older) — pure function.
- Server/Database dropdown derivation from an entry list — pure function.
- Row-count derivation from `ExecuteQueryResult` (SELECT vs DML) — pure function.
- `HistoryRecordRequest` construction from a result + connection — pure function.
These extract into a `HistoryService` static helper or a small `WebHistoryLogic` class for xUnit
coverage; the bridge round-trips and the Razor UI are verified live.

## Verification ("full feature full cycle")
Build + run web paired to a local engine + loopback SQL connection; drive via Playwright:
run a query → capture → open `/history` → entry appears → search/group/select → preview + versions →
favorite → rename → open-in-editor → re-execute → remove-older → delete → export-download. Probes:
empty search, no-connection state, special characters, paging.

## Out of scope
- Engine changes (none needed).
- New IPC message types or distinct-server/db IPC (dropdowns derived client-side).
- AI chat history (separate, already exists).
