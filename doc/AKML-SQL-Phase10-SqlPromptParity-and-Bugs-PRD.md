# AKML SQL — Phase 10: SQL Prompt Parity Closure & Bug Fixes

> **Version:** 1.0 | **Date:** 2026-05-13 | **Author:** Mohamed Khamis
> **Status:** Draft (for review) | **Classification:** Internal Planning
> **Depends on:** Phases 1–9 (foundation through AI assistance)
> **Active branch context:** `018-options-dialog-phase2` (Options Dialog Phase 2 merged; Phase 3 pending)
> **Reconciles:** Specs 010–016, `doc/progress.md`, `doc/bugs.md`, `doc/codebase-audit-2026-05-05.md`, and the four SQL Prompt source-of-truth files under `doc/SQL-PROMPT/`

---

## 1. Executive Summary

This PRD is the single, code-verified reconciliation of *what is actually missing* between AKML SQL and Redgate SQL Prompt 11.3, together with the live bug backlog. It exists because three separate sources currently disagree:

- **`doc/progress.md` (last updated 2026-04-03)** claims "absolute 100% SQL Prompt v11 parity achieved" across all 12 capability areas.
- **`specs/014-sql-prompt-parity/tasks.md`** marks only Phase 1 + Phase 2 (Setup + Foundational IPC scaffolding) + Phase 3 (US1) as complete with phases 4–23 still pending. That file was last touched before the spec 014 PR (`#229`) merged to master, so the on-disk tasks.md is stale.
- **`git log` on master and `src/` greps on 2026-05-13** show a different reality: spec 014 PR `#229` merged on or before 2026-04-12 (commit `b48c249`) and brought **US1 (pre-execution safety, `f337729`)**, **US1 polish (`db194e9` — Phase 3b SafetyWarningDialog WPF rewrite + SchemaProgressMargin arc spinner)**, and **US5 (environment-based tab coloring core, `d7069d5`)** into master. Spec 015 PR shipped **13 of 14 user stories** in commit `ec09c45` (regression fix `4b0aec4`), with **US14 (installer branding) explicitly deferred**. Spec 016 PR shipped **Phase 1 + Phase 2 foundational ThemeRegistry/HostThemeWatcher infrastructure plus the first batch of 5 WPF surface migrations** (commits `5e1b0f8` + `2ac0407`). The "100% parity" *claim* is still incorrect, but the gap is roughly **half** of what an unmodified reading of the spec tasks.md files implied.

Phase 10's scope, after that reconciliation:

1. **Close the verified remaining gap** between AKML SQL and SQL Prompt 11.3 — **17 of 20 user stories in spec 014 are still outstanding** (US2, US3, US4 [Command Palette source aggregation], US6, US7, US8, US9, US10, US11 [regression test only], US12, US13, US14, US15, US16, US17, US18, US19, US20) — plus the deferred Options Dialog Phase 3 work and the remainder of spec 016 (most WPF surfaces still on legacy chrome).
2. **Clear the remaining bug backlog** — **BUG-B14 (installer icon + banner)** from spec 015, and the **14 code-level TODOs** flagged by the 2026-05-05 codebase audit (8 of which are still open as of HEAD `3ec5755`).
3. **Bring the documentation back into agreement with the code** — `progress.md`, `bugs.md`, `AKML_SQL_Gap_Analysis_1.md`, `CLAUDE.md`, and the on-disk `specs/014-sql-prompt-parity/tasks.md` are all out of date.

This PRD references the existing specs rather than restating their requirements. Each gap row cites the authoritative spec/user-story or audit finding so engineers can drill down without ambiguity. **Spec 014's FR-001..FR-105 remain the source of truth for functional requirements; this PRD sequences and prioritizes them and adds the bugs and cross-cutting work that the specs do not capture in one place.**

### Phase 10 Principle: Verify before claiming

Every "shipped" assertion in this PRD has a code reference (file path + class name) or a verifying test name. When the PRD says "ships in Milestone M2", that milestone closes only when the corresponding user-story tasks in `specs/0NN/tasks.md` flip to `[X]` *and* a build verification confirms the feature is reachable from a hot-swapped SSMS 22 install.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 10 — SQL Prompt Parity Closure & Bug Fixes |
| **Targets** | All 6 hosts (SSMS 20/21/22, VS 2019/2022/2026) |
| **Primary specs** | `specs/014-sql-prompt-parity/`, `specs/015-bug-fixes-polish/`, `specs/016-wpf-theme-refresh/` |
| **Source-of-truth refs** | `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md`, `…/Features_AI.md`, `…/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md`, `…/SQL-Prompt-History/SQL_Prompt_SQL_History.md` |
| **Engineering specs to reuse** | 010 (core parity), 011 (remaining gaps), 012 (history), 013 (sqlprompt-parity-gaps capture) — all logically rolled into 014 |
| **Estimated effort** | 8–10 weeks single-FTE, or 4–5 weeks at 2-FTE concurrency (see §8 Roadmap) |
| **Risk level** | Medium. Most engine primitives already exist (per spec 014 Phase 2 audit findings) — the work is shell-side wiring + UI. |

---

## 3. Reconciliation: `progress.md` Claims vs. Code Reality

The 2026-04-03 progress log claims 100% SQL Prompt parity. Spot-checks against `src/` on 2026-05-13 (HEAD `3ec5755`, branch `018-options-dialog-phase2`) confirm the following. The third column cites the **authoritative spec or task** that should be used for tracking real status from now on.

