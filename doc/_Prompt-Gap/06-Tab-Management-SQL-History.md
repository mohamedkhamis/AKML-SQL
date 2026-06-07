# 06 — SSMS Tab Management & SQL History

Scope: SQL History (record/restore/search queries), tab restore on crash, starred queries, retention, and tab coloring by server/database. **SSMS only.**

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. SQL History (v10.13+) — the modern feature

| Feature | Description | Where | Status |
|---|---|---|---|
| Record queries as you type | Continuously records query content + version history | auto | 🟡 snapshots on execute/close/focus, not keystroke |
| SQL History window | New dockable/movable tab listing queries | SSMS toolbar ▸ SQL History | ✅ |
| Per-query metadata | File name, server + database, last-updated time, version timestamps, SQL content | window | ✅ |
| Reopen unsaved/closed query | Reopen a closed-without-saving query in its last state | window | ✅ |
| Crash/shutdown auto-restore | Restores all open queries after SSMS crash or close-without-save | auto (toggleable) | ✅ |
| Restore open queries on startup | Reopens previous session's tabs | Options (toggle) | ✅ |
| Search history | Full-text search across entire query history | window ▸ search | ✅ |
| Open / close query | Open or close the selected query | window actions | ✅ |
| Star / favorite | Mark a query as favorite; filter to starred | star icon / star filter | ✅ |
| Rename query | Rename (not while open) | three-dot menu | ✅ |
| Remove query | Delete a query and its history | three-dot menu | ✅ |
| Remove older than | Bulk-delete all queries older than the selected one | three-dot menu | ❌ |
| Auto-trim retention | Periodic background trim of old versions; configurable retention (default 7 days); keeps latest version; doesn't remove executions; can disable | Options ▸ Queries ▸ History | 🟡 purges whole entries; config-only; no disable toggle |

## 2. Tab History (legacy, v10.12 and older)

| Feature | Description | Status |
|---|---|---|
| Legacy tab history | Older mechanism for recovering tabs (superseded by SQL History) | ➖ Redgate legacy; AKML built modern equivalent |

## 3. Tab coloring

| Feature | Description | Where | Status |
|---|---|---|---|
| Color tabs by server | Right-click tab ▸ Tab Color (Server) ▸ environment (e.g. Production → red) | tab context | 🟡 auto pattern-based; no right-click menu |
| Color tabs by database | Right-click DB in Object Explorer ▸ Tab Color (Database) ▸ environment | OE context | ❌ matcher is server-name only |
| Color DB on any server | Add a server/blank + database row mapped to an environment | Options ▸ Tabs ▸ Color | ❌ no database match target |
| Environments | Named environments (Production, Development, Test, …) each with a color | Options ▸ Tabs ▸ Color ▸ Edit environments | ✅ |
| Edit environment colors | Pick custom colors per environment | Edit environments dialog | 🟡 hex text input + preview, no color picker |
| Gradient colors toggle | "Use gradient colors" on/off | Edit environments dialog | ✅ |
| Restore default environments | Reset to default environment set | right-click in dialog | 🟡 page Restore Defaults skips rules list |
| SSMS version support | Tab coloring works in SSMS 2012+ | — | ✅ multi-version visual-tree walk; SSMS+VS |

---

> AKML note: this whole scope is a strong differentiator area. SQL Prompt's SQL History records *version* history per query (not just executed text) and auto-restores after crashes — worth checking your equivalent records both edits and executions, and survives shell crashes.
