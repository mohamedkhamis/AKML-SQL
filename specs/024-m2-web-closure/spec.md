# Feature Specification: M2 — Web Edition Formatter & Analyser MVP Closure

**Feature Branch**: `024-m2-web-closure`
**Created**: 2026-05-26
**Status**: Draft
**Input**: User description: "For the M2 PRD (Blazor WASM Standalone: Formatter & Analyser MVP). Spec 021 already shipped 124 of 154 tasks across M1–M6; for the M2 slice (Phase 3 / User Story 1), five tasks are deferred and explicitly call out the need for an interactive workstation session or a parity corpus that did not exist when M2 code landed. This closure spec captures only those five genuinely-unmet items so the M2 quality bar can be retired."

---

## Overview

The M2 PRD asks for a browser-based SQL formatter and analyser at parity with the IDE plugin: same formatted output, same analysis findings, the same look and feel across Light / Dark / HighContrast themes, all within a defined bundle-size budget.

Spec 021 Phase 3 (User Story 1) landed the bulk of that work — `Editor.razor`, `EditorComponent`, `FormatterService`, `AnalyserService`, `ProfileStore`, `AnalysisSettingsStore`, `ThemeService`, `ProblemsListComponent`, the diagnostics ring buffer, editor-session persistence, and the M2 quickstart doc are all on `master` and exercised by ~50 bUnit and service tests. The M2 PRD's Definition of Done is **structurally complete in services and bundle**.

What is **not** done splits into two slices:

1. The **verification slice** — five Phase 3 tasks deferred because they each need either an interactive workstation session (running the IDE plugin and the web edition side-by-side), a Release `dotnet publish` on a Windows host with the full SDK (so trimming + Brotli compression run for a real bundle measurement), a Playwright runner against `dotnet run`, or a parity corpus that did not exist when M2 code landed. Without those five items the M2 quality bar is **stated but not demonstrated**.
2. The **product UI slice** — three PRD §5 feature-scope rows (`Open .sql file via <InputFile>`, `Import .akmlstyle`, `Import .sqlpromptstylev2`, `Export current profile`) are marked **Yes** in the PRD but ship with backend code only and **no UI wiring**. `SqlPromptImporter` / `SqlPromptExporter` / `ProfileSerializer` exist in `src/AkmlSql.Analysis` and `src/AkmlSql.Formatting`; `ProfileOrigin.SqlPromptImport` is rendered in the picker option-groups; the production `Editor.razor` has Save but no Open; and `ProfilePickerComponent.razor`'s comment promises `<InputFile>` + Blob+download but the component renders only a `<select>` + a Delete button. A user cannot open a SQL file from disk, cannot import a profile they crafted in the IDE, and cannot export the active profile back out.

This specification covers exactly the unmet verification work **and** the missing UI affordances — six user stories — leaving every shipped service surface and the bundle-budget result untouched. It is a **closure spec**, structured the same way spec 023 closed M1.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Demonstrate visual parity across all three themes (Priority: P1)

A maintainer can open both the WPF IDE plugin and the web edition side-by-side, capture screenshots of the editor surface in Light, Dark, and HighContrast modes, compare them pair-by-pair, and record the deltas in a checked-in audit document. Where the deltas reveal visible quality gaps, the top five are closed in the web CSS so the two surfaces meet the same visual quality bar.

**Why this priority**: This is the most user-visible M2 acceptance criterion. The M2 PRD's success metric "theme tokens generated from `theme-tokens.json` match the WPF tokens on visual inspection" cannot be retired without screenshot evidence. Until the audit lands, the project ships a web edition that **claims** parity with the WPF surface but has never been compared to it. Any user who runs both products on the same machine is the first reviewer — and the first to find drift.

**Independent Test**: Open the `M2-THEME-PARITY-AUDIT.md` document; confirm it contains paired screenshots (WPF + web) for all three themes, a deltas table identifying every visible discrepancy, and a list of the five gaps closed in `src/AkmlSql.Web/wwwroot/css/`. A second reviewer can reproduce the comparison from the procedure section of the same document.

**Acceptance Scenarios**:

