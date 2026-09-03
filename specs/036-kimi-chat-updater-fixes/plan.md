# Implementation Plan: Readable Options Navigation, Kimi-Capable Schema-Aware AI Chat, and a Working Update Channel

**Branch**: `036-kimi-chat-updater-fixes` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/036-kimi-chat-updater-fixes/spec.md`

## Summary

Five independent slices, ordered by the spec's priorities:

1. **Schema-aware AI (P1)** — every AI request in the shell invents a random `SessionId`, so the engine finds no session, hands the model an empty schema, and the assistant answers as if the database were empty. Fix by resolving the real `AkmlSqlSessionId` from the active editor buffer (a resolver already exists), then widen the engine-side context builder so general questions get the full inventory instead of an incidentally-filtered subset.
2. **Kimi provider (P1)** — add Kimi as an OpenAI-compatible provider in the factory, the model-family guard, and the Options page; fix the two existing provider-name mismatches (`AzureOpenAI`/`LMStudio`) discovered in the same mapping; wire the Options page to the `AiProviderTest` IPC pair that already exists engine-side but has no caller; move AI keys off plaintext onto the DPAPI protector already used for SQL credentials.
3. **Chat copy (P2)** — make bubbles selectable, label per-block SQL copies, add a copy-conversation action.
4. **Options hover (P2)** — the nav's mouse-over trigger sets background without foreground and is ordered *after* the selected trigger, so a selected+hovered item paints white-on-light. Pair every hover background with a foreground and add a contrast sweep test.
5. **Update channel (P3)** — the check points at `updates.akmlsql.com`, which does not exist. Point it at the live site's `releases.json`, generate the update manifest from the same deploy step that stages the release, and extend the updater with a `--download` mode that fetches, verifies SHA-256, and hands the shell a verified local path to launch after one confirmation.

Everything reuses an existing mechanism: no new IPC message types, no new storage, no new infrastructure.

## Technical Context

**Language/Version**: C# — net472 (shell, `LangVersion latest`), netstandard2.0 + net10.0 (Core), net10.0 (Engine, AI, Updater, Site)
**Primary Dependencies**: VS SDK 17.14.x, WPF (programmatic, no XAML), MessagePack (IPC), `Microsoft.Extensions.AI` + OpenAI SDK (providers), Serilog, System.Text.Json, `System.Security.Cryptography.ProtectedData` (already referenced by Core on both TFMs), Inno Setup 7
**Storage**: `%AppData%\AKML SQL\config.json` (settings), `%AppData%\AKML SQL\cache\` (update result + downloaded installer), `src/AkmlSql.Site/wwwroot/releases.json` (release manifest), GitHub Releases (binary CDN)
**Testing**: xunit 2.x; `[StaFact]` for WPF surfaces. Existing files to extend rather than replace: `WindowChromeTests`, `OptionsNavStructureTests`, `AiChatPanelCopyButtonTests`, `AiProviderModelAutofillTests`, `AiFailureMessageTests` (`tests/AkmlSql.Shell.Shared.Tests/`); `ProviderModelMismatchTests` (`tests/AkmlSql.AI.Tests/`); `ConstantsTests` (`tests/AkmlSql.Core.Tests/`)
**Target Platform**: Windows x64 — SSMS 22 and Visual Studio 2026 hosts, plus the out-of-process engine and updater
**Project Type**: Desktop IDE extension with an out-of-process engine, a CLI updater, and a static-SSR product site
**Performance Goals**: Schema context assembly adds < 200 ms to an AI request on a 500-object database; the provider connection test returns within the configured AI timeout; the update check stays off the UI thread entirely and the installer download never blocks the host process
**Constraints**: No new IPC message types (77/177 already exist and are unused by the shell). No blocking calls on the VS UI thread. AI keys never logged. The `docs/theme-tokens.json` drift gate must stay green. Format-parity and completion-corpus ratchets must not move. The AI key DPAPI entropy string `"AkmlSql-ApiKey-v1"` must be preserved byte-for-byte or existing stored keys become unreadable.
**Scale/Scope**: ~5 slices, ~14 source files touched across 6 projects, 1 PowerShell deploy script, plus 1 manual clean-machine verification that cannot be automated (FR-046)

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1 design. Constitution v1.0.0.*

| Principle | Assessment | Verdict |
|---|---|---|
| **I. Process Isolation & Host Safety** | All new logic lands engine-side or in shared net10.0 libraries. The shell changes are UI wiring only: resolve a session id, add buttons, call existing IPC. The installer download runs in the **updater process**, never in the shell — a ~72 MB transfer inside SSMS would be a host-safety violation. No `.GetAwaiter().GetResult()` on the UI thread. | ✅ PASS |
| **II. Build Integrity** | Shell projects built with full MSBuild only. No SDK/toolchain version changes, so no forced `obj`/`bin` clean. No theme CSS is hand-edited — the hover fix changes WPF trigger construction in C#, not `docs/theme-tokens.json`, so the drift gate is untouched. | ✅ PASS |
| **III. Tests & Corpora Non-Regressible** | Every touched `src/` project has a matching `tests/` project; new behaviour lands with tests in the existing files listed above. Nothing in this feature touches the formatter or completion paths, so the format-parity (977 goldens) and completion-corpus (1,342 cases, ~97.5% gate) ratchets are unaffected and must remain green. | ✅ PASS |
| **IV. Git Consent** | No git mutation anywhere in this plan or in `tasks.md`. Changes are delivered uncommitted. The deploy script change is *authored* here but running it (which publishes a release) requires the user's explicit instruction. | ✅ PASS |
| **V. Simplicity & Convention Fidelity** | Every slice extends an existing mechanism instead of adding a parallel one: `AiProviderTest` 77/177 already exists (wire it, don't invent one); `RefactorCommandHelper.TryGetActiveEditor` already resolves the session id (reuse it, don't write a third resolver); `CredentialManager` already wraps AI keys (promote it to Core, don't add a second crypto path); `releases.json` already carries version/hash/CDN URL (read it, don't add a second manifest source). No new configurability beyond the one budget setting FR-026 explicitly requires. | ✅ PASS |

**Additional constraints check**: secrets stay unlogged and move *from* plaintext *to* DPAPI (a strict improvement); the downloaded installer path is validated with `Path.GetFullPath()` canonicalisation before launch; config writes stay atomic via `ConfigManager`; no network listener is introduced — the updater makes outbound HTTPS calls only; technology pins are unchanged.

**Documentation currency**: `doc/ipc-api.md` gains the AI-provider-test entry (the message existed but was undocumented as unwired), `doc/configuration.md` gains the schema-budget setting and the key-encryption note, `doc/deployment.md` gains the update-manifest generation step, and `doc/progress.md` records the spec-036 table plus the FR-046 verification evidence.

**Gate result: PASS — no violations, Complexity Tracking section omitted.**

## Project Structure

### Documentation (this feature)

```text
specs/036-kimi-chat-updater-fixes/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — 12 decisions with verified anchors
├── data-model.md        # Phase 1 output — entities, fields, validation, state
├── quickstart.md        # Phase 1 output — the acceptance gate for "done"
├── checklists/
│   └── requirements.md  # Spec quality checklist (all items pass)
├── contracts/
│   ├── ai-provider-test.md      # Existing IPC 77/177 — the contract the shell must honour
│   ├── kimi-provider.md         # Provider id, defaults, family detection, error mapping
│   ├── schema-context.md        # Session binding + inventory/budget/truncation contract
│   └── update-manifest.md       # releases.json → update manifest shape + updater CLI
└── tasks.md             # Phase 2 output — NOT created by /speckit.plan
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── Constants.cs                          # [E] UpdateManifestUrl → live endpoint
│   ├── Config/
│   │   ├── AiModelFamily.cs                  # [B] Kimi family + default model
│   │   ├── AppSettings.cs                    # [B][D] AiSettings: schema budget knob
│   │   └── ApiKeyProtector.cs                # [B] NEW — DPAPI wrap/unwrap, both TFMs
│   └── Update/
│       ├── UpdateManifest.cs                 # [E] doc comment + fields alignment
│       └── UpdateResult.cs                   # [E] verified local installer path
├── AkmlSql.AI/
│   ├── Providers/AiProviderFactory.cs        # [B] "kimi" case + provider-name aliases
│   └── Context/
│       ├── SchemaContextBuilder.cs           # [D] inventory-first, budget, truncation
│       └── SchemaContextFormatter.cs         # [D] truncation notice in prompt text
├── AkmlSql.Engine/
│   ├── Ai/Security/CredentialManager.cs      # [B] delegate to Core ApiKeyProtector
│   └── Handlers/Ai/*.cs                      # [D] compression level + budget plumbing
├── AkmlSql.Shell.Shared/
│   ├── Dialogs/SettingsWindow.cs             # [A] hover fg pairing, both lists
│   ├── Dialogs/Pages/AiAssistancePage.cs     # [B] Kimi entry, name fix, Test button
│   ├── Ai/AiChatPanel.cs                     # [C][D] session id, selectable text, copies
│   ├── Ai/AiChatToolWindow.cs                # [D] active-editor rebinding
│   ├── Ai/GhostTextAdornment.cs              # [D] real session id
│   ├── Commands/TextToSqlCommand.cs          # [D] real session id
│   ├── Commands/CheckUpdateCommand.cs        # [E] guided flow + confirmation
│   └── Update/UpdateLauncher.cs              # [E] manual check bypasses throttle
├── AkmlSql.Updater/Program.cs                # [E] --download mode, SHA-256 verify
└── AkmlSql.Site/wwwroot/releases.json        # [E] source of truth (unchanged shape)

scripts/deploy-site-iis.ps1                   # [E] emit update manifest beside releases.json

tests/
├── AkmlSql.Core.Tests/                       # ConstantsTests, ApiKeyProtectorTests
├── AkmlSql.AI.Tests/                         # ProviderModelMismatchTests, SchemaContext*
├── AkmlSql.Engine.Tests/                     # handler session-binding tests
├── AkmlSql.Shell.Shared.Tests/               # WindowChrome, OptionsNav, AiChatPanel*
└── AkmlSql.Site.Tests/                       # manifest/releases.json consistency
```

`[A]`–`[E]` map to the five slices in the Summary.

**Structure Decision**: No new projects. The feature slots into the existing engine/shared-library/shell split exactly as Constitution principle I requires — the only new source file is `ApiKeyProtector.cs`, placed in `AkmlSql.Core/Config/` beside `SqlCredentialStore.cs` because it must compile for **both** netstandard2.0 (shell) and net10.0 (engine), which is precisely why it cannot stay in `AkmlSql.Engine`.

## Phase 0 — Research

Complete. See [research.md](./research.md): 12 decisions (R1–R12), each with the verified `file:line` anchor it rests on, the rationale, and the alternatives rejected. Three findings changed the shape of the work versus the spec's first draft:

- **R3** — `AiProviderTest` (77/177) is fully implemented and registered engine-side but has **no shell caller**. FR-009 is a wiring task, not a new contract. The spec appendix has been corrected.
- **R4** — AI API keys are stored in **plaintext** in `config.json` today. FR-008 requires promoting the engine's existing DPAPI protector into `AkmlSql.Core` so the net472 Options page can use the same one, preserving the `"AkmlSql-ApiKey-v1"` entropy exactly.
- **R6** — the relevance filter's fallback-to-all only fires at *zero* matches, so noise tokens ("my", "do") that incidentally substring-match an object name produce a tiny arbitrary subset. This, not the object cap, is the main reason general questions get a bad context.

No NEEDS CLARIFICATION markers remain: the two spec-level questions were resolved by the user on 2026-09-02 (assisted-not-silent update flow; desktop-only Kimi).

## Phase 1 — Design & Contracts

Complete. Artifacts:

- **[data-model.md](./data-model.md)** — six entities (AI Provider Profile, Schema Context, Editor Session Binding, Chat Message, Release Record, Update Outcome) with fields, validation rules traced to FR numbers, and the two state machines that matter (schema-context assembly; update flow from *idle* through *verified* to *installed*).
- **[contracts/ai-provider-test.md](./contracts/ai-provider-test.md)** — the existing 77/177 request/response shape, the key-encoding rule, timeout, and the error-cause taxonomy FR-014 requires.
- **[contracts/kimi-provider.md](./contracts/kimi-provider.md)** — canonical provider id, display name, default model and endpoint, family-detection patterns for `AiModelFamily`, the provider-name alias table that fixes Azure/LM Studio, and the failure-to-message mapping.
- **[contracts/schema-context.md](./contracts/schema-context.md)** — how a request binds to an editor session, the inventory-first assembly rule, the budget and truncation signal, and the per-feature detail levels.
- **[contracts/update-manifest.md](./contracts/update-manifest.md)** — the update manifest shape generated from `releases.json`, the consistency invariant, and the updater CLI surface (`--check`, `--download`).
- **[quickstart.md](./quickstart.md)** — 24 numbered validation scenarios grouped by slice, each naming the FR it proves. This is the acceptance gate for "done" per the constitution's Development Workflow section.

**Agent context**: `update-agent-context.ps1 -AgentType claude` was run; it reported no new technology to add, which is the expected outcome — this feature introduces no dependency, framework, or language not already recorded in `CLAUDE.md`.

## Post-Design Constitution Re-Check

Re-evaluated against the completed Phase 1 artifacts:

| Principle | Post-design assessment | Verdict |
|---|---|---|
| I. Process Isolation | Design confirms zero new shell-side computation. The one risky item — the installer download — is explicitly assigned to the updater process in `contracts/update-manifest.md`. | ✅ PASS |
| II. Build Integrity | No design element touches VSSDK properties, theme CSS generation, or toolchain pins. | ✅ PASS |
| III. Tests & Corpora | `quickstart.md` names a test file for every FR group; no corpus is in the touched path. | ✅ PASS |
| IV. Git Consent | No artifact instructs a git operation. | ✅ PASS |
| V. Simplicity | Design added exactly one new file (`ApiKeyProtector.cs`) and zero new IPC types, storage locations, or processes. The one new setting (schema budget) is required verbatim by FR-026. | ✅ PASS |

**Re-check result: PASS.** No entries for Complexity Tracking; that section is omitted as the template directs.

## Risks and Open Items

| Risk | Impact | Mitigation |
|---|---|---|
| FR-046 needs a clean machine plus a real published release | Cannot be closed on the dev box; blocks "done" for slice E | Sequence it last; every other slice E requirement is verifiable locally against a staged manifest. Record evidence in `doc/progress.md`. |
| Kimi key needed to verify slice B end-to-end | Story 2 acceptance scenarios 4–7 cannot run without one | Unit-level work (factory case, family detection, name mapping, Options round-trip) is fully testable without a key; only the live call is gated. |
| Changing key storage from plaintext to DPAPI | An existing user's saved key must keep working | `Decrypt` already passes plaintext through unchanged, so reads are backward-compatible; the next save upgrades it in place. Entropy string must not change. |
| Widening schema context increases prompt size | Higher token cost and latency per AI call | FR-026's explicit budget is the control; `contracts/schema-context.md` pins the default and the truncation signal. Measure against the 200 ms goal on a 500-object database. |
| Editing `releases.json` generation in the deploy script | A mistake here breaks the live download page | The script's existing behaviour is additive and idempotent (it skips versions already staged); the manifest is emitted from the same in-memory object, and `AkmlSql.Site.Tests` asserts the two agree. |
