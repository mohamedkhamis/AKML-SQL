---
description: "Task list for 037-multi-ai-agents"
---

# Tasks: Multiple AI Agents with Guided Setup

**Input**: Design documents from `/specs/037-multi-ai-agents/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Test tasks are included and are **not optional here**. Constitution III ("Tests & Parity
Corpora Are Non-Regressible") requires new behaviour to land with tests, and every contract in
`contracts/` carries a Test coverage table that this list implements.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested and
demonstrated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different files, no dependency on an incomplete task
- **[Story]**: `[US1]` … `[US6]`, mapping to the user stories in spec.md
- Every task names the exact file it touches

## Path Conventions

Repository root is `C:\Repos\AKML\AKML-SQL`. Sources under `src/`, tests under `tests/`, one test
project per source project. Shell projects (`AkmlSql.Shell.Shared` consumers) build with **full
MSBuild only** — never `dotnet build`.

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
```

## Phase-ordering note

US1 and US2 are both P1. **US1 is implemented first** (Phase 3), because the empty state is the
user's headline request and it is testable against a hand-written config. Phase 3 includes one
bridging task (T029) that makes the AI Assistance page edit the **active agent** through a working
copy — single-agent editing. Phase 4 (US2) then grows that same editor into the multi-agent list.
Nothing from T029 is thrown away.

---

## Phase 1: Setup

**Purpose**: Establish a trustworthy baseline and the one fixture the migration tests need.

- [ ] T001 Build the whole solution green in one pass before touching anything: `"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet` then `"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal`
- [ ] T002 [P] Record baseline pass counts for `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.AI.Tests`, `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.Shell.Shared.Tests`, plus the format-parity (977) and completion-corpus (1,342 / ~97.5%) ratchets, in `specs/037-multi-ai-agents/baseline.md`
- [ ] T003 [P] Add a legacy pre-agents config fixture (populated `ai.provider`, `ai.model`, a `dpapi:`-prefixed `ai.apiKey`, no `ai.agents`) at `tests/AkmlSql.Core.Tests/Config/Fixtures/legacy-single-provider-config.json`

**Checkpoint**: Baseline recorded. Any later red in T002's list is this feature's regression.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The agent model, the resolver, and the load-time wiring. **Every user story depends on
this phase.** It includes migration and mirroring, because a build that has the agent list but not
the migration would break every existing user — each phase must leave the product working.

**⚠️ CRITICAL**: No user story work begins until T016 passes.