1. **Given** the IDE plugin and the web edition are both running on the same Windows host with the OS in Light mode, **When** the maintainer captures matching editor screenshots from each, **Then** the audit document records the pair, calls out every visible delta, and labels each delta as either closed or accepted-with-reason.
2. **Given** the OS theme is switched to Dark and again to HighContrast, **When** the maintainer repeats the capture for each theme, **Then** the audit document contains a complete 3-theme × 2-surface = 6-screenshot matrix with side-by-side commentary.
3. **Given** the audit identifies more than five visible gaps, **When** the maintainer ranks them by user impact, **Then** the top five are addressed by edits under `src/AkmlSql.Web/wwwroot/css/` and the remaining gaps are recorded as named follow-ups with rationale for deferral.
4. **Given** the audit is complete, **When** a fresh reviewer opens the audit document without running the build, **Then** the reviewer can see every delta and every closure from the screenshots alone.

---

### User Story 2 — Demonstrate formatter parity over a real corpus (Priority: P2)

A maintainer can run the web edition's formatter and the IDE plugin's formatter against the same 20 representative SQL scripts under the same three formatting profiles, and a checked-in test confirms the formatted output is byte-identical for every script-profile pair. Any divergence is recorded as a finding, not silently accepted.

**Why this priority**: The M2 PRD's most measurable success metric is "built-in profiles match the WPF surface byte-for-byte on the same input." Today the web edition's formatter has ~7 structural unit tests (default profile, profile override, no-op for canonical input, null guard) — enough to prove the wiring works, not enough to prove parity. A user importing a `.akmlstyle` style they crafted in the IDE and running it through the web edition has no guarantee the result matches what the IDE would have produced. This story converts that guarantee from "the code paths share a project reference" into recorded evidence.

**Independent Test**: Run the parity test suite against the corpus; confirm every script × profile pair produces byte-identical output or a recorded, explained finding; the test fails the build if any unexplained drift appears.

**Acceptance Scenarios**:

1. **Given** a parity corpus of at least 20 representative SQL scripts covering SELECT, multi-statement batches, stored procedures, CTEs, window functions, MERGE, DDL, and inline-comment-heavy code, **When** each script is formatted by the web edition under each of the three primary profiles, **Then** the output matches the IDE plugin's output for the same script + profile byte-for-byte.
2. **Given** any script-profile pair where the output diverges, **When** the test records the divergence, **Then** the divergence is captured with the script id, the profile id, a unified diff of the two outputs, and a disposition (resolved / accepted-with-reason).
3. **Given** the corpus is sourced from `tests/format-parity/` (the spec-020 parity corpus), **When** a new corpus item is added there, **Then** the M2 parity test picks it up automatically without code changes.
4. **Given** the parity test is part of the standard `dotnet test` run, **When** a regression is introduced in the formatter pipeline, **Then** the test fails on CI with the offending script-profile pair named.

---

### User Story 3 — Demonstrate analyser parity over the same corpus (Priority: P2)

A maintainer can run the web edition's analyser and the IDE plugin's analyser against the same parity corpus and a checked-in test confirms the two produce identical finding sets — same rule id, same severity, same message, same line, same column — for every script. Any divergence is recorded.

**Why this priority**: Equally important as US2 — the same logic, applied to analysis findings. A user who relies on a specific rule firing in the IDE has the same expectation when the web edition opens the same file. The current `AnalyserServiceTests.cs` confirms the analyser runs in the browser and produces *some* output for canned inputs; it does not confirm the output matches what the IDE produces for the same input. Without this story, the 130+ rules are proven to execute but not proven to agree with the IDE.

**Independent Test**: Run the analyser parity test against the corpus; confirm every script's findings set matches the IDE plugin's findings set exactly across rule id, severity, message, line, and column, or that every divergence is recorded.

**Acceptance Scenarios**:

