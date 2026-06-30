# 07 — SQL Prompt AI

Scope: the AI-powered features (opt-in, subscription-only; **not** in perpetual licenses).

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. Prompt AI window (core)

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| Open Prompt AI | Open the AI window on empty editor or a selection | `Alt + Z`; right-click ▸ Open Prompt AI; selection icon; SQL Prompt menu | 🟡 chat panel Ctrl+Shift+A, no unified window |
| Selection-scoped editing | If only a fragment is selected, AI edits just that (highlighted orange) | — | 🟡 acts on selection, no orange highlight |
| Natural-language prompt box | Free-text instructions; not limited to English | window | ✅ |
| Generate SQL | Create SQL from a natural-language request, schema-aware (joins, indexing, subqueries) | window | ✅ |
| Modify/tweak SQL | Rewrite or adjust existing SQL via instruction | window | 🟡 only via freeform chat, no rewrite path |
| Explain SQL | Plain-language explanation of selected SQL; scrollable explanation pane | "Explain SQL" button | ✅ |
| Accept / decline changes | Apply or revert to pre-AI state | window | ✅ DiffPreviewPanel Accept/Reject |
| Regenerate | Re-run the request for a slightly different result | regenerate button | ❌ no regenerate button |
| Selection icon toggle | "Show icon for editor selection" (on by default) | Options ▸ Prompt AI | ❌ config flag orphan, no adornment/toggle |

## 2. History & suggestions

| Feature | Description | Where | Status |
|---|---|---|---|
| In-session History tab | List of queries made while the window is open; revert to any prior state; "Latest Query" entry (not persisted after closing window) | window ▸ History | ❌ no History tab or revert |
| Follow-up suggestions | Auto-offered next-step prompts; clickable to send as a prompt | window | ❌ config flag orphan, not rendered |
| Initial suggestions from SQL History | On open, generate suggestions from your recent SQL History (opt-in setting) | Options ▸ Prompt AI ▸ "Generate initial suggestions using SQL History" | ❌ not implemented, no setting |

## 3. Targeted AI actions

| Feature | Description | Where | Status |
|---|---|---|---|
| Fix SQL query with AI | One-click fix of errors in a selected query | selection / popup | ✅ AiFixCommand.cs full ExecuteAsync() wired: sends AiFix IPC, shows FixPreviewPanel, applies fix |
| Error-fix suggestion popup | Offers to fix when an error is detected | popup | 🟡 OfferFixForError() defined and status-bar notification fires on AutoFixOnError; no calling site wired to an execution-error event; popup not shown proactively |
| Optimize SQL query with AI | Suggest an optimized rewrite of a query | menu / window | 🟡 real call, results dumped to temp .sql |
| AI code completion (Preview) | Greyed "ghost text" predictions as you type; accept `Tab`, dismiss `Esc`, manual `Ctrl+Alt+Up` | Options ▸ Prompt AI ▸ "Enable AI code completion" | 🟡 real Tab/Esc; no manual-trigger command |
| Ghost-text auto delay | Auto-request after N ms (default 500); adjustable; or manual-only mode | Options ▸ Prompt AI | 🟡 hardcoded 300ms; config unused, not adjustable |
| Requires code suggestions on | AI completion needs Options ▸ Behavior ▸ Show code suggestions enabled | — | ➖ SQL-Prompt-internal dependency |

## 4. Query Index Analysis (AI/ML)

| Feature | Description | Where | Status |
|---|---|---|---|
| Analyze Query Indexes | ML model estimates the performance impact of candidate indexes for a query | right-click ▸ Analyze Query Indexes | 🟡 real LLM call, not ML impact model |
| Index Analysis tool window | Shows hinted vs existing indexes with estimated impact | window | 🟡 results in temp .sql, no tool window |
| Copy create-index script | Button to copy the `CREATE INDEX` for each suggestion | window | 🟡 emitted as text, no per-suggestion copy button |
| Scope/limits | SELECT queries with WHERE/JOIN; non-clustered rowstore only; simulated stats (uniform distribution) | — | ➖ SQL-Prompt-internal constraint |
| Requires v11.05+ & internet | Query sent only to Redgate's service; not retained; not third-party | — | ➖ Redgate-service architecture, BYO-key differs |

## 5. Data handling / governance

| Feature | Description | Status |
|---|---|---|
| Opt-in by default | All AI features off until enabled | ✅ Enabled defaults false; consent gate |
| Schema-awareness fallback | If schema can't be fetched, AI proceeds without schema and warns | 🟡 proceeds with empty schema, no warn |
| Org opt-out | Organizations can disable AI features | ❌ no policy/registry/admin disable |
| Data handling doc + FAQ | Documented privacy/data handling | 🟡 web-scoped doc only, no desktop FAQ |

---

> AKML note: your multi-model BYO-key architecture already exceeds SQL Prompt's single-service model. For *UX* parity the items to match are: the `Alt+Z` window, selection-scoped edit (orange highlight), Explain button, in-session revert history, follow-up + initial suggestions, ghost-text completion, one-click fix/optimize, and the index-analysis tool window.
