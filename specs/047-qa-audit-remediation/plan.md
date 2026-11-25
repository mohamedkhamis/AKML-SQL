# Implementation Plan: QA Audit Remediation

**Branch**: `047-qa-audit-remediation` | **Date**: 2026-11-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/047-qa-audit-remediation/spec.md` (clarified 2026-11-21, five answers)

## Summary

Fix 32 of the 33 issues of the 2026-11-20 QA audit in the order of the spec's ten stories. Research
([research.md](./research.md) R1–R24) located every cause. 27 are confirmed in code or by a measurement; five are
strong hypotheses (FMT-003, FMT-004, FMT-005, the snippet half of UX-002, the WEB-008 token names), each to be pinned
by a failing test first.

| # | Story | Issues | Approach |
| --- | --- | --- | --- |
| 1 | CLI analyzer | CLI-001 | Root `AkmlSql.Analysis` and `AkmlSql.Core` for the trimmer, re-enable reflection JSON for `.casettings`, and add a release-gate smoke run of the published exe in both build scripts (R1). |
| 2 | Engine pairing | WEB-001, ENG-001 | The installer writes `akml-engine.json` (bridge port and scheme) next to the IIS bundle, and `PageEngine` reads it after `HEAD /` (R2). Browsers get real labels, plus a list/revoke IPC pair (227–230, `pairing.manage.v1`) behind a Settings "Paired browsers" list, with no automatic pruning (R3). |
| 3 | Saved connections | WEB-002, WEB-004, WEB-003, WEB-010 | Save with Remember runs the existing test-and-remember round trip, so a password is kept only when the login works; no IPC change (R4). The reconnect form gets a default name (R5). A one-time `navigator.storage.persist()` and a Diagnostics row (R6). A banner on `127.0.0.1` / `[::1]` names `localhost` (R6). `EffectivePort` is `[JsonIgnore]` (R7). |
| 4 | Formatter | FMT-001…005, SSMS-004 | Fixes at the confirmed seams: the procedure-parameter paren rule (R8); a moved `BEGIN` takes its owner's indent (R9); a batch start after `GO n` (R10). Spacing and comments are fixed from failing cases (R11). A final pipeline step keeps the input's line endings (R12). Five new corpus files are checked for every style. |
| 5 | Advisory markers | UX-001, SSMS-001, SSMS-002 | `squiggleStyle` becomes `marker` (default) or `underline`, with a one-time migration of the never-read old default. Information and Hint findings then get only the existing margin dot in SSMS and the gutter dot in the web (CSS class). There is a new Options and Settings row, and `showAdvisoryHints` is wired to its documented lightbulb meaning (R13). An opened file is analysed once the engine connects (R14). |
| 6 | Syntax errors | WEB-005 | An opt-in parse-error finding (`SYNTAX`) in the shared analysis engine, used by the web only. Parse works offline in the browser and sets the red mark automatically, with honest status text (R15). |
| 7 | Workspace | SN-001, WEB-013, WEB-011, WEB-014, WEB-012, UX-002, SSMS-003 | User snippets are merged into completion (R16). The explorer says "may be incomplete" when the engine's login lacks VIEW DEFINITION, a cause measured on this machine (R17). Expand refactorings use the cached schema snapshot (R18). Explorer empty state, Escape and focus on the Refactor dialog, plain import messages (R19). Find Invalid Objects counts the references checked (R20). |
| 8 | AI prompts | AI-001, AI-002 | The schema section and rule appear only when a schema is shared. Web Fix is disabled without an error, as in SSMS (R21). |
| 9 | Documentation | DOC-001…003 | New sections and symptom headings (headings are fully indexed by site search), a configuration summary and link fix, and docs-quality tests (R22). |
| 10 | Accessibility | WEB-007…009 | Editor name, badge tokens, the tab close button as a sibling, labels, headings and landmarks. A new axe gate across 6 routes × 3 themes (R23). |

Every issue lands with a permanent test that fails before its fix ([quickstart.md](./quickstart.md) §2, R24). WEB-006
and an engine-held connection list are out of scope (spec Assumptions).

**Spec amendments made at plan time** (recorded in spec.md):

- **A1 (SSMS-001):** `showAdvisoryHints` keeps its documented meaning, lightbulbs for findings without an automatic
  fix, instead of "hide hint markers". The marker default already keeps advisory findings off the text.
- **A2 (WEB-013):** the measured cause is SQL Server metadata visibility for `NT AUTHORITY\SYSTEM`, so the acceptance
  becomes "7 when the engine's account can see them; otherwise the groups say why".

## Technical Context

**Language/Version**:

- C# (LangVersion latest) on .NET Framework 4.7.2 for the SSMS shell (`AkmlSql.Shell.Shared` .projitems →
  `AkmlSql.Ssms22`);
- netstandard2.0 + net10.0 for `AkmlSql.Core`;
- .NET 10 for the engine, the shared libraries (Analysis, Formatting, IntelliSense, AI), the web edition (Blazor
  WebAssembly), the site, the CLI and all test projects;
- JavaScript ES modules for the web helpers; PowerShell 5.1 and Inno Setup 7 Pascal Script for the installer.

**Primary Dependencies**:

- Existing: VS SDK `17.14.*` (editor taggers, glyphs), ScriptDom `TSql170Parser`, MessagePack (IPC), Serilog,
  System.Text.Json, CodeMirror 6 (bundled), xunit 2.x, bUnit, Playwright `1.*` and the node test runner.
- **New (test-only):** `Deque.AxeCore.Playwright` `4.10.*` in `tests/AkmlSql.Web.E2E.Tests` (Complexity Tracking).

**Storage**:

- `config.json` › `codeAnalysis`: `squiggleStyle` values `marker|underline` and the new marker
  `squiggleStyleVersion`.
- Web IndexedDB: `savedSqlConnections` loses the written `effectivePort`, and the `analysisSettings` record gains
  `AdvisoryDisplay`; both are additive, with no DB version change.
- `localStorage` key `akml.originBanner.dismissed`.
- The engine's `tokens.json` is unchanged in shape; labels are richer for new mints.
- A new static `{app}\Web\akml-engine.json`.

**Testing**:

- Core.Tests, Formatting.Tests (parity corpus + `sp047-*` cases), Analysis.Tests, AI.Tests, Engine.Tests,
  Shell.Shared.Tests (STA), Web.Tests (bUnit + node), Web.E2E.Tests (Playwright, serial, Debug builds), Site.Tests,
  Installer.Tests, E2E.Tests (`PublishedCli`).
- Live-SQL, desktop and SSMS checks use the existing opt-in categories and the quickstart's manual rows.

**Target Platform**: Windows x64 (SSMS 22, the engine as a service or launched by SSMS, IIS or standalone hosting);
Chromium, Edge and Firefox for the web edition; the product site (static SSR).

**Project Type**: desktop extension + out-of-process engine + Blazor WASM web app + CLI + installer + static site.

**Performance Goals**:

- SC-002: first engine pairing < 30 s. The manifest fetch is one same-origin GET with a 5 s cap, after `HEAD /`.
- Format: the added line-ending step is O(n), well inside the current p50 budget.
- Analysis: the parse-error finding reuses the parse the engine already does, with no second parse.
- Completion: user snippets come from a cached list, with no IndexedDB read per keystroke.
- Phase A: one extra scalar query (`HAS_PERMS_BY_NAME`) on the open connection.

**Constraints**:

- IPC append-only: 227–230 and `SchemaPhasePayload` key 8 are new; the meaning of `TotalScanned` changes for peers
  that ship in one installer.
- No password in the browser or a log (FR-010).
- No theme CSS hand edits (Principle II): a new token pair, only if needed, goes through `docs/theme-tokens.json`.
- The parity and completion ratchets may not fall.
- The shell stays thin (Principle I): the line-ending step is in the shared formatter, not the shell.

**Scale/Scope**: 32 issues; about 60 source files touched across 13 projects (the source tree below); about 40 new or
extended test classes; 5 corpus inputs plus goldens; 6 doc files; 1 new installer helper script; 1 new test-only
package.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 (end of file).*

| Principle | Status | Evidence |
| --- | --- | --- |
| I. Process isolation & host safety | PASS | **Kept out of the shell:** analysis, formatting and prompt changes are in the shared libraries (Analysis, Formatting, AI); pairing, Phase A and Find Invalid Objects are in the engine. **In the shell:** UI and host wiring only (tagger presentation, an Options row, deferred analysis through existing helpers, dialog text). Line endings are fixed in the shared pipeline, not the shell (R12). |
| II. Build integrity | PASS | Shell projects built with MSBuild; one-pass solution build is the gate. Theme changes go through `docs/theme-tokens.json` + the generator (`-CheckOnly` stays green). No SDK or toolchain change. The CLI gains a release-gate step instead of relying on a trimmed build silently. |
| III. Tests & corpora non-regressible | PASS | Every issue lands with a test that fails before its fix (FR-038, quickstart §2). New `sp047-*` corpus cases; goldens captured from the fixed formatter and reviewed by hand. No ratchet lowered; any golden change must be one the new rules require. The completion corpus is untouched. |
| IV. Git consent | PASS | The plan makes no git mutation. Implementation delivers uncommitted changes and asks. The branch was created by `/speckit.specify` at the owner's command. |
| V. Simplicity & convention fidelity | PASS | Reuse first: the existing test-and-remember IPC (WEB-002), `BearerTokenStore.List/RevokeByHash` (ENG-001), `MigrateTriggerDelay`'s marker pattern (SSMS-001), `WarningGlyph` and `lintGutter` (UX-001), `WhenEngineReadyAsync` / `TriggerReanalysis` (SSMS-002), `TsqlParserService` in WASM (WEB-005), the shared dialog focus helpers (WEB-012), the `HAS_PERMS_BY_NAME` style (WEB-013). The two additions that look like machinery are justified below. |

**Additional constraints**:

- Path checks: the installer helper refuses reparse points and non-existent roots (canonical path).
- Size limits are unchanged.
- Config writes are atomic (the migration is persisted by the normal save).
- Secrets: no token, password or hash is ever logged; revoke logs labels only.
- Documentation currency: `doc/configuration.md`, `doc/analysis-rules.md`, `doc/ipc-api.md`, the topics pages,
  `doc/progress.md` and `CLAUDE.md` are updated in the same change.

## Project Structure

### Documentation (this feature)

```text
specs/047-qa-audit-remediation/
├── plan.md                          # this file
├── spec.md                          # clarified 2026-11-21; plan-time amendments A1, A2
├── research.md                      # Phase 0 — R1–R24 (causes, decisions, alternatives)
├── data-model.md                    # Phase 1 — manifest, pairing entry, saved connection, settings, payload key 8, …
├── quickstart.md                    # Phase 1 — build, tests per issue, validation scenarios V1–V22 (acceptance gate)
├── contracts/
│   ├── engine-pairing.md            # akml-engine.json, page-engine order, failure UI, browser label, IPC 227–230, UI
│   ├── connections-and-storage.md   # Save with Remember, prefill, EffectivePort, persistence, origin banner
│   ├── analysis-display.md          # severity presentation, squiggleStyle + migration, showAdvisoryHints, SYNTAX, Parse, open-file analysis
│   ├── formatter.md                 # sp047 corpus, rules asserted per style, seams, line endings
│   ├── workspace-ai.md              # snippets, explorer, refactor menu, imports, Find Invalid Objects, AI prompts
│   └── cli-docs-a11y.md             # CLI release gate, docs content and checks, accessibility + axe gate
├── checklists/requirements.md
└── tasks.md                         # /speckit.tasks output (not created here)
```

### Source Code (repository root)

```text
src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj                 # TrimmerRootAssembly ×2, JSON reflection switch (R1)
build.ps1 · doc/Deploy-Build-Release.ps1                     # "Analyzer CLI (smoke)" release gate (R1)