- [ ] T004 [P] Create `AiAgent`, `AgentHealth`, `AgentHealthStatus` (string constants, not an enum) and `FeatureAgentAssignments` per data-model.md E1–E3 in `src/AkmlSql.Core/Config/AiAgent.cs` — plain `{ get; set; }` properties with `[JsonPropertyName]`, netstandard2.0-safe (no `init`, no records)
- [ ] T005 [P] Create the `AiFeature` enum (`Chat, TextToSql, Explain, Fix, Optimize, IndexSuggestions, GhostText`) per data-model.md E4 in `src/AkmlSql.Core/Config/AiFeature.cs`
- [ ] T006 Extend `AiSettings` with `Agents`, `ActiveAgentId`, `FeatureAgents` and `FallbackOrder` per data-model.md E5 in `src/AkmlSql.Core/Config/AppSettings.cs`, keeping every existing property untouched (depends on T004)
- [ ] T007 Create `AiAgentResolver` with `IsUsable` (S1), `RequiresApiKey`, `RequiresEndpoint`, `Active`, `ResolveFor`, `SuggestName`, `SuggestCopyName` per data-model.md E6 in `src/AkmlSql.Core/Config/AiAgentResolver.cs` (depends on T004, T005, T006)
- [ ] T008 Implement `AiAgentResolver.Project(AiSettings, AiAgent)` per `contracts/agent-model.md` § Projection in `src/AkmlSql.Core/Config/AiAgentResolver.cs` — connection fields and request parameters from the agent, every global concern from the settings, no aliasing of mutable members (depends on T007)
- [ ] T009 Implement `AiAgentResolver.MirrorActiveAgent(AiSettings)` (V18 — no-op when `Agents` is empty, blanking the flat fields when the list is non-empty but nothing is active) and `AiAgentResolver.Normalize(AiSettings)` (migration V14, a call to `MirrorActiveAgent`, derived `Enabled` V19) in `src/AkmlSql.Core/Config/AiAgentResolver.cs` — both idempotent, `Normalize`'s migration guarded on `Agents.Count == 0 && Provider != ""`, neither throwing, and `ApiKey` copied **verbatim** (depends on T007)
- [ ] T010 Wire both paths in `src/AkmlSql.Core/Config/ConfigManager.cs`: call `AiAgentResolver.Normalize(settings.Ai)` after deserialization in **both** `Load()` and `Load(string path)`, and call `AiAgentResolver.MirrorActiveAgent(settings.Ai)` at the top of `Save(AppSettings)` before serialization — so the flat fields on disk match the active agent the moment the write completes. `Save` must **not** call `Normalize`: migration and repair are load-time concerns and must never rewrite a caller's settings on the write path. The atomic temp-file-plus-rename write is otherwise untouched (depends on T009)
- [ ] T011 [P] Write `tests/AkmlSql.Core.Tests/Config/AiAgentModelTests.cs` covering V1–V12 at model level, S1 usability across all eight providers (including no-key-needed locals), and the 20-agent ceiling
- [ ] T012 [P] Write `tests/AkmlSql.Core.Tests/Config/AiAgentResolverTests.cs` covering `Project` (globals preserved, connection fields substituted, no aliasing), `ResolveFor`, `Active`, `SuggestName`, `SuggestCopyName`, and `MirrorActiveAgent` — including the round-trip that pins the on-disk invariant: save settings whose active agent differs from the flat fields, then re-read the **raw JSON** (not through `Load`) and assert `ai.provider`/`ai.model`/`ai.apiKey` already match the active agent; plus the empty-list guard (flat fields untouched when `Agents` is empty) and `Enabled` being left alone by `MirrorActiveAgent` — per `contracts/agent-model.md` § Test coverage
- [ ] T013 [P] Write `tests/AkmlSql.Core.Tests/Config/AiAgentMigrationTests.cs` covering V14 using the T003 fixture: one agent named for its provider, active, `apiKey` byte-identical and never passed to `Unprotect`; migration idempotent across two loads; legacy spelling `"AzureOpenAI"` → `azure`; empty config stays empty with `Enabled == false`; mirroring V18 in both branches
- [ ] T014 Run `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` and confirm green including the pre-existing `ApiKeyProtectorTests` and `ProviderIdNormalizationTests` (depends on T011, T012, T013)
- [ ] T015 Add a regression test to `tests/AkmlSql.Core.Tests/Config/AiAgentResolverTests.cs` asserting that a migrated config produces flat fields `AiProviderFactory.Create` accepts unchanged, and that `AiCommandVisibility`'s `Enabled` semantics are preserved (depends on T009)
- [ ] T016 Build the whole solution green with full MSBuild and confirm no `ctoFiles.json` / `resources.json` / `mergeCto.cache` appeared at a drive root (depends on T010)

**Checkpoint**: Existing users migrate silently, everything behaves exactly as before, and the agent
model is available to every story.

---

## Phase 3: User Story 1 — A first-time user is led into setting up an agent (Priority: P1) 🎯 MVP

**Goal**: An unconfigured chat panel says so, offers one button, and that button opens Options on the
AI Assistance page with an agent ready to fill in. Saving makes chat work with no restart.

**Independent Test**: With `ai.agents` empty and `ai.provider` blank, open the chat panel — the
onboarding card appears instead of the greeting, the input is disabled, the button deep-links to the
AI page, and after saving one agent the panel becomes usable without restarting the host.

### Tests for User Story 1

> Write these first and confirm they fail before implementing.

- [ ] T017 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/AiChatEmptyStateTests.cs` — card shown and greeting absent with no usable agent; input and Send disabled; exactly one primary action; card replaced and input enabled once a usable agent exists (per `contracts/chat-agent-selection.md` § Test coverage)
- [ ] T018 [P] [US1] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatEmptyStateTests.cs` with the six reason-specific wordings of FR-021 (no agents, needs key, needs model, needs endpoint, key will not decrypt, all disabled), each naming the offending agent
- [ ] T019 [P] [US1] Extend `tests/AkmlSql.Shell.Shared.Tests/OptionsNavStructureTests.cs` with a deep-link test: building the window with an initial page key selects the AI Assistance leaf and expands its parent
- [ ] T020 [P] [US1] Add a test to `tests/AkmlSql.Shell.Shared.Tests/AiChatEmptyStateTests.cs` asserting the configuration refresh is a no-op when the settings signature is unchanged, so the 2-second tick stays cheap