1. **Given** the parity corpus is available with desktop baseline findings already recorded, **When** the web edition's analyser runs the corpus, **Then** every finding is identical to the IDE baseline along all five attributes (rule id, severity, message, line, column).
2. **Given** a finding diverges in any attribute, **When** the test records the divergence, **Then** the script id, the rule id, both finding objects, and a disposition (resolved / accepted-with-reason) are captured.
3. **Given** the desktop baseline does not yet exist, **When** the corpus is loaded, **Then** the test produces an actionable error pointing at the baseline-generator procedure rather than silently passing.
4. **Given** the analyser parity test is part of the standard `dotnet test` run, **When** a regression is introduced in analysis logic or rule discovery, **Then** the test fails on CI with the offending script + rule named.

---

### User Story 4 — Demonstrate end-to-end user flow in a real browser (Priority: P3)

A maintainer can run a browser automation test against a live web edition and confirm that each of the M2 PRD's User Story 1 acceptance scenarios — paste SQL, run format, see analysis results, click a problem to jump to its line, switch theme, change profile — completes end-to-end in a real Chromium browser with no exception, no hang, and no regression in interaction latency.

**Why this priority**: The bUnit unit tests cover the service surface; they cannot prove the page actually renders, the JavaScript interop fires, the editor responds to keystrokes, the click-to-jump scrolls to the right line, or the theme swap repaints correctly. The PRD's success metric "a user can paste a 100-line stored procedure, format it, and see analysis results in under 5 seconds total interaction time" is measurable only in a real browser. This story replaces "we believe it works" with "the browser test passed."

**Independent Test**: Run the E2E suite against `dotnet run` on the web edition; every scenario passes; the wall-clock time for the headline flow is recorded.

**Acceptance Scenarios**:

1. **Given** the web edition is running locally with no engine process, **When** the test pastes the M2 sample stored procedure, triggers Format, then triggers Analyse, **Then** both operations complete with no browser-console exception and the total elapsed time is recorded.
2. **Given** the problems list contains at least one finding, **When** the test clicks a finding, **Then** the editor caret moves to the finding's line and the line is visible in the viewport.
3. **Given** the user switches the OS theme preference mid-session, **When** the test re-renders, **Then** the editor and chrome swap to the matching theme without breakage.
4. **Given** the profile picker is opened, **When** the test selects a different built-in profile and re-formats, **Then** the formatted output reflects the new profile.

---

### User Story 5 — Record the actual cost of M2's web bundle (Priority: P4)

A maintainer planning M3 / M4 work can read an actual measured number for the M2 web bundle's compressed download size and decide on that basis whether lazy-loading is needed before M3 adds the bridge transport surface. If the bundle exceeds the M1 decision document's target, the spec records which assets to lazy-load and the maintainer applies the change.

**Why this priority**: M3 adds WebSocket transport, schema cache sync, and live IntelliSense — all of which will grow the bundle. M2's bundle is the baseline against which M3 measures growth. Without an M2 number, M3 has no way to know it has regressed. This story is P4 only because it does not block M2 functionality; it does block making sound size decisions for the next milestone.

**Independent Test**: Open `M2-BUNDLE-SIZE.md`; confirm it records an actual compressed `_framework/` total in MB, the machine and SDK version that produced it, and a verdict against the M1 target.

**Acceptance Scenarios**:

1. **Given** a Release publish of the web edition runs cleanly on a Windows host with the full .NET SDK, **When** the maintainer sums the compressed `_framework/*.br` files, **Then** the actual total is recorded as a single number in `M2-BUNDLE-SIZE.md`.
2. **Given** the recorded number exceeds the M1 decision document's target, **When** the maintainer chooses an asset to lazy-load, **Then** the choice is recorded with rationale and the asset is moved to a lazy-loaded path.
3. **Given** the recorded number is within the target, **When** the maintainer records the verdict, **Then** the document captures the headroom remaining and the next-checkpoint trigger (e.g. M3 must re-measure).

---

### User Story 6 — Wire the missing M2 PRD file-I/O affordances into the UI (Priority: P2)

A user who runs the web edition for the first time can open a `.sql` file from disk via a file picker in the editor toolbar, import an `.akmlstyle` profile or a `.sqlpromptstylev2` profile via the profile-picker's Import affordance, and download the active profile to disk as either format via an Export affordance — without using DevTools, the browser address bar, or the diagnostic `/spike` page.

