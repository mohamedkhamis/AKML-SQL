---
description: "Task list for spec 036 — Kimi provider, schema-aware AI chat, copy actions, Options hover, update channel"
---

# Tasks: Readable Options Navigation, Kimi-Capable Schema-Aware AI Chat, and a Working Update Channel

**Input**: Design documents from `/specs/036-kimi-chat-updater-fixes/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks ARE included. The spec requires them explicitly (FR-004: "enforced by an automated test that fails the build on regression"), every contract carries a Test coverage table, and Constitution III requires new behaviour to land with tests.

**Organization**: Grouped by user story. The five stories touch near-disjoint file sets and are independently shippable — any one can be delivered alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5, mapping to the user stories in spec.md

## Path Conventions

Repository root is `c:\Repos\AKML\AKML-SQL`. Source under `src/`, tests under `tests/`, scripts under `scripts/`, docs under `doc/`. Shell projects build with full MSBuild only — never `dotnet build`.

---

## Phase 1: Setup (Environment)

**Purpose**: Get a working dev + verification environment. No production code changes.

- [X] T001 Build the solution and publish the engine per `doc/deployment.md`, deploy to SSMS 22, and clear the MEF cache at `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/`
- [X] T002 [P] Provision the verification database: at least two tables, varied column types, a primary key and a foreign key between them — record the object names in `specs/036-kimi-chat-updater-fixes/quickstart.md` sign-off notes
- [X] T003 [P] Stage a local update-manifest fixture (a newer version than installed) for the US5 local scenarios, served from the dev site or a local file URL

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the ratchet floor that every later phase is measured against.

**⚠️ NOTE ON THIS PHASE**: This feature has unusually little foundational work, and that is a real finding rather than an omission. The five slices touch disjoint files — schema binding (AI handlers + chat panel), provider config (factory + Options page), chat rendering (chat panel), Options theming (SettingsWindow), and the update channel (updater + deploy script). Nothing structural blocks them. The two tasks below block everything only because Constitution II and III require a known-green baseline before any change.

- [X] T004 Confirm the full solution builds green in one pass: `MSBuild AKML-SQL.slnx -t:Restore` then `-t:Build -p:Configuration=Release -m`
- [X] T005 [P] Record the current format-parity golden pass count and the completion-corpus pass rate as the ratchet floor, in `specs/036-kimi-chat-updater-fixes/quickstart.md` sign-off notes — these may only go up (Constitution III)
**Checkpoint**: Baseline captured — any user story phase may now begin.

---

## Phase 3: User Story 1 — The AI chat actually knows the database (Priority: P1) 🎯 MVP

**Goal**: Every schema-aware AI request binds to the editor's real session so the assistant sees the connected database, and general questions receive the full object inventory instead of an incidentally-filtered subset.

**Independent Test**: Connect an editor to the verification database, ask "list the tables in this database" and "describe the columns of &lt;known table&gt;". Both answers must contain the real names and no invented ones. Requires no provider change, no theming change, no updater change.

### Tests for User Story 1

- [X] T006 [P] [US1] Create `tests/AkmlSql.AI.Tests/SchemaContextAssemblyTests.cs` covering: general prompt yields the full inventory (FR-024); a prompt whose noise token incidentally substring-matches one object still yields the full inventory (FR-025, research R6); a named object is promoted to level-3 detail with columns, PK and FKs (FR-023); FK 1-hop neighbours are promoted; exceeding the budget sets `Truncated` and `TotalObjectCount` (FR-026); an unbound context renders distinctly from a connected-but-empty database (FR-028)
- [X] T007 [P] [US1] Create `tests/AkmlSql.Engine.Tests/AiSessionBindingTests.cs`: a request carrying the session id of a connected `SessionState` resolves to that database's cache and produces a non-empty context; a request carrying an unknown id produces the explicit unbound context, not an empty one (FR-021)
- [X] T008 [P] [US1] Create `tests/AkmlSql.Shell.Shared.Tests/AiChatSessionBindingTests.cs`: the panel issues no request when no editor is bound and shows the no-connection message; the header text reflects the bound server and database (FR-027, FR-028)
- [X] T009 [P] [US1] Add a ghost-text case to `tests/AkmlSql.AI.Tests/` asserting inline completion still assembles at detail level 1 (latency path unchanged)

### Implementation for User Story 1

- [X] T010 [US1] Extend `TryGetActiveEditor` in `src/AkmlSql.Shell.Shared/Refactoring/RefactorCommandHelper.cs` to expose whether the session id came from the buffer's `AkmlSqlSessionId` property or from the `Guid.NewGuid()` fallback — keep the existing fallback intact, refactoring depends on it (research R2)
- [X] T011 [US1] Replace the fabricated `SessionId = Guid.NewGuid().ToString("N")` at `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs:236` with the resolved editor session id, resolved at send time on every message; refuse to send when unbound
- [X] T012 [P] [US1] Replace the fabricated session id at `src/AkmlSql.Shell.Shared/Ai/GhostTextAdornment.cs:134` — read `AkmlSqlSessionId` from the adornment's own text view buffer, which it already has
- [X] T013 [P] [US1] Replace both fabricated session ids in `src/AkmlSql.Shell.Shared/Commands/TextToSqlCommand.cs` (line ~236 in `ExtractPromptFromEditor`, line ~137 in the catch) with the resolved editor session id
- [X] T014 [P] [US1] Audit the `sessionId` variable actually passed by `src/AkmlSql.Shell.Shared/Commands/AiExplainCommand.cs`, `AiFixCommand.cs`, `AiOptimizeCommand.cs` and `AiIndexAnalysisCommand.cs` and fix any that resolve to a fabricated id — passing a variable is not proof it holds a real one (research R1)
- [X] T015 [P] [US1] Add `SchemaContextMaxObjects` (JSON `schemaContextMaxObjects`, default 500) to `AiSettings` in `src/AkmlSql.Core/Config/AppSettings.cs`
- [X] T016 [P] [US1] Add `Truncated`, `TotalObjectCount` and `DetailedObjectNames` to `SchemaContext` in `AkmlSql.Core.Models.Ai`
- [X] T017 [US1] Rewrite the assembly in `src/AkmlSql.AI/Context/SchemaContextBuilder.cs` per `contracts/schema-context.md`: always include the full inventory at level 1, promote prompt-named objects and their FK 1-hop neighbours to level 3, apply the budget last, and delete the filter-then-cap path so relevance can no longer remove inventory (depends on T015, T016)
- [X] T018 [US1] Emit the truncation notice and distinguish "no database connection" from "connected, no objects" in `src/AkmlSql.AI/Context/SchemaContextFormatter.cs` — the current `"(No schema objects available)"` conflates them (depends on T016)
- [X] T019 [US1] Replace the hardcoded `compressionLevel: 2` with the per-feature level from `contracts/schema-context.md` across the seven handlers in `src/AkmlSql.Engine/Handlers/Ai/` (chat, explain, fix, optimize, index analysis, text-to-SQL at level 3; ghost text at level 1)
- [X] T020 [US1] Wire the chat header to the active connection in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` and `AiChatToolWindow.cs` — drive the existing but uncalled `SetDatabaseContext`, and re-resolve when the active window changes (FR-027)
- [X] T021 [US1] Surface the schema-loading state in the chat panel using the existing `MessageTypes.SchemaStatusRequest` (80) signal that `SchemaProgressMargin` already polls — do not add a second progress mechanism (FR-029)
- [X] T022 [US1] Explain the privacy-mode consequence in the chat panel when `IdentifierMap` is non-empty: say why real names cannot appear and name the `privacyMode` setting (FR-030)
- [X] T023 [US1] Measure context assembly on a 500-object database and confirm it adds under 200 ms per request; record the figure in the quickstart sign-off notes