| Feature | `progress.md` claim | Code reality (verified by Grep on 2026-05-13) | Owning spec / source |
|---|---|---|---|
| Pre-execution safety dialog (DELETE/UPDATE/MERGE without WHERE, inside JOIN, inside proc/trigger bodies) | Implemented (commit `f337729`, Phase 3 UI polish in `Phase 3b`) | ✅ **Present and on master** — `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs`, `Safety/ExecutionInterceptor.cs`, `Safety/ExecutionCommandFilter.cs`. Phase 3b polish committed as `db194e9` on `master`. | Spec 014 US1, FR-001..009. Status: **shipped**. Action: regression test against US1 acceptance scenarios in M0. |
| Column Picker inside completion popup (`Ctrl+Left`, PK/FK badges, multi-select) | "Wildcard expansion popup with inline preview" — implied | ❌ **Absent** — Grep finds no `ColumnPicker` / `ColumnPickerControl` / `ColumnPickerSelection` class | Spec 014 US2, FR-010..016. Genuine gap. |
| Wildcard `*` + Tab expansion (inline, not chord) | Listed under "Format Triggers" | ❌ **Absent** — no `TabWildcardExpansionFilter` or equivalent `IOleCommandTarget` for `cmdidTab` | Spec 014 US3, FR-017..019. Engine `WildcardExpansionHandler` exists; only the Tab-key filter is missing. |
| Command Palette (4-source: AKML commands + options + host commands + DB objects) | "Command Palette with 32 commands" | ⚠️ **Partial** — `CommandPaletteWindow` exists with AKML-commands only; no `HostCommandSource` / `AkmlOptionsSource` / `DatabaseObjectSource` | Spec 014 US4, FR-047..052. |
| Environment-based tab coloring with right-click submenus and Manage Environments dialog | "Tab coloring by environment (Production=red, Staging=orange, Dev=green, Azure=blue)" | ⚠️ **Partial** — US5 core *shipped* on master (`d7069d5`): `ApplyTabColor`/`ClearTabColor` visual tree walk, `EnvironmentMatcher` with 29 unit tests, `RepaintAllTabs` live re-render, rules editor (Label/Pattern/Color) inline in Settings. Still missing: per-tab right-click submenu (`TabContextMenuExtender`), separate `EnvironmentPaletteWindow` dialog, gradient-on-rendered-tab. | Spec 014 US5, FR-041..046. Status: **rules editor done; right-click submenu still a gap (FR-041)**. |
| Code Analysis Issues tool window (dockable, click-to-navigate, CSV export) | "VS Error List integration" | ❌ **Absent** — Grep finds no `IssuesToolWindow`, `AllIssuesWindow`, `CodeAnalysisIssuesWindow` | Spec 014 US6, FR-035..040. |
| Full `Ctrl+B` refactoring chord family (8 chords) | "21 format commands" + chord list (Y/U/C/W/Q already present) | ⚠️ **Partial** — `Ctrl+B,Ctrl+Y/U/C/W/Q` shipped; `Ctrl+B,Ctrl+B` (brackets toggle), `Ctrl+B,Ctrl+I` (inline proc), `Ctrl+B,Ctrl+E` (encapsulate) still need binding | Spec 014 US7, FR-028..030. |
| Object Definition Box (Summary + Script tabs adjacent to completion) | "Object Definition Panel (Summary/Script tabs via QuickInfo IPC)" | ⚠️ **File exists** — `src/AkmlSql.Shell.Shared/Editor/Completion/ObjectDefinitionPanel.cs` present. Resizable behaviour, persistence of size, and `Ctrl`-transparency for the panel itself unverified | Spec 014 US8, FR-020..024. Needs functional audit before declaring done. |
| Inline `-- akml-format off / on` markers (editor action that inserts them) | "Formatting Region Directives: `-- noformat`, `-- AKML formatting off/on`, `-- SQL Prompt formatting off/on`" | ⚠️ **Scanner present, UI action absent** — `NoformatScanner` accepts the markers; no Actions-List entry inserts them around a selection | Spec 014 US9, FR-031..034. |
| AI keyboard shortcuts (`Alt+Z`, `Shift+Alt+R`, `Ctrl+Alt+Z`, `Ctrl+Alt+↑`) | "AI Chat Panel" command exists | ❌ **Absent** — Grep finds no `cmdidAiPanel`, no `AltZ` chord, no `AiKeyboardShortcut` registrations | Spec 014 US10, FR-053..057. |
| Dual-instance awareness in completion (per-text-view connection, no `ActiveDocument` fallback) | Not explicitly mentioned | ⚠️ **Partial fix on 2026-04-09** — per-text-view file-path lookup landed in `SsmsConnectionDetector`. Needs regression test (spec 014 US11) and documentation. | Spec 014 US11, FR-025..027. |
| Smart Rename with DB-wide dependency preview | "Safe Rename — cross-script rename, generates ALTER scripts" | ❌ **Absent** — `SafeRename` covers in-document only. No `SmartRenameDialog`, no `DependencyPreview`. | Spec 014 US15, FR-069..073. |
| Find Invalid Objects (DB-wide broken-reference scan + tool window) | Not mentioned | ⚠️ **DTOs only** — `FindInvalidObjectsRequest/Response/Record.cs` in `Core/Ipc/Messages/`; no engine handler, no tool window | Spec 014 US14, FR-065..068. |
| Result-grid Copy as IN Clause / Script as INSERT / Open in Excel | "Copy as IN clause" + "Script Generator (INSERT/UPDATE/DELETE from selected rows)" listed in §9 of progress.md | ⚠️ **Partial** — Copy-as-IN exists; Script-as-INSERT exists; Open-in-Excel precision preservation needs verification against FR-077 | Spec 014 US16, FR-074..078. |
| Code Analysis lightbulb quick-fixes + Issue Details popup | "Lightbulb quick-fix suggestions with contextual refactoring actions" | ⚠️ **Skeleton** — gutter lightbulb infrastructure exists but Issue Details popup with rule-id / problem / remediation / Apply Fix not wired | Spec 014 US17, FR-079..083. |
| AI Explain / Index Analysis / Comment-to-SQL / Auto-fix-on-error / Panel history / Editor selection icon | "AI Explain (query explanation in plain English)", "AI Index Analysis", etc., listed under §13 | ⚠️ **IPC types exist, surfaces missing** — `AiExplainRequest/Response`, `AiIndexAnalysisRequest/Response` exist; editor-selection-icon, auto-fix-on-error toast, comment-to-SQL Tab trigger, and panel-history tab not present | Spec 014 US18, FR-084..091. |
| Completion polish (8 items: `Ctrl+Shift+P` toggle, `Ctrl+Shift+D` refresh, custom commit keys, category cycle, `MS_Description`, parameter highlight, encrypted decryption, temp-table IntelliSense, template config) | Not explicitly itemised | ❌ **All 8 sub-items unverified or absent** | Spec 014 US19, FR-092..100. |
| Execute Current Batch (`Alt+Shift+F5`) + Execute To Cursor (`Ctrl+Shift+F5`) | Not mentioned for these specific shortcuts | ❌ **Absent** — no `cmdidExecuteCurrentBatch` / `cmdidExecuteToCursor` anywhere in `src/` | Spec 014 US20, FR-101..103. |
| Browse Open Tabs (`Ctrl+Q` popup) | Not mentioned | ❌ **Setting key only** — `AppSettings.Navigation.BrowseOpenTabsShortcut` exists; no popup implementation | Spec 014 US20, FR-105. |
| F1 contextual help across every UI surface | Not mentioned | ⚠️ **Skeleton present** — `Help/F1HelpListener.cs` created in spec 014 Phase 2 (T020); no per-surface registrations beyond US1 yet | Spec 014 FR-104. |

**Conclusion:** of the 19 features spot-checked, **3 are present (US1 safety, US5 tab coloring core, ObjectDefinitionPanel file)**, **9 are partial (engine or DTOs present, shell wiring or UI missing)**, and **7 are wholly absent**. The "100% parity" claim is overstated by ~50–60%.

### 3.2 Master-branch deliveries since 2026-04-11 (post-`progress.md`)

The dev log stopped updating after Phase 3b. Since then the following has merged to `master` (commits in chronological order):