### Implementation for User Story 1

- [ ] T021 [US1] Add `ShowDialog(string? initialPageKey)` and an `InitialAgentId` property to `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, routing through the existing private `SelectTreeLeafByPageKey` instead of selecting `_navTree.Items[0]`, and leaving the no-argument `ShowDialog()` behaviour identical
- [ ] T022 [US1] Extract the save-and-notify body of `Execute` into a static `ShowOptions(string? pageKey, string? agentId)` in `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`, preserving the theme-reopen loop, `ConfigManager.Save`, `TabColoringManager.RepaintAllTabs` and the `AnalysisSettingsChanged` notification; make `Execute` call it (depends on T021)
- [ ] T023 [P] [US1] Create the onboarding card control in `src/AkmlSql.Shell.Shared/Ai/AiChatEmptyState.cs` — theme tokens only, frozen brushes, hoisted `FontFamily`, `AutomationProperties.Name` on the button, one primary action per `contracts/chat-agent-selection.md` § The card
- [ ] T024 [US1] Add reason detection to `src/AkmlSql.Shell.Shared/Ai/AiChatEmptyState.cs` producing the six FR-021 wordings and the id of the offending agent (first in list order when several are unusable) (depends on T023)
- [ ] T025 [US1] Replace the unconditional greeting at `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs:238` with a render that shows the card when no agent is usable and the greeting — naming the resolved chat agent — when one is (depends on T023)
- [ ] T026 [US1] Gate sending in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`: disable `_inputBox` and `_sendButton` while no agent is usable, without disturbing the editor binding, header, schema-status poll or privacy note (depends on T025)
- [ ] T027 [US1] Add `RefreshConfiguration()` to `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` — a 5-second cached settings read on the existing `_bindingTimer` tick, following the `AiCommandVisibility` idiom, returning early when the settings signature is unchanged; also call it on `Loaded` (depends on T025)
- [ ] T028 [US1] Wire the card's button to `OptionsCommand.ShowOptions("AI Assistance", offendingAgentId)` followed by an immediate `RefreshConfiguration()` in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`; the panel must never call `ConfigManager.Save` itself (depends on T022, T027)
- [ ] T029 [US1] Make `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` `Load`/`Save` operate on the **active agent** held in a working copy of `settings.Ai.Agents`, creating one when `InitialAgentId` is empty and the list is empty, and writing the list back in `Save` — single-agent editing; the list UI arrives in US2 (depends on T021)
- [ ] T030 [US1] Preserve the PR #251 key contract in the new agent-backed path in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`: the decrypt-failure flag is set **after** the key text assignment, an empty box under a failed decrypt never overwrites the stored key, and typing clears both the flag and `_keyNotice` (depends on T029)
- [ ] T031 [US1] Make the other AI surfaces report the no-agent state and offer the same route in `src/AkmlSql.Shell.Shared/Commands/AiExplainCommand.cs`, `AiFixCommand.cs`, `AiOptimizeCommand.cs`, `AiIndexAnalysisCommand.cs` and `TextToSqlCommand.cs`; ghost text stays silent (FR-022)
- [ ] T032 [US1] Build the shell test project with full MSBuild and run `FullyQualifiedName~AiChatEmptyState|FullyQualifiedName~OptionsNav`, confirming the new tests pass and `AiChatSessionBindingTests`, `AiChatPanelCopyButtonTests` and `AiProviderModelAutofillTests` stay green (depends on T017–T031)

**Checkpoint**: A user with nothing configured is led into setting up an agent and can ask a question,
with no restart. This plus Phase 2 is the shippable MVP.

---

## Phase 4: User Story 2 — A user keeps several agents side by side (Priority: P1)

**Goal**: The AI Assistance page manages a list of up to 20 named agents — add, duplicate, remove,
rename, set active — each with its own provider, model, key and parameters.

**Independent Test**: Add three agents with different providers, close and reopen the dialog, and
confirm all three persist with their own settings and their own keys.

### Tests for User Story 2