**Why this priority**: These are M2 PRD §5 feature-scope rows marked **Yes** that ship with backend code but no UI today. The backend pipeline (`SqlPromptImporter`, `SqlPromptExporter`, `ProfileSerializer`) is built and tested at the IDE layer; the missing piece is the WASM-side `<InputFile>` element, the file-extension dispatcher, the IndexedDB save path for an imported profile, and the Blob+download exporter. Without these affordances, the PRD's "Import `.akmlstyle` — Yes / Import `.sqlpromptstylev2` — Yes / Export current profile — Yes / Open `.sql` file — Yes" lines remain promises the user can't act on, even though every prerequisite already lives on disk. This story is P2 (same as US2/US3) because it is product UI — not verification — and the parity tests it enables (a user-imported `.sqlpromptstylev2` running through the web formatter) cannot run without it.

**Independent Test**: Open the web edition in a Chromium browser; click the editor toolbar's Open button → choose a `.sql` file → confirm the editor's text replaces; click the profile picker's Import button → choose an `.akmlstyle` file → confirm it appears under the **User** option-group; click Import again → choose a `.sqlpromptstylev2` file → confirm it appears under the **SQL Prompt** option-group; click the Export button while a user profile is active → confirm a download is offered with the chosen extension. No spec-021 service code is modified.

**Acceptance Scenarios**:

1. **Given** the user has a 100-line `.sql` file on their disk and the web edition open, **When** the user clicks the editor toolbar's Open button and selects the file, **Then** the editor replaces its current text with the file's contents (subject to the 10 MB per-document size limit already enforced by `DocumentSizeLimit`), the analysis findings reset, and the toolbar status reflects the file name.
2. **Given** the user exported an `.akmlstyle` profile from the IDE plugin, **When** they click the profile picker's Import button and select that file, **Then** the profile is persisted via `IProfileStore.SaveAsync` with `ProfileOrigin.User`, appears in the **User** option-group, and is selectable as the active profile; re-formatting uses its settings.
3. **Given** the user exported a `.sqlpromptstylev2` style from SQL Prompt or the IDE plugin, **When** they click Import and select that file, **Then** the file is routed through `SqlPromptImporter.Import(...)`, persisted with `ProfileOrigin.SqlPromptImport`, appears in the **SQL Prompt** option-group, and is selectable as the active profile.
4. **Given** a user or SQL-Prompt-origin profile is currently active, **When** the user clicks Export and chooses a format (`.akmlstyle` or `.sqlpromptstylev2`), **Then** a download is triggered via the existing `akml-download.js` interop with a sensible filename (e.g. `<profile-name>.akmlstyle`) and the file round-trips through the IDE plugin without warnings.
5. **Given** the active profile is built-in (`builtin.default` / `builtin.ansi`), **When** the user opens the profile picker, **Then** the Export button is disabled (or hidden) because built-in profiles are not user content; the affordance reappears the moment a user/SQL-Prompt profile becomes active.

---

### Edge Cases

- **Audit screenshot variance from OS-level font rendering or DPI scaling**: capture the host's DPI and font-smoothing setting alongside each screenshot pair; treat any sub-pixel variance attributable to DPI as accepted-with-reason rather than a closeable delta.
- **Parity-corpus script that triggers a known formatter limitation already accepted in spec 020**: the parity test treats spec-020-documented limitations as accepted-with-reason rather than failing; the disposition links back to the spec-020 tasks.md entry.
- **Baseline-revision drift mid-test-run** (someone updates the IDE plugin between baseline capture and web-side comparison): the test embeds the baseline revision into the baseline file and refuses to compare against a mismatched build.
- **Playwright test runs against a stale `dotnet run` after a code change** without seeing the change: the test harness builds the project before launching the browser and aborts if the build is dirty.
- **Bundle measurement on a machine without Brotli compression in the toolchain**: the bundle-size audit explicitly notes the compression status and the document is invalid until the measurement is captured from a build with Brotli active.
- **Audit captures more than five visible gaps**: the spec mandates closing only the top five; remaining gaps must be filed as named follow-ups (with the audit document linking them) rather than silently deferred.
- **User opens a `.sql` file larger than 10 MB**: the editor refuses the load via the existing `DocumentSizeLimit` guard and surfaces a non-blocking status message; the prior editor contents stay intact.
- **User imports an `.akmlstyle` or `.sqlpromptstylev2` whose deserialisation throws**: the import path catches the exception, leaves the active profile unchanged, surfaces an error in the toolbar status, and does not write a corrupt entry to IndexedDB.
- **`.sqlpromptstylev2` contains an option `SqlPromptImporter` does not map**: the existing FR-023 affordance from spec 020 (logs the unmapped option, continues import) applies unchanged; the imported profile is usable for every option that did map.
- **User exports while a built-in profile is active**: the Export affordance is disabled because built-ins are read-only IP — users should not be able to "export" them as if they were customisations.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Theme parity audit