| Commit | Date | Scope |
|---|---|---|
| `db194e9` | 2026-04-11 | Spec 014 Phase 3b — SafetyWarningDialog WPF rewrite, SchemaProgressMargin arc spinner |
| `d7069d5` | 2026-04-12 | Spec 014 US5 — environment-based tab coloring core (rules editor in Settings) |
| `4228300` | 2026-04-12 | Split JOIN assist from AutoAlias; add `JoinOnFkProvider` for ON-clause completion |
| `55cf19b` | 2026-04-13 | Code-review cleanup: cache `ConfigManager.Load`, extract `FkHelpers`, remove duplication |
| `b48c249` | 2026-04-13 | **Merge PR #229 — spec 014 to master** |
| `ec09c45` | 2026-04-15 | **Spec 015 — 13 of 14 user stories shipped** (US14 deferred). UPDATE/ALTER TABLE completion, Analysis button wiring, search "no connection" fix, DROP TABLE safety, history star badge sync, Advanced Search CamelCase, schema progress to bottom-right toast, Options dark-theme text, query rename, Document Outline, installer desktop-shortcut removed, dynamic `1.YY.MMDDHHmm` version, AI inline help. |
| `4b0aec4` | 2026-04-18 | Spec 015 regression fixes — VSIX `$version$` substitution; 4-part `1.YY.MMDD.HHmm` per VSIX ushort limit; adornment anchor via `SetLeft/SetTop`; ExecutionCommandFilter caches `Query.Execute` GUID for SSMS 22. |
| `5e1b0f8` | 2026-04-30 | Spec 016 Phase 4 batch 1 — first 5 WPF surfaces migrated to ThemeTokens (HistoryDiffWindow, SafetyWarningDialog, ProfileEditorDialog, DocumentOutlineControl + legacy SettingsDialog deletion) |
| `2ac0407` | 2026-05-?? | Spec 016 T046 — `SchemaProgressMargin` to ThemeTokens + reduce-motion |
| `7b8bd53` | 2026-05-?? | Fix `Ctrl+Shift+D` refresh shortcut and bottom-right schema progress toast |
| `f138024` | 2026-05-05 | Codebase audit (the document this PRD reconciles against) |
| `5efe39a` … `3ec5755` | 2026-05-06 → 2026-05-13 | Options Dialog Redgate-parity Phase 1 + Phase 2 (5 new sub-pages, 19 per-file builders, post-PR review fixes) on branches `017-options-dialog-phase1` then `018-options-dialog-phase2` |

This means the only **unmerged** SQL Prompt parity work as of 2026-05-13 is on branch `018-options-dialog-phase2`: the Phase 2 Options dialog page-split (already complete, awaiting PR merge) and Phase 3 (plan committed, not yet implemented).

### 3.1 Documentation hygiene defects to fix as part of Phase 10

- **`doc/progress.md`** — last-updated stamp 2026-04-03, includes the unverified "100% parity" assertion. Action: replace the "Gap Analysis vs SQL Prompt v11 (2026-04-03)" table with a pointer to this PRD's §3.
- **`CLAUDE.md`** — "Active branch: `014-sql-prompt-parity`" is stale; current is `018-options-dialog-phase2`. Action: update to reflect actual branch and active spec.
- **`doc/AKML_SQL_Gap_Analysis_1.md`** (2026-04-02) — same "absolute 100% parity" conclusion as `progress.md`. Action: add a "Superseded by Phase 10 PRD §3" header banner; keep the file for historical reference.
- **`doc/bugs.md`** (March 2026) — every one of 37 bugs marked fixed. Action: append a note that this file is closed; live bugs now tracked in spec 015 + codebase-audit § 1.

---

## 4. Gap Features (Authoritative List)

Each row references the owning spec/user-story so the PRD does not duplicate requirement text. **For functional requirements always read the cited FR; for tasks always read the cited `tasks.md` line; for tests always read the cited test file.** When a row says "engine present, shell missing", the work is purely the shell-side wiring on top of an existing handler.

### 4.1 Safety (P1)

| # | Feature | Spec ref | State | Notes |
|---|---|---|---|---|
| F-S1 | Pre-execution safety warning dialog (DELETE/UPDATE/MERGE without WHERE, in JOIN, in proc/trigger body) | Spec 014 US1, FR-001..009; tasks T021–T035 | ⚠️ Implementation landed (commit `f337729`); Phase 3b UI polish uncommitted | **Action: commit Phase 3b (SafetyWarningDialog WPF rewrite, `EnvironmentSeverity=Disabled` regression fix, SchemaProgressMargin arc spinner)**. Then close US1. |

### 4.2 Completion UX (P2, P3)

| # | Feature | Spec ref | State | Engine present? |
|---|---|---|---|---|
| F-C1 | Column Picker inside completion popup (`Ctrl+Left`, PK/FK badges, multi-select, alias-qualified insert) | Spec 014 US2, FR-010..016; tasks T045–T052 | ❌ Absent | Phase A/B schema cache provides PK/FK metadata. Shell-only UI work. |
| F-C2 | Wildcard `*` + Tab inline expansion | Spec 014 US3, FR-017..019; tasks T053–T056 | ❌ Absent | `WildcardExpansionHandler` ships; only `TabWildcardExpansionFilter` `IOleCommandTarget` missing. |
| F-C3 | Object Definition Box (Summary + Script tabs, resize-persist, `Ctrl` transparency) | Spec 014 US8, FR-020..024 | ⚠️ File exists, behaviour unverified | `QuickInfoProvider` engine path already returns object metadata. Audit and complete. |
| F-C4 | Completion polish — 9 sub-items: `Ctrl+Shift+P` toggle, `Ctrl+Shift+D` refresh, custom commit keys, category cycle (`Ctrl+Up/Down`), `MS_Description` tooltips, parameter highlight, encrypted decryption, customizable `ALTER`/`INSERT` templates, temp-table IntelliSense | Spec 014 US19, FR-092..100; tasks T186–T201 | ⚠️ `Ctrl+Shift+D` shipped (commit `7b8bd53`); other 8 sub-items unverified or absent | `CompletionPolishSettings` POCO ships (per spec 014 Phase 2). Engine + shell work for each remaining sub-item. |
| F-C5 | UPDATE SET and ALTER TABLE column completion | Spec 015 US1, FR-001..003 | ✅ **Shipped** — `ec09c45`, fix in `CursorContextAnalyzer.cs` | Historical record only; no remaining work. |
| F-C6 | Dual-instance awareness (per-text-view connection, no `ActiveDocument` fallback) | Spec 014 US11, FR-025..027 | ⚠️ Partial fix on 2026-04-09 | Needs regression test (T138–T140) and `progress.md`-level documentation. |

### 4.3 Refactoring (P2, P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-R1 | Full `Ctrl+B` refactoring chord family — add `Ctrl+B,Ctrl+B` (Brackets toggle), `Ctrl+B,Ctrl+I` (Inline Stored Procedure), `Ctrl+B,Ctrl+E` (Encapsulate as Stored Procedure) | Spec 014 US7, FR-028..030; tasks T107–T118 | ⚠️ 5 of 8 chords shipped |
| F-R2 | Smart Rename — DB-wide dependency preview with Actions/Warnings/Dependencies tabs, transactional apply, FK + extended-property + permission preservation | Spec 014 US15, FR-069..073; tasks T147–T160 | ❌ Absent (current `SafeRename` is document-scope only) |