- [ ] T033 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/AiAgentListPageTests.cs` covering Add (unique suggested name, selected), Duplicate (key copied, renamed, health reset), Remove (confirmation, active/assignment/fallback repair, nearest selection) and Set active (refused for an unusable agent)
- [ ] T034 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiAgentListPageTests.cs` with the working-copy contract: switching selection retains unsaved edits, editing one agent leaves others untouched, and Cancel discards everything
- [ ] T035 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/AiAgentListPageTests.cs` with validation: duplicate name refused case- and trim-insensitively naming the agent, 21st agent refused naming the limit, and rename preserving the active id, assignments and fallback order
- [ ] T036 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/AiAgentKeyProtectionTests.cs` — per-agent decrypt-failure guard: an undecryptable key survives a save, typing clears the guard and the notice together, and switching between a decryptable and an undecryptable agent keeps both correct
- [ ] T037 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/OptionsHoverContrastTests.cs` to cover the agent list's hovered and selected rows in both themes

### Implementation for User Story 2

- [ ] T038 [US2] Create `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAgentListView.cs` — the `ListBox`, its row rendering (name, provider · model, health badge, active marker) and the Add / Duplicate / Remove / Set-active button row, following the `TabsPage.cs:49-71` precedent, theme tokens only
- [ ] T039 [US2] Add the agent list and its buttons to the top of `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` above the existing provider rows, with a "Selected agent" group header over the editor (depends on T038)
- [ ] T040 [US2] Add a Name text row to the selected-agent editor in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, registered with `ctx.RegisterSearch` (depends on T039)
- [ ] T041 [US2] Implement `CommitEditorToSelectedAgent()` in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` and call it from exactly three places — selection change, before any CRUD action, and `Save` (depends on T039)
- [ ] T042 [US2] Implement `BindEditorTo(agent)` in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, preserving the key-assignment-then-flag ordering from T030 per `contracts/options-agents-ui.md` § Per-agent key protection (depends on T041)
- [ ] T043 [US2] Implement Add and Duplicate in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` using `AiAgentResolver.SuggestName` / `SuggestCopyName`, a fresh `Guid.NewGuid().ToString("N")` id, and the 20-agent refusal (depends on T041)
- [ ] T044 [US2] Implement Remove in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` — confirmation naming the agent, repair of `_activeId`, assignments and fallback order, selection moved to the nearest survivor (depends on T041)
- [ ] T045 [US2] Implement Set-active in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, refusing an unusable agent with a reason (depends on T041)
- [ ] T046 [US2] Implement whole-working-copy validation and the OK refusal in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` and `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` — V1–V12, message naming the agent and the field, offending agent selected before the message, name validation firing on focus loss (depends on T041)
- [ ] T047 [US2] Move the provider-switch model auto-correction to apply to the selected agent only in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, keeping `AiModelFamily.DefaultModelFor` semantics unchanged (depends on T042)
- [ ] T048 [US2] Update the per-page reset case for `"AI Assistance"` at `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:1819` so resetting the page clears agents, assignments and fallback order as well as the flat fields (depends on T039)
- [ ] T049 [US2] Build with full MSBuild and run the shell suite, confirming the new tests pass and every pre-existing Options test stays green (depends on T033–T048)

**Checkpoint**: Several agents can be kept, edited and removed side by side, with keys safe.

---

## Phase 5: User Story 3 — A user switches the answering agent from the chat panel (Priority: P2)

**Goal**: A picker in the chat header chooses which agent answers, each answer says who produced it,
and the choice persists.

**Independent Test**: With two agents configured, send a message, switch agents in the picker, send
another, and confirm the second answer is attributed to the second agent and the choice survives a
panel reopen.

### Tests for User Story 3

- [ ] T050 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/AiChatAgentPickerTests.cs` covering: enabled agents only, the resolved chat agent shown, the picker shown with exactly one agent, and the `Add agent…` entry routing through `ShowOptions`
- [ ] T051 [P] [US3] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatAgentPickerTests.cs` with the R7 decision — selecting writes `FeatureAgents.Chat` and **not** `ActiveAgentId`, and selecting the active agent clears the assignment to `""`
- [ ] T052 [P] [US3] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatAgentPickerTests.cs` with persistence across a panel rebuild, the conversation surviving a switch, per-answer attribution, `AgentName == null` rendering no attribution, and the fallback line being stated rather than hidden
- [ ] T053 [P] [US3] Extend `tests/AkmlSql.Shell.Shared.Tests/AiChatPanelCopyButtonTests.cs` so the copied conversation attributes each answer to its agent while preserving order, spacing and the existing copy-failure flash

