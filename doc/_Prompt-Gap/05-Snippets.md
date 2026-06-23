# 05 — SQL Code Snippets

Scope: snippet insertion, the Snippet Manager, placeholders, SSMS template params, and sharing.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

> **Parity bar = desktop SSMS 22 / VS 2026.** Several snippet behaviours currently work only in AKML's Web edition and are broken on desktop — those are marked ❌ "web-only" here, not partial. §3 placeholder rows reflect engine-level token support.

---

## 1. Using snippets

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| Insert by name | Type snippet name (e.g. `ssf` → `SELECT * FROM`) then an insertion key | `Enter`/`Tab` (Tab-only in some alias-ambiguous cases) | ✅ |
| Snippets in suggestions box | Snippets appear as a category; preview shown in object definition box | `Ctrl+Space` ▸ Snippets | 🟡 6 hardcoded items appear; no preview pane |
| Insert at indent level | Code inserted at the current indentation | — | ❌ plain text Replace; no indent reflow |
| Built-in snippets | Ships with many (e.g. `cdb` → CREATE DATABASE; `ssf`; etc.) | — | 🟡 only 6 (hardcoded) / 11 (web) vs many |
| Wrap selection (surround) | Snippets using `$SELECTEDTEXT$` appear in the Actions list to wrap a selection (e.g. BEGIN…END) | Actions list | ✅ |

## 2. Snippet Manager

| Feature | Description | Where | Status |
|---|---|---|---|
| Open Snippet Manager | Browse/manage all snippets | SQL Prompt ▸ Snippet Manager | ✅ |
| Search snippets | Find by name or description | Snippet Manager | ✅ name/desc/shortcode/category/tags |
| Create from selection | Highlight code ▸ right-click ▸ Create Snippet; auto-names from initials | editor context | ✅ |
| New snippet | Define name + optional description + code | Snippet Manager ▸ New | ✅ |
| Edit / delete defaults | Modify or remove built-in snippets | Snippet Manager | ❌ built-ins read-only by design |
| Cursor position control | Define where the caret lands after insertion | via `$CURSOR$` | ✅ |

## 3. Placeholders (full default set)

| Placeholder | Inserts / does | Status |
|---|---|---|
| `$CURSOR$` | Sets caret position after insert | ✅ |
| `$DATE$` | Current date; supports custom format `$DATE(MM/dd/yyyy)$` | ✅ |
| `$DBNAME$` | Connected database name | ✅ |
| `$GUID$` | A new GUID | ✅ |
| `$MACHINE$` | Machine name running SQL Prompt | ✅ |
| `$PASTE$` | Clipboard contents | 🟡 supported as `$CLIPBOARD$` |
| `$SELECTEDTEXT$` | The selected text (enables surround/wrap snippets) | ✅ |
| `$SELECTIONSTART$` … `$SELECTIONEND$` | Pre-selects a block of the inserted snippet | 🟡 engine resolves/returns offsets; Tab-expand path ignores them |
| `$SERVER$` | Connected SQL Server name | ✅ |
| `$TIME$` | Current time; supports custom format `$TIME(HH:mm:ss)$` | ✅ |
| `$USER$` | Connected user name | 🟡 Windows OS user, not connected SQL user |
| Custom placeholder | `$myplaceholder$` with default value + insertion order via Placeholders list | ✅ |

## 4. SSMS templates inside snippets

| Feature | Description | Status |
|---|---|---|
| Template parameters | Use SSMS template parameters within snippet code | ➖ native SSMS pass-through; AKML does not parse |

## 5. Sharing & getting more

| Feature | Description | Where | Status |
|---|---|---|---|
| Shared folder for snippets | Store `.sqlpromptsnippet` files in a network/Dropbox share | docs: "Using a shared folder for snippets" | 🟡 Team source defined but never wired in engine |
| Share via Redgate Platform | Cloud team spaces (Toolbelt Essentials subscription) | Snippet Manager ▸ Redgate Platform | ➖ Redgate cloud subscription |
| Community snippet repo | Clone a public GitHub snippet repo | external | ➖ Redgate community content |
| tSQLt / SQL Test snippets | Downloadable test/assertion/isolation snippets | external | ➖ Redgate/external content |