**Checkpoint**: Ask the chat about the verification database and get real answers. Quickstart scenarios 26–40.

---

## Phase 4: User Story 2 — Choose Kimi and use my own key (Priority: P1)

**Goal**: Kimi is a selectable, testable, first-class provider; API keys move off plaintext; and the two existing provider-name mismatches that break Azure OpenAI and LM Studio are fixed in the same mapping.

**Independent Test**: Select Kimi in Options, paste a key, press Test connection, save, reopen, send one non-schema chat message. Requires no schema work, no theming, no updater.

### Tests for User Story 2

- [X] T024 [P] [US2] Create `tests/AkmlSql.Core.Tests/ApiKeyProtectorTests.cs`: wrap/unwrap round-trip, `dpapi:` prefix detection, plaintext passthrough on read (backward compatibility), and a guard asserting the entropy source string is exactly `"AkmlSql-ApiKey-v1"`
- [X] T025 [P] [US2] Create `tests/AkmlSql.Core.Tests/ProviderIdNormalizationTests.cs` covering every row of the alias table in `contracts/kimi-provider.md`, including the legacy `AzureOpenAI` and `LMStudio` spellings
- [X] T026 [P] [US2] Extend `tests/AkmlSql.AI.Tests/ProviderModelMismatchTests.cs`: `kimi-*` and `moonshot-*` detect as `kimi`; `gpt-4o` under provider kimi is refused; `kimi-latest` under provider openai is refused; unrecognised names still return null
- [X] T027 [P] [US2] Create `tests/AkmlSql.AI.Tests/KimiProviderFactoryTests.cs`: the factory builds a client for `kimi` with the default endpoint applied, and throws a message naming Kimi when the key or model is missing
- [X] T028 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiProviderModelAutofillTests.cs`: select → save → load round-trip for **all eight** providers (this is the regression Azure OpenAI and LM Studio fail today), plus autofill leaves a user's unrecognised model name untouched
- [X] T029 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiFailureMessageTests.cs`: the five FR-014 causes each render distinctly and no raw provider payload reaches the user
- [X] T030 [P] [US2] Extend the engine handler tests for `AiProviderTestHandler` with one case per FR-014 taxonomy row, asserted against synthesised exceptions