### Implementation for User Story 3

- [ ] T054 [P] [US3] Add `[Key(6)] public string? AgentName { get; set; }` to `src/AkmlSql.Core/Ipc/Messages/AiChatResponse.cs` with a comment recording that MessagePack explicit keys make this additive-safe in both directions
- [ ] T055 [P] [US3] Create the picker control in `src/AkmlSql.Shell.Shared/Ai/AiAgentPicker.cs` — agents by name, an `Add agent…` trailing entry, theme tokens, `AutomationProperties.Name`, disabled agents excluded
- [ ] T056 [US3] Place the picker in the chat header beside the database label and the `⧉ Conversation` button in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` (depends on T055)
- [ ] T057 [US3] Show the resolved chat agent (S3) as the picker's selection in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`, refreshed by `RefreshConfiguration()` (depends on T056)
- [ ] T058 [US3] Implement selection handling in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` — write `FeatureAgents.Chat`, clear it to `""` when the chosen agent is already active, persist through the shared save-and-notify path, do not clear the conversation (depends on T057)
- [ ] T059 [US3] Wire the `Add agent…` entry to `OptionsCommand.ShowOptions("AI Assistance", null)` and select the newly added agent on return in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` (depends on T058)
- [ ] T060 [US3] Render per-answer attribution in `AddAssistantMessage` in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`, rendering nothing when `AgentName` is null and stating the fallback plainly when one was used (depends on T054)
- [ ] T061 [US3] Track the per-turn agent name alongside `_history` in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` and emit it in `OnCopyConversationClick`, preserving order, blank-line spacing, trailing trim and the clipboard funnel (depends on T060)
- [ ] T062 [US3] Set `AgentName` on the response from the agent that actually answered in `src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs` (depends on T054)
- [ ] T063 [US3] Build with full MSBuild, run the shell suite and `dotnet test tests/AkmlSql.Engine.Tests`, confirming green (depends on T050–T062)

**Checkpoint**: A mixed-agent conversation reads correctly in the panel and on the clipboard.

---

## Phase 6: User Story 4 — A user points different features at different agents (Priority: P2)

**Goal**: Each of the seven AI features uses its assigned agent, or follows the active agent; a
failing agent falls through an ordered chain.

**Independent Test**: Assign ghost text to one agent and chat to another, exercise both, and confirm
from the engine log which agent served each request.

### Tests for User Story 4

