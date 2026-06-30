# Redgate SQL Prompt 11 — Full Feature Inventory (for AKML SQL parity review)

**Source:** Official product documentation at `https://documentation.red-gate.com/sp` (SQL Prompt 11.x), cross-checked with Redgate release notes and product-learning articles (up to June 2026).
**Purpose:** A complete, scope-separated checklist of *every* SQL Prompt feature, setting, and small behavior, so you can tick off what AKML SQL already covers, what is partial, and what is missing.

---

## How to use these files

Each feature is listed in a table with a **Status** column for your AKML review. Suggested legend:

- `✅` Done / at parity
- `🟡` Partial / needs work
- `❌` Missing
- `➖` Not planned / out of scope

The "Where / Shortcut" column tells you the menu path, Options pane, or keyboard shortcut so you can locate the equivalent surface in AKML SQL.

---

## File index (by scope)

| # | File | Scope |
|---|------|-------|
| 00 | `00-INDEX-and-Questions.md` | This file: index, method, open questions |
| 01 | `01-Code-Completion-IntelliSense.md` | Autocomplete / suggestions box / column picker / aliases / tooltips / object definition |
| 02 | `02-Formatting-and-Styles.md` | Format SQL, styles, the Edit Style option groups, import/export/share, disable-formatting |
| 03 | `03-Refactoring-and-Actions.md` | SQL Prompt Actions, object/batch refactors, database refactors, query/result refactors |
| 04 | `04-Code-Analysis.md` | Static analysis, rule categories & codes, auto-fix, issue list, sharing rules |
| 05 | `05-Snippets.md` | Snippet manager, insertion, placeholders, SSMS templates, sharing |
| 06 | `06-Tab-Management-SQL-History.md` | SQL History, tab restore, starred queries, tab coloring by server/database |
| 07 | `07-SQL-Prompt-AI.md` | Prompt AI window, generate/explain/modify, fix, optimize, AI code completion, index analysis |
| 08 | `08-Options-Settings-Reference.md` | Every Options pane and the granular settings on each |
| 09 | `09-Editions-Licensing-Platform-Integrations.md` | Editions, licensing tiers, Command Palette, Bulk Actions, Redgate Platform, integrations, hosts |

---

## AKML parity scorecard (code audit — 2026-06-07)

Files 01–09 now have their **Status** columns filled from a file-by-file **code** audit (verified against `src/`, not the PRD). Every ✅/🟡 row carries a terse `file:line` / class evidence note.

| File | Scope | ✅ | 🟡 | ❌ | ➖ | Rows |
|---|---|--:|--:|--:|--:|--:|
| 01 | Code Completion / IntelliSense | 13 | 13 | 23 | 0 | 49 |
| 02 | Formatting & Styles | 26 | 29 | 9 | 1 | 65 |
| 03 | Refactoring & Actions | 4 | 8 | 13 | 0 | 25 |
| 04 | Code Analysis | 10 | 8 | 3 | 1 | 22 |
| 05 | Snippets | 6 | 10 | 8 | 4 | 28 |
| 06 | Tab Management & SQL History | 13 | 5 | 3 | 1 | 22 |
| 07 | SQL Prompt AI | 5 | 11 | 8 | 3 | 27 |
| 08 | Options & Settings | 18 | 19 | 15 | 0 | 52 |
| 09 | Editions / Licensing / Platform | 8 | 9 | 5 | 12 | 34 |
| **Total** | | **103** | **112** | **87** | **22** | **324** |

≈32 % at parity (✅), ≈35 % partial (🟡), ≈27 % missing (❌), ≈7 % out-of-scope (➖ — mostly Redgate-cloud / license-tier rows that don't apply to AKML's free/MIT model). **Actionable gaps (🟡 + ❌) = 199.**

---

### UPDATE — RE-AUDIT (code audit — 2026-06-23)

All 324 rows were re-verified against current code (one reader per file, bounded). The 2026-06-07 table above is kept for history; the **current** distribution is:

