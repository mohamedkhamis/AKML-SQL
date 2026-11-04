# Implementation Plan: AI Chat — Copilot-style experience in SSMS and the web edition

**Branch**: `046-ai-chat-copilot-ux` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/046-ai-chat-copilot-ux/spec.md`

## Summary

Make the SSMS AI Chat answer about the **query window that is active**, not about whichever text
view last had the keyboard: one resolver (`ChatBindingResolver`) decides the binding from the
active document and the extension's tracked editors, keeps the last query window when a tool
window is active, and feeds the header, the schema poll, Send and "Insert into query" alike. A
refusal names what was found and writes one log line. Then give both editions a Copilot-style
conversation: answers are parsed by a small shared block parser (`ChatMarkdown`, in Core) and
rendered as prose, lists and **code panels with their own Copy / Insert actions**; turns are
attributed "Name · Provider · model"; the composer carries the agent selector, a Send control whose
tooltip names Ctrl+Enter, a self-explaining placeholder, and a **New chat** control; an empty
conversation shows a welcome with three starter questions. The engine adds two append-only reply
fields (the answering agent's provider and model). The web chat gains the same selector (it changes
the one active agent the web edition shares), the renderer with Insert at caret, New chat in place
of Clear, and the hints.

**Approach** ([research.md](./research.md) R1–R14): document-based binding with a sticky fallback
(R1) and an Information-level refusal log line (R2); shared parser + two renderers rather than a
markdown package (R5); FlowDocument prose and read-only code panels in WPF so text stays
selectable (R6); the picker moves into the composer bar and its entries read "Name · Provider ·
model" through Core's existing `AiProviderIds.DisplayName` (R9); `AiChatResponse` keys 7 and 8
(R4); web turns store the agent label they were answered with (R10). No new theme token, no new
IPC message type, no new package (R11, R13).

**Spec amendments made at plan time**: none. The owner's three decisions of 2026-11-04 (whole
answers in SSMS, one conversation, agent-switch note) are already in the spec.

## Technical Context

**Language/Version**: C# (LangVersion latest) on .NET Framework 4.7.2 for the SSMS shell
(`AkmlSql.Shell.Shared` .projitems → `AkmlSql.Ssms22`); netstandard2.0 + net10.0 for
`AkmlSql.Core` (the parser must compile for both); .NET 10 for the engine, the web edition (Blazor
WebAssembly) and all test projects; JavaScript ES modules for the web helpers
**Primary Dependencies**: existing only — VS SDK 17.14.* (WPF, `IVsTextManager`, DTE), MessagePack
(IPC), Serilog, xunit 2.x + Xunit.StaFact (shell), bUnit (web), Playwright 1.* (browser), node test
runner (JS). No markdown package is added (R5)
**Storage**: SSMS — none new (conversation in memory for the SSMS session; `config.json`
`ai.featureAgents.chat` as today). Web — IndexedDB `chatHistory` record gains an `AgentLabel` per
assistant turn (additive, old records read as before); the `_active` agent preference as today
**Testing**: `tests/AkmlSql.Core.Tests` (parser), `tests/AkmlSql.Shell.Shared.Tests` (STA panel
tests, pure resolver decision tests), `tests/AkmlSql.Engine.Tests` (reply fields),
`tests/AkmlSql.Web.Tests` (bUnit + node), `tests/AkmlSql.Web.E2E.Tests` (Playwright, serial, Debug
build, mock provider); SSMS scenarios are manual (quickstart §SSMS) — the Messages-pane repro
cannot be automated
**Target Platform**: SSMS 22 (x64) with the Windows x64 engine; browsers (Chromium, Edge, Firefox)
for the web edition
**Project Type**: desktop extension (WPF tool window) + out-of-process engine + Blazor WASM web app
**Performance Goals**: binding resolution ≤ 5 ms on the UI thread per 2-second tick (DTE documents
enumerated only when the active document path changed); header follows a window switch within
2 s (FR-007); a 5,000-line answer renders in < 200 ms; Send tooltip appears within 1 s of hover
(SC-004)
**Constraints**: IPC append-only (keys 7 and 8 on `AiChatResponse`, no new message type,
capability list unchanged); never send under a fabricated session id (spec 036 R1); no new theme
token (code panels reuse `SurfaceCanvas` / `BorderDefault`, inline code `SurfaceInputReadOnly`);
shell stays thin (parsing in Core, no AI logic in the shell); web chat has no database binding;
unusable agents stay hidden (spec 037 S1); `AiChatRequest` unchanged
**Scale/Scope**: shell — 3 new files (`ChatBindingResolver`, `ChatMessageRenderer`,
`AiChatWelcomeCard`) and 4 modified (`AiChatPanel`, `AiAgentPicker`, `AiEditorSqlInserter`,
`ConnectionWiringHelper`); Core — 1 new file (`Text/ChatMarkdown.cs`); engine — 2 files
(`AiChatResponse`, `AiChatHandler` + the fallback helper); web — 2 new components
(`ChatMessageView`, `AiAgentSelector`), `AiChatPanel.razor`, `Editor.razor` wiring,
`IChatHistoryStore` model, `akml-ui.js`; ~12 test classes new or extended; ~25 user-facing
strings; 6 user stories

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 (see end of file).*

| Principle | Status | Evidence |
| --- | --- | --- |
| I. Process isolation & host safety | PASS | The shell gains UI and a host lookup (`ChatBindingResolver` reads DTE and the tracked views it already keeps); the parser is a shared Core library consumed by shell and web; the engine change is two reply fields; no AI or schema logic moves into the shell (R1, R4, R5). |
| II. Build integrity | PASS | No theme token added or edited, so `generate-theme-css.ps1 -CheckOnly` and `ThemeCssSyncTests` are untouched; shell built with MSBuild; no SDK or package change; one-pass solution build is the gate (quickstart §Build). |
| III. Tests & corpora non-regressible | PASS | Every change lands with tests (quickstart §Tests): parser (Core), resolver decision + panel STA tests (shell), reply round-trip (engine), bUnit + node + Playwright (web). Format and completion corpora untouched. Existing chat tests that assert on the old bubble or the "Clear" label are updated, not deleted (R12). |
| IV. Git consent | PASS | No git mutation in this plan; implementation delivers uncommitted changes and asks. |
| V. Simplicity & convention fidelity | PASS | Reuse first: `ConnectionWiringHelper.OpenViews` and `SsmsConnectionDetector` for the binding; `AddConversationNotice` for both new notices; `AiProviderIds.DisplayName` for labels; the existing `AiAgentPicker`, `ComboBoxTheming`, `FlashButtonContent`, `ThemeTokens`; web `Icon`, `akml-ui.js copyText`, `OnAiAcceptResultAsync`. One small shared parser instead of a package. No configurability added. |

**Additional constraints**: no secret touches the log (the refusal line carries a document name,
booleans and a reason only); IPC local-only unchanged; document and snippet limits unchanged;
technology pins unchanged; `doc/ipc-api.md`, `doc/use-cases.md`, `doc/progress.md` and
`CLAUDE.md` updated in the same change (R13).

## Project Structure

### Documentation (this feature)

```text
specs/046-ai-chat-copilot-ux/
├── plan.md                         # this file
├── spec.md                         # feature spec (clarified 2026-11-04)
├── research.md                     # Phase 0 — decisions R1–R14
├── data-model.md                   # Phase 1 — binding, block model, turn/reply additions, states
├── quickstart.md                   # Phase 1 — build, tests, validation scenarios (acceptance gate)
├── contracts/
│   ├── chat-binding.md             # SSMS binding resolver, refusal texts, log line, notices
│   ├── chat-rendering.md           # markdown subset, block model, both renderers, block actions, welcome, scroll
│   └── chat-composer-and-agents.md # keys, tooltips, placeholder, selector format, New chat, IPC keys 7–8, web selector
├── checklists/requirements.md
└── tasks.md                        # /speckit.tasks output (not created here)
```

### Source Code (repository root)

```text
src/AkmlSql.Core/
├── Text/ChatMarkdown.cs                     # NEW — block parser (netstandard2.0 + net10.0), R5
├── Config/AiProviderIds.cs                  # reused — DisplayName(providerId) for labels, R9
└── Ipc/Messages/AiChatResponse.cs           # + AgentProvider (Key 7), AgentModel (Key 8), R4