- [ ] T064 [P] [US4] Write `tests/AkmlSql.Engine.Tests/Ai/AgentFeatureResolutionTests.cs` covering all seven features resolving to their assigned agent, unassigned features following the active agent, and an active-agent change moving unassigned features
- [ ] T065 [P] [US4] Extend `tests/AkmlSql.Engine.Tests/Ai/AgentFeatureResolutionTests.cs` with the ordering assertion of `contracts/agent-resolution.md`: a cloud agent assigned to a feature trips the privacy-consent gate even when the active agent is local
- [ ] T066 [P] [US4] Extend `tests/AkmlSql.Engine.Tests/Ai/AgentFeatureResolutionTests.cs` with a deleted assigned agent and a disabled assigned agent both falling back to the active agent, and the notice firing **once per feature per engine process** rather than per request, with a second request for the same feature producing no further notice and a different feature producing its own (V22)
- [ ] T067 [P] [US4] Write `tests/AkmlSql.Engine.Tests/Ai/AgentFallbackChainTests.cs` covering chain order, stop-at-first-success, cancellation and consent never falling back, the offline provider remaining last, per-attempt timeout bounding, and the answering agent's name being returned
- [ ] T068 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/AiFeatureAssignmentPageTests.cs` covering the seven assignment dropdowns, their "Use active agent" default, and round-tripping through save and load

### Implementation for User Story 4

- [ ] T069 [US4] Add an abstract `AiFeature Feature { get; }` to `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs` and resolve-then-project **before** `CheckPrivacyConsent`, leaving the three catch blocks and the per-request `SettingsProvider()` read untouched
- [ ] T070 [P] [US4] Declare `Feature` on each handler: `AiChatHandler.cs`, `AiTextToSqlHandler.cs`, `AiExplainHandler.cs`, `AiFixHandler.cs`, `AiOptimizeHandler.cs`, `AiIndexAnalysisHandler.cs` and `AiGhostTextHandler.cs` in `src/AkmlSql.Engine/Handlers/Ai/` (depends on T069)
- [ ] T071 [US4] Add the fallback notice for a missing or disabled assigned agent in `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs`, scoped **once per feature per engine process** (V22) — the flag lives in the engine, which is the only place that can observe the condition (depends on T069)
- [ ] T072 [US4] Extend `ExecuteWithFallbackAsync` in `src/AkmlSql.Engine/Ai/AiPipelineServices.cs` to walk `settings.FallbackOrder` after the selected agent and fall through to `CreateFromFallback` last, preserving the existing `catch … when` filter verbatim and bounding each attempt by its own agent's timeout (depends on T069)
- [ ] T073 [US4] Return the answering agent's name and a `usedFallback` flag from `ExecuteWithFallbackAsync` in `src/AkmlSql.Engine/Ai/AiPipelineServices.cs` and thread it to `AiChatHandler` (depends on T072)
- [ ] T074 [US4] Add the "Feature assignments" group with seven dropdowns to `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, each offering "Use active agent" plus every agent by name, registered with `ctx.RegisterSearch`
- [ ] T075 [US4] Add fallback-order editing to `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` and persist it into the working copy (depends on T074)
- [ ] T076 [US4] Show the fallback explanation in the answer when the engine reports one was used, in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` (depends on T073)
- [ ] T077 [US4] Build with full MSBuild and run the Engine and shell suites, confirming green (depends on T064–T076)

**Checkpoint**: Cheap models can serve ghost text while a strong model serves chat, and a dead agent
no longer takes the feature down with it.

---

## Phase 7: User Story 5 — A user can see which agents actually work (Priority: P3)

**Goal**: Per-agent health badges, a per-agent connection test that uses the editor's current values,
and failure messages that name the agent.

**Independent Test**: Configure one agent with a valid key and one with a wrong key, test both, and
confirm the list shows two distinct, non-technical outcomes.

### Tests for User Story 5

- [ ] T078 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/AiAgentHealthTests.cs` covering the four statuses and their labels, `needsKey` computed with zero RPC calls against `FakeRpcClientAccessor`, and health reset to `unknown` on a provider/model/key/endpoint edit
- [ ] T079 [P] [US5] Extend `tests/AkmlSql.Shell.Shared.Tests/AiAgentHealthTests.cs` with the failure taxonomy of FR-056 — auth failure, unreachable endpoint, unknown model, family mismatch, missing endpoint, engine not connected — each a distinct message
- [ ] T080 [P] [US5] Extend `tests/AkmlSql.Shell.Shared.Tests/AiFailureMessageTests.cs` so a configuration-caused live failure names the agent and offers a route to its settings (FR-057)
- [ ] T081 [P] [US5] Add a test to `tests/AkmlSql.Shell.Shared.Tests/AiAgentHealthTests.cs` asserting no API key reaches any health `Message`, status line or log call (FR-058, V24)

### Implementation for User Story 5

- [ ] T082 [US5] Render the health badge and last-checked time in the list rows in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAgentListView.cs`, with the semantic colours documented as the theme-token exception
- [ ] T083 [US5] Compute `needsKey` locally from `RequiresApiKey(provider) && (ApiKey == "" || KeyDecryptFailed)` with no request in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` (depends on T082)
- [ ] T084 [US5] Point the existing Test-connection button at the selected agent's current editor values via `AiProviderTestRunner.BuildRequest` in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`, leaving `src/AkmlSql.Shell.Shared/Ai/AiProviderTestRunner.cs` unchanged (depends on T083)
- [ ] T085 [US5] Write the test outcome into the working-copy agent's `Health` (status, `CheckedUtc`, `LatencyMs`, `Message` capped at 500 chars) in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` (depends on T084)
- [ ] T086 [US5] Reset `Health.Status` to `unknown` on any provider, model, key or endpoint edit in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` (depends on T085)
- [ ] T087 [US5] Add the two locally determinable pre-flight refusals — missing endpoint (V9) and model-family mismatch (V7) — before sending a test, in `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` (depends on T084)
- [ ] T088 [US5] Name the agent in configuration-caused live failures and offer the route to its settings in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` and `src/AkmlSql.Shell.Shared/Ai/AiIpcTimeouts.cs` (depends on T085)
- [ ] T089 [US5] Build with full MSBuild and run the shell suite, confirming green (depends on T078–T088)