### Implementation for User Story 2

- [X] T031 [P] [US2] Create `src/AkmlSql.Core/Config/ApiKeyProtector.cs` with `Protect`/`Unprotect`/`IsProtected`, carrying the entropy `SHA256(UTF8("AkmlSql-ApiKey-v1"))` verbatim from `CredentialManager.cs:23` — it must compile for both `netstandard2.0` and `net10.0`, which is why it lives in Core (research R4)
- [X] T032 [US2] Reduce `src/AkmlSql.Engine/Ai/Security/CredentialManager.cs` to a thin forwarder over `ApiKeyProtector` so existing engine call sites and tests are unchanged (depends on T031)
- [X] T033 [P] [US2] Create `src/AkmlSql.Core/Config/AiProviderIds.cs` with the canonical ids and the alias normalisation table from `contracts/kimi-provider.md`
- [X] T034 [P] [US2] Add Kimi to `src/AkmlSql.Core/Config/AiModelFamily.cs`: `Detect` returns `"kimi"` for names starting `kimi` or `moonshot`; `DefaultModelFor("kimi")` returns `"kimi-latest"`; unrecognised names still return null
- [X] T035 [US2] Add the `"kimi"` case to `src/AkmlSql.AI/Providers/AiProviderFactory.cs` as its own family-guarded branch (require key, require model, `RequireModelFamily`, then delegate to `CreateOpenAiClient` with the default endpoint) — it must **not** fall through to the `custom` case, which deliberately skips the family guard; also normalise the provider id before the switch and add `"kimi" => "Moonshot (Kimi)"` to `FamilyDisplayName` (depends on T033, T034)
- [X] T036 [US2] Add "Kimi (Moonshot)" to the provider list in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` and convert the positional index→string switches in `Load` and `Save` to key off the canonical id — the positional coupling is how the Azure/LM Studio mismatch survived (research R8) (depends on T033)
- [X] T037 [US2] Wrap the API key with `ApiKeyProtector.Protect` on `Save` and unwrap on `Load` in `AiAssistancePage.cs`; reads must accept legacy plaintext so no migration step is needed (depends on T031, T036)
- [X] T038 [US2] Add a "Test connection" button beside the API key field in `AiAssistancePage.cs`, sending `MessageTypes.AiProviderTest` (77) with the **current dialog field values** per `contracts/ai-provider-test.md` — the message, DTOs and engine handler already exist and have never had a caller; use `AiIpcTimeouts.ForAiRequestMs`, show a busy state, and never log the key (depends on T036)
- [X] T039 [US2] Extend the exception mapping in `src/AkmlSql.Engine/Ai/AiProviderTestHandler.cs:122-129` to distinguish the five FR-014 causes; a 429 must read as quota/rate-limit and never as "AI is disabled"

**Checkpoint**: Kimi configurable, testable and working across every AI feature. Quickstart scenarios 8–18.

---

## Phase 5: User Story 3 — Get anything out of the chat (Priority: P2)

**Goal**: SQL blocks, whole messages, arbitrary selections and the whole conversation can all reach the clipboard.

**Independent Test**: Ask a question returning prose plus two SQL blocks; copy each block, the whole message, a mouse selection, and the conversation. Independent of provider and of schema access.

**⚠️ File overlap**: this story and US1 both edit `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`. They are logically independent but must not be edited simultaneously by two people.

### Tests for User Story 3

- [X] T040 [P] [US3] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatPanelCopyButtonTests.cs`: the per-message copy button survives the text-host change (FR-016); each SQL block gets its own labelled action (FR-015); the conversation copy attributes and orders every turn (FR-018); a clipboard failure leaves the bubble present and re-copyable (FR-019); every copy control carries an automation name (FR-020)