src/AkmlSql.Installer/
├── web-engine-manifest.ps1                                  # NEW — writes/removes {app}\Web\akml-engine.json (R2)
├── web-installer.iss                                        # Web_PostInstall calls it (IIS/None), -Remove for Service
└── web-hosting-repair.ps1 · web-iis-repair.ps1              # rewrite it on a port/exposure change

src/AkmlSql.Core/
├── Config/AppSettings.cs                                    # SquiggleStyle default "marker", SquiggleStyleVersion (R13)
├── Config/ConfigManager.cs                                  # MigrateSquiggleStyle (R13)
├── Ipc/RpcMessage.cs                                        # PairingList 227/228, PairingRevoke 229/230 (R3)
├── Ipc/Messages/PairingMessages.cs                          # NEW (R3)
└── Ipc/Messages/SchemaPhasePayload.cs                       # Key 8 MetadataLimited (R17)

src/AkmlSql.Analysis/AnalysisEngine.cs                       # reportParseErrors → SYNTAX findings (R15)
src/AkmlSql.Formatting/
├── Rules/ParenthesisRules.cs                                # FMT-001 (R8)
├── Rules/ControlFlowRules.cs                                # FMT-002 (R9)
├── Pipeline/LayoutEngine.cs  (+ SqlPrompt/Layout/SqlPromptPrinter.cs if its case fails)   # FMT-003 (R10)
├── Layout/* · Rules/WhitespaceRules.cs · Rules/ListRules.cs # FMT-004/005 at the seam the failing case shows (R11)
└── Pipeline/FormatterPipeline.cs                            # keep input line endings (R12)
src/AkmlSql.AI/Prompts/PromptTemplates.cs · IndexAnalysisPrompt.cs · OptimizePrompt.cs     # AI-001 (R21)
src/AkmlSql.IntelliSense/Schema/DatabaseCache.cs             # MetadataLimited (R17)

src/AkmlSql.Engine/
├── Pairing/BearerTokenStore.cs · Pairing/BridgeConnection.cs   # TokenHash per connection (R3)
├── Handlers/Handshake/HandshakeHandler.cs                   # records TokenHash (R3)
├── Handlers/Pairing/PairingManageHandler.cs                 # NEW — list/revoke (R3)
├── Transports/WebSocketTransport.cs                         # close sockets of a revoked pairing (R3)
├── Capabilities.cs · EngineHandlerRegistry.cs               # pairing.manage.v1; register handler
├── Schema/SchemaMetadataService.cs                          # VIEW DEFINITION probe in Phase A (R17)
├── Handlers/Schema/SchemaPhaseSerializer.cs                 # MetadataLimited → key 8 (R17)
└── Handlers/Refactoring/FindInvalidObjectsHandler.cs        # total references checked (R20)

src/AkmlSql.Shell.Shared/
├── Analysis/DiagnosticTagger.cs                             # marker mode (R13)
├── Analysis/LightbulbProvider.cs                            # showAdvisoryHints (R13)
├── Analysis/AnalysisController.cs                           # deferred analysis until the engine connects (R14)
├── Editor/ConnectionWiringHelper.cs                         # re-analyse after ConnectionChanged (R14)
├── Dialogs/Pages/CodeAnalysisPage.cs                        # "Advisory findings" row (R13)
└── Refactoring/FindInvalidObjectsDialog.cs                  # summary text, hidden empty grid (R20)

src/AkmlSql.Web/
├── Services/PageEngine.cs · Services/IEngineAutoConnect.cs  # manifest step; LocalPairResult (R2)
├── Services/IEngineBridge.cs                                # browser label (R3)
├── Services/IPairingService.cs                              # NEW — list/revoke over the bridge (R3)
├── Services/ISqlConnectionService.cs                        # TestAndRememberAsync (R4)
├── Services/ISavedSqlConnectionStore.cs · Services/IConnectionStore.cs   # [JsonIgnore]; persistence request (R6, R7)
├── Services/BrowserStorageState.cs                          # NEW (R6)
├── Services/IAnalyserService.cs                             # reportParseErrors: true (R15)
├── Services/IAnalysisSettingsStore.cs                       # AdvisoryDisplay (R13)
├── Services/ICompletionService.cs · Services/ISnippetStore.cs   # user snippets + Changed event (R16)
├── Services/SchemaSnapshotCache.cs                          # NEW — payload → DatabaseCache (R18)
├── Services/IRefactoringService.cs                          # SchemaCache for the three Expand kinds; reasons (R18)
├── Services/Settings/SettingsCatalog.cs                     # diagnostics-storage, analysis-advisory-display
├── Pages/Editor.razor                                       # Parse offline + auto mark + status; refactor menu buttons/Escape; origin banner (R15, R19, R6)
├── Pages/Styles.razor · Pages/Snippets.razor                # import messages; badge; preview a11y (R19, R23)
├── Shared/ConnectionManagerModal.razor                      # Save with Remember; prefill name (R4, R5)
├── Shared/StatusBar.razor                                   # one-click failure notice (R2)
├── Shared/SchemaTreeComponent.razor                         # metadata-limited note; filter empty state (R17, R19)
├── Shared/RefactorPreviewPanel.razor                        # Escape, focus (R19)
├── Shared/AiPanel.razor                                     # Fix disabled without an error (R21)
├── Shared/TabStrip.razor · Shared/MainLayout.razor          # close sibling; landmarks (R23)
├── Shared/ConnectionPickerComponent.razor                   # badge token (R23)
├── Shared/Settings/{ConnectionsSection,DiagnosticsSection,CodeAnalysisSection}.razor   # paired browsers; storage row; advisory row
└── wwwroot/js/{akml-page-engine,akml-bridge,akml-indexeddb,akml-editor}.js             # readManifest; browserLabel; persist; marker class; aria

docs/theme-tokens.json (+ generated CSS)                     # only if a badge token pair is missing (R23)
doc/topics/{connecting,troubleshooting,browser-setup}.md · doc/configuration.md · doc/analysis-rules.md · doc/ipc-api.md · doc/progress.md · CLAUDE.md

tests/
├── fixtures/cli/{multi.sql,sem1.sql,pe004-off.casettings}   # NEW (R1)
├── format-parity/corpus/sp047-0{1..5}-*.sql (+ goldens)     # NEW (R8–R11)
└── <suite>/…  — the test classes listed in quickstart.md §2
```

**Structure Decision**: no new project. Each fix lives where the defect is (R1–R23), and shared behaviour stays in the
shared libraries so SSMS, the web edition and the CLI change together. New files are limited to one installer helper,
one IPC message file, one engine handler, four small web services and committed test fixtures.

## Complexity Tracking

No constitution violation to justify. Three choices recorded for the reviewer:

| Choice | Why | Simpler alternative rejected because |
| -------- | ----- | -------------------------------------- |
| Test-only package `Deque.AxeCore.Playwright` | SC-008 and FR-037 need an automated WCAG scan that runs with the existing Playwright E2E suite; it is pinned, typed and never shipped | Vendoring `axe.min.js` means a hand-copied third-party file with no version source; manual axe runs would not satisfy FR-038 |
| New IPC pair 227–230 for pairings | The owner chose label + per-pairing revoke from the browser (Q5); the store already has `List` / `RevokeByHash`, and nothing exposes them | The installer repair helper as the only revoke path has no UI and needs elevation; reusing an existing message type would overload its meaning |
| Static `akml-engine.json` written by the installer | The installer is the only party that knows the bridge port of an IIS install | Port probing is slow guesswork; an IIS header or ARR proxy adds configuration and a new failure mode (R2) |

## Post-design Constitution re-check (after Phase 1)

| Principle | Status | Note |
| --- | --- | --- |
| I | PASS | The contracts keep every analysis, format and prompt rule in shared code. The shell contract is presentation and wiring only (analysis-display.md §2, §5). |
| II | PASS | Contracts route any new colour through the token file and generator. The build gate gains a CLI smoke step. No SDK or toolchain change. |
| III | PASS | quickstart.md §2 maps every issue to a test that fails first. The formatter contract asserts rules independently of goldens and forbids capturing goldens from broken output. |
| IV | PASS | Nothing committed. |
| V | PASS | The data model adds only fields the fixes need (append-only). No configurability beyond the two settings the spec requires (`squiggleStyle` values, web `AdvisoryDisplay`). |