**Checkpoint**: A silent agent explains itself in one glance.

---

## Phase 8: User Story 6 — An existing user upgrades and loses nothing (Priority: P3)

**Goal**: Harden load-time repair beyond the migration delivered in Phase 2, and prove compatibility
both forwards and backwards.

**Independent Test**: Take a current-release `config.json` with a configured provider and key, open
the new build, and confirm one agent carrying those settings, active and working, with no user
action — then hand-corrupt the file in each documented way and confirm the dialog still opens.

### Tests for User Story 6

- [ ] T090 [P] [US6] Extend `tests/AkmlSql.Core.Tests/Config/AiAgentResolverTests.cs` with V13 (dangling `activeAgentId` repaired), V16 (dangling feature assignment cleared) and V17 (dangling, duplicate and self-referencing fallback entries removed)
- [ ] T091 [P] [US6] Extend `tests/AkmlSql.Core.Tests/Config/AiAgentResolverTests.cs` with V15 (malformed agent dropped with a warning, the rest surviving), V20 (unknown health status → `unknown`) and V21 (agents beyond the 20th dropped)
- [ ] T092 [P] [US6] Add a test to `tests/AkmlSql.Core.Tests/Config/AiAgentMigrationTests.cs` asserting `Normalize` never throws on any malformed input and that `Load` still returns usable settings

### Implementation for User Story 6

- [ ] T093 [US6] Implement V13, V16 and V17 repairs in `AiAgentResolver.Normalize` in `src/AkmlSql.Core/Config/AiAgentResolver.cs`
- [ ] T094 [US6] Implement V15, V20 and V21 in `AiAgentResolver.Normalize` in `src/AkmlSql.Core/Config/AiAgentResolver.cs`, logging each drop with `Log.Warning` naming the index and never throwing (depends on T093)
- [ ] T095 [US6] Surface the notice when a feature assignment was cleared by normalisation in `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`, honouring the once-per-feature-per-engine-process scope the engine already enforces (T071) rather than introducing a second, shell-side counter (FR-049, V22) (depends on T093)
- [ ] T096 [US6] Run quickstart scenarios 60–70 manually, including the downgrade check in scenario 69 (a config written by this build opened by the previous release) and the two-host check in scenario 70 (depends on T093, T094)
- [ ] T097 [US6] Run `dotnet test tests/AkmlSql.Core.Tests` and confirm green (depends on T090–T094)