### Implementation for User Story 3

- [X] T041 [US3] Replace the `TextBlock` in `CreateMessageBubble` (`src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs:341-349`) with a read-only, borderless, transparent `TextBox` so text is selectable; preserve the existing two-column `Grid` layout, the `SetResourceReference` theming on `Foreground`, and confirm the bubble does not swallow the `Enter` key the input box binds to send (FR-017, research R9)
- [X] T042 [P] [US3] Label each per-SQL-block copy action so it is clear which block it belongs to when a message contains several, in `AddAssistantMessage` (`AiChatPanel.cs:299-323`) (FR-015)
- [X] T043 [P] [US3] Add a copy-conversation action built from the existing `_history` list of `ChatTurnDto`, attributing each turn to its speaker and preserving order (FR-018)
- [X] T044 [US3] Tell the user when a copy fails in `OnCopyMessageClick` — the handler currently returns silently on exception — and leave the message on screen and re-copyable (FR-019)
- [X] T045 [P] [US3] Confirm every copy control is keyboard-reachable and carries an `AutomationProperties.Name`, matching the existing per-message button (FR-020)

**Checkpoint**: Nothing in the chat has to be retyped. Quickstart scenarios 19–25.

---

## Phase 6: User Story 4 — Options navigation stays readable on hover (Priority: P2)

**Goal**: No hover or selection state in the Options dialog produces unreadable text, in any shipped theme.

**Independent Test**: Open Options in each theme and sweep the pointer down the navigation, including over the selected item. Purely visual; independent of everything else.

### Tests for User Story 4

- [X] T046 [P] [US4] Create `tests/AkmlSql.Shell.Shared.Tests/OptionsHoverContrastTests.cs`: for every state (normal, hovered, selected, selected-and-hovered) × every theme (Light, Dark, host-derived, High Contrast), resolve the effective label and background brushes from the style's setters **and triggers**, and assert a contrast ratio of at least 4.5:1 — extend the existing `GetForegroundFromStyle` reflection helper at `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs:355-362` rather than writing a new one; use `[StaFact]` (FR-004)

### Implementation for User Story 4