### 4.4 Navigation & Discovery (P2, P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-N1 | Script navigation chords — `Ctrl+B,Ctrl+S` Summarize Script outline, `F12` Script Object as ALTER, `Ctrl+F12` Select in Object Explorer, `Ctrl+B,Ctrl+F` Find Unused Variables/Parameters | Spec 014 US13, FR-061..064; tasks T076–T088 | ❌ Absent |
| F-N2 | Find Invalid Objects — DB-wide broken-reference scan, dockable tool window, Script as ALTER per row, multi-row selection | Spec 014 US14, FR-065..068; tasks T089–T097 | ⚠️ DTOs only |
| F-N3 | Browse Open Tabs popup (`Ctrl+Q`) across all SSMS/VS windows for active host | Spec 014 US20, FR-105 | ❌ Absent (setting key only) |
| F-N4 | F1 contextual help on every AKML SQL UI surface | Spec 014 FR-104 | ⚠️ Listener skeleton only |
| F-N5 | Document Outline shows SQL structure | Spec 015 US10, FR-019..021 | ✅ **Shipped** — `DocumentOutlineCommand` wired via `IVsEditorAdaptersFactoryService` fallback, Refresh button + empty-state |

### 4.5 Code Analysis (P2)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-A1 | Code Analysis Issues window — dockable, columns (rule id, severity, description, line, column), click-to-navigate, sort, group, CSV export, docked-position persistence | Spec 014 US6, FR-035..040; tasks T068–T075 | ❌ Absent |
| F-A2 | Lightbulb quick-fixes + Issue Details popup — orange (fixable) / blue (advisory), `Ctrl`-hover popup with rule id + remediation + Apply Fix, Disable-Rule action (inline `-- akml-disable` or `.casettings`), Phase-B-queued fix for schema-dependent rules | Spec 014 US17, FR-079..083; tasks T098–T106 | ⚠️ Squiggles + bare lightbulb present; popup + Apply Fix absent |
| F-A3 | Analysis toolbar button produces visible results and logs | Spec 015 US2, FR-004..005 | ✅ **Shipped** — `AnalysisController` wired to `ErrorListReporter` with Debug logging in `ec09c45` |

### 4.6 Tab Coloring (P2)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-T1 | Right-click query tab → Tab Color (Server / Database / Server Group) submenus | Spec 014 US5 FR-041 | ❌ Absent (no `TabContextMenuExtender`) |
| F-T2 | Options → Tabs → Color: edit environments (add/remove/rename, color picker, gradient toggle) | Spec 014 US5 FR-043 | ✅ **Shipped** — inline rules editor (Label/Pattern/Color) in Settings → Tabs page (`d7069d5`) |
| F-T3 | Live re-render on assignment change (no restart) | Spec 014 US5 FR-042 | ✅ **Shipped** — `RepaintAllTabs` invoked from both Settings and OptionsCommand save paths |
| F-T4 | Inheritance: Server Group → Server → Database resolution with WCAG-AA high-contrast clamp | Spec 014 US5 FR-045..046 | ⚠️ Priority-resolution shipped via `EnvironmentMatcher` (29 tests); WCAG-AA clamp unverified |

### 4.7 Command Palette (P2)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-CP1 | Aggregate four sources: AKML SQL commands, AKML SQL options, SSMS/VS host commands, (SSMS) DB objects | Spec 014 US4 FR-048; tasks T057–T067 | ⚠️ AKML-commands only today |
| F-CP2 | Most-recent items per host (10 entries), shown first when search box is empty | Spec 014 US4 FR-052 | ❌ Absent |
| F-CP3 | Selecting an Options result scrolls Settings dialog to the matching control | Spec 014 US4 FR-050 | ❌ Absent |

### 4.8 Formatting Ergonomics (P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-F1 | Editor action "Disable formatting for selected text" — wraps selection in `-- akml-format off / on` markers | Spec 014 US9 FR-031; tasks T127–T130 | ❌ Action absent (scanner exists) |
| F-F2 | Format Styles editor — multi-style (built-ins + user styles), Create/Copy/Rename/Delete/Import/Export CRUD, 3-column layout, switching-while-dirty prompt | Options Dialog Phase 3 (`docs/superpowers/plans/2026-05-08-options-dialog-phase3.md`) | ❌ Not started |
| F-F3 | Three new built-in styles: `aligned.akmlstyle`, `verbose.akmlstyle`, `redgate-compatible.akmlstyle` | Options Dialog Phase 3 | ❌ Not started |
| F-F4 | Redgate `.sqlpromptstylev2` importer polish — `ImportWarning` records, post-import dialog with translated / unsupported counts | Options Dialog Phase 3 | ⚠️ Importer exists, warnings UI missing |

### 4.9 AI Features (P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-AI1 | Keyboard shortcuts — `Alt+Z` open AI panel, `Shift+Alt+R` Fix, `Ctrl+Alt+Z` Optimize, `Ctrl+Alt+↑` manual ghost-text | Spec 014 US10 FR-053..057; tasks T131–T137 | ❌ Absent |
| F-AI2 | Explain SQL (right-click selection, AKML SQL menu, Command Palette) | Spec 014 US18 FR-084 | ⚠️ Engine handler exists (`AiExplainRequest`); shell surface missing |
| F-AI3 | Query Index Analysis (ML-based, existing-vs-hinted plan, copyable `CREATE INDEX`) | Spec 014 US18 FR-085 | ⚠️ Engine handler exists; shell surface missing |
| F-AI4 | Auto-fix-on-error toast after failed execution | Spec 014 US18 FR-086 | ❌ Absent |
| F-AI5 | Comment-to-SQL — `-- generate: <NL>` + Tab triggers AI | Spec 014 US18 FR-087 | ❌ Absent |
| F-AI6 | AI panel History tab with revert-to-state action | Spec 014 US18 FR-088 | ❌ Absent |
| F-AI7 | Editor-selection AI icon with Explain / Fix / Optimize hover actions | Spec 014 US18 FR-089 | ❌ Absent |
| F-AI8 | Follow-up prompt buttons (1–3 per answer) | Spec 014 US18 FR-090 | ❌ Absent |
| F-AI9 | AI Assistance inline help text + DPAPI-only key storage | Spec 015 US13 FR-030..032 | ✅ **Shipped** — `CredentialManager.cs` (`dpapi:` + Base64 + per-user + app entropy); AI Assistance Options page has inline help text for Claude/Gemini |

### 4.10 Result-Grid Productivity (P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-G1 | Copy as IN Clause — proper quoting per type, NULL omission with status message | Spec 014 US16 FR-074..075 | ⚠️ Present; behaviour matches FR but NULL-omission status message unverified |
| F-G2 | Script as INSERT with `SET IDENTITY_INSERT ON/OFF` opt-in | Spec 014 US16 FR-076 | ⚠️ Present; IDENTITY toggle unverified |
| F-G3 | Open in Excel with full numeric precision (text-format for >15-digit values) | Spec 014 US16 FR-077 | ⚠️ Settings flag exists; export path verification needed against FR-077 |