- **FR-001**: The audit MUST capture paired screenshots of the editor surface — IDE plugin alongside web edition — for each of the three themes (Light, Dark, HighContrast).
- **FR-002**: The audit MUST record every visible delta between each paired screenshot in a tabular form (surface element, IDE rendering, web rendering, disposition).
- **FR-003**: The audit MUST close the five highest-user-impact deltas by editing `src/AkmlSql.Web/wwwroot/css/`; remaining deltas MUST be filed as named follow-ups.
- **FR-004**: The audit MUST live at `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`, replacing the existing placeholder, so spec 021's T036 task can be marked done.
- **FR-005**: The audit MUST record the host OS theme/DPI/font-smoothing settings alongside each screenshot pair so the comparison is reproducible.

#### Formatter parity

- **FR-006**: A parity test MUST exist that runs the web edition formatter against every script in `tests/format-parity/corpus/` under every supported profile and asserts byte-identical output versus the IDE plugin baseline.
- **FR-007**: The parity test MUST cover at least 20 representative scripts spanning SELECT, multi-statement batches, stored procedures, CTEs, window functions, MERGE, DDL, and inline-comment-heavy code, with at least 3 profiles per script.
- **FR-008**: Any divergence MUST be recorded with the script id, the profile id, a unified diff, and an explicit disposition (resolved / accepted-with-reason); accepted-with-reason entries MUST link to a spec-020 tasks.md entry or an equivalent recorded limitation.
- **FR-009**: The parity test MUST live under `tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs` (extending the existing structural-coverage test class) and run as part of the standard `dotnet test` invocation.

#### Analyser parity

- **FR-010**: A parity test MUST exist that runs the web edition analyser against every script in the corpus and asserts the finding set matches the IDE plugin baseline along five attributes: rule id, severity, message, line, column.
- **FR-011**: Any finding-set divergence MUST be recorded with the script id, the offending finding(s), and a disposition (resolved / accepted-with-reason).
- **FR-012**: The parity test MUST live under `tests/AkmlSql.Web.Tests/Analyse/AnalyserServiceTests.cs` (extending the existing class) and run as part of the standard `dotnet test` invocation.
- **FR-013**: If the desktop baseline is missing or stale, the parity test MUST fail with an actionable error pointing at the baseline-generator procedure rather than silently passing.

#### Browser end-to-end

- **FR-014**: An end-to-end browser test suite MUST exist that drives the web edition through every M2 PRD User Story 1 acceptance scenario in a real Chromium browser.
- **FR-015**: The end-to-end suite MUST live under `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs` per spec 021's Phase 3 plan.
- **FR-016**: The end-to-end suite MUST record the wall-clock time for the headline flow (paste 100-line stored procedure → format → analyse → see findings) and fail if it exceeds the M2 PRD's success-criterion ceiling (5 seconds).
- **FR-017**: The end-to-end suite MUST be runnable from a single command against either a developer's local `dotnet run` or a CI runner, with the launch step embedded in the harness so a stale build cannot be silently tested.

#### Bundle-size audit

