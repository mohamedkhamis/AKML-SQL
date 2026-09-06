# Implementation Plan: Multiple AI Agents with Guided Setup

**Branch**: `037-multi-ai-agents` | **Date**: 2026-09-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/037-multi-ai-agents/spec.md`

## Summary

Replace AKML SQL's single AI configuration with a **named list of up to 20 agents**, and make the
zero-configuration path lead into setup instead of into an error.

The technical approach is deliberately additive. `AiSettings` gains `agents`, `activeAgentId`,
`featureAgents` and `fallbackOrder`; its existing flat provider fields **stay and mirror the active
agent**, so `AiProviderFactory`, the seven engine handlers, the web service and any hand-written
`config.json` keep working with no coordinated change and no migration step. One new Core helper
(`AiAgentResolver`) owns normalisation, migration, and the agent → `AiSettings` projection, and is
invoked from the two `ConfigManager.Load` overloads so no reader ever sees a half-migrated state, and
its mirroring step invoked again from `ConfigManager.Save` so the flat fields on disk are never stale.
Engine-side, agent selection resolves in exactly one place — `AiHandlerBase.HandleAsync`, before the
consent gate. Shell-side, the Options AI page grows a list-plus-editor over its existing rows, and
the chat panel replaces its unconditional greeting with an empty state that opens Options deep-linked
to the AI page.

Five slices, marked `[A]`–`[E]` in the structure below, map to the spec's six user stories.

## Technical Context

**Language/Version**: C# — `net472` (shell, LangVersion latest), `netstandard2.0` + `net10.0` (Core), `net10.0` (engine, AI library)
**Primary Dependencies**: existing only — `Microsoft.Extensions.AI`, provider SDKs (Anthropic.SDK, OpenAI, OllamaSharp), MessagePack (IPC), System.Text.Json (config), Serilog, WPF (programmatic, no XAML), VS SDK 17.14.x
**Storage**: `%AppData%/AKML SQL/config.json` — the existing `AppSettings` document, written atomically. API keys wrapped with DPAPI (`ApiKeyProtector`, entropy `"AkmlSql-ApiKey-v1"`, unchanged)
**Testing**: xunit 2.x. `AkmlSql.Core.Tests` (model, normalisation, migration, projection), `AkmlSql.Shell.Shared.Tests` (`[StaFact]` WPF, Options page and chat panel), `AkmlSql.Engine.Tests` (handler resolution, fallback chain)
**Target Platform**: Windows x64 — SSMS 22 and Visual Studio 2026 shells over the out-of-process .NET 10 engine
**Project Type**: Desktop IDE extension with an out-of-process engine (shell ↔ engine over named pipe + MessagePack)
**Performance Goals**: agent resolution < 5 ms per request (SC-012); Options dialog opens within its current budget at 20 agents (SC-011); no additional per-keystroke or per-tick disk I/O in the host process
**Constraints**: no new IPC message types; no new settings store; no change to the DPAPI entropy string; privacy mode and consent stay global, never per agent; `netstandard2.0`-safe C# in Core (plain `{ get; set; }`, no `init`); shell projects build with full MSBuild only
**Scale/Scope**: ≤ 20 agents per user; 7 AI features assignable; ~6 files added, ~10 modified, 0 deleted

## Constitution Check

*GATE: evaluated before Phase 0, re-evaluated after Phase 1 design. Constitution v1.0.0.*

| Principle | Pre-design | Post-design | Notes |
|---|---|---|---|
| **I. Process Isolation & Host Safety** | PASS | PASS | No AI or provider work moves in-process. The shell picks a name and saves settings; the engine resolves the agent and builds the client, exactly as today. The one shell-side addition (an empty-state card and a picker) is UI. New shared code lands in `AkmlSql.Core` and `AkmlSql.Shell.Shared`, never copy-pasted into the two shell projects. |
| **II. Build Integrity** | PASS | PASS | No SDK, toolchain or package change. Shell projects built with full MSBuild. The theme-token gate is untouched — the new UI consumes existing tokens and adds none. |
| **III. Tests & Corpora Non-Regressible** | PASS | PASS | Every slice lands with tests in the matching `tests/` project. The format-parity (977) and completion-corpus (1,342 / ~97.5%) ratchets are not on any path this feature touches and must stay green. |
| **IV. Git Consent** | PASS | PASS | No git mutation is part of this plan. Work is delivered uncommitted. |
| **V. Simplicity & Convention Fidelity** | PASS | PASS | See the four reuse decisions below. |

**Simplicity evidence** (the principle most at risk in a feature that adds a list where a scalar was):

1. **No new IPC.** Per-agent testing reuses the existing `AiProviderTest` (77/177) pair through
   `AiProviderTestRunner` unchanged; agent attribution is one additive MessagePack key on an
   existing response.
2. **No new settings store, no new file, no new watcher.** Agents live in `config.json`; change
   detection reuses the chat panel's existing binding timer and the 5-second cached-read idiom
   `AiCommandVisibility` already uses.
3. **No new provider abstraction.** `AiProviderFactory` keeps taking an `AiSettings`; an agent is
   projected onto one, which is the trick `CreateFromFallback` already performs.
4. **No new dialog.** Deep-linking reuses the private `SelectTreeLeafByPageKey` that the settings
   search box already drives; the agent editor is the AI page's existing rows.

**Deviation declared**: none. Complexity Tracking is empty.

**Post-design re-check note**: the Phase 1 design added one Core class (`AiAgentResolver`), one Core
model (`AiAgent` + `AgentHealth` + `FeatureAgentAssignments`), one enum (`AiFeature`), and one
MessagePack key. It removed nothing and duplicated nothing. The gates stand.

## Project Structure

### Documentation (this feature)

```text
specs/037-multi-ai-agents/
├── plan.md                          # This file
├── spec.md                          # Feature specification (58 FRs, 12 SCs)
├── research.md                      # Phase 0 — R1..R15 decisions
├── data-model.md                    # Phase 1 — entities, validation rules, state machines
├── quickstart.md                    # Phase 1 — numbered manual validation scenarios
├── checklists/
│   └── requirements.md              # Spec quality checklist (all pass)
├── contracts/
│   ├── agent-model.md               # Storage shape, normalisation, migration, mirroring
│   ├── agent-resolution.md          # Which agent serves a request; fallback chain
│   ├── options-agents-ui.md         # Options page list + editor obligations
│   └── chat-agent-selection.md      # Empty state, picker, attribution
└── tasks.md                         # Phase 2 — created by /speckit.tasks, NOT by /speckit.plan
```

### Source Code (repository root)

Slice markers: **[A]** agent model & storage · **[B]** Options page · **[C]** chat empty state &
picker · **[D]** engine resolution & fallback · **[E]** health & testing.

```text
src/
├── AkmlSql.Core/
│   ├── Config/
│   │   ├── AiAgent.cs                        # NEW [A] AiAgent, AgentHealth, FeatureAgentAssignments, AgentHealthStatus
│   │   ├── AiAgentResolver.cs                # NEW [A][D] Normalize / Migrate / Mirror / Project / ResolveFor(feature)
│   │   ├── AiFeature.cs                      # NEW [D] chat, textToSql, explain, fix, optimize, indexSuggestions, ghostText
│   │   ├── AppSettings.cs                    # MOD [A] AiSettings gains Agents, ActiveAgentId, FeatureAgents, FallbackOrder
│   │   ├── ConfigManager.cs                  # MOD [A] both Load overloads call Normalize; Save calls MirrorActiveAgent
│   │   ├── AiProviderIds.cs                  # unchanged — canonical ids reused
│   │   ├── AiModelFamily.cs                  # unchanged — defaults + mismatch detection reused
│   │   └── ApiKeyProtector.cs                # unchanged — entropy string MUST NOT change
│   └── Ipc/Messages/
│       └── AiChatResponse.cs                 # MOD [C] + [Key(6)] string? AgentName
│
├── AkmlSql.AI/
│   └── Providers/AiProviderFactory.cs        # unchanged — still takes an AiSettings
│
├── AkmlSql.Engine/
│   ├── Ai/AiPipelineServices.cs              # MOD [D] ExecuteWithFallbackAsync walks fallbackOrder, then offline
│   └── Handlers/Ai/
│       ├── AiHandlerBase.cs                  # MOD [D] abstract AiFeature Feature; project BEFORE consent gate
│       ├── AiChatHandler.cs                  # MOD [C][D] Feature => Chat; sets AgentName on the response
│       ├── AiTextToSqlHandler.cs             # MOD [D] Feature => TextToSql
│       ├── AiExplainHandler.cs               # MOD [D] Feature => Explain
│       ├── AiFixHandler.cs                   # MOD [D] Feature => Fix
│       ├── AiOptimizeHandler.cs              # MOD [D] Feature => Optimize
│       ├── AiIndexAnalysisHandler.cs         # MOD [D] Feature => IndexSuggestions
│       └── AiGhostTextHandler.cs             # MOD [D] Feature => GhostText
│
└── AkmlSql.Shell.Shared/
    ├── Dialogs/
    │   ├── SettingsWindow.cs                 # MOD [B] ShowDialog(initialPageKey), InitialAgentId
    │   └── Pages/
    │       ├── AiAssistancePage.cs           # MOD [B][E] agent list + CRUD + editor over existing rows
    │       └── AiAgentListView.cs            # NEW [B] list control, row rendering, health badge
    ├── Commands/
    │   └── OptionsCommand.cs                 # MOD [B] static ShowOptions(pageKey, agentId) — one save+notify path
    └── Ai/
        ├── AiChatPanel.cs                    # MOD [C] empty state, agent picker, per-answer attribution
        ├── AiChatEmptyState.cs               # NEW [C] onboarding card + "Add AI agent" action
        ├── AiAgentPicker.cs                  # NEW [C] chat-header picker incl. "Add agent…" entry
        └── AiProviderTestRunner.cs           # unchanged — already takes raw field values