**Checkpoint**: Existing users are safe, hand-edited files cannot lock anyone out of the dialog, and
downgrade works.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T098 [P] Document the `ai.agents`, `ai.activeAgentId`, `ai.featureAgents` and `ai.fallbackOrder` schema, including the flat-field mirroring rule, in `doc/configuration.md`
- [ ] T099 [P] Add the new IPC field `AiChatResponse.AgentName` (key 6) to the AI chat response schema in `doc/ipc-api.md`
- [ ] T100 [P] Add the spec 037 entry — scope, decisions, deferred items — to `doc/progress.md`
- [ ] T101 [P] Update the "Latest merged work" and AI-related notes in `CLAUDE.md` to mention multi-agent AI configuration
- [ ] T102 [P] Add a short "Multiple AI agents" section to `doc/architecture.md` describing the resolution seam in `AiHandlerBase` and the mirroring invariant
- [ ] T103 Verify SC-011 and SC-012: open the Options dialog with 20 agents configured and confirm it opens within its current budget, and measure agent resolution at under 5 ms per request
- [ ] T104 Confirm the format-parity goldens (977) and the completion corpus (1,342 / ~97.5%) match the T002 baseline exactly
- [ ] T105 Grep `%AppData%\AKML SQL\logs\` for every API key used during validation and confirm zero matches (SC-009)
- [ ] T106 Run the full quickstart (`specs/037-multi-ai-agents/quickstart.md`, scenarios 1–75) end to end
- [ ] T107 Build the whole solution green in one pass with full MSBuild and confirm no new warnings and no drive-root CTO artifacts
- [ ] T108 Review the diff against Constitution V — no speculative generality, no parallel mechanisms, comment density and naming matching the surrounding files

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)** — no dependencies
- **Phase 2 (Foundational)** — depends on Phase 1. **Blocks every user story.**
- **Phase 3 (US1)** — depends on Phase 2
- **Phase 4 (US2)** — depends on Phase 2; builds on T029/T030 from Phase 3
- **Phase 5 (US3)** — depends on Phase 2; T059 needs T022 (`ShowOptions`) from Phase 3
- **Phase 6 (US4)** — depends on Phase 2; T074/T075 sit on the page built in Phase 4
- **Phase 7 (US5)** — depends on Phase 2 and Phase 4 (the list renders the badges)
- **Phase 8 (US6)** — depends on Phase 2
- **Phase 9 (Polish)** — depends on every story you intend to ship

### User Story Dependencies

- **US1 (P1)** — needs only Phase 2. The empty state is testable against a hand-written config.
- **US2 (P1)** — needs only Phase 2, but reuses T029's working copy rather than duplicating it.
- **US3 (P2)** — needs Phase 2; borrows `ShowOptions` from US1.
- **US4 (P2)** — needs Phase 2. Fully independent engine-side; its UI lands on US2's page.
- **US5 (P3)** — needs Phase 2 and US2's list to render badges into.
- **US6 (P3)** — needs Phase 2 only. Entirely independent of the UI stories.

### Within Each Story

Tests are written first and must fail before implementation. Model before resolver, resolver before
handler, controls before the panel that hosts them, implementation before the build-and-run task
that closes the phase.

### Parallel Opportunities

- **Phase 1**: T002, T003
- **Phase 2**: T004, T005 together; then T011, T012, T013 together
- **Phase 3**: T017–T020 together; T023 alongside T021/T022
- **Phase 4**: T033–T037 together
- **Phase 5**: T050–T053 together; T054 and T055 together
- **Phase 6**: T064–T068 together; T070 across seven handler files
- **Phase 7**: T078–T081 together
- **Phase 8**: T090–T092 together
- **Phase 9**: T098–T102 together

Once Phase 2 closes, US1, US4 and US6 can proceed in parallel by different people — they touch the
shell UI, the engine, and Core respectively.

---

## Parallel Example: User Story 2

```bash
# Write all five test files for US2 together, confirm they fail:
Task: "AiAgentListPageTests — CRUD in tests/AkmlSql.Shell.Shared.Tests/AiAgentListPageTests.cs"
Task: "AiAgentListPageTests — working copy contract"
Task: "AiAgentListPageTests — validation and rename stability"
Task: "AiAgentKeyProtectionTests in tests/AkmlSql.Shell.Shared.Tests/AiAgentKeyProtectionTests.cs"
Task: "OptionsHoverContrastTests — agent list rows"
```

---

## Implementation Strategy

### MVP (Phases 1 → 4)

1. Phase 1 Setup — baseline recorded
2. Phase 2 Foundational — agents exist, existing users migrate silently
3. Phase 3 US1 — the empty chat leads into setup **(the user's headline request)**
4. Phase 4 US2 — several agents side by side **(the user's other headline request)**
5. **STOP and VALIDATE** with quickstart scenarios 1–29
6. Ship

### Incremental Delivery

Each later phase is independently shippable and leaves the product working:

- **+ Phase 5 (US3)** — switch agents from chat, attributed answers
- **+ Phase 6 (US4)** — per-feature assignment and the fallback chain
- **+ Phase 7 (US5)** — health badges and per-agent testing
- **+ Phase 8 (US6)** — load-time hardening and proven compatibility
- **+ Phase 9** — documentation, performance and the full quickstart

---

## Notes

- **Never `dotnet build` a shell project** — full MSBuild only (VSSDK CodeTaskFactory).
- **`ApiKeyProtector`'s entropy string `"AkmlSql-ApiKey-v1"` must not change.** Every stored key
  becomes permanently unreadable if it does. This is the single most dangerous line in the feature.
- **Migration copies `ApiKey` verbatim** — never unwrap, never re-wrap.
- **Projection must run before `CheckPrivacyConsent`** (T069). Getting this backwards lets a cloud
  agent slip past the consent gate.
- **The chat picker writes `FeatureAgents.Chat`, never `ActiveAgentId`** (T051, T058). This is the
  decision most likely to be implemented wrong.
- Commit after each task or logical group — **only when the user explicitly asks** (Constitution IV).
- Stop at any checkpoint to validate a story independently.