src/AkmlSql.Engine/
├── Handlers/Ai/AiChatHandler.cs             # fills keys 7–8 from the answering agent
└── Ai/AiPipelineServices.cs                 # ExecuteWithFallbackAsync also returns the answering agent (R4)

src/AkmlSql.Shell.Shared/
├── Ai/ChatBindingResolver.cs                # NEW — document-based binding + sticky fallback + pure Decide(), R1
├── Ai/ChatMessageRenderer.cs                # NEW — ChatBlock → WPF (FlowDocument prose, code panels), R6
├── Ai/AiChatWelcomeCard.cs                  # NEW — welcome + three starters, R7
├── Ai/AiChatPanel.cs                        # uses the resolver everywhere; composer bar; New chat; tooltips; notices; follow-scroll
├── Ai/AiAgentPicker.cs                      # entries "Name · Provider · model", R9
├── Ai/AiEditorSqlInserter.cs                # TryInsertAtCaret(sql, documentPath) targets the bound document, R1
└── Editor/ConnectionWiringHelper.cs         # TryFindTrackedView(filePath) over OpenViews, R1

src/AkmlSql.Web/
├── Shared/AiChatPanel.razor                 # renderer, selector in the composer, New chat, tooltips, placeholder, follow-scroll
├── Shared/ChatMessageView.razor             # NEW — ChatBlock → HTML with per-block Copy / Insert, R5
├── Shared/AiAgentSelector.razor             # NEW — usable agents as "Name · Provider · model" + Manage agents…, R9
├── Pages/Editor.razor                       # passes OnInsertSql → OnAiAcceptResultAsync
├── Services/IChatHistoryStore.cs            # ChatTurn.AgentLabel (additive), R10
└── wwwroot/js/akml-ui.js                    # isAtBottom / scrollToBottom helpers, R8

