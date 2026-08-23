# SQL History — Session Grouping and Date-Based Naming — Design

**Date:** 2026-08-12
**Branch:** 030-closure-followups
**Status:** Approved (design); implementation pending. NOT committed (per project git rule).

## Problem

Every execution writes a history row, and the list de-duplicates those rows by `content_hash`
(SHA-256 of the whitespace-normalised, case-folded SQL). Two consequences make the list unusable:

1. **Editing a query mid-session mints a new entry.** Identical re-runs collapse with a `×N` count,
   but changing one predicate produces a different `content_hash` and therefore a separate row. One
   work session on one query sprays a pile of near-duplicate entries.
2. **The display name is meaningless.** `tab_title` holds the SSMS document filename, which for an
   unsaved scratch document is a random 8-character name (`dwnhdxfq.sql`). Entries with no
   `tab_title` fall back to showing raw SQL text.

Observed live: dozens of rows all titled `dwnhdxfq..sql`, one carrying `×276`.

## Goal

One history entry per **query session** (one editor tab's lifetime), auto-named `query-01`,
`query-02`, … with the counter resetting each local day.

## Decisions

| # | Decision | Chosen |
|---|----------|--------|
| 1 | Grouping unit | One editor tab / query session |
| 2 | Numbering | `query-NN`, resets daily; date comes from the existing day headers, not the name |
| 3 | Name priority | manual rename > real saved filename > `query-NN` |
| 4 | Existing rows | Backfill by (local date, `tab_title`, server, database); nothing deleted |

## Scope boundary — storage stays per-execution

The change is at the **grouping layer only**. Each execution still writes one `history` row.

This is deliberate and was confirmed with the user: the approved layout shows a run count (`×276`)
and a version list per entry. Both are derived from per-execution rows. Collapsing at write time
would make those numbers unrepresentable, and would also destroy the per-run status, duration, and
row-count data the detail pane already displays.

The existing `Deduplicate` filter flag is retained: on (default) = one row per session; off = the
raw per-execution list.

## Architecture

### Session identity

`HistoryRecordRequest` (IPC type 40, fire-and-forget) gains one field:

```csharp
/// <summary>
/// Opaque, client-owned identifier that is stable for the lifetime of ONE editor document.
/// Null/empty is permitted (the engine then falls back to the legacy inference rule), so an
/// older shell paired with a newer engine keeps working.
/// </summary>
public string? SessionKey { get; set; }
```

Producers:

- **SSMS / VS shell** (`AkmlSql.Shell.Shared/History/ExecutionCapture.cs`) — a GUID minted on the
  first execution from a document and cached in a `ConditionalWeakTable` keyed by the document's
  `ITextBuffer`, so the key dies with the tab. Closing and reopening the same `.sql` file therefore
  starts a new session, matching "one tab, one entry".
- **Web** (`AkmlSql.Web`) — a GUID persisted in the editor session record (`EditorSessionRecord`),
  so a page reload — which drops the Blazor circuit — keeps the same session. A new GUID is minted
  by "Reset editor session".

### Schema (`HistoryDatabase`, schema version bump)

```sql
CREATE TABLE IF NOT EXISTS query_sessions (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    session_key   TEXT    NOT NULL,
    local_date    TEXT    NOT NULL,   -- 'YYYY-MM-DD', local, day the session began
    ordinal       INTEGER NOT NULL,   -- 1-based within local_date
    name          TEXT    NOT NULL,
    name_source   INTEGER NOT NULL,   -- 0=auto, 1=file, 2=manual
    server        TEXT,
    database_name TEXT,
    created_at    TEXT    NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_session_key  ON query_sessions (session_key);
CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_date_ordinal ON query_sessions (local_date, ordinal);

-- Column first, THEN its index — the index cannot reference a column that does not exist yet.
ALTER TABLE history ADD COLUMN session_id INTEGER REFERENCES query_sessions(id);
CREATE INDEX IF NOT EXISTS IX_history_session ON history (session_id);
```

The `ALTER TABLE` follows the existing `is_open` migration idiom in `HistoryDatabase.InitializeAsync`
— execute, and swallow the `duplicate column` `SqliteException`.

`local_date` is stored rather than derived from `executed_at` so a session started at 00:30 cannot be
re-bucketed by a later timezone conversion. The day boundary is **local** midnight.

### Naming

On the first execution carrying an unknown `SessionKey`, in one transaction:

```sql
INSERT INTO query_sessions (session_key, local_date, ordinal, name, name_source, ...)
SELECT @key, @localDate, COALESCE(MAX(ordinal), 0) + 1,
       'query-' || printf('%02d', COALESCE(MAX(ordinal), 0) + 1), 0, ...
FROM query_sessions WHERE local_date = @localDate;
```

Concurrency: two shell windows executing in the same instant can read the same `MAX(ordinal)`. The
`IX_qs_date_ordinal` unique index converts that race into a constraint violation; the insert is
retried once, which re-reads the new maximum. This is a real race in a multi-window setup, not a
theoretical one.

Beyond 99 the name widens naturally (`query-100`) — `printf('%02d')` pads but does not truncate.

**Name priority** is enforced through `name_source`:

- A later execution supplying a real filename upgrades `0 → 1` and rewrites `name`.
- `Rename` (IPC `HistoryAction` 6) sets `name_source = 2`.
- Nothing ever overwrites `name_source = 2`.

"Real filename" is determined **authoritatively for new rows** and only heuristically for backfill:

- **New rows.** The producing client already knows whether the document is backed by a saved file:
  the shell has the document's `source` path, the web knows whether the buffer came from a named
  document. It sends `TabTitle` **only for a genuinely saved document**, and leaves it null for an
  unsaved scratch tab. No pattern matching is involved, so `report01.sql` is treated correctly.
- **Backfill only.** Legacy rows have already lost that distinction, so a `tab_title` is treated as
  a scratch name when it is null/empty or matches, case-insensitively:

  ```
  ^(SQLQuery\d+|[a-z0-9]{8})\.sql$
  ```

  This covers SSMS's `SQLQuery1.sql` and its random 8-character temp form (e.g. `dwnhdxfq.sql`).
  It is a heuristic with a known false positive: a genuinely saved file whose name happens to be
  eight alphanumeric characters (`report01.sql`) is auto-renamed to `query-NN`. It applies to
  pre-migration rows only, is one rename away from correction, and is documented at the call site.

**Deletion does not renumber.** Deleting `query-02` leaves a gap in that day's sequence. Renumbering
would rename entries the user may have referenced elsewhere; a stable name outranks a dense sequence.

### Read model

`HistoryDatabase.SearchAsync` de-duplication partitions by `session_id` instead of `content_hash`:

- representative row — latest execution in the session (`ROW_NUMBER() ... ORDER BY executed_at DESC, id DESC`, as today);
- `exec_count` — `COUNT(*) OVER (PARTITION BY session_id)`;
- name — `JOIN query_sessions`;
- version count — `COUNT(DISTINCT content_hash) OVER (PARTITION BY session_id)`.

Because the name now lives in exactly one row, the `FIRST_VALUE(h.tab_title) OVER (...)` window
expression — which exists today only so a rename survives later re-executions — is **deleted**. The
`NameFilter` clause moves from `h.tab_title LIKE ...` to the joined session name.

Versions are derived from distinct `content_hash` values within the session, newest first. The
existing `history_versions` table and its `GetVersions` / `SaveVersion` actions are **not** touched;
they serve the diff window and remain as they are.

### Backfill

One-time, inside the schema-version migration transaction, over rows with `session_id IS NULL`:

1. Group by `(local date of executed_at, COALESCE(tab_title,''), COALESCE(server,''), COALESCE(database_name,''))`.
2. Create one `query_sessions` row per group, ordinalised by the group's earliest execution within
   that date.
3. Name it by the same priority rule — scratch-looking `tab_title` → `query-NN`; anything else kept
   as `name_source = 1`.
4. Set `history.session_id` for the group's rows.

No row is deleted or edited beyond `session_id`. The migration is idempotent: it only considers
`session_id IS NULL`, so re-running never renumbers existing sessions.

Accepted limitation: two genuinely unrelated scratch tabs that shared a name on the same day against
the same database merge into one entry. Old rows carry no tab identity, so this is inference, and the
user accepted it explicitly.

### UI

Both surfaces already render a name, a count, and a detail pane, so this is a rebind rather than new
UI:

- `src/AkmlSql.Web/Pages/History.razor` — `DisplayName(e)` returns the session name; the raw-SQL
  fallback is removed. Row shows `×runs · N versions`. Rename targets the session.
- `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` — same rebind; the existing
  `entry.TabTitle = newName` rename path writes the session name.
- The detail pane gains a **Versions** list (distinct SQL texts in the session, newest first).

## Testing

Engine (`tests/AkmlSql.Engine.Tests`):

- N executions with one `SessionKey` → one row, `exec_count = N`, version count = distinct texts.
- Ordinal resets across a local-date boundary (session at 23:59 then 00:01 → `query-NN` then `query-01`).
- Concurrent assignment: parallel inserts for the same `local_date` never duplicate an ordinal and
  never throw out of the retry.
- Manual rename survives both a re-execution and a subsequent real-filename upgrade attempt.
- A record with null/empty `SessionKey` still lands (legacy-shell compatibility).
- Backfill over a fixture of legacy rows produces the expected groups, names, and ordinals, and is
  idempotent across two runs.
- Backfill scratch-name heuristic: `SQLQuery1.sql` and `dwnhdxfq.sql` are scratch; `MonthlyReport.sql`
  is not. The known false positive (`report01.sql`) is asserted so the limitation stays visible
  rather than being discovered later as a bug.

Shell (`tests/AkmlSql.Shell.Shared.Tests`): the per-buffer `SessionKey` is stable across executions
from one buffer and differs across buffers; `TabTitle` is populated for a saved document and null for
an unsaved scratch document.

Web (`tests/AkmlSql.Web.Tests`): the session key survives a simulated reload and changes on reset.

## Out of scope (YAGNI)

- No second versions table — versions are derived.
- No renumbering after deletion.
- No cross-machine history sync.
- No change to retention, export, favourites, FTS5 search, or the diff window.

## Risk

The backfill rewrites `session_id` across the whole `history` table in one transaction. The live
database already holds thousands of rows. Mitigation: create `IX_history_session` before the update,
run inside the existing init transaction, and log start/finish with row counts, so a slow first
start after upgrade is explicable rather than mysterious.