tests/
├── AkmlSql.Core.Tests/Config/
│   ├── AiAgentModelTests.cs                  # NEW [A] validation rules V1..V24
│   ├── AiAgentMigrationTests.cs              # NEW [A] US6 — legacy config → one agent, key intact
│   └── AiAgentResolverTests.cs               # NEW [A][D] projection, mirroring, feature resolution
├── AkmlSql.Engine.Tests/Ai/
│   ├── AgentFeatureResolutionTests.cs        # NEW [D] each handler uses its assigned agent
│   └── AgentFallbackChainTests.cs            # NEW [D] chain order; offline still last; consent never falls back
└── AkmlSql.Shell.Shared.Tests/
    ├── AiAgentListPageTests.cs               # NEW [B] add/duplicate/remove/rename/validation/working copy
    ├── AiAgentKeyProtectionTests.cs          # NEW [B][E] per-agent decrypt-failure guard (extends PR #251 contract)
    ├── AiChatEmptyStateTests.cs              # NEW [C] empty state, disabled input, deep link
    ├── AiChatAgentPickerTests.cs             # NEW [C] selection, persistence, attribution, copy
    └── AiProviderModelAutofillTests.cs       # MOD  existing provider/model/key contracts, now per agent
```

**Structure Decision**: The existing three-layer split is kept exactly as it is — shared model and
policy in `AkmlSql.Core` (dual-targeted so both the net472 shell and the net10 engine consume the
same code), request handling in `AkmlSql.Engine`, UI in `AkmlSql.Shell.Shared` compiled into both
shell hosts via `.projitems`. No new project. The three new shell files are UI components that would
otherwise push `AiAssistancePage` and `AiChatPanel` past a reviewable size; each is a single control
with one responsibility, in the same folder as its consumer.

## Delivery Order and Independence

| Slice | Stories | Depends on | Independently shippable? |
|---|---|---|---|
| **[A]** agent model, normalisation, migration, mirroring | US6 | — | Yes — existing configs migrate; behaviour otherwise unchanged |
| **[B]** Options page list + editor | US2 | [A] | Yes — a user can manage agents; everything still runs the active one |
| **[C]** chat empty state + picker + attribution | US1, US3 | [A]; empty state alone needs only [A] | Yes — the empty state is testable before the picker exists |
| **[D]** engine resolution + fallback chain | US4 | [A] | Yes — with no assignments configured, behaviour is identical to today |
| **[E]** health, per-agent testing, error attribution | US5 | [A], [B] | Yes — diagnosis quality on top of a working feature |

**MVP**: [A] + [C]-empty-state + [B]. That delivers precisely what the user asked for first — several
agents, and a chat that leads you into creating one.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| A save blanks a key that could not be decrypted | User loses a working key with no way back | Per-agent decrypt-failure guard, ported from the PR #251 contract and pinned by `AiAgentKeyProtectionTests`. Entropy string never changes. |
| Consent checked against the wrong provider | A cloud call escapes the consent gate | Projection runs before `CheckPrivacyConsent` in `AiHandlerBase`; asserted by an engine test that a cloud agent assigned to a feature still trips consent |
| Migration runs twice and duplicates the agent | Two identical agents after every load | `Normalize` is idempotent by construction (migrates only when `agents` is empty and `provider` is set); pinned by a test that loads twice |
| Flat fields drift from the active agent | The web service or an older build uses stale settings | `MirrorActiveAgent` runs on **both** paths — inside `Normalize` on every load, and inside `ConfigManager.Save` on every write — so the invariant holds on disk, not just in memory |
| Options page becomes unreviewable | Constitution V | List rendering extracted to `AiAgentListView`; the editor reuses rows already present |
| Chat picker repoints every feature | US4 becomes unusable | R7 — the picker writes `featureAgents.chat` only |
| Hover/selection contrast regression in the new list | The exact bug spec 036 fixed | Theme tokens only; `OptionsHoverContrastTests` extended to the agent list |

## Complexity Tracking

No Constitution violations. This table is intentionally empty.