| File | Scope | ✅ | 🟡 | ❌ | ➖ | Rows |
|---|---|--:|--:|--:|--:|--:|
| 01 | Code Completion / IntelliSense | 31 | 14 | 4 | 0 | 49 |
| 02 | Formatting & Styles | 54 | 7 | 3 | 1 | 65 |
| 03 | Refactoring & Actions | 16 | 8 | 1 | 0 | 25 |
| 04 | Code Analysis | 15 | 4 | 2 | 1 | 22 |
| 05 | Snippets | 16 | 6 | 2 | 4 | 28 |
| 06 | Tab Management & SQL History | 18 | 3 | 0 | 1 | 22 |
| 07 | SQL Prompt AI | 6 | 12 | 6 | 3 | 27 |
| 08 | Options & Settings | 35 | 11 | 6 | 0 | 52 |
| 09 | Editions / Licensing / Platform | 8 | 9 | 5 | 12 | 34 |
| **Total** | | **199** | **74** | **29** | **22** | **324** |

**Net change: ✅ 103 → 199 (+96) · 🟡 112 → 74 · ❌ 87 → 29 · ➖ 22 (flat). Actionable gaps (🟡 + ❌) = 103** (was 199). Almost all of the movement is spec-030: formatter `Rules/*` wired into the pipeline (T008–T012), IntelliSense surfaces (QuickInfo/SignatureHelp/temp-table/column-picker/category-grouping/alias-policy/connection-scope, T025–T036), Options pages consuming those flags (T077–T083), refactor actions + Script-as-ALTER + Smart-Rename + inline-proc/EXEC (T058–T068), snippet expand/surround/create-from-selection + placeholders (T039–T047), and history/tab-coloring (T070–T075). 07-AI was deliberately **excluded** from spec-030 (only address as UX-parity if asked); 09-Editions is mostly licensing → `➖`. Status glyphs in files 01–08 were refreshed to match; 09 was unchanged. Two engine gaps caught during this pass were fixed (global `@@`-variable casing → follows the GlobalVariables style option; `$SERVER$` snippet now resolves from the session connection); `$PASTE$`/clipboard stays `🟡` (engine-ready, shell capture unbuilt).

> **Parity bar = desktop SSMS 22 / VS 2026.** AKML Web-edition-only behaviours that are broken on desktop count as ❌, not partial (most visible in file 05 snippets). The Web edition itself is a differentiator, not a SQL Prompt parity target.

### Headline finding: "built but not wired"

The biggest planning signal is that several whole surfaces are **implemented in code but unreachable** — low effort to light up, and they collapse a large share of the 🟡 column:

- **Formatter layout rules** (file 02) — `Rules/{Dml,Ddl,Join,List,Parenthesis,ControlFlow}` + `AlignmentCalculator` + `CollapseEvaluator` are full, unit-tested classes that `FormatterPipeline.Format` never invokes (it only runs LayoutEngine → LineBreakDecider → CasingEngine). Wiring them in lights up CASE/CTE/DDL/INSERT/UPDATE/MERGE layout, leading commas, alignment, collapse-short and max-line wrapping — the bulk of file 02's 🟡 rows.
- **Format actions** (files 02/03) — the semicolons/qualify/wildcards/brackets `IFormatAction` classes exist but are **never instantiated**; the engine's `HandleFormatAction` returns "not supported here." Needs ~6 dispatch cases.
- **`.casettings` in the live editor** (file 04) — per-rule enable/severity + suppressions only apply in the CLI; the live `AnalysisEngine` is handed a `null` directory. Thread the document path through.
- **Hover tooltips & signature help** (file 01) — `QuickInfoSource` / `SignatureHelpSource` shell classes are skeleton stubs that only log; the engine handlers already exist. Wire the IPC.
- **Temp-table IntelliSense** (file 01) — `TempTableTracker` exists with tests but is never called from `CompletionEngine`.
- **One-click AI Fix** (file 07) — the handler is real, but the command's error hook (`OfferFixForError`) is never called, so the command always bails before sending.

Net: much of the 🟡 column is **wiring existing code**, not greenfield work — worth weighting first when planning.

---

## Important note on completeness

The public documentation fully enumerates menus, Options panes, refactors, snippet placeholders, AI features, tab/history behavior, and code-analysis categories. However, it **does not** print every individual checkbox inside the **Edit Formatting Style** dialog (those granular toggles such as "GROUP BY: place each column on a new line", "add non-aggregated SELECT columns to GROUP BY", etc., live only in the live UI).

File `02` therefore lists:
1. The option *groups* that the docs confirm exist, and
2. The known granular toggles per group (the ones to verify directly against the live **Edit Style** dialog in SQL Prompt 11).