- **FR-018**: The bundle-size audit MUST record the compressed `_framework/*.br` total from a Release publish in `specs/021-web-edition/M2-BUNDLE-SIZE.md`, replacing the existing placeholder so spec 021's T054 task can be marked done.
- **FR-019**: The audit MUST record the host machine, the .NET SDK version, the WebAssembly tooling version, and confirmation that Brotli compression was active during the measurement.
- **FR-020**: The audit MUST compare the measurement against the M1 decision document's target and produce an explicit verdict (within target / over target).
- **FR-021**: If the measurement is over target, the audit MUST identify the largest single asset and record a lazy-loading plan; the plan MUST be applied before the audit is marked complete.
- **FR-022**: The audit MUST record the headroom for M3 — the difference between the current measurement and the next-milestone budget — so M3's growth can be tracked.

#### File-I/O UI affordances

- **FR-023**: The editor toolbar in `src/AkmlSql.Web/Pages/Editor.razor` MUST expose an Open affordance that uses `<InputFile accept=".sql">` to read a single file's text and replace the editor's content; the read MUST honour the existing `DocumentSizeLimit` 10 MB ceiling and MUST surface a non-blocking status message on rejection without clobbering the prior content.
- **FR-024**: `ProfilePickerComponent.razor` MUST expose an Import affordance backed by `<InputFile accept=".akmlstyle,.sqlpromptstylev2">`; the handler MUST dispatch on file extension — `.akmlstyle` → `ProfileSerializer.Deserialize` → save with `ProfileOrigin.User`; `.sqlpromptstylev2` → `SqlPromptImporter.Import` → save with `ProfileOrigin.SqlPromptImport` — and persist the result via `IProfileStore.SaveAsync` so the new profile appears in the matching option-group on the next list refresh.
- **FR-025**: Imported profiles MUST appear in the correct option-group (`User` or `SQL Prompt`), MUST be selectable as the active profile immediately, and MUST be deletable via the existing Delete button; the built-in profiles MUST remain non-deletable.
- **FR-026**: `ProfilePickerComponent.razor` MUST expose an Export affordance that — when the active profile is non-built-in — triggers a download via the existing `akml-download.js` JS interop; the user MUST be able to choose between `.akmlstyle` (via `ProfileSerializer.Serialize`) and `.sqlpromptstylev2` (via `SqlPromptExporter.ExportToString`); the filename MUST default to `<profile-name>.<extension>` with reserved-character sanitisation.
- **FR-027**: The Export affordance MUST be disabled or hidden when the active profile's `Origin == BuiltIn`; user-visible state MUST reflect the change within one render cycle of the active-profile selection.
- **FR-028**: Every file-I/O path (open, import, export) MUST be exercised by a bUnit test under `tests/AkmlSql.Web.Tests/` that covers the happy path **and** the error path (oversize file, malformed XML / JSON, built-in-profile-export-blocked); the tests MUST run as part of the standard `dotnet test` invocation.

### Key Entities *(include if feature involves data)*