### 4.11 Execution Shortcuts (P3)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-X1 | Execute Current Batch (`Alt+Shift+F5`) — batch between surrounding `GO` markers | Spec 014 US20 FR-101 | ❌ Absent |
| F-X2 | Execute To Cursor (`Ctrl+Shift+F5`) — from start-of-batch to line above cursor | Spec 014 US20 FR-102 | ❌ Absent |
| F-X3 | Both must trigger US1 safety check on the about-to-run text | Spec 014 US20 FR-103 | Blocked by F-X1/F-X2 |

### 4.12 Settings Surface (P3, cross-cutting)

| # | Feature | Spec ref | State |
|---|---|---|---|
| F-S1 | Options entry + description for every feature added by spec 014 / spec 015 | Spec 014 US12 FR-058 | ⚠️ Per-page additions for spec 014 Phase 2 done; remainder follow each user story landing |
| F-S2 | Options search box finds every new feature by display label and description | Spec 014 US12 FR-059 | ⚠️ Search index needs extension as new pages land |
| F-S3 | Toggling a feature off takes effect within 1 s without restart | Spec 014 US12 FR-060 | ⚠️ Existing `ConfigManager.SettingsChanged` event covers most; per-feature verification |

---

## 5. Open Bugs

### 5.A Code-level TODOs (`doc/codebase-audit-2026-05-05.md`)

The audit found **14 real TODOs** (no HACK, no FIXME). All are still live as of 2026-05-13.

**P0 — Visible feature gap**

| # | File:line | Description | Effort |
|---|---|---|---|
| BUG-A1 | `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs:51` | Skeleton — `SignatureRequest` IPC never sent; signature help silently dead | M |
| BUG-A2 | `src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs:73` | Skeleton — `QuickInfoRequest` IPC never sent | M |
| BUG-A3 | `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs:66` | "Best match selection based on active parameter" — depends on BUG-A1 | S |

**Action for BUG-A1..A3**: pick one direction this milestone — either wire them via `PipeRpcClient` (pattern in `CompletionController` for `MessageTypes.Completion*`) **or** delete the skeleton classes and their MEF exports. Half-implemented features are worse than missing ones.

**P1 — Quality-of-life integrations**

| # | File:line | Description | Effort |
|---|---|---|---|
| BUG-A4 | `Formatting/FormatOnSaveHandler.cs:47` | "Wire to formatter pipeline via engine IPC when available" — no-op today | M |
| BUG-A5 | `Formatting/FormatOnPasteHandler.cs:50` | Same (paste trigger) | M |
| BUG-A6 | `Formatting/FormatOnDelimiterHandler.cs:62` | Same (semicolon/closing-paren trigger) | M |
| BUG-A7 | `Productivity/CrudGenerationCommand.cs:71` | "Show a dialog to collect schema, table, and operation options" — uses word-at-caret heuristic today | S |

**Action for BUG-A4..A6**: extract one shared `FormatRequestDispatcher` and have each handler hook a different event. Three TODOs collapse into one.

**P2 — SSMS host-specific polish**

| # | File:line | Description | Effort |
|---|---|---|---|
| BUG-A8 | `Tabs/TabTooltipProvider.cs:129` | "SSMS-specific connection context retrieval" — falls back to caption parsing | M |
| BUG-A9 | `Tabs/TabTooltipProvider.cs:158` | "Walk the WPF visual tree to find the tab header" — richer hover positioning | L |
| BUG-A10 | `Tabs/TabColoringManager.cs:896, 904` | Same connection-context problem as A8 | M |
| BUG-A11 | `Productivity/Grid/GridAccessHelper.cs:18` | "SSMS 20 uses a different results pane class than 21/22" | S |

**Action for BUG-A8 / A10**: extract one `SsmsConnectionContextResolver` that both call.

**P3 — Cosmetic / placeholder values**

| # | File:line | Description | Effort |
|---|---|---|---|
| BUG-A12 | `Engine/Snippets/SnippetRequestHandler.cs:66` | `WasFormatted = false  // TODO: integrate format-on-expand` — DTO always false | S |
| BUG-A13 | `Engine/Snippets/SnippetRequestHandler.cs:95` | `UsageCount = 0  // TODO: integrate usage tracker` — DTO always 0 | S |
| BUG-A14 | `Installer/AkmlSqlSetup.iss:42` | "T096: On uninstall, restore native SSMS IntelliSense if AKML SQL disabled it" | S |

**Action for BUG-A12 / A13**: if no UI displays these fields, delete the fields. Keeping misleading values in the wire format is worse than not having them.

### 5.B Spec 015 — Multi-Area Bug Fixes (13 of 14 user stories SHIPPED in commit `ec09c45` + regression fix `4b0aec4`)

Spec 015 was created 2026-04-14, shipped 2026-04-15 (regression fix 2026-04-18). **Only BUG-B14 remains open.**

| # | Title | Priority | FR refs | Status |
|---|---|---|---|---|
| BUG-B1 | IntelliSense Autocomplete for UPDATE SET and ALTER TABLE | P1 | FR-001..003 | ✅ Shipped — fix in `CursorContextAnalyzer.cs` (SET detection scans past table name) |
| BUG-B2 | Analysis Button Produces Visible Results and Logs (currently silent) | P2 | FR-004..005 | ✅ Shipped — `AnalysisController` wired to `ErrorListReporter` with Debug logging |
| BUG-B3 | Search Uses Active Connection | P3 | FR-006..007 | ✅ Shipped — fix in `NavigationRequestHandler` |
| BUG-B4 | Delete Warning Triggers for `DROP TABLE` | P4 | FR-008..009 | ✅ Shipped — DROP TABLE safety dialog always on by default; suppression logs WARNING |
| BUG-B5 | Star Badge Count in SQL History stays in sync | P5 | FR-010..011 | ✅ Shipped |
| BUG-B6 | Advanced Search in SQL History returns results | P6 | FR-012..013 + FR-013a | ✅ Shipped — CamelCase token path no longer drops results silently |
| BUG-B7 | Schema Progress as Bottom-Right Notification Box | P7 | FR-016..018 | ✅ Shipped — `SchemaProgressMargin` now bottom-right adornment with arc spinner + fade |
| BUG-B8 | Options Dark Theme — readable dropdowns and hovered button labels | P8 | FR-022..023 | ✅ Shipped (also reinforced by spec 016 token migrations) |
| BUG-B9 | Query Rename in SQL History | P9 | FR-014..015 | ✅ Shipped — context-menu rename + placeholder for new queries |
| BUG-B10 | Document Outline Shows SQL Structure | P10 | FR-019..021 | ✅ Shipped — `DocumentOutlineCommand` wired via `IVsEditorAdaptersFactoryService` fallback; Refresh button + empty-state message |
| BUG-B11 | Installer: remove "Create desktop shortcut" option | P11 | FR-024..025 | ✅ Shipped |
| BUG-B12 | Version Scheme — `Major.YY.MMDDHHmm` | P12 | FR-026..027 | ✅ Shipped (regression-fixed to `1.YY.MMDD.HHmm` 4-part for VSIX ushort segment limit) |
| BUG-B13 | AI Assistance inline help + **DPAPI-only key storage** | P13 | FR-030..032 | ✅ Shipped — `src/AkmlSql.Engine/Ai/Security/CredentialManager.cs` with `dpapi:` prefix + Base64 + per-user scope + app entropy; consumed by `AiProviderFactory.DecryptApiKey` for every provider |
| BUG-B14 | Installer: Icon and Banner Design | P14 | FR-028..029 | 🚧 **Open** — assets in place; commit message says "branding deferred; comment added" |