Rows that come from the live dialog rather than the docs are flagged **[verify in UI]** so you know which to confirm hands-on.

Your GROUP BY example ("add the non-aggregated column when you press space / select it") is captured in two places: as a *suggestion-box* behavior in file `01` (Prompt helps fill in GROUP BY clauses) and as a *formatting/clause* option group in file `02`.

---

## Open questions for you (so I can tailor the next pass)

1. **Target depth on formatting toggles** — Do you want me to do a hands-on/secondary pass to enumerate *every* checkbox in the live Edit Style dialog (≈200+ toggles across SELECT/INSERT/UPDATE/DELETE/MERGE, JOIN, CASE, CTE, DDL, parentheses, wrapping, etc.)? The docs alone don't list them; I can reconstruct the full set from the `.sqlpromptstylev2` schema / a sample exported style if you can share one of your `.akmlstyle` files for direct field-by-field mapping.

2. **Scope of "parity"** — Are you targeting *SSMS-only* parity, or also Visual Studio behaviors (some shortcuts differ, e.g. Rename is `F2` in SSMS but `Shift+F2` in VS)? And do you care about Azure Data Studio parity (Redgate shipped a separate, reduced ADS build)?

3. **AI features** — AKML already has a multi-model AI architecture. Do you want the SQL Prompt AI file scoped purely as *feature parity* (what actions exist: generate/explain/fix/optimize/index-analysis/ghost-text), or also as *UX parity* (Alt+Z window, follow-up suggestions, history tab, initial suggestions from history)?

4. **Edition gating** — Should I mark which features are perpetual-license vs subscription-only vs Toolbelt-Essentials-only? I've put this in file `09`; tell me if you'd rather have the gating annotated inline on every feature row instead.

5. **Output format** — Do you prefer these as Markdown checklists (current), or would a single consolidated **spreadsheet** (one row per feature, columns for category, SQL Prompt behavior, AKML status, priority, notes) be more useful for your 90+ feature benchmarking workflow? I can generate that from these files on request.

6. **Competitor columns** — You benchmark against dbForge, DataGrip, Toad, SSMSBoost, ApexSQL. Want me to add columns for those tools too, or keep this strictly SQL Prompt vs AKML for now?

---

## AKML answers to the open questions (post-audit)

1. **Formatting toggle depth** — Not needed as a first step. The audit mapped file 02 against AKML's real `FormattingProfile` / `FormatSettingSchema` / LayoutEngine: AKML already *has* most setting fields and even full rule implementations — they're just not invoked by the pipeline (see "built but not wired" above). The highest-leverage work is **wiring the existing `Rules/*` passes into `FormatterPipeline`**, not enumerating more checkboxes. A `.sqlpromptstylev2` round-trip diff remains the way to close the long tail afterward.
2. **Parity scope** — AKML targets **SSMS 22 + VS 2026** (shared source, both supported) **+ a Blazor Web edition** SQL Prompt has no analog for. No Azure Data Studio build, no SSMS 21, no Fabric (file 09 marks those ❌/➖). So judge parity on SSMS 22 + VS 2026 desktop; the Web edition is a differentiator, not a parity target.
3. **AI features** — Scope as **UX parity + wiring**, not capability. AKML's multi-model BYO-key AI (Claude / OpenAI / Gemini / Ollama) already exceeds SQL Prompt's single Redgate service. The gaps are UX: no unified Alt+Z window (chat panel only), no in-session revert history, no follow-up / initial suggestions, ghost-text manual-trigger unwired, Index Analysis dumps to a temp file (no tool window), and the one-click Fix is structurally unreachable (file 07).
4. **Edition gating** — N/A. AKML is **MIT-licensed and free with BYO-key AI** — there are no perpetual / subscription / Toolbelt tiers, so SQL Prompt's license-tier rows are ➖ "different model" in file 09. No inline per-row gating needed; AI is opt-in by config, not by license.
5. **Output format** — Status is filled inline (markdown) with per-row evidence + the scorecard above. If you want a single sortable sheet (`category | SP behavior | AKML status | effort | notes`) for the 90+ benchmark, it can be generated from these 9 files on request.
6. **Competitor columns** — Out of scope for this pass (strictly SQL Prompt 11 vs AKML). Can be added later.