- **Theme parity audit document** (`M2-THEME-PARITY-AUDIT.md`): paired screenshots × three themes, a deltas table, a list of closed deltas, a list of accepted-with-reason deltas, host environment metadata.
- **Parity corpus item**: a SQL script under `tests/format-parity/corpus/`, paired with one or more profiles and an desktop baseline output file. Reused across FR-006 and FR-010.
- **Parity test record**: per (corpus item × profile) pair, the web edition output, the IDE baseline output, a diff (if any), and a disposition.
- **Browser test scenario**: one of the four M2 PRD User Story 1 acceptance scenarios, encoded as a browser-driver script with timing assertions.
- **Bundle-size audit record** (`M2-BUNDLE-SIZE.md`): the compressed total, the per-asset breakdown, the host metadata, the verdict against the M1 target, and (if over) the lazy-loading plan.
- **Imported profile record**: a `ProfileRecord` created by the import handler — `Id` (generated, kebab-cased from filename), `Name` (from the file's metadata or filename stem), `Origin` (`User` for `.akmlstyle`, `SqlPromptImport` for `.sqlpromptstylev2`), `Profile` (the deserialised `FormattingProfile`). Persisted in IndexedDB via `IProfileStore.SaveAsync`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A second reviewer can open `M2-THEME-PARITY-AUDIT.md`, find paired screenshots for all three themes, and identify every captured delta along with its closure or deferral disposition — without running the build.
- **SC-002**: The formatter parity test runs to completion on the standard `dotnet test` invocation and produces a single PASS verdict (byte-identical output across the full corpus × three profiles) or a FAIL with every divergence listed.
- **SC-003**: The analyser parity test runs to completion and produces a PASS verdict (identical finding sets across all five attributes) or a FAIL with every divergence listed.
- **SC-004**: A maintainer running the end-to-end suite against a fresh `dotnet run` sees all four acceptance scenarios pass within 5 seconds combined for the headline flow.
- **SC-005**: The bundle-size audit records an actual compressed total in `M2-BUNDLE-SIZE.md` from a Release publish, with the host metadata and a verdict against the M1 target.
- **SC-006**: All five of spec 021's deferred Phase 3 tasks (T036, T041, T047, T053, T054) can be marked complete with their deferral notes removed; their checkboxes flip from `[ ]` to `[X]`.
- **SC-007**: After this spec lands, the M2 PRD's Definition of Done has every checkbox closed against recorded evidence rather than against shipped code alone.
- **SC-008**: The web edition's M3 work can use the M2 bundle-size measurement as a regression baseline; M3 cannot start without an M2 number in `M2-BUNDLE-SIZE.md`.
- **SC-009**: A first-time user can — in a single Chromium session against the published web edition, without DevTools — open a `.sql` file from disk, import an `.akmlstyle` profile they exported from the IDE plugin, import a `.sqlpromptstylev2` style, select the imported profile as active, and export the active profile back to disk in either format. Every M2 PRD §5 feature-scope row marked **Yes** has a clickable UI affordance behind it.

---

## Assumptions

- The format-parity corpus from spec 020 has either landed in `tests/format-parity/corpus/` or its creation is bundled into FR-006/FR-010 work; the corpus is the shared source for both parity tests.
- The IDE plugin used to produce the baseline outputs is the version on the same `master` commit as the web edition under test; baseline drift is captured by embedding the baseline revision in each baseline file.
- The maintainer running the theme parity audit has access to a Windows workstation that can run both the IDE plugin and the web edition at the same time, and can drive both at the same OS theme.
- The bundle-size measurement happens on a Windows host with the full .NET SDK and WebAssembly tooling — the same environment that produces release artifacts — so trimming and Brotli compression match production behaviour.
- The Playwright tests can be run from the developer's machine; CI integration is a nice-to-have but not a blocker for this spec's completion (CI wiring is a follow-up).

---

## Dependencies

- **Spec 021** Phase 3 (User Story 1) — every shipped piece (`Editor.razor`, services, components, IndexedDB adapters, theme system, profile system, document-size guard) is the substrate this spec audits. No code in spec 021 is modified except the placeholder audit documents and the deferred-task notes.
- **Spec 020** — the format-parity corpus's lineage; if FR-006/FR-010's corpus extends spec 020's, the spec-020 limitations are inherited as accepted-with-reason dispositions.
- **M1 decision document** (`docs/m1-wasm-decision.md`) — the source of the bundle-size target FR-020 compares against.
- **IDE plugin baseline** — the formatter and analyser outputs from the WPF surface must be captured before FR-006 and FR-010 can compare; the baseline-generator procedure is part of those FRs.
- **Existing backend pipeline for US6** — `src/AkmlSql.Analysis/SqlPromptImporter.cs`, `src/AkmlSql.Formatting/Profiles/SqlPromptExporter.cs`, and `src/AkmlSql.Formatting/Profiles/ProfileSerializer.cs` are the pre-existing IDE-layer round-trip pipeline US6 wires into the WASM UI. No new parser/serializer is introduced; the spec-020 FR-023 unmapped-option behaviour applies unchanged.
- **Existing JS interop for US6 download** — `src/AkmlSql.Web/wwwroot/js/akml-download.js` is the existing Blob+download helper FR-026 calls; no new JS module is introduced.