**Net Spec 015 status**: 13 of 14 user stories shipped (92%). The DPAPI security gap I flagged in the first draft of this PRD is **not** a gap — verification on 2026-05-13 confirms keys are encrypted at rest with `DataProtectionScope.CurrentUser` plus `SHA256("AkmlSql-ApiKey-v1")` application entropy.

### 5.C `doc/bugs.md` — historical, all resolved

The March 2026 bug doc lists 37 bugs (7 critical, 10 high, 12 medium, 8 low), all marked fixed. Keep as historical reference; do not re-track.

---

## 6. Cross-Cutting Work

These are not single user-facing features but they unblock or interact with feature work above.

### 6.1 WPF Theme Refresh (Spec 016)

**Status**: Phase 1 (Setup) + Phase 2 (Foundational ThemeRegistry / HostThemeWatcher / ThemeTokens / ThemeAwareWindow / FocusVisualStyles) **complete** and merged. Phase 4 Batch 1 — 5 WPF surfaces migrated to ThemeTokens (`5e1b0f8`): `HistoryDiffWindow`, `SafetyWarningDialog`, `ProfileEditorDialog`, `DocumentOutlineControl`, legacy `SettingsDialog` deleted. Phase 4 Batch 2+ pending. Plus `SchemaProgressMargin` migration (`2ac0407`).

| Surface tier | Scope | Status |
|---|---|---|
| **Theme infrastructure** | `ThemeRegistry`, `ThemeTokens` (35 keys), `Typography`, `Spacing`, `ThemePalette`, `HostThemeWatcher`, `ThemeAwareWindow`/`UserControl`, `FocusVisualStyles.HighStakes`, legacy `ThemeManager` as `[Obsolete]` facade, static-audit script | ✅ Complete (T001–T016) |
| **Modal dialogs (13)** | `SettingsWindow` (primary), `AboutDialog`, `SafetyWarningDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `SnippetManagerDialog`, `BulkFormatProgressDialog`, `ProfileEditorDialog`, `TextToSqlInputDialog`, `CellEditDialog`, `HistoryDiffWindow` | ⚠️ Partial — `HistoryDiffWindow`, `SafetyWarningDialog`, `ProfileEditorDialog` migrated. **8 of remaining 10 are WinForms** (`AboutDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `BulkFormatProgressDialog`, `TextToSqlInputDialog`, `CellEditDialog`) — incompatible with WPF token system per spec 016 A-final; they stay on pre-refresh chrome unless ported to WPF in a follow-up spec. `SettingsWindow` (the primary target) and `SnippetManagerDialog` are still on legacy chrome and **must migrate in Phase 10**. |
| **Dockable tool windows (5)** | `HistoryToolWindow`, `AiChatToolWindow`, `DocumentOutlineToolWindow`, `ObjectSearchWindow`, `CommandPaletteWindow` | ⚠️ Partial — `DocumentOutlineControl` migrated. 4 remain. |
| **Editor adornments / margins (5)** | schema-progress margin, completion popup chrome, peek-definition control, analysis-finding tooltips, editor toolbar | ⚠️ `SchemaProgressMargin` migrated (`2ac0407`). 4 remain. |

**Cross-cutting risk**: the WinForms exclusion shrinks spec 016 SC-003's "zero hardcoded chrome hex" claim — already qualified by spec 016 A-final to "in WPF surfaces only". Phase 10 will not change this scope.

### 6.2 Options Dialog Phase 3 (`docs/superpowers/plans/2026-05-08-options-dialog-phase3.md`)

**Status**: Phase 1 (light-theme bug fix + tree restructure + chrome tests) and Phase 2 (5 new sub-pages + 19 per-file page builders + engine wiring) are **shipped on the current branch `018-options-dialog-phase2`** but **not yet merged to master** (HEAD `3ec5755`). Phase 3 plan committed 2026-05-08; **0 implementation commits**. Estimated ~4 engineering days at 1 FTE.

Blocks: Format Styles parity with SQL Prompt (see F-F2..F-F4), environment color editor (see F-T1 — the Phase 3 "Block D" sub-dialog is one path to closing the right-click submenu gap).

| Block | Scope | Effort |
|---|---|---|
| A | `ProfileEditorViewModel` extension + three new built-in styles | 0.5 d |
| B | 3-column Style Editor UI + Style file CRUD | 1.5 d |
| C | Redgate importer polish + warnings UI | 0.5 d |
| D | Environment color editor sub-dialog | 0.75 d |
| E | Format › Styles page slim + tests | 0.75 d |

### 6.3 Code-level Refactoring Opportunities (`codebase-audit-2026-05-05.md` §5)

Independent of features; do as part of the relevant feature milestones if they touch the same files.

| ROI | Target | Effort | When |
|---|---|---|---|
| ★ Highest | **PipeRpcServer dispatch table** — replace 55-case switch (lines 160-683) with `Dictionary<int, IMessageHandler>`; PipeRpcServer drops from 937 → ~250 lines | 1 d | Before adding new MessageTypes in M2 |
| ★ High | **AppSettings.cs split** — 19 nested classes / 961 lines → per-domain files | 0.5 d | Before adding new settings sections in M3 |
| Medium | **`CommandFactory.RegisterMenu` extraction** — collapses 40+ duplicate `Initialize(AsyncPackage/Package, …)` overloads | Half-day | Any time |
| Medium | **`SettingsWindow.cs` split** (3,196 lines) | 2 d | Concurrent with spec 016 work |
| Medium | **`HistoryToolWindowControl.cs` split** (2,201 lines) | 2 d | Concurrent with spec 015 BUG-B5/B6/B9 work |
| Medium | **`AiRequestHandler.cs` split** (1,892 lines) | 2 d | Before spec 014 US18 (AI feature reach) |
| Medium | **`CompletionController.cs` split** (1,466 lines) | 1.5 d | Concurrent with spec 014 US19 (completion polish) |

### 6.4 Documentation hygiene (see also §3.1)

| Doc | Action |
|---|---|
| `doc/progress.md` | Replace stale "100% parity" sections with pointer to this PRD §3. Keep the dev-history sections (PR/commit references). |
| `CLAUDE.md` | Update "Active branch" line. Update "Spec 014 Phase 3b" reference once that commit lands. |
| `doc/AKML_SQL_Gap_Analysis_1.md` | Add "Superseded by Phase 10 PRD §3" banner. |
| `doc/bugs.md` | Append "Closed — live bugs in spec 015 + codebase-audit § 1". |
| `specs/013-sqlprompt-parity-gaps/` | Add `tasks.md` OR formally mark "rolled into 014". Six of ten US already covered by 010–012. |

