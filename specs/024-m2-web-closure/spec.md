# Feature Specification: M2 — Web Edition Formatter & Analyser MVP Closure

**Feature Branch**: `024-m2-web-closure`
**Created**: 2026-05-26
**Status**: Draft
**Input**: User description: "For the M2 PRD (Blazor WASM Standalone: Formatter & Analyser MVP). Spec 021 already shipped 124 of 154 tasks across M1–M6; for the M2 slice (Phase 3 / User Story 1), five tasks are deferred and explicitly call out the need for an interactive workstation session or a parity corpus that did not exist when M2 code landed. This closure spec captures only those five genuinely-unmet items so the M2 quality bar can be retired."

---

## Overview

The M2 PRD asks for a browser-based SQL formatter and analyser at parity with the IDE plugin: same formatted output, same analysis findings, the same look and feel across Light / Dark / HighContrast themes, all within a defined bundle-size budget.

Spec 021 Phase 3 (User Story 1) landed the bulk of that work — `Editor.razor`, `EditorComponent`, `FormatterService`, `AnalyserService`, `ProfileStore`, `AnalysisSettingsStore`, `ThemeService`, `ProblemsListComponent`, the diagnostics ring buffer, editor-session persistence, and the M2 quickstart doc are all on `master` and exercised by ~50 bUnit and service tests. The M2 PRD's Definition of Done is **structurally complete**.

What is **not** done is the **verification slice**: the five Phase 3 tasks deferred because they each need either an interactive workstation session (running the IDE plugin and the web edition side-by-side), a Release `dotnet publish` on a Windows host with the full SDK (so trimming + Brotli compression run for a real bundle measurement), a Playwright runner against `dotnet run`, or a parity corpus that did not exist when M2 code landed. Without those five items the M2 quality bar is **stated but not demonstrated**.

This specification covers exactly the unmet verification work — five user stories, one per deferred task — leaving every shipped M2 surface untouched. It is a **closure spec**, structured the same way spec 023 closed M1.

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

### Edge Cases

- **Audit screenshot variance from OS-level font rendering or DPI scaling**: capture the host's DPI and font-smoothing setting alongside each screenshot pair; treat any sub-pixel variance attributable to DPI as accepted-with-reason rather than a closeable delta.
- **Parity-corpus script that triggers a known formatter limitation already accepted in spec 020**: the parity test treats spec-020-documented limitations as accepted-with-reason rather than failing; the disposition links back to the spec-020 tasks.md entry.
- **Baseline-revision drift mid-test-run** (someone updates the IDE plugin between baseline capture and web-side comparison): the test embeds the baseline revision into the baseline file and refuses to compare against a mismatched build.
- **Playwright test runs against a stale `dotnet run` after a code change** without seeing the change: the test harness builds the project before launching the browser and aborts if the build is dirty.
- **Bundle measurement on a machine without Brotli compression in the toolchain**: the bundle-size audit explicitly notes the compression status and the document is invalid until the measurement is captured from a build with Brotli active.
- **Audit captures more than five visible gaps**: the spec mandates closing only the top five; remaining gaps must be filed as named follow-ups (with the audit document linking them) rather than silently deferred.

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

### Key Entities *(include if feature involves data)*

- **Theme parity audit document** (`M2-THEME-PARITY-AUDIT.md`): paired screenshots × three themes, a deltas table, a list of closed deltas, a list of accepted-with-reason deltas, host environment metadata.
- **Parity corpus item**: a SQL script under `tests/format-parity/corpus/`, paired with one or more profiles and an desktop baseline output file. Reused across FR-006 and FR-010.
- **Parity test record**: per (corpus item × profile) pair, the web edition output, the IDE baseline output, a diff (if any), and a disposition.
- **Browser test scenario**: one of the four M2 PRD User Story 1 acceptance scenarios, encoded as a browser-driver script with timing assertions.
- **Bundle-size audit record** (`M2-BUNDLE-SIZE.md`): the compressed total, the per-asset breakdown, the host metadata, the verdict against the M1 target, and (if over) the lazy-loading plan.

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
