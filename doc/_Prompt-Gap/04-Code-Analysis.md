# 04 — SQL Code Analysis (static analysis)

Scope: real-time static analysis, the rule set & categories, auto-fix, the issues list, and rule sharing.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. Core behavior

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| Background static analysis | Parses code as you type/review and checks against built-in rules | auto | ✅ debounced 300ms + on-open |
| Green wavy underline | Marks code that breaks a rule | editor | ✅ DiagnosticTagger IErrorTag |
| Lightbulb indicator | Blue = info; **orange = auto-fixable** | left margin | 🟡 lightbulb shown; no blue/orange icon distinction |
| Issue Details popup | Rule description, why it matters, often a link to an article | `Ctrl` (cursor in underlined area) | 🟡 squiggle hover shows message; no Ctrl popup/article link |
| Toggle analysis on/off | Enable/disable all analysis | `Ctrl + Shift + A`; SQL Prompt menu | 🟡 Options checkbox only; no shortcut/menu toggle |
| Manage rules dialog | Enable/disable individual rules (Code Analysis Rules dialog) | SQL Prompt ▸ Manage code analysis rules | ❌ no per-rule grid; Options has master toggles only |
| Disable a single rule from Issue Details | Quick-disable the offending rule | Issue Details | 🟡 lightbulb writes %AppData% .casettings; inert in live editor |
| Show issues list | Tabular list of all issues in a script | SQL Prompt ▸ Show List… | ✅ results grid (shell) + live Problems panel (web) |
| Auto-fix | One-click fix for orange (auto-fixable) issues | lightbulb / Actions | ✅ real FixAction (semicolon, dbo., SET NOCOUNT) |

## 2. Rule categories (prefixes)

SQL Prompt groups rules into categories shown in the Manage Rules dialog. Each rule has a prefix + number.

| Prefix | Category | Examples (representative) | Status |
|---|---|---|---|
| `BP` | Best Practices | BP006 TOP without ORDER BY; BP013 `Execute(string)` (injection risk); BP022 avoid MONEY/SMALLMONEY | ✅ 28 BP rules (numbering differs) |
| `DEP` | Deprecated syntax | DEP021 non-standard column alias; deprecated joins/clauses/hints | ✅ 8 DEP rules |
| `PE` | Performance | PE001 procedure not schema-qualified; PE003 SELECT…INTO use; PE008/PE009 SET NOCOUNT; PE017 scalar UDF misuse; PE019 `[NOT] EXISTS` vs `[NOT] IN` | ✅ 31 PE rules (numbering differs) |
| `ST` | Style | ST002 old-style column alias; ST006 old-style TOP; ST011/ST012 table variable vs temp table | ✅ 24 ST rules (numbering differs) |
| `EI` / Execution | Execution issues | parameter mismatch, cursor open/fetch issues, transaction balance, etc. | 🟡 6 EX rules; no cursor/txn-balance checks |
| Naming | Naming convention | object naming conventions | ✅ 6 NM rules |
| Misc | Miscellaneous | rules not in other categories | ❌ no Misc bucket; AKML adds Security+Design instead |
| Script | Script-level | issues about the script rather than the SQL itself | ❌ no script-level category |

> The full set is 100+ rules. The dialog lets you toggle each. (Some SQL Code Guard rules are intentionally not implemented in SQL Prompt — relevant if you're aiming to *exceed* parity.)

## 3. Sharing & managing rule sets

| Feature | Description | Where | Status |
|---|---|---|---|
| CASettings file | Rules export to a Code Analysis Settings file (XML) with all rules; edit to taste | export | 🟡 hand-edit .casettings JSON; no in-product export |
| Share rules (folder) | Distribute a CASettings file to the team | docs: "Sharing Code Analysis rules" | 🟡 upward .casettings honored by CLI only; inert in live editor |
| Share rules via Redgate Platform | Cloud sharing of CA rules (Toolbelt Essentials subscription) | Redgate Platform | ➖ Redgate cloud/subscription |
| Per-team / per-database settings | Maintain multiple CASettings files | manual | 🟡 multiple .casettings honored by CLI only; inert in live editor |
| Bulk code analysis | Run analysis across a whole codebase at once | Bulk Actions / Command Palette (`Alt+S`) | ✅ folder scan command + Analyzer CLI |

---

### Quick keyboard reference (this scope)
Toggle analysis `Ctrl+Shift+A` · Open Issue Details `Ctrl` (in underlined region).