---

## 7. Prioritized Roadmap

The roadmap below is sequenced to (a) merge the in-flight Options Dialog Phase 2 work first, (b) ship the highest-frequency daily-use gaps next, (c) layer polish and AI on top, and (d) hold the WPF refresh continuation as a parallel track so it does not block feature milestones.

### Milestone M0 — Merge in-flight + regression verify (target: 0.5 week)

**Theme**: get the working tree onto master and verify the recently-shipped features still work end-to-end.

- Merge branch `018-options-dialog-phase2` to `master` (Options Dialog Phase 1 + Phase 2: light-theme bug fix, tree restructure, 5 new sub-pages, 19 per-file builders, chrome tests, post-PR review fixes).
- Regression-test spec 014 US1 (safety dialog), US5 (tab coloring rules editor), spec 015 13 user stories against their acceptance scenarios. Each failure becomes a bug ticket.
- Verify spec 016 ThemeRegistry / HostThemeWatcher live-switch across the 5 migrated surfaces in both Light and Dark themes.
- Documentation hygiene (§3.1, §6.4): update `progress.md`, `bugs.md`, `AKML_SQL_Gap_Analysis_1.md`, `CLAUDE.md`, and `specs/014-sql-prompt-parity/tasks.md` to reflect what actually shipped.

### Milestone M1 — Daily-use parity, batch 1 (target: 2 weeks; depends on M0)

**Theme**: the features SQL Prompt users miss the most in their first hour of AKML SQL — completion and analysis surfaces.

- F-C1 — Column Picker (Spec 014 US2)
- F-C2 — Wildcard `*` + Tab (Spec 014 US3)
- F-A1 — Code Analysis Issues window (Spec 014 US6)
- F-A2 — Lightbulb quick-fixes + Issue Details popup (Spec 014 US17)
- F-T1 — Right-click query-tab Tab Color submenus (Spec 014 US5 FR-041)
- F-T4 — WCAG-AA high-contrast clamp on environment colors
- BUG-B14 — installer icon + banner (Spec 015 US14 — the last open bug)
- 6.3 — refactor: `PipeRpcServer` dispatch table; `AppSettings.cs` split (one-time, blocks new sections in later milestones)

### Milestone M2 — Daily-use parity, batch 2 (target: 2 weeks; depends on M1)

**Theme**: the navigation and discovery gaps; finish Command Palette.

- F-CP1..3 — Command Palette 4-source aggregation, recent items, Options-result deep link (Spec 014 US4)
- F-N1 — Script navigation chords: Summarize, Script-as-ALTER, Select in OE, Find Unused (Spec 014 US13)
- F-N2 — Find Invalid Objects (Spec 014 US14) — DTOs already shipped; build handler + tool window
- F-N3 — Browse Open Tabs popup (`Ctrl+Q`, Spec 014 US20 FR-105)
- F-N4 — F1 help registrations across all AKML SQL UI surfaces (Spec 014 FR-104) — `F1HelpListener` skeleton already shipped
- F-G1..G3 — Result-grid productivity audit + finish (Spec 014 US16) — verify Copy as IN NULL-status, IDENTITY_INSERT toggle, Excel precision
- BUG-A1..A3 — decide SignatureHelp / QuickInfo direction; wire or delete
- BUG-A4..A6 — wire Format-on-Save/Paste/Delimiter or delete handlers (extract one shared `FormatRequestDispatcher`)

### Milestone M3 — Refactoring & execution shortcuts (target: 2 weeks)

**Theme**: keyboard-first ergonomics; database-wide refactoring.

- F-R1 — `Ctrl+B,Ctrl+B/I/E` chord additions (Spec 014 US7) — `Ctrl+B,Ctrl+Y/U/C/W/Q` already shipped
- F-R2 — Smart Rename with dependency preview (Spec 014 US15)
- F-X1..X3 — Execute Current Batch + Execute To Cursor (Spec 014 US20)
- F-C3 — Object Definition Box audit + completion of Summary/Script tabs + resize-persist + `Ctrl` transparency (Spec 014 US8)
- F-C4 — Completion polish 8 remaining sub-items (Spec 014 US19) — `Ctrl+Shift+D` already shipped
- F-C6 — Dual-instance awareness regression test (Spec 014 US11)
- F-F1 — Editor action "Disable formatting for selected text" (Spec 014 US9)

### Milestone M4 — WPF theme refresh continuation + Options Dialog Phase 3 (target: 2 weeks; concurrent w/ M3)

**Theme**: spec 016 finishing — migrate remaining ~15 WPF surfaces to ThemeTokens. Ship Options Dialog Phase 3.

- Spec 016 — migrate `SettingsWindow` (the primary target!), `SnippetManagerDialog`, `AiChatToolWindow`, `HistoryToolWindow`, `ObjectSearchWindow`, `CommandPaletteWindow`, completion popup chrome, peek-definition, analysis-finding tooltips, editor toolbar
- Options Dialog Phase 3 (Blocks A–E, ~4 days): 3-column Format Styles editor + three new built-in styles (`aligned.akmlstyle`, `verbose.akmlstyle`, `redgate-compatible.akmlstyle`) + Redgate `.sqlpromptstylev2` importer warnings UI + Environment Color Editor sub-dialog + Format › Styles page slim-to-dropdown

### Milestone M5 — AI feature reach + finishing (target: 2 weeks; depends on M2)

**Theme**: round out AI; close the last cosmetic / placeholder TODOs.

- F-AI1 — AI keyboard shortcuts: `Alt+Z`, `Shift+Alt+R`, `Ctrl+Alt+Z`, `Ctrl+Alt+↑` (Spec 014 US10)
- F-AI2..AI8 — Explain, Index Analysis, Auto-fix-on-error toast, Comment-to-SQL, Panel history tab, Editor selection icon, Follow-up buttons (Spec 014 US18)
- BUG-A12 / A13 — delete or wire `WasFormatted` / `UsageCount` DTO fields
- BUG-A14 — installer T096 restore native SSMS IntelliSense on uninstall
- BUG-A8 / A10 — `SsmsConnectionContextResolver` extraction
- BUG-A11 — `GridAccessHelper` SSMS 20 fallback
- 6.3 — `AiRequestHandler` / `CompletionController` / `SettingsWindow` / `HistoryToolWindowControl` splits if not already done

---

## 8. Success Criteria

Functional SC are inherited from each underlying spec. The criteria below are the **Phase 10 closure criteria** — what makes Phase 10 itself "done".