- [X] T047 [US4] Fix the navigation tree hover in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:415-422`: pair a token-derived foreground with the `TreeHover` background on the mouse-over trigger, and add a `MultiTrigger` for selected-and-hovered that keeps a readable pair. Reordering alone is insufficient — it would only move the problem to the hover affordance (research R7). Never hardcode a colour; High Contrast maps these tokens to system brushes (FR-001, FR-002, FR-005)
- [X] T048 [P] [US4] Apply the same foreground pairing to the search-results list at `SettingsWindow.cs:735-742` — it does not show the bug today only because its triggers happen to be ordered the other way, leaving it one palette change from the same defect (FR-003)
- [ ] T049 [US4] Verify under Windows High Contrast that the paired brushes still come from `SystemColors` via the tokens and remain readable (FR-004 edge case)

**Checkpoint**: Options is usable at a glance in every theme. Quickstart scenarios 1–7.

---

## Phase 7: User Story 5 — Updates reach me, install cleanly, and are proven (Priority: P3)

**Goal**: The update check reaches a live endpoint, the guided flow downloads and verifies the installer, and the whole path is demonstrated on a clean machine.

**Independent Test**: Point the check at a staged newer manifest, follow the notification through download, verification and the confirmation dialog. Independent of all AI and theming work.

### Tests for User Story 5

- [X] T050 [P] [US5] Update `tests/AkmlSql.Core.Tests/ConstantsTests.cs:45` — it currently asserts the dead `updates.akmlsql.com` string and has been locking the bug in; assert the new endpoint is HTTPS and on the live site host (FR-033)
- [X] T051 [P] [US5] Add tests to `tests/AkmlSql.Site.Tests/`: the generated manifest and the newest `releases.json` entry name the same version, file and hash (FR-036); the manifest's `downloadUrl` is always absolute HTTPS, never a site-relative path
- [X] T052 [P] [US5] Add tests to `tests/AkmlSql.Core.Tests/` for version comparison: strictly-newer only, equal and older report no update, SemVer pre-release suffixes stripped before comparison (FR-037)
- [X] T053 [P] [US5] Add tests to `tests/AkmlSql.Installer.Tests/`: a checksum mismatch deletes the file, exits 2 and sets `FailureReason` (FR-040); a cancelled download leaves no `.partial` on disk (FR-039a); result-file writes stay atomic
- [X] T054 [P] [US5] Add a test to `tests/AkmlSql.Shell.Shared.Tests/` that the manual check calls `LaunchUpdater` directly and runs even when `LastUpdateCheck` is recent (FR-042)

### Implementation for User Story 5

- [X] T055 [P] [US5] Point `Constants.UpdateManifestUrl` in `src/AkmlSql.Core/Constants.cs:24` at `https://akml.khamis.work/update-manifest.json` and update the stale doc comment on `src/AkmlSql.Core/Update/UpdateManifest.cs` that still names the dead host (FR-033)
- [X] T056 [P] [US5] Add `VerifiedInstallerPath`, `DownloadState` and `FailureReason` to `src/AkmlSql.Core/Update/UpdateResult.cs` per `data-model.md` (FR-039, FR-039a, FR-041)
- [X] T057 [US5] Add the `--download` mode to `src/AkmlSql.Updater/Program.cs` per `contracts/update-manifest.md`: download to `%AppData%\AKML SQL\cache\`, verify SHA-256 against the manifest, delete and exit 2 on mismatch, set `VerifiedInstallerPath` on success, keep every result-file write atomic, delete any `.partial` in a `finally`, reject non-HTTPS URLs, and stay anonymous (FR-034, FR-039a, FR-040) (depends on T056)
- [X] T058 [P] [US5] Emit `update-manifest.json` into `src/AkmlSql.Site/wwwroot/` from the **same `$entry` object** already built for `releases.json` in `scripts/deploy-site-iis.ps1:158-172` — one write, two files, no second computation of version or hash; it must stay inside the staging block that precedes `# --- 1. Publish ---`, because `MapStaticAssets` resolves its asset list at build time and a file dropped in afterwards would 404 silently (FR-036)
- [X] T059 [US5] Replace the browser hand-off in `src/AkmlSql.Shell.Shared/Commands/CheckUpdateCommand.cs:56-70` with the guided flow: launch the updater with `--download`, show progress with a working cancel, then a confirmation dialog naming the applications that must close, then `Process.Start` on the `Path.GetFullPath()`-canonicalised verified path with the installer's normal UI — never `/VERYSILENT` (FR-039, FR-039a, FR-040) (depends on T057)
- [X] T060 [US5] Build the confirmation dialog following the FR-005 safety convention — Cancel is `IsCancel = true` and holds initial focus on `Loaded`, the proceed button is not the default — modelled on `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs`; declining must install nothing, close nothing and retain the offer (spec scenario 4a)
- [X] T061 [P] [US5] Make the manual "Check for updates" command call `UpdateLauncher.LaunchUpdater()` directly, bypassing the 24-hour throttle in `LaunchIfDue`, and report all three outcomes — up to date, update available, check failed (FR-042)
- [X] T062 [US5] Run quickstart scenarios 41–51 locally against the staged manifest fixture from T003 — dated results recorded in `quickstart.md` sign-off notes (2026-09-02)
- [ ] T063 [US5] **Manual, cannot be automated** — run quickstart scenarios 52–60 on a clean Windows machine: install the previous published build, populate it (settings, query history, a snippet, a format style, a saved SQL credential), publish a newer release, follow the flow to a completed in-place upgrade, verify every artefact survived and that the in-product, installed and published versions agree, then record the dated evidence in `doc/progress.md` (FR-043, FR-044, FR-045, FR-046, SC-009, SC-011). **Pending user availability: needs a clean machine and a real release publish; not attempted in this branch.**

**Checkpoint**: Updates are discoverable, verified and proven. Quickstart scenarios 41–60.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T064 [P] Document the AI provider connection test in `doc/ipc-api.md` — messages 77/177 exist but were never documented as unwired
- [X] T065 [P] Document `schemaContextMaxObjects` and the move of `ai.apiKey` to DPAPI-wrapped storage in `doc/configuration.md`
- [X] T066 [P] Document the update-manifest generation step and the `--download` mode in `doc/deployment.md`, and note that `/VERYSILENT` remains the unattended-deployment path only
- [X] T067 [P] Add the Kimi provider row to any provider list in `doc/` and to the AI Assistance page help text
- [X] T068 Add the spec-036 progress table to `doc/progress.md` with per-phase results and any deferred follow-ups, per the constitution's documentation-currency rule
- [X] T069 Rebuild the full solution green in one pass and re-run every touched test project
- [X] T070 Re-check the ratchets against the T005 floor: format-parity goldens and the completion-corpus rate must be unmoved or higher (Constitution III)
- [X] T071 Run the full `quickstart.md` validation and record sign-off per slice

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies
- **Phase 2 (Foundational)**: after Setup — blocks all stories only for the ratchet baseline
- **Phases 3–7 (User Stories)**: each depends only on Phase 2. They do not depend on each other
- **Phase 8 (Polish)**: after the stories you intend to ship