tests/
├── AkmlSql.Core.Tests/Text/ChatMarkdownTests.cs                     # NEW
├── AkmlSql.Shell.Shared.Tests/ChatBindingResolverTests.cs           # NEW — pure Decide() cases
├── AkmlSql.Shell.Shared.Tests/AiChatSessionBindingTests.cs          # extended — refusal texts, log line, header/send agreement
├── AkmlSql.Shell.Shared.Tests/AiChatRenderingTests.cs               # NEW — STA renderer + block actions
├── AkmlSql.Shell.Shared.Tests/AiChatComposerStateTests.cs           # extended — tooltips, placeholder, New chat
├── AkmlSql.Shell.Shared.Tests/AiChatAgentPickerTests.cs             # extended — label format, switch notice
├── AkmlSql.Engine.Tests/Ai/AiChatResponseAgentFieldsTests.cs        # NEW — keys 7–8 round trip
├── AkmlSql.Web.Tests/AiChatPanelComposerTests.cs                    # extended — tooltip, placeholder, New chat
├── AkmlSql.Web.Tests/ChatMessageViewTests.cs                        # NEW — blocks, Copy / Insert
├── AkmlSql.Web.Tests/AiAgentSelectorTests.cs                        # NEW
├── AkmlSql.Web.Tests/js/akml-ui.scroll.test.mjs                     # NEW
└── AkmlSql.Web.E2E.Tests/AiChatComposerKeysTests.cs                 # extended — Ctrl+Enter tooltip, code panel in a real browser

doc/ipc-api.md · doc/use-cases.md · doc/progress.md · CLAUDE.md                  # updated in the same change
```

**Structure Decision**: no new project. The parser lives in `AkmlSql.Core/Text` beside the other
pure text helpers (`SqlMessageText`, `GridTextFormats`) because it is consumed by both the shell
(net472) and the web (net10.0) and has no engine dependency. Shell code stays in the shared
`.projitems`; the two web components sit in `Shared/` with the other panels.

## Complexity Tracking

No constitution violations to justify. Two design choices that might look like extra machinery,
recorded for the reviewer:

| Choice | Why | Simpler alternative rejected because |
|--------|-----|--------------------------------------|
| Own block parser (`ChatMarkdown`) instead of a markdown package | One behaviour in both editions; netstandard2.0 shell cannot take a new assembly without resolver work; the subset is small (headings, lists, inline code, bold, fences) | Markdig in the web only would render differently from SSMS, and a package in the VSIX adds an assembly-load risk for a 150-line need |
| `ChatBindingResolver` as a new class rather than a fix inside `TryGetActiveRealSessionId` | The other AI commands (Explain, Fix, Text to SQL) are invoked *from* the editor and must keep the focused-view rule; the chat alone needs document-based, sticky binding | Changing the shared helper would alter commands the spec does not touch |

## Post-design Constitution re-check (after Phase 1)

| Principle | Status | Note |
| --- | --- | --- |
| I | PASS | Contracts confirm: shell = UI + host lookup; Core = parser; engine = two fields. |
| II | PASS | `data-model.md` and `contracts/` introduce no token, package or SDK change. |
| III | PASS | `quickstart.md` lists a test per contract rule; corpora untouched. |
| IV | PASS | Nothing committed by the plan. |
| V | PASS | The composer-bar move is the spec's Copilot layout, not an opportunistic refactor; everything else extends existing seams. |
