---

description: "Task list for M2 — Web Edition Formatter & Analyser MVP Closure"
---

# Tasks: M2 — Web Edition Formatter & Analyser MVP Closure

**Input**: Design documents from `/specs/024-m2-web-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (4 files), quickstart.md (all present)

**Tests**: This closure spec **is** verification work — every user story produces tests, audit documents, or measured numbers as its deliverable. There is no separate "tests" section; the work itself is the testing. No TDD ordering applies because we are not adding application code that needs failing tests to drive it (the application is already shipped via spec 021).

**Organization**: Tasks are grouped by user story so each story can land independently. US1 (theme audit) is entirely independent of US2–US5; US2 + US3 share the parity-baseline infrastructure from the Foundational phase; US4 (Playwright) needs `data-testid` attributes added to existing components; US5 (bundle audit) is entirely independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to a user story (US1–US5); omitted for Setup, Foundational, and Polish tasks
- Paths are absolute repository paths

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: Make the directories and tools the five user stories consume.

- [X] T001 Create the parity-baseline directory tree at `tests/format-parity/baselines/{default,ansi}/` (web ships **2** built-in profiles `builtin.default` + `builtin.ansi`, not 3; FR-007 deviation noted in implementation log). `.gitkeep` in each.
- [X] T002 [P] Create the screenshot directory at `specs/021-web-edition/screenshots/` with a `.gitkeep` placeholder.
- [X] T003 [P] Build `tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj -c Release` (0 warnings/errors, 3.26 s). Playwright Chromium install (`pwsh ... playwright.ps1 install chromium`) is the maintainer's first-run step before US4; deferred from this CLI session.

**Checkpoint**: Directories exist, Playwright is ready. Foundational phase can start.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Cross-cutting infrastructure that **two or more** user stories consume — `data-testid` attributes US4 depends on, and the parity-baseline loader US2 + US3 both build on. No user story phase can start until this phase is done.

**⚠️ CRITICAL**: T005 → T009 are sequential within Phase 2 (T009 reads what T007 + T008 produce). T004 and the T005-chain are independent across files, so T004 runs in parallel with the chain.

- [X] T004 [P] Added seven `data-testid` attributes across five surfaces: `sql-editor` on `EditorComponent.razor` root div; `analyse-button` on the toolbar button in `Editor.razor`; `format-complete` + `analyse-complete` conditional `<span hidden>` markers in `Editor.razor` (latched by new `_hasFormatted` / `_hasAnalysed` fields set inside `FormatAsync` / `AnalyseAsync`); `problem-item` on the `<li>` row template in `ProblemsListComponent.razor` (carrying `data-line` + `data-column`); `profile-picker` on the `<select>` in `ProfilePickerComponent.razor`; `error-banner` on a new conditional `<div>` in `MainLayout.razor` driven by `GlobalError` (rendered only when set). Web build green (0 warnings / 0 errors).
- [X] T005 [P] `tests/AkmlSql.Web.Tests/Parity/ParityCorpusLoader.cs` written — `EnumerateCorpus` walks `tests/format-parity/corpus/*.sql`, `LoadFormatterBaseline(id, profile)` parses + validates the marker line per `contracts/parity-baseline-format.md`, `LoadAnalyserBaseline(id)` deserialises the JSON envelope, both throw on IDE-build stamp mismatch with an actionable regeneration hint. `ProfileIds = { "default", "ansi" }` reflects the actual 2-profile zoo.
- [X] T006 [P] `tests/AkmlSql.Web.Tests/Parity/ParityDispositionsRegistry.cs` written — static `AcceptedReason(corpusId, profileId, ruleId?)` returns the `ReasonLink` for matching entries or `null` (= true failure). Starts empty; entries added during US2 / US3 triage.
- [X] T007 `tests/AkmlSql.Web.Tests/Parity/ParityBaselineGenerator.cs` written — opt-in `[Trait("Category","ParityBaseline")]`, gated on `AKML_REGEN_PARITY_BASELINE=1`, mirrors `IProfileStore.BuildBuiltInProfiles()` so baselines match what the web edition runs. Emits UTF-8 (no BOM), LF endings, trailing newline, deterministic finding-array sort.
- [X] T008 `tests/format-parity/ide-plugin-version.txt` created with `1.26.0526.0000` (placeholder; bump alongside any real IDE-plugin release).
- [X] T009 Ran `AKML_REGEN_PARITY_BASELINE=1 dotnet test --filter "Category=ParityBaseline"` — generated **39 baseline files** (13 corpus × 2 profiles formatter + 13 analyser). Test passed in 1.33 s. Files now under `tests/format-parity/baselines/{default,ansi}/`. Note: `default` and `ansi` profiles currently produce byte-identical output because both default to `Casing.ReservedKeywords = "uppercase"`; meaningful per-profile divergence will appear when the web edition adds more `ansi`-specific knobs.

**Checkpoint**: `data-testid`s in place (US4 unblocked); parity infrastructure + populated baselines on disk (US2 + US3 unblocked); screenshot dir + Playwright binaries ready. All five user stories can now run in parallel.

---

## Phase 3: User Story 1 — Theme parity audit (Priority: P1) 🎯 MVP

**Goal**: produce `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md` with paired screenshots × three themes, a deltas table, top-5 CSS closures, and a pass verdict — closing the most user-visible M2 acceptance criterion.

**Independent Test**: A second reviewer opens the audit document, finds paired screenshots for all three themes, identifies every captured delta with its disposition, and can reproduce the comparison from §6 without running the build. The verdict in §7 is `AUDIT PASSES`.

- [ ] T010 [US1] Boot both surfaces side-by-side per `specs/024-m2-web-closure/quickstart.md` §US1 step 1 — SSMS 22 open with `tests/format-parity/corpus/03-stored-proc.sql` loaded; `dotnet run --project src/AkmlSql.Web -c Release` in a separate terminal with the same file pasted into Chromium
- [ ] T011 [P] [US1] Capture the Light theme pair: switch Windows to Light mode; Snipping Tool captures editor region only (exclude title bar); save as `specs/021-web-edition/screenshots/light-wpf.png` and `specs/021-web-edition/screenshots/light-web.png`
- [ ] T012 [P] [US1] Capture the Dark theme pair: switch Windows to Dark mode; save as `specs/021-web-edition/screenshots/dark-wpf.png` and `specs/021-web-edition/screenshots/dark-web.png`
- [ ] T013 [P] [US1] Capture the HighContrast theme pair: switch Windows to HighContrast (Settings → Accessibility → Contrast themes → HighContrast Black); save as `specs/021-web-edition/screenshots/high-contrast-wpf.png` and `specs/021-web-edition/screenshots/high-contrast-web.png`
- [ ] T014 [US1] Diff every pair and write the audit deltas table at `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md` per `specs/024-m2-web-closure/contracts/theme-audit-format.md` §3 — one row per visible delta with `Theme / Surface element / IDE rendering / Web rendering / Disposition`; record host environment (OS version, DPI, font smoothing, monitor) in §1; embed the screenshot matrix in §2
- [ ] T015 [US1] Close the top-five deltas by editing files under `src/AkmlSql.Web/wwwroot/css/` — `editor.css`, `chrome.css`, or `themes/{light,dark,high-contrast}.css` as needed; record each edit's `before` / `after` snippet in `M2-THEME-PARITY-AUDIT.md` §4 per `theme-audit-format.md`
- [ ] T016 [US1] File remaining deltas (those beyond the top-5) as named follow-ups in `M2-THEME-PARITY-AUDIT.md` §5 with rationale for deferral
- [ ] T017 [US1] Re-capture the affected screenshot pairs from T015 to verify the CSS closures landed visibly; replace the relevant files in `specs/021-web-edition/screenshots/`
- [ ] T018 [US1] Write `M2-THEME-PARITY-AUDIT.md` §6 (procedure) — concrete reproduction steps a second reviewer can follow — and §7 verdict (`AUDIT PASSES`); verify against the validation checklist at the bottom of `theme-audit-format.md`

**Checkpoint**: User Story 1 functional. `M2-THEME-PARITY-AUDIT.md` exists with the full 7-section schema; spec 021 T036 can be flipped (deferred to Polish phase).

---

## Phase 4: User Story 2 — Formatter parity over a real corpus (Priority: P2)

**Goal**: a `dotnet test` run produces a PASS verdict for the formatter over `tests/format-parity/corpus/*.sql` × {default, compact, expanded} profiles — closing the M2 PRD's "built-in profiles match the WPF surface byte-for-byte" success metric.

**Independent Test**: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "FullyQualifiedName~FormatterServiceTests"` exits 0; every (script × profile) pair is either byte-identical to the baseline or recorded as `ACCEPTED_WITH_REASON` in `ParityDispositionsRegistry.cs` with a non-null `reasonLink`.

- [ ] T019 [US2] Extend `tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs` with a new `[Theory]` driver `Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles` that iterates `ParityCorpusLoader.Pairs()` and asserts `FormatterService.Format(script, profile).FormattedText == baseline.expected.sql` byte-exact for each pair (after the LF normalisation rule from `contracts/parity-baseline-format.md`); on mismatch, emit a `ParityTestRecord` per `data-model.md` Entity 3 with a unified diff and either fail the test or skip via `ParityDispositionsRegistry` lookup
- [ ] T020 [US2] Run the new theory: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "FullyQualifiedName~FormatterServiceTests"`; collect the divergences list
- [ ] T021 [US2] Triage every divergence — for **true regressions**, file a follow-up in `doc/progress.md` and either fix the formatter or accept the divergence as a known limitation; for **already-documented limitations**, add an entry to `ParityDispositionsRegistry.cs` with `reasonLink` pointing at the corresponding `specs/020-sqlprompt-visual-parity/tasks.md` task ID
- [ ] T022 [US2] Re-run T020 until green; commit the updated `ParityDispositionsRegistry.cs` alongside the test changes

**Checkpoint**: Formatter parity green over ≥ 20 scripts × 3 profiles. Spec 021 T041 can be flipped (deferred to Polish).

---

## Phase 5: User Story 3 — Analyser parity over the same corpus (Priority: P2)

**Goal**: a `dotnet test` run produces a PASS verdict for the analyser over the same corpus (default profile only, per `data-model.md` Entity 2) — closing the parity guarantee on the 130+ analysis rules.

**Independent Test**: `dotnet test ... --filter "FullyQualifiedName~AnalyserServiceTests"` exits 0; every script's web edition findings list (sorted by `(line, column, ruleId)`) equals the baseline JSON's `findings` array along all five attributes, or every divergence has an `ACCEPTED_WITH_REASON` entry in the registry.

- [ ] T023 [US3] Extend `tests/AkmlSql.Web.Tests/Analyse/AnalyserServiceTests.cs` with `Analyser_MatchesIdeBaseline_AcrossCorpus` `[Theory]` that iterates the corpus, runs `AnalyserService.AnalyseAsync(script)`, normalises the output to the same sort order as `baseline.expected.json.findings`, and asserts equality across `RuleId / Severity / Message / Line / Column`; emit `ParityTestRecord`s on mismatch with the offending finding object embedded
- [ ] T024 [US3] Run the new theory: `dotnet test ... --filter "FullyQualifiedName~AnalyserServiceTests"`; triage divergences via `ParityDispositionsRegistry` (same registry as US2; analyser entries use the optional `ruleId` field of the registry key)
- [ ] T025 [US3] Re-run T024 until green; commit registry updates

**Checkpoint**: Analyser parity green. Spec 021 T047 can be flipped (Polish).

---

## Phase 6: User Story 4 — Playwright User Story 1 E2E (Priority: P3)

**Goal**: a `dotnet test` run drives the four M2 PRD User Story 1 acceptance scenarios end-to-end in a real Chromium browser; all four pass; the headline flow elapses in ≤ 5 s.

**Independent Test**: `dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "FullyQualifiedName~UserStory1Tests"` exits 0 with four `[Fact]` results; Scenario 1's output line `Headline flow took X.XXs` shows `X.XX < 5.00`.

- [ ] T026 [US4] Create `tests/AkmlSql.Web.E2E.Tests/Harness/DotnetRunFixture.cs` implementing `IAsyncLifetime` per `contracts/playwright-harness-contract.md` "Lifecycle" — runs `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -nologo` first, aborts on non-zero exit, then launches `dotnet run --project src/AkmlSql.Web -c Release --no-build`, parses the `Now listening on:` line from stdout to discover the port, polls `http://localhost:<port>/` until 2xx/3xx (250 ms back-off, 60 s timeout); on dispose stops the process and disposes Playwright
- [ ] T027 [P] [US4] Create `tests/AkmlSql.Web.E2E.Tests/Harness/HeadlineFlowTimer.cs` per the contract — thin `Stopwatch` wrapper exposing `Start()`, `Elapsed`, `Stop()`
- [ ] T028 [P] [US4] Create `tests/AkmlSql.Web.E2E.Tests/Fixtures/100-line-stored-proc.sql` — a representative 100-line stored procedure with parameters, control flow, JOINs, and a CTE (reuse the spec-023 spike corpus `03-stored-proc.sql` if it qualifies; copy it locally so the E2E test does not depend on the spec-021 corpus dir layout)
- [ ] T029 [US4] Create `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs` with four `[Fact]` methods named per `contracts/playwright-harness-contract.md` "The four scenarios" — `PasteAndFormat_100LineProc_FormatsAndAnalyses_Under5Seconds` (asserts `timer.Elapsed < TimeSpan.FromSeconds(5)` and `error-banner` empty); `ProblemsList_ClickItem_MovesEditorCaretToFindingLine` (uses `data-line` attribute on `problem-item`); `ThemeSwitch_MidSession_SurfaceRepaintsWithoutBreakage` (uses `page.EmulateMediaAsync` for ColorScheme.Light ↔ Dark); `ProfilePicker_SwitchProfile_ReformatProducesNewOutput` (selects `builtin.compact`, re-formats, compares output)
- [ ] T030 [US4] Wire the shared collection fixture: add `[CollectionDefinition("DotnetRun")]` and `[Collection("DotnetRun")]` on `UserStory1Tests` so `DotnetRunFixture` is shared across the four tests (one `dotnet run` for all four scenarios)
- [ ] T031 [US4] Run the suite: `dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "FullyQualifiedName~UserStory1Tests"`; on failure, debug with `--logger "console;verbosity=detailed"` and Playwright's `slowMo` / `headless: false` options
- [ ] T032 [US4] Record the headline-flow elapsed time from Scenario 1's output in the test logs and in a follow-up note for the M2 PRD's success-metrics tracking

**Checkpoint**: All four Playwright scenarios green. Spec 021 T053 can be flipped (Polish).

---

## Phase 7: User Story 5 — Bundle-size audit (Priority: P4)

**Goal**: produce `specs/021-web-edition/M2-BUNDLE-SIZE.md` with the compressed `_framework/*.br` total, host metadata, and a verdict against the M1 decision document's target.

**Independent Test**: Opening `M2-BUNDLE-SIZE.md` shows every required section per `contracts/bundle-measurement-protocol.md`; the verdict line is `WITHIN_TARGET` (with headroom) or `OVER_TARGET` (with applied lazy-loading plan).

- [ ] T033 [US5] Verify host environment per `contracts/bundle-measurement-protocol.md` Step 1 — capture OS version, `dotnet --version` output, `dotnet workload list` excerpt (must show `wasm-tools` or `wasm-tools-net10`), and `git rev-parse HEAD`; record into the `M2-BUNDLE-SIZE.md` header + §1
- [ ] T034 [US5] Run the Release publish: `dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -nologo`; record the exact command and exit code 0 into `M2-BUNDLE-SIZE.md` §2; abort the audit if exit code != 0
- [ ] T035 [US5] Run the Brotli-active verification PowerShell from `contracts/bundle-measurement-protocol.md` Step 3 against `src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework`; record `Brotli confirmed active: yes` in §3 only if the script exits cleanly; abort the audit otherwise
- [ ] T036 [US5] Sum the compressed total: `(Get-ChildItem $framework -Recurse -Filter *.br | Measure-Object -Property Length -Sum).Sum / 1MB`; record the total + a sorted per-asset breakdown (descending by size) + top-5 assets called out in `M2-BUNDLE-SIZE.md` §3 / §4
- [ ] T037 [US5] Write the verdict line in §5: compare the compressed total against the M1 decision document's target (`docs/m1-wasm-decision.md`); if `WITHIN_TARGET`, record headroom in MB and next-checkpoint trigger ("M3 must re-measure before merge"); if `OVER_TARGET`, identify the largest asset and write a lazy-loading plan
- [ ] T038 [US5] If T037's verdict is `OVER_TARGET`: apply the lazy-loading plan to `src/AkmlSql.Web/` (move the offending asset to lazy-load), re-run T034 → T036 → T037 until the verdict is `WITHIN_TARGET`; only then commit the audit document

**Checkpoint**: `M2-BUNDLE-SIZE.md` exists with a green verdict; spec 021 T054 can be flipped (Polish).

---

## Phase 8: Polish & cross-cutting concerns

**Purpose**: Flip the spec 021 deferred-task checkboxes from `[ ]` to `[X]`, remove their deferral notes, and run the final closure verification.

- [ ] T039 [P] In `specs/021-web-edition/tasks.md`, flip T036 from `[ ]` to `[X]` and replace the deferral note with a one-line "Closed by spec 024 — see `specs/024-m2-web-closure/` and `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`"
- [ ] T040 [P] Same flip for T041 in `specs/021-web-edition/tasks.md` — note points at the extended `tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs` + the populated `tests/format-parity/baselines/`
- [ ] T041 [P] Same flip for T047 in `specs/021-web-edition/tasks.md` — note points at the extended `tests/AkmlSql.Web.Tests/Analyse/AnalyserServiceTests.cs`
- [ ] T042 [P] Same flip for T053 in `specs/021-web-edition/tasks.md` — note points at `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs`
- [ ] T043 [P] Same flip for T054 in `specs/021-web-edition/tasks.md` — note points at `specs/021-web-edition/M2-BUNDLE-SIZE.md`
- [ ] T044 Run the closure verification block from `specs/024-m2-web-closure/quickstart.md` "Closure verification (end-to-end)" — confirm all five `[ ] → [X]` flips, confirm the four expected artefacts exist, run `dotnet test` over Formatter + Analyser + Playwright filters and observe green
- [ ] T045 [P] Append a Spec 024 entry to `doc/progress.md` per the existing per-spec section pattern — record the 5 user stories, the 5 closed deferred tasks, the bundle measurement, the headline-flow timing, and any `ACCEPTED_WITH_REASON` divergences with their `reasonLink`s

**Checkpoint**: M2 milestone closed. PR-ready. All spec 021 Phase 3 deferred tasks marked `[X]`.

---

## Dependencies & execution order

### Phase dependencies

- **Phase 1 (Setup)**: No prior dependencies; T001 is sequential (it creates dirs), T002 + T003 are [P].
- **Phase 2 (Foundational)**: Depends on Phase 1. Within Phase 2: T004 + T005 + T006 are [P] (different files); T007 depends on the corpus existing (already does per spec 020); T008 + T009 are sequential (T009 reads what T007 + T008 produce).
- **Phase 3 (US1)**, **Phase 4 (US2)**, **Phase 5 (US3)**, **Phase 6 (US4)**, **Phase 7 (US5)**: All start after Phase 2 completes. **US1 is fully independent** of the others — does not touch any test code. **US2 + US3** share `ParityCorpusLoader` + `ParityDispositionsRegistry` from Phase 2 but operate on different test classes, so they run in parallel. **US4** depends on T004's `data-testid` additions (Phase 2). **US5** is entirely independent.
- **Phase 8 (Polish)**: Depends on every user story having flipped its corresponding spec-021 task. T039–T043 are [P] (different lines in the same file — git rebase / merge handles this, but file-level sequential is safer if running by hand).

### Within each user story

- **US1**: T010 must run first (sets up the side-by-side environment). T011 / T012 / T013 are [P] (three independent screenshot pairs). T014–T018 are sequential — each section of the audit document depends on the previous.
- **US2**: T019 → T020 → T021 → T022 is strictly sequential (test exists → run → triage → re-run).
- **US3**: Same shape — T023 → T024 → T025.
- **US4**: T026 + T027 + T028 are [P] (three files). T029 depends on all three. T030 + T031 + T032 are sequential.
- **US5**: T033 → T034 → T035 → T036 → T037 → T038 is strictly sequential — each step's success is a precondition for the next.

### Parallel opportunities

After Phase 2 completes, **all five user stories can run in parallel** if there are five engineers / sessions. Realistic single-engineer order: US1 (most user-visible, fastest to spot drift) → US2 + US3 (back-to-back, share the corpus loader) → US4 (highest cycle time per scenario debug) → US5 (lowest blocking risk, but unlocks M3).

---

## Implementation strategy

### MVP — User Story 1 only

If only one user story ships, ship US1. It is the most user-visible M2 acceptance criterion and the only one that produces evidence anyone using the product can see. Phases 1 + 2 (subset: just T002 for the screenshot dir) + Phase 3 + the US1 Polish flip (T039) is a complete, demoable M2 closure increment. Estimated effort: ~45 minutes per the quickstart.

### Incremental delivery

1. MVP (above) → ship the theme parity audit.
2. Add US2 + US3 (in parallel) → ship the parity test green light. Now the parity guarantee is recorded, not just claimed.
3. Add US4 → ship the Playwright safety net. Future regressions in the editor / format / analyse flow are caught on CI.
4. Add US5 → ship the bundle baseline. M3 has a regression target.
5. Polish phase → commit the spec 021 flips and the progress.md entry. PR.

### Parallel team strategy

Once Phase 2 completes:

- Engineer A: US1 (theme audit) — interactive workstation work.
- Engineer B: US2 + US3 — back-to-back, both modify the parity test classes + registry.
- Engineer C: US4 — Playwright harness + four scenarios.
- Engineer D: US5 — bundle-size audit on a separate Windows host with Brotli toolchain.

US1 ↔ US2/US3 ↔ US4 ↔ US5 have no file overlap (with one tiny exception: the registry from Phase 2's T006 is written by US2 and US3, but they touch different keys of the same dictionary — coordinate via PR review).

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase.
- Every [US*] task is independently testable per the spec's `Independent Test` block for that story.
- The closure spec discipline (no new application surfaces; no new test framework; no spec-021 surfaces touched beyond placeholders + deferred-task notes) holds across every task. The single intentional exception is T004 — seven `data-testid` attribute additions, justified as the Playwright contract's prerequisite.
- Per project rules in `CLAUDE.md`: never commit / push / run `git add` without explicit user approval. This task list assumes the user approves commits between phases or at the user's discretion.
- The closure verification block in `quickstart.md` (run at T044) is the single source of truth for "M2 PRD Definition of Done is closed against recorded evidence."