| ID | Criterion | Measurement |
|---|---|---|
| **SC10-01** | Every user story in specs 014, 015, 016, plus every code-level TODO in `codebase-audit-2026-05-05.md` § 1, is marked `[X]` in its respective `tasks.md` or deliberately moved to "out of scope (Phase 11)". | Cross-reference check at milestone boundaries. |
| **SC10-02** | A reviewer reading `doc/progress.md` and this PRD § 3 finds **no contradiction** between the two — every "shipped" claim in progress.md is supported by a code path (file + class). | Manual audit at the end of M5; the reconciliation table in § 3 reaches 0 rows in "❌ Absent" and 0 in "⚠️ Partial". |
| **SC10-03** | Engine + Core + Formatting + E2E test suites stay green at every milestone (Engine ≥ 867, Core ≥ 478, Formatting ≥ 458, E2E baseline). | CI on milestone close. |
| **SC10-04** | Build verification: each milestone hot-swaps cleanly into SSMS 22 via `bash hotswap-ssms22.sh`; the milestone's primary user story can be exercised manually. | Hot-swap smoke test recorded in `doc/progress.md` per milestone. |
| **SC10-05** | DPAPI key storage is verified (BUG-B13 — already shipped); **`config.json` contains only `dpapi:<base64>` blobs**, never plaintext, for any AI provider across the test corpus. | Static analysis grep on `%AppData%\AKML SQL\config.json` after a fresh AI setup. |
| **SC10-06** | F1 help opens documentation on 100% of AKML SQL surfaces. | Coverage walk-through against `F1HelpListener.Count`. |
| **SC10-07** | Spec 014 SC-001..SC-019 are met where the corresponding user story shipped. SC inheritance: any SC whose user story is in Phase 10's roadmap is in scope. | Per-user-story validation per acceptance scenarios. |
| **SC10-08** | Documentation hygiene actions in § 3.1 / § 6.4 are complete. | Reviewer reads `progress.md`, `CLAUDE.md`, `bugs.md`, `AKML_SQL_Gap_Analysis_1.md` and confirms each correctly points at this PRD or has a "closed" banner. |
| **SC10-09** | The reconciliation table § 3 is closed: every row shows ✅ or has a documented deferral. | Sign-off audit at end of M5. |

---

## 9. Out of Scope

Explicitly deferred from Phase 10 to a future "Phase 11" effort:

- **WinForms theme adapter / port to WPF** for the 8 WinForms dialogs identified in spec 016 (Assumptions A-final). These surfaces remain on pre-refresh chrome.
- **Redgate Platform integration** (cloud sync of snippets / styles / analysis rules). Spec 014 OOS, Spec 016 OOS.
- **Full Redgate `.sqlpromptoptionsettings` importer** (current scope is `.sqlpromptstylev2` only).
- **Multi-project `.akmlsettings` overrides** for per-project style selection.
- **Localization** (Phase 10 keeps English-only). Spec 016 OOS.
- **AI model self-hosting** (calling external providers only). Spec 014 OOS.
- **Azure Synapse / Microsoft Fabric / SQL 2025 preview dialect extensions**. Spec 014 OOS.
- **SQL History migration from older formats**. Spec 014 OOS.
- **Command Palette recent-items cross-machine sync**. Spec 014 OOS.
- **High Contrast as a first-class third theme** (Phase 10 ships safe-fallback only). Spec 016 Clarifications Q3.
- **The 4 large-file class splits** in `codebase-audit § 5.3..5.6` (SettingsWindow, HistoryToolWindowControl, AiRequestHandler, CompletionController) are *opportunistically* in scope where they collide with feature work — but committing to all four is deferred.

---

## 10. Dependencies & Constraints

- **All 6 host SDKs installed** on the build machine (SSMS 20/21/22, VS 2019/2022/2026). Shell projects MUST be built individually via MSBuild — see `CLAUDE.md` "Build Commands".
- **Engine published to bin** before installer build (`dotnet publish src/AkmlSql.Engine/...`). The engine is the source of truth for safety checks, schema metadata, refactoring, AI dispatch and analysis.
- **Spec 014 Phase 1+2 IPC scaffolding** (commits up to `fba63d6`) is the foundation. Three new `MessageType` ints (`90/190 FindInvalidObjects`, `91/191 FindUnusedVariables`, `92/192 EncryptedObjectDecryption`) are reserved; reuse pattern dictates **no new MessageType ints** for any spec-014 feature beyond these three.
- **Active config schema** is `AppSettings` in `Core/Config/AppSettings.cs` (now ~961 lines, 19 nested sections). Splitting this (6.3) is M2 work; until then keep additions in the existing file.
- **Test gate**: Engine ≥ 867, Core ≥ 526 (spec 015 added 5 unit tests for clause detection + column completions; spec 014 Phase 2 added 19) must hold for every PR. Known-flake `ConfigManagerTests.Load_WhenFileAbsent_CreatesDefaultsAndSavesFile` (1-in-3 flake, parallel test runner race) is documented in `doc/spec-014-progress.md` and not a regression.

---

## 11. Open Questions

The following choices are *not yet decided* and would each unblock or accelerate a milestone:

1. **Spec 013 disposition** — does the team merge its 4 remaining gaps (Options dialog colors, icon colors, formatting markers, installer features) into spec 014's `tasks.md`, or formally close it as superseded? *Recommended: close as superseded; six of ten US already absorbed by 010–012, and the remaining four are tracked here in F-F1 / F-T2 / BUG-B11 / BUG-B14.*
2. **BUG-A1..A3 direction** — wire `SignatureHelp` / `QuickInfo` IPC or delete the skeleton classes? *Recommended: wire; the engine providers exist (`SignatureProvider`, `QuickInfoProvider`) and per `bugs.md` BUG-005 the IPC was supposedly fixed in March 2026 — the shell-side wiring was the gap.*
3. **WinForms dialog port priority** — accept spec 016's exclusion (separate future spec) or bundle a WinForms theme adapter into M4? *Recommended: accept the exclusion; 8 WinForms ports is a 2-week effort that does not buy the user-visible "polished Options window" the original request asked for.*
4. **`SettingsWindow` migration timing** — the spec 016 primary target *itself* is not yet migrated to ThemeTokens despite the foundational infrastructure landing 2 weeks ago. Should this be elevated into M0 (alongside the branch merge) or stay in M4 with the rest of the surface migrations? *Recommended: keep in M4; M0 should not balloon, and SettingsWindow migration touches many files concurrent with Options Dialog Phase 3.*

---

## 12. Glossary of Source Citations Used in This PRD

| Citation form | Meaning |
|---|---|
| **Spec 014 US7** | User Story 7 in `specs/014-sql-prompt-parity/spec.md` |
| **Spec 014 FR-053** | Functional Requirement 53 in `specs/014-sql-prompt-parity/spec.md` |
| **Spec 014 tasks T021** | Task T021 in `specs/014-sql-prompt-parity/tasks.md` |
| **Spec 015 US1** | User Story 1 in `specs/015-bug-fixes-polish/spec.md` |
| **Spec 016 FR-001** | Functional Requirement 1 in `specs/016-wpf-theme-refresh/spec.md` |
| **BUG-A#** | TODO from `doc/codebase-audit-2026-05-05.md § 1` |
| **BUG-B#** | Bug from `specs/015-bug-fixes-polish/spec.md` |
| **Options Dialog Phase N** | Plan in `docs/superpowers/plans/2026-05-0N-options-dialog-phaseN.md` |
| **§N** | Section N of this PRD |

---

*End of Phase 10 PRD. Source-of-truth crawl date: 2026-05-13.*