### Story dependencies

All five stories are independent. There are no cross-story logical dependencies. Two file-level overlaps require sequencing if worked in parallel:

| Overlap | Stories | Handling |
|---|---|---|
| `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` | US1 (session binding, header) and US3 (selectable text, copies) | Do not edit simultaneously; land US1's changes first if both are in flight |
| `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` | US2 only | none |

### Within User Story 1

T006–T009 (tests) → T010 (resolver) → T011–T014 (call sites, parallel) → T015–T016 (models, parallel) → T017–T018 (assembly, formatter) → T019 (handler levels) → T020–T022 (panel UX) → T023 (measurement)

### Within User Story 2

T024–T030 (tests, all parallel) → T031, T033, T034 (parallel foundations) → T032, T035 (consume them) → T036 → T037, T038 (page wiring) → T039 (engine taxonomy)

### Within User Story 5

T050–T054 (tests, all parallel) → T055, T056, T058 (parallel) → T057 (updater, needs T056) → T059 (shell, needs T057) → T060 → T061 → T062 (local validation) → T063 (manual, last)

---

## Parallel Execution Examples

**User Story 1 tests — launch together:**

```
T006 tests/AkmlSql.AI.Tests/SchemaContextAssemblyTests.cs
T007 tests/AkmlSql.Engine.Tests/AiSessionBindingTests.cs
T008 tests/AkmlSql.Shell.Shared.Tests/AiChatSessionBindingTests.cs
T009 ghost-text level-1 case in tests/AkmlSql.AI.Tests/
```

**User Story 1 call-site fixes — launch together after T010:**

```
T012 src/AkmlSql.Shell.Shared/Ai/GhostTextAdornment.cs
T013 src/AkmlSql.Shell.Shared/Commands/TextToSqlCommand.cs
T014 AiExplain / AiFix / AiOptimize / AiIndexAnalysis commands
```

**User Story 2 foundations — launch together:**

```
T031 src/AkmlSql.Core/Config/ApiKeyProtector.cs
T033 src/AkmlSql.Core/Config/AiProviderIds.cs
T034 src/AkmlSql.Core/Config/AiModelFamily.cs
```

**Cross-story — with multiple people, after Phase 2:**

```
Developer A: Phase 3 (US1, schema access)   — the MVP
Developer B: Phase 4 (US2, Kimi)
Developer C: Phase 6 (US4, hover) then Phase 7 (US5, updates)
```

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational
2. Phase 3 (T006–T023)
3. **Stop and validate**: quickstart scenarios 26–40. The assistant now answers real questions about the connected database — the single highest-value fix in this feature
4. Ship or demo

### Incremental delivery

1. Setup + Foundational → baseline captured
2. **US1** → schema-aware chat (MVP)
3. **US2** → Kimi selectable, keys encrypted, Azure/LM Studio unbroken
4. **US4** → Options readable (cheapest slice; can be pulled forward at any point)
5. **US3** → copy anything out of the chat
6. **US5** → update channel, ending with the manual clean-machine run

Each step is independently valuable and independently shippable.

### Suggested pull-forward

US4 (hover) is three implementation tasks and one test. If a quick visible win is wanted before the MVP lands, take Phase 6 first — it blocks nothing and nothing blocks it.

---

## Notes

- `[P]` means different files with no incomplete dependency
- Shell projects (`AkmlSql.Ssms22`, `AkmlSql.VS2026`, and anything importing `AkmlSql.Shell.Shared`) build with full MSBuild only — `dotnet build` fails on VSSDK's CodeTaskFactory
- Do not commit, stage or push anything — Constitution IV requires an explicit instruction for each git operation
- T063 is the only task that cannot be completed on the development machine
- The DPAPI entropy string in T031 must be copied byte-for-byte; changing it makes every already-stored API key unreadable
