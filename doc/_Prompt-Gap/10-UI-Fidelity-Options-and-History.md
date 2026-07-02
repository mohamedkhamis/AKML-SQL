# AKML-SQL vs Redgate SQL Prompt 11 — Non-AI Scope Fit & UI Fidelity (Options + SQL History)

> **Provenance.** Generated 2026-07-02 by a 6-agent workflow (business-scope refresh + SQL History & Options UI-fidelity deep-dives, each adversarially verified, then synthesized). Anchored on the existing `doc/_Prompt-Gap` audit (re-audited 2026-06-23) and refreshed against branch `030-closure-followups`. **AI features excluded by request.** Parity bar = SSMS 22 / VS 2026 desktop. Reference image: `doc/_Prompt-Gap/SQL History Redgate.png` (Redgate's actual SQL History window). No Options reference screenshot was available.

> **Implementation update (2026-07-02).** The two **High**-priority items from §5 are built and verified (TDD; SSMS 22 extension compiles clean):
> - **SQL History open/closed folder toggles** — `HistoryViewModel.ToggleOpenFilter(bool)` (3-state, mutually-exclusive cycle; `ClearFilters` now resets `IsOpenFilter`) wired to two line-art folder toggle buttons in the toolbar with accent active-state. Tests: `HistoryOpenFilterToggleTests`. *(committed `80e93ff`)*
> - **Options "Inserted Code › Special characters" pane** — new `SpecialCharactersPage` consolidating bracket-identifier policy (moved from Qualification) + add-parentheses & auto-close (moved from IntelliSense); registered in all four places (builder dict / nav tree / page list / Reset switch). Tests: `SpecialCharactersPageTests`. *(committed `80e93ff`)*
>
> **Medium/Low batch (2026-07-02, UI surfaces; TDD; 34/34 Shell tests pass; SSMS 22 clean):**
> - **History — consistent absolute timestamps**: new testable `HistoryTimeFormat.Absolute` (culture short-date + `HH:mm`) applied to the preview header, version rows, and list rows (were three different formats). Test: `HistoryTimeFormatTests`.
> - **History — row label**: dropped the `server→database` suffix (`ServerLabelConverter` shows server only; DB stays in the right-pane metadata bar).
> - **History — empty / disconnected states**: `HistoryViewModel.IsDisconnected` + a centered overlay that shows "History unavailable — engine not connected" vs "No queries found" instead of a silent blank list. Test: `Search_WhenEngineNotConnected_FlagsIsDisconnected`.
> - **Options — nav grouping** (report §4 rec #2): Aliases moved under **Inserted Code**, Join conditions under **Suggestions** (SQL Prompt layout); "Miscellaneous › Main" renamed to **Application** (removes the false "Main" parity signal). Test: `OptionsNavStructureTests`.
>
> **Screenshot addendum.** Real SQL Prompt Options screenshots were captured from the Redgate docs and saved to this folder: `SQL Prompt Options - Special characters Redgate.png` and `SQL Prompt Options - bottom bar Redgate.png`. They **validate the new Special-characters pane** (title, Brackets group, add-parentheses, closing-characters, per-page "Restore Defaults") and the global chrome (Restore all defaults / Import… / Export… bottom-left). They also reveal three refinements SQL Prompt makes that AKML does not yet mirror (deferred follow-ups): (1) brackets is a single **checkbox** "Enclose identifiers within square brackets" — confirms AKML's `Always`/`Never` dropdown values are inert; (2) parentheses label is "…function **or data type**"; (3) auto-close is **five per-character checkboxes** (single quote / double quote / comment / parenthesis / square bracket), not one toggle — needs a settings-model + engine expansion.
>
> **Still open** (out of this UI-focused batch): the Special-characters refinements above; Options "Connections & memory" pane, clickable "?" help, ranked/transparency/definition-box toggles, settings-folder relocate; and all engine-deep (Formatting/Refactoring/IntelliSense) and edge-of-scope platform-breadth items.
>
> **Special-characters wiring batch (2026-07-02, third pass; TDD; 45/45 Shell tests; SSMS 22 clean):**
> - **Auto-close characters is now REAL behavior**, not an inert toggle: new unit-tested `AutoClosePairs` decision logic (word/quote guards, `/*`→`*/`, per-char gating) + `CompletionController` TYPECHAR glue with type-over via a tracking point. `SpecialCharacterSettings` gained SQL Prompt's five per-character toggles (screenshot defaults: single ✓ double ✗ comment ✓ paren ✗ square ✓); the pane shows master + five checkboxes with SQL Prompt's exact labels.
> - **Add-parentheses is now wired**: committing a Function completion appends `()` with the caret inside (guarded when parens already present); label matches SQL Prompt ("…when inserting a function or data type").
> - **Ctrl-transparency toggle added** (Suggestions › Behavior): the behavior already existed always-on in two places (`CompletionController.UpdatePopupCtrlTransparency`, `AkmlCompletionPopup.OnCtrlPollTick`) — both now honor `IntelliSense.CtrlTransparentPopups`; closes the §2.1 "transparent-on-Ctrl" ❌ row honestly.
> - **CORRECTION to this report**: the claim that BracketMode "Always/Never are a dead control" (§4 mapping + rec #3 + §5 backlog) was **wrong** — `CompletionHandler.cs:70` → `CompletionEngine.BracketMode` → `ObjectProvider.ApplyBrackets` implements all three modes. The dropdown is honest and a superset of SQL Prompt's checkbox; no action needed.
> - **Runtime caveat**: `AutoClosePairs` decisions and all Options round-trips are unit-tested, but the TYPECHAR buffer glue (type-over, caret-between-parens) needs an in-SSMS smoke test.

## 1. Executive summary

Within the non-AI scope, **AKML-SQL is a functional near-peer of Redgate SQL Prompt 11 on the SSMS 22 / VS 2026 desktop bar.** Five of the six core product areas (IntelliSense, Formatting, Refactoring/Actions, Code Analysis, Snippets) rate **"mostly" at parity**, SQL History rates **"at parity"** (the strongest area), and Options is a **functional superset** of SQL Prompt's non-AI Options tree. The headline finding of the prior parity audit — a "built but not wired" backlog — is essentially closed: the HEAD closure commit `6e26fe4` lit up analysis-lightbulb colouring, Command-Palette DB-object search, and in-IDE bulk formatting, and the still-staged working tree makes linked-server IntelliSense functional end-to-end and closes MERGE-statement WHEN layout. The remaining genuine deltas are **edge-of-scope platform breadth** (host coverage, ARM64/ADS/Fabric, SQL 2025 parsing, Entra-ID modes, Redgate-Platform cloud sharing) rather than missing SQL-editing capability. UI fidelity is high on both audited surfaces, with SQL History a polished, theme-correct near-clone (one genuinely missing filter control) and Options matching SQL Prompt's global chrome and content order (divergences are organizational, not gaps).

| Area | SQL Prompt non-AI scope | AKML fit | Key gap |
|------|-------------------------|----------|---------|
| Code Completion / IntelliSense | Context-aware suggestions, prefix/CamelCase/mid-string match, ranked order, column picker, auto-alias/qualify, JOIN-ON & GROUP-BY help, tooltips, temp-table & linked-server scope | **mostly** | No PK/FK column glyphs; ranked-suggestion usage-history & toggle absent; limited "other" object types |
| Formatting & Styles | Format SQL via editable/shareable styles across DML/DDL/CASE/CTE/JOIN, casing, lists, parens, wrapping, DECLARE/SET, comments; format-time actions; CLI/bulk; live preview | **mostly** | Comment alignment not applied by wired path; Expand/Qualify inert in batch/CLI (no schema cache); no pre-v8 style auto-import |
| Refactoring & Actions | Ctrl Actions list + object/DB refactors (Script-as-ALTER, Smart Rename, Split, Encapsulate, Inline, INSERT→UPDATE, grid script-as) | **mostly** | Encapsulate-as-proc still a stub; no F2/Shift+F2 rename keybind; Split doesn't rewrite dependents; Open-in-Excel no auto-launch |
| Code Analysis | 100+ categorized rules, wavy underlines, blue/orange lightbulbs, issue list, one-click auto-fix, per-rule severity, shareable `.casettings`, bulk | **mostly** | No Ctrl+Shift+A on/off; no in-product `.casettings` export; no Misc/Script-level categories |
| Snippets | Shortcode expansion, suggestions-box category w/ preview, Snippet Manager, create-from-selection, surround/wrap, full placeholder token set | **mostly** | Small built-in set (~6 desktop); no snippet preview pane; `$PASTE$`/`$SELECTIONSTART/END$`/`$USER$` gaps; no indent reflow |
| Tab Management & SQL History | Dockable history (content + versions), crash/startup restore, search, star/rename/remove/trim; tab coloring by server/db/env | **at-parity** | Version snapshots on execute/close (not per-keystroke); tab-color-by-server auto-only; env color is hex text, no picker |
| Options & Settings | Full Options tree with Import/Export + per-page/all restore-defaults | **mostly** | Organizational (Aliases/Join swapped; no dedicated Special-chars / Connections-&-memory panes); static help vs clickable "?" |
| Editions / Licensing / Platform | Hosts (SSMS 21/22 x64+ARM64, VS 2019/22, ADS, Fabric), Entra ID, tiers, Command Palette, Bulk Actions, Redgate Platform | **partial** | Host breadth (SSMS 22 x64 + VS 2026 only); no ADS/Fabric/ARM64; SQL 2022 parser (no 2025); no Platform cloud sharing |

---

## 2. Business / feature scope fit (non-AI)

**Branch-delta note.** The `git log --since=2026-06-23` window is slightly wider than the true delta — it includes the audit-refresh commit `2096f53` itself — so genuine post-audit movement is everything above `2096f53` plus the still-staged working tree. Post-audit closure movement: (a) a large PR #247 review-remediation wave (`5c6977f` engine/library 13 bugs, `965ac20` web, `c9e2d59` shell, `ae8e214`, `c3e2fd8`); (b) parity feature batches `2162a2e`, `5d3154d` (DECLARE/SET + comments + snippet pack), `0a283ac` (schema-aware Expand/Qualify actions), `3579d50`, `e107bdf` (T009 paren hug); (c) SQL History work `97aef4d` (tool-window redesign), `22cace3` (web history), `8621512` (deterministic dedup), `1a917d3` (uninstaller stops wiping history); (d) HEAD closure `6e26fe4` lighting up analysis lightbulb colouring (T054/FR-027), Command-Palette DB-object search (T086/FR-045), and in-IDE bulk formatting (T087/FR-046). The still-staged working tree adds two code-verified closures: linked-server IntelliSense end-to-end and MERGE WHEN layout.

**Code Completion / IntelliSense — mostly.** `CompletionEngine` merges keywords + schema + snippets + functions; `QuickInfoSource`/`SignatureHelpSource` are wired to engine IPC (T025/T026); `TempTableTracker → CompletionEngine.AvailableTempTables` (T029); category grouping + owner-name toggle (T034); all-columns-after-SELECT (T032). **Delta:** linked-server suggestions moved from "threaded but inert" to fully functional — `SchemaMetadataService.cs:454 LoadLinkedServersAsync` queries `sys.servers WHERE is_linked=1` in Phase A → `DatabaseCache.LinkedServers` → `ObjectProvider.cs:300-331 ToLinkedServerItem` (gated by `IncludeLinkedServers`) surfaces four-part-name completions; `+LinkedServerCompletionTests`. Residual: no PK/FK glyph icons; ranked suggestions have no usage-history and no toggle; `Ctrl+Shift+P` on/off inert; "other suggestions" limited to synonyms/schemas/system-procs/variables; cross-DB three-part qualify-on-insert absent.

**Formatting & Styles — mostly.** The audit's "built but not wired" headline is **resolved**: `FormatterPipeline.cs:29 LayoutRules = RuleEngine.DefaultOrder`, invoked at `ApplyLayoutRules` (lines 161, 235); `RuleEngine.cs:30-39 DefaultOrder` runs all 7 rule sets (Dml, Join, List, Parenthesis, Ddl, Declare, ControlFlow). Stage-8 format-time actions at `FormatterPipeline.cs:290-314`. **Delta:** MERGE WHEN layout closed — `DmlRules.cs` CASE-depth guard + `FormatterPipeline.cs:84 NormalizeMergeWhenLayout` + CasingEngine Merge keyword + 6 regenerated `12-merge-statement__*.sql` goldens + `MergeLayoutTests`. Two audit "🟡" rows are now stale (ParenthesisRules and DeclareRules/DECLARE-SET are both in DefaultOrder, hence wired). Residual: comment alignment preserved but not aligned by the wired path; Expand-wildcards/Qualify-names are live in the editor via the schema bridge but **inert in batch/CLI** (no schema cache — Stage 8 deliberately skips them); no pre-v8 `(old)`-suffix style auto-import.

**Refactoring & Actions — mostly.** `FormatRequestHandler.cs:198-206` dispatches Casing/Semicolons/Expand/Qualify/Brackets. `ScriptAsAlterCommand`, `SafeRenameCommand` (over `sys.sql_expression_dependencies`), `InlineStoredProcedureCommand`, `InlineExecCommand`, `InsertToUpdateCommand`, `FindInvalidObjectsCommand` all initialized in both packages; `LightbulbProvider.cs:113-144` offers Expand/Qualify/sp_executesql/BEGIN-END surround. **Delta:** no new refactor primitives on-branch (closure landed pre-audit); the lightbulb now surfaces contextual actions. Residual: Expand/Qualify return schema-stub in the batch path; Encapsulate-as-proc (`Ctrl+B,Ctrl+E`) is a placeholder stub (`ExtractToProcOperation` unreachable); rename has no F2/Shift+F2 keybind; Split-table doesn't auto-rewrite dependents; Select-in-Object-Explorer unimplemented (T077); Open-in-Excel writes XLSX but no auto-launch.

**Code Analysis — mostly.** `AnalysisEngine` + `RuleRegistry` auto-discovery (120+ rules / 8 categories); `DiagnosticTagger` green underlines; debounced 300 ms + on-open; `ManageRulesDialog` (T053); `AnalysisController.cs:99/131` live `.casettings` upward-search + `DisableRuleGloballyFixAction` (T051). **Delta:** lightbulb blue/orange distinction closed — `6e26fe4` adds `CodeIssueInfo.AutoFixable [Key(10)]` populated from `RuleMetadataCatalog`; `FixAction.cs` shows `KnownMonikers.IntellisenseLightBulb` (orange, auto-fixable) vs `StatusInformation` (blue, advisory); `LightbulbProvider.cs:95` passes `issue.AutoFixable`. Residual: no `Ctrl+Shift+A` toggle; no in-product `.casettings` export; no Misc/Script-level categories.

**Snippets — mostly** *(audit-sourced; not independently re-verified this pass).* `SnippetLoader` + `SnippetRequestHandler` (expand/list/save/delete); create-from-selection; surround via `$SELECTEDTEXT$`; `$SERVER$` from session connection; `$CURSOR$` caret; Snippet Manager search. **Delta:** built-in pack expanded in `5d3154d` (audit-adjacent); no snippet-specific on-branch movement. Residual: only ~6 built-in snippets desktop (11 web); no suggestions-box preview pane; `$PASTE$` only as `$CLIPBOARD$`; `$SELECTIONSTART/END$` ignored on Tab-expand; `$USER$` resolves OS not SQL user; no insert-at-indent reflow; shared-folder team snippets unwired in engine.

**Tab Management & SQL History — at-parity** (audit 18✅/3🟡/0❌, the strongest area). History window with per-query metadata, reopen, crash + startup restore, full-text search, star filter, rename/remove/remove-older-than, retention auto-trim; tab-color rules CRUD + environments + gradient + restore-defaults across SSMS + VS. **Delta:** tool-window redesigned for parity (`97aef4d`); deterministic dedup via `ROW_NUMBER()` so the query name is sticky across re-execution (`8621512`); uninstaller no longer wipes `%AppData%\AKML SQL` history on upgrade (`1a917d3`); web SQL History page added (`22cace3`) as a differentiator, not a parity target. Residual: version recording snapshots on execute/close/focus (not per-keystroke); tab-color-by-server is auto pattern-based (no right-click Tab Color menu); env color is hex text + preview, no visual picker.

**Options & Settings — mostly.** `SettingsWindow` + `Dialogs/Pages` idiom, 23 pages each with `IPageBuilder.Help` (T083/FR-044); Import/Export whole `AppSettings` JSON; per-page + Restore-All. **Delta:** `ConnectionScopePage`'s include-linked-servers toggle now backs a functional feature; otherwise the Options closure landed pre-audit. Residual: no "use ranked suggestions" / "transparent popups on Ctrl"; qualification is single-mode; settings-folder relocate read-only; no explicit object-definition-box toggle. *(See §4 for the full pane→page fidelity map.)*

**Editions / Licensing / Platform — partial.** `DbObjectProvider` (ObjectSearch IPC, palette DB-object search, T086) + `BulkFormatCommand` wired both hosts (`AkmlSqlPackage.cs:141` SSMS22 / `:136` VS2026, T087). `akmlsql-format` CLI; folder-scan bulk analysis; `AkmlSql.Updater`; View Logs. Licensing rows are ➖ (MIT/free, no tiers); Redgate-Platform cloud sharing is ➖. **Delta:** Command-Palette DB-object search closed (`DbObjectProvider` inserts schema-qualified name); in-IDE bulk formatting closed (`CmdBulkFormat 0x091D` → `BulkFormatWizard` → `BulkFormatRequest`, `6e26fe4`). Residual: host breadth (SSMS 22 x64 only; VS 2026 only; no SSMS 21 / ARM64 / Azure Data Studio / Fabric); menu is a top-level "AKML SQL" menu, not under Extensions; `TSql170` = SQL 2022 (no 2025 preview); Entra-ID reuse only (no dedicated app / interactive-MFA); palette shortcut is `Ctrl+Shift+P` not SQL Prompt's `Alt+S`/`Alt+P`.

---

## 3. SQL History — UI fidelity

Adversarial folding applied to the fidelity report: the "preview timestamps use relative time" assertion is **dropped** (self-contradicted — the header at `HistoryToolWindowControl.cs:1327-1331` is unconditionally `yyyy-MM-dd HH:mm:ss`, absolute); the list-row timestamp diagnosis is **reframed** from "relative-vs-absolute" to a date-**format** mismatch (for the aged rows in the reference, `RelativeTimeConverter` at lines 2116-2118 already returns absolute `yyyy-MM-dd HH:mm`); three missed image-visible gaps are **added** (group-header chevron style, version-row page glyph, top-right "..." overflow); and the version sub-panel is **down-rated match → partial**.

| Element | SQL Prompt | AKML | Verdict | Note (file:line) |
|---------|-----------|------|---------|------------------|
| Docked tab chrome (pin/close, gear + "...") | Dockable tab; gear/ellipsis top-right | Host-provided chrome | **match** (partial on top-right) | Chrome inherited (`:35`). Reference shows **both** a gear **and** a "..." overflow top-right; AKML exposes neither in-window — add an in-window gear→Options and consider the "..." affordance |
| Search box (rounded, magnifier, clear) | Rounded field, magnifier, "Search" | `searchBorder` CornerRadius 6 + line-art magnifier + clear-X | **match** | Shorten placeholder "Search SQL history…" → "Search" (`:191`) |
| "Recent queries" toolbar heading | Bold label | SemiBold TextBlock | **match** | `:276-284` |
| Refresh icon (circular arrow) | Reloads list | ~300° arc path → SearchCommand | **match** | `:236-241`, `:461-478` |
| Favourites star (filter to starred) | Filled-star toggle | `_favoritesStarButton` → FavoritesOnly, StatusWarning active | **match** | `:244-258`, `:291-296` |
| **Two folder icons = open / closed filter toggles** | Docs: folders filter open-only / closed-only queries | No control binds `IsOpenFilter`; reachable only via `is:`/`open:` search prefix | **missing** | VM plumbing exists (`HistoryViewModel.cs:136` decl, `:390` consume). Structural gap confirmed from image; the **semantic** "these two glyphs = open/closed toggles" rests on Redgate docs, not the image |
| Source / database cylinder dropdown | No equivalent in this strip | `BuildSourceIcon` stacked-cylinder → All/Servers/Databases menu | **extra** | `:261-372`, `:480-506` — occupies the slot where Redgate's folders sit; pair *alongside*, not *instead of*, the missing toggles |
| Date grouping + collapsible chevrons | Dated headers, down-chevron collapse | Grouped via `DateBucketConverter`; rotating-triangle ToggleButton | **partial** | Grouping matches, but chevron **style** diverges: AKML draws a bare rotating triangle (`BuildChevronTemplate :705-726`, `M 0,0 L 8,0 L 4,5 Z`) vs the reference's **circle-enclosed** glyph. Buckets also lack Yesterday/This-Month granularity |
| List-row far-left star | Hollow/filled, click toggles | `starText` + Favorite converters → ToggleFavoriteCommand | **match** | `:737-756`, `:2296-2322` |
| List-row filename (bold) | Bold filename line 1 | `nameText` SemiBold via QueryNameConverter | **match** | `:776-785`, `:2064-2077` |
| List-row date/time | Absolute `M/d/yyyy HH:mm`, every row | `RelativeTimeConverter` | **divergent** | For the aged rows shown in the reference AKML **already renders absolute** (`yyyy-MM-dd HH:mm`, `:2116-2118`); the real mismatch is **date format** (`yyyy-MM-dd` vs `M/d/yyyy`), not relative-vs-absolute. Relative strings only fire for <24 h / yesterday entries (not in the reference). Align to CurrentCulture short-date; keep relative as tooltip |
| List-row execution count ("Executed N times") | Not shown | `ExecCountConverter` italic, count>1 only | **extra** | `:837-864` — keep but demote visually |
| List-row server\instance (right) | server\instance only, no DB, no arrow | `ServerArrowDatabaseConverter` "server→database" | **partial** | `:793-808`, `:2080-2100` — drop the "→database" suffix (or move DB to tooltip) |
| List-row open/closed status dot | No per-row dot | `connDot ●` green=open/red=closed | **extra** | `:810-820` — keep for at-a-glance state, but it is **not** a substitute for the missing open/closed **filter** toggles |
| Row overflow / ellipsis menu | "…" appears on hover | Always-visible vertical "⋮" | **partial** | `:758-770` — reveal on hover, use horizontal "…" (low priority) |
| Selected-row highlight | Light-blue fill + subtle border | SurfaceSelection + 3px AccentPrimary left rail | **partial** | `:917-948` — thin/drop the left accent bar (Redgate is a plain fill) |
| Version / "History for &lt;file&gt;" sub-panel | Titled panel, page-icon + date version rows | Header relabel + version rows from GetVersions | **partial** (was match) | `:1012-1043`, `:1558-1656`. Three divergences: **no page glyph** on version rows (`:1610-1640` builds only 2 TextBlocks), `MMM dd, HH:mm` vs `M/d/yyyy HH:mm` (`:1607`), and AKML-added "vN (current)" labels the reference lacks |
| Right code-preview + syntax highlight | Read-only, keywords blue | `SqlPreviewTokenizer` → AccentPrimary/StatusSuccess/TextSecondary | **match** | `:1347-1445` — Redgate blue ≈ AKML AccentPrimary |
| Search-match highlight in preview | Subtle term highlight | `FindHighlightRegions` + HistoryMatchHighlight | **extra** | `:1359-1445` — at-parity-or-better |
| Grey header bar (filename / timestamp) | Solid medium-grey band | SurfaceElevated + bottom border | **partial** | `:1055-1087`. Header timestamp is **absolute** `yyyy-MM-dd HH:mm:ss` (`:1331`) vs Redgate `M/d/yyyy HH:mm`; verify SurfaceElevated reads as a distinct grey in light theme |
| Bottom metadata bar (● server · db \| vN of M) | No separate bar (just the Open button) | `metaBar` server + version label | **extra** | `:1089-1162` — keep but lighten so the pane's bottom edge reads as clean as Redgate |
| "Open" button (accent, bottom-right) | Dark-navy Open, new tab | `openButton` AccentPrimary → OpenInNewTabCommand | **match** | `:1096-1113`, `:1192-1218` |
| Context-menu / ellipsis actions | Open, Rename, Remove, Remove-older-than | Superset: Copy/Open/Re-execute/Rename/Favorite/Compare/Export/Delete/Remove-older | **match** | `:950-1006` — ensure Rename disabled while the query is open |
| Loading state | Unobtrusive indicator | "Loading…" status strip bound to IsLoading | **extra** | `:1224-1265` — a spinner (SchemaProgressMargin ellipse pattern) would read better than text |
| Empty state (no results) | Empty list, zero count | No dedicated placeholder; blank ListView + "0 entries found" | **partial** | `:1251-1262` — add centered "No queries found" + "Clear filters" hint |
| Disconnected / engine-not-ready | N/A (in-process) | Silent return + blank list when pipe down | **missing** | `SearchInternalAsync` bail `HistoryViewModel.cs:334-339`; `GetFullSql` bail `VM:609-610`; `LoadVersionHistory` bail is in `HistoryToolWindowControl.cs:1577-1578` (not VM:577-578). Add a "History unavailable — engine not connected (Retry)" banner |
| Source / server dropdown menu | Not present in this strip | `BuildSourceMenu` All/Servers/Databases | **extra** | `:299-372` — retain as value-add |
| Theming (light/dark/HC) | Follows SSMS theme | All brushes via `SetResourceReference` to ThemeTokens | **match** | `:92`, `:142-143`, `ThemeTokens.cs:109-116` — a strength, no hardcoded chrome hex |

**Low-confidence image detail (not a refutation):** rows 2–8 in the reference show a small mark at the right edge only on *server-less* rows, more plausibly a truncation/placeholder artifact than a status indicator; it does not contradict AKML's per-row `connDot` (which renders on every row).

### SQL History — prioritized recommendations

| # | Recommendation | Priority | Effort |
|---|----------------|----------|--------|
| 1 | **Wire the two open / closed folder-filter toggles.** `IsOpenFilter` already exists and is honoured by `SearchInternalAsync` (`VM:136,390`); add two toolbar toggle buttons cycling null→true→false→null and re-running SearchCommand — pure wiring + two glyphs; closes the single most visible functional gap | **High** | S |
| 2 | **Absolute timestamps (`M/d/yyyy HH:mm`) in list rows, version rows, and preview header.** Align list rows (`:826`), version rows (`:1607` `MMM dd, HH:mm`), and header (`:1331` `yyyy-MM-dd HH:mm:ss`) to CurrentCulture short-date; keep relative as tooltip | **Medium** | S |
| 3 | **Disconnected-engine + empty-result affordances.** Add a "History unavailable — engine not connected (Retry)" banner (distinct from a genuine empty result) and a "No queries found" placeholder | **Medium** | M |
| 4 | **Simplify list-row right label to server\instance only** — drop the "→database" suffix (`:2080-2100`) | **Low** | S |
| 5 | **Chevron + version-row glyph fidelity** — use a circle-enclosed chevron glyph (`:705-726`) and add a small page/file glyph on each version row (`:1610-1640`); tune the grey header band vs the reference | **Low** | S |
| 6 | **Reconcile AKML enrichments with Redgate minimalism** — demote the open/closed dot (`:810`), "Executed N times" (`:849`), and bottom metadata bar (`:1089-1162`) visually; do not let the dot stand in for the missing filter toggles | **Low** | S |

---

## 4. Options dialog — UI fidelity

> **Pixel-level parity is UNVERIFIED.** `referenceImageAvailable = false` — no SQL Prompt Options-window screenshot was found online or locally. Chrome spacing, fonts, icon glyphs, and row rhythm cannot be assessed at the pixel level; **a user-supplied SQL Prompt Options screenshot is required** for a true visual pass. The assessment below is structural / coverage parity, which is **strong**: AKML's Options tree is a functional superset of SQL Prompt's non-AI tree.

Adversarial folding applied: **Snippets removed** from the "AKML-only additions" claim (it maps to SP §10 folder-locations and is a core SQL Prompt feature — internal contradiction); the "every documented SQL Prompt pane has an AKML page" claim is **corrected** — it omitted SQL Prompt's Execution Warnings pane, which AKML covers via `SafetyPage` (now added as a mapped row); the §8 Query Results verdict is **flipped** from "narrower" to **broader**; and §2.1 is credited for the present "Decrypt encrypted objects" toggle.

### Pane → page mapping

| SQL Prompt pane / section | AKML page (file:line) | Fit | Note |
|---------------------------|-----------------------|-----|------|
| Global — Import / Export options | Bottom-bar buttons `SettingsWindow.cs:327-337`; serializer `:1446-1552` | **match** | Placement + "…" wording match |
| Global — Restore Defaults (per page) | Underlined link top-right `:1203-1214`; reset `:1586-1623` | **match** | — |
| Global — Restore All Defaults | Button bottom-left `:322-325`; confirm `:1625-1640` | **match** | — |
| Global — Per-page help ("?") | `IPageBuilder.Help` (`IPageBuilder.cs:29`) → static accent-bordered paragraph `:1228-1246` | **partial** | Coverage complete, but affordance differs — static paragraph vs SQL Prompt's clickable "?" popup with friendly-name links |
| Global — Dark theme | Dark/Light/System dropdown `GeneralPage.cs:18-22`; `PageTheme.cs:66-91` | **match** | AKML exceeds (adds System auto-detect) |
| §1 Main / Behavior | `IntelliSensePage.cs:19-49` (+ commit keys `:106-116`, tooltips `CompletionPolishPage.cs:23-31`); lands first (`:442`,`:210-215`) | **match** | Object-definition toggle absent (see §2.1) |
| §2.1 Suggestions ▸ Behavior | `IntelliSensePage.cs:19-121`; **Decrypt encrypted objects present** `CompletionPolishPage.cs:36-39` | **partial** | Missing "Use ranked suggestions", "transparent-on-Ctrl", object-definition on/off (per `08-Options-Settings-Reference.md:38-41`) |
| §2.2 Suggestions ▸ Types | `SuggestionTypesPage.cs:21-45` | **match** | Per-object-kind toggles coarser than SQL Prompt |
| §2.3 Suggestions ▸ Connections | `ConnectionScopePage.cs:26-42` | **partial → functional** | Linked-server toggle now backs a **functional** feature (audit comment `:41` is stale) |
| §2.4 Suggestions ▸ Join conditions | `JoinCompletionPage.cs:20` + master toggle `IntelliSensePage.cs:75-83`; nav under **Inserted Code** `:451-454` | **divergent** | SQL Prompt groups Join under **Suggestions** — grouping swapped |
| §3.1 Inserted code ▸ Objects & statements | `InsertStatementsPage.cs:23` (INSERT) + `:36` (EXEC) | **partial** | No ALTER-expansion group |
| §3.2 Inserted code ▸ Qualification | `QualificationPage.cs:25-46` | **partial** | Single-mode; no per-object-kind qualify scope |
| §3.3 Inserted code ▸ Aliases | `AliasesPage.cs` (`:28`/`:36`/`:45`); Display "Suggestions › Aliases" `:20`, nav under **Suggestions** `:442-448` | **divergent** | SQL Prompt groups Aliases under **Inserted code** — grouping swapped |
| §3.4 Inserted code ▸ Special characters | `SpecialCharactersPage.cs` (consolidated 2026-07-02: brackets + add-parens + per-char auto-close) | **match** (was divergent) | ~~Bracket Always/Never are a dead control~~ **CORRECTED**: BracketMode was always fully wired (`CompletionHandler.cs:70` → `ApplyBrackets`); auto-close + add-parens behaviors wired in the third pass |
| §4 Format ▸ Styles | `FormattingPage.cs:24-34` (+ triggers/safety `:37-76`); CRUD via editor + `:150-176` | **match** | Format-time actions remain profile-level (as designed) |
| §5 Tabs ▸ Color | `TabsPage.cs:18-105`; CRUD `:1696-1835` | **match** | Minor: Tabs nav-ordered after Queries (SQL Prompt places it before) |
| §6 Queries ▸ History | `HistoryPage.cs:17-56` (7 knobs) | **match** | Richer than SQL Prompt (retention + auto-trim) |
| **Queries ▸ Execution Warnings** | `SafetyPage.cs:8-13` (Display "Queries › Execution Warnings"); nav `:466` | **match** (added) | Production-server / DELETE-&-UPDATE-without-WHERE / DROP / TRUNCATE / open-transaction warnings `SafetyPage.cs:17-40+` — omitted from the original report's Queries enumeration |
| §8 Query Results | `GridPage.cs`: Excel Export `:34-39` **+ aggregate stats / NULL highlight / row numbers / freeze headers `:16-31`** | **match — broader** | Corrected: AKML is **broader** (5 toggles / 2 groups), not narrower |
| §9 Connections & memory | No dedicated pane — SQL-auth `IntelliSensePage.cs:124-140`; cache/memory `SchemaCachePage.cs:17-35` | **partial** | Split across two pages; connection coverage partial (SQL-auth + creds only) |
| §10 Settings & snippet folder locations | Snippet folders `SnippetsPage.cs:49`; settings/log paths **read-only** `GeneralPage.cs:37-42` | **partial** | No settings-folder relocate; no dedicated "folder locations" pane |
| §11 SQL Prompt Labs | `LabsPage.cs:20-44`; nav `:477-479` | **match** | — |
| §12 Sharing your settings | Export `:1453-1490` + team snippet folder `SnippetsPage.cs:49` | **partial** | File/team-folder sharing exists; no Redgate-Platform integration (out of AKML scope) |
| §7 Prompt AI | `AiAssistancePage.cs:11-13` | **excluded** | Out of scope for this audit (surfaced as a top-level leaf) |
| AKML extra — General / Application | `GeneralPage.cs:15-48` (theme/update/telemetry/paths/version) | **extra** | No SQL Prompt equivalent — rename the "Miscellaneous › Main" leaf (`:479`) so it stops masquerading as SQL Prompt's Main pane |
| AKML extra — Editor group | `SettingsWindow.cs:459-462`; `EditorPage`/`NavigationPage`/`RefactoringPage` | **extra** | AKML-specific editor surfaces |
| AKML extra — Queries ▸ Execution & Code Analysis | Execution `:464-468`; Code Analysis leaf `:473` | **extra** | SQL Prompt manages Code Analysis outside Options |
| AKML extra — Sidebar search box (Ctrl+E) | `BuildSearchBox :536-644`, results `:650-713`, index `:1065-1079` | **extra** | No SQL Prompt equivalent — genuine enhancement |

**Organization-parity notes.** Content **order** tracks SQL Prompt well (Suggestions ▸ Behavior first, then Inserted Code, Format, Queries). The real divergences are organizational, not gaps: (1) **Aliases and Join-conditions are swapped** vs SQL Prompt (AKML nests Aliases under Suggestions and Join under Inserted Code; SQL Prompt does the reverse); (2) **no dedicated "Special characters", "Connections & memory", or "Settings & snippet folder locations" panes** — those settings are folded into IntelliSense/Qualification/SchemaCache/General/Snippets; (3) per-page help is a static paragraph, not a clickable "?" dialog; (4) Tabs sits after Queries (SQL Prompt places it before) and an Editor group is inserted.

### Options — prioritized recommendations

| # | Recommendation | Priority | Effort |
|---|----------------|----------|--------|
| 1 | **Consolidate a dedicated "Inserted code ▸ Special characters" pane** — currently scattered across `IntelliSensePage.cs:92-102` (auto-close + add-parens) and `QualificationPage.cs:32-38` (brackets); biggest organizational divergence | **High** | M |
| 2 | **Un-swap Aliases and Join grouping** — nest Aliases under Inserted code and Join under Suggestions (nav-order + Display-string change: `AliasesPage.cs:20`, `SettingsWindow.cs:442-448`/`:451-454`) | **Medium** | S |
| 3 | ~~Wire or hide the inert Bracket "Always/Never" options~~ **RESOLVED as incorrect** — the engine has always honored all three modes (`CompletionHandler.cs:70` → `ObjectProvider.ApplyBrackets`); no change needed | ~~Medium~~ | — |
| 4 | **Rename "Miscellaneous ▸ Main" → "General"/"Application"** (`SettingsWindow.cs:479`) — removes the false parity signal vs SQL Prompt's Main/Behavior pane | **Medium** | S |
| 5 | **Provide (or clearly consolidate) a "Connections & memory" pane** — currently split between `IntelliSensePage.cs:124-140` and `SchemaCachePage.cs` | **Medium** | M |
| 6 | **Upgrade per-page help to a clickable "?" affordance** with friendly-name links (`SettingsWindow.cs:1228-1246`) | **Low** | M |
| 7 | **Add missing Suggestions ▸ Behavior toggles** (object-definition box, ranked, transparency) per `08-Options-Settings-Reference.md:38-41` | **Low** | S |
| 8 | **Enable relocating the settings folder** (`GeneralPage.cs:37-42` currently read-only) **and supply a SQL Prompt Options screenshot** for a pixel-level audit | **Low** | M |

---

## 5. Prioritized gap backlog (both surfaces + residual non-AI feature gaps)

| Item | Surface / Area | Priority | Effort | Why |
|------|----------------|----------|--------|-----|
| Wire the two open/closed folder-filter toggles | SQL History | **High** | S | Single most visible functional gap; VM plumbing (`IsOpenFilter`) already exists |
| Consolidate a "Special characters" pane | Options | **High** | M | Biggest organizational divergence vs SQL Prompt's single pane |
| Absolute `M/d/yyyy HH:mm` timestamps (rows/versions/header) | SQL History | **Medium** | S | Prominent format mismatch vs the reference |
| Disconnected-engine + empty-result affordances | SQL History | **Medium** | M | Out-of-process pipe-down silently shows a blank list — a failure mode Redgate lacks |
| Un-swap Aliases / Join grouping | Options | **Medium** | S | Realigns nav to SQL Prompt at near-zero cost |
| ~~Wire or hide inert Bracket Always/Never~~ **RESOLVED as incorrect** (BracketMode was always fully wired) | Options | — | — | Corrected in the third pass; see §4 |
| Rename "Miscellaneous ▸ Main" → General/Application | Options | **Medium** | S | Removes false parity signal |
| Provide/consolidate "Connections & memory" pane | Options | **Medium** | M | Improves discoverability of connection settings |
| Expand/Qualify inert in batch/CLI (no schema cache) | Formatting | **Medium** | M | Live-editor only; batch path returns schema-stub |
| Encapsulate-as-proc still a stub (`ExtractToProcOperation` unreachable) | Refactoring | **Medium** | M | Named SQL Prompt refactor with no reachable entry point / chord |
| No F2 / Shift+F2 rename keybinding | Refactoring | **Medium** | S | Context-menu-only vs SQL Prompt's keyboard-first rename |
| Simplify list-row right label (drop "→database") | SQL History | **Low** | S | Rows busier than the reference |
| Chevron circle-glyph + version-row page glyph + grey header tuning | SQL History | **Low** | S | Image-visible fidelity nits |
| Demote AKML-only enrichments (dot/exec-count/metadata bar) | SQL History | **Low** | S | Keep richness but restore Redgate minimalism |
| Clickable "?" help; ranked/transparency/definition-box toggles; settings-folder relocate | Options | **Low** | S–M | Fidelity uplift + small additive Behavior toggles |
| PK/FK column glyphs; ranked-suggestion usage-history + toggle; wider "other" object types | IntelliSense | **Low** | M | Metadata-list richness parity |
| Comment alignment on the wired path; pre-v8 `(old)` style auto-import | Formatting | **Low** | M | Layout completeness |
| No `Ctrl+Shift+A` analysis toggle; no in-product `.casettings` export | Code Analysis | **Low** | S | Convenience/sharing parity |
| Snippet count/preview pane; `$PASTE$`/`$SELECTIONSTART/END$`/`$USER$`; indent reflow; team-folder wiring | Snippets | **Low** | M | Starter-set + placeholder-token parity |
| Version snapshots on execute/close (not per-keystroke); tab-color right-click menu; visual color picker | SQL History/Tabs | **Low** | M | Behavioral richness parity |
| Host breadth (SSMS 21/ARM64, VS 2019/22, Azure Data Studio, Fabric); SQL 2025 parser; Entra-ID app/MFA; palette shortcut; Extensions-menu placement; Redgate-Platform sharing | Editions / Platform | **Low (edge-of-scope)** | L | Platform breadth, not SQL-editing capability; some rows are ➖ under AKML's MIT/free model |

---

## 6. Assumptions & limits

- **AI features are excluded throughout.** SQL Prompt's Prompt AI pane and any AKML AI Assistance surfaces (`AiAssistancePage.cs:11-13`) are out of scope and not scored.
- **Parity bar = SSMS 22 / VS 2026 desktop.** Web-edition-only behaviour (e.g. web SQL History `22cace3`, web-only snippets) is treated as a differentiator, **not** a parity target; where a capability exists only on web it counts as a desktop gap.
- **The SQL History screenshot is treated as the Redgate reference** (`doc/_Prompt-Gap/SQL History Redgate.png`). It depicts the SQL History **tool window**, not the Options dialog — so it neither confirms nor refutes Options parity.
- **No Options reference screenshot was available** (`referenceImageAvailable = false`; none found online or locally). Options coverage/structure is assessed with high confidence, but **pixel-level chrome parity (fonts, spacing, icon glyphs, row rhythm) is UNVERIFIED** pending a user-supplied SQL Prompt Options-window screenshot.
- **Adversarial-pass low-confidence / unverifiable items** (carried through, not asserted): (a) the semantic reading that the reference's two folder glyphs specifically toggle open/closed queries rests on Redgate **docs**, not the image (the structural gap — no bound control — is confirmed); (b) the small right-edge marks on server-less reference rows 2–8 are more plausibly a truncation artifact than a status indicator and are **not** treated as a refutation of AKML's per-row dot; (c) the SQL History date-group expand/collapse direction convention is a low-confidence read from the PNG. The dropped/reframed claims — "preview timestamps are relative" (code-contradicted at `HistoryToolWindowControl.cs:1331`) and "list rows use relative time for the reference rows" (they render absolute for aged rows; the true issue is date **format**) — are corrected above, not repeated as findings.
- **Snippets is a core SQL Prompt feature**, not an AKML-only Options addition (it maps to SP §10 folder-locations); the genuinely AKML-only Options additions are the Editor group, Queries ▸ Execution, Code Analysis, and the sidebar search box.
- Snippets scope-fit evidence is **audit-sourced and not independently re-verified** this pass; all other areas were branch/code-verified where file:line citations appear.
