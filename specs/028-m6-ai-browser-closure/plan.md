# Implementation Plan: M6 — AI Parity Closure (Privacy Modes, Streaming, All Providers, Index Analysis, Ghost Text)

**Branch**: `028-m6-ai-browser-closure` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/028-m6-ai-browser-closure/spec.md`

## Summary

Close the genuinely-unmet M6 work. The entire M6 *scaffold* already shipped under spec 021 Phase 7 (T121–T138): `AkmlSql.AI` (net10.0) is extracted and consumed by engine + web; the browser already has a Web-Crypto key vault, an origin-allow-listed direct-to-provider client, a prompt service, a four-action AI panel, a chat panel, a settings page, and error mapping. What is missing is the user-facing depth the PRD makes central: **privacy disclosure modes fed from the M5 schema cache, streaming/typewriter responses, native Claude browser-direct, Index Analysis as a fifth action, Ghost Text, chat persistence + markdown export**, and the deferred verification (US5 E2E T137, AiPanel bUnit T134, privacy network-capture audit T146) plus a feature-parity audit and the privacy/CORS docs. Seven user stories, priority order.

Four scope reconciliations were settled with the user (the first three at `/speckit.specify`, the fourth during this plan after an empirical CORS test), each recorded in [research.md](./research.md):

- **Key storage keeps the shipped non-extractable-`CryptoKey` vault** — *not* the PRD §4.3 passphrase/PBKDF2 design. (research Decision/Recon 1.)
- **Privacy = the PRD's four *disclosure* modes** (full / names-only / no-schema / fully-local), a different axis than the engine's redaction modes; engine `anonymous` is out of scope for the browser. (Recon 2.)
- **All five providers must work browser-direct → narrowed to "every provider whose API permits it."** A plan-phase cross-origin `fetch` test proved **Claude** (native + `anthropic-dangerous-direct-browser-access`), **Gemini** (OpenAI-compat + Bearer), and **local** work browser-direct, but **OpenAI and Azure are CORS-blocked by their own APIs**. M6 ships what works and **documents OpenAI/Azure as not-available-browser-direct** (no AKML proxy, no engine relay; PRD §10). (Recon 3 — empirically verified, see research Decision 3.)
- **Schema-aware prompting requires the `SchemaPhasePayload → DatabaseCache` rehydrator that M5 deliberately deferred.** The browser cache stores one-way `SchemaPhasePayload` MessagePack bytes; the canonical `SchemaContextBuilder` (in `AkmlSql.AI`) needs a `DatabaseCache`. M6 builds the modest (~120-line) reverse mapper M5's research Decision 3 named — a **conscious reversal** of that deferral, now justified because AI prompting needs the canonical builder (and it also unblocks the M5 cached-heavyweight follow-up for free). (research Decision 1.)

The structural moves are: **one new shared mapper** (`SchemaPhaseRehydrator` in `AkmlSql.IntelliSense`, WASM-safe, tested in the shared test project), a **three-axis provider abstraction** in `AkmlSql.Web` (request-builder × auth-applier × SSE-parser) so Claude's native wire and streaming sit beside the OpenAI shape, **two new IndexedDB stores** (`aiFeatureSettings`, `chatHistory`), **~100 lines of hand-rolled CodeMirror ghost-text JS**, and a set of new/extended web services + Razor surfaces. No new IPC message types (browser AI stays engine-bypassed); no engine behavioural change.

## Technical Context

**Language/Version**: C# 12 / .NET 10 (`net10.0`) for `AkmlSql.AI`, `AkmlSql.IntelliSense`, `AkmlSql.Engine`, and test projects; Blazor WebAssembly (`net10.0`) for `AkmlSql.Web`; JavaScript (ES modules) for the CodeMirror 6 editor layer (`wwwroot/js/akml-editor.js`).
**Primary Dependencies**: existing `AkmlSql.AI` (prompt builders incl. `IndexAnalysisPrompt`, `SchemaContextBuilder`/`SchemaContextFormatter`, `PrivacyTransformer`, `StreamCoalescer`); `Microsoft.SqlServer.TransactSql.ScriptDom` (already in `AkmlSql.IntelliSense`); `System.Net.Http.HttpClient` (Blazor WASM `BrowserHttpHandler` — **.NET 10 response streaming is on by default**); CodeMirror 6 (`@codemirror/state`, `view`, `autocomplete`, `language` — all already loaded by `akml-editor.js`); `MessagePack` (already integrated; drives the schema-payload deserialize); xUnit + bUnit + Playwright .NET (already integrated). **No new NuGet packages, no new npm/CDN packages, no new IPC message types.**
**Storage**: **Two new IndexedDB stores** — `aiFeatureSettings` (global default privacy mode + per-feature overrides + ghost-text enable/rate-limit) and `chatHistory` (persisted conversations). Existing stores unchanged (`aiKeys`, `keyMaterial`, `schemaEntries`, `snippets`, `analysisSettings`, …). `DB_VERSION` bumps 1 → 2 in `akml-indexeddb.js` + the `StoreNames`/`STORES` registries. Markdown export reuses the existing `akml-download.js` `downloadBase64`. The parity + privacy audits are checked-in markdown.
**Testing**: `dotnet test` (xUnit + bUnit) for the rehydrator, privacy-mode mapping, Anthropic-wire builder/parser, streaming SSE parsers, ghost-text controller logic, chat-store round-trip, AiPanel component (T134); `dotnet test --filter Category=BridgeE2E` (or the established web-E2E trait) for the US5 / US7 end-to-end against a **mock-provider harness** (the deferred T137). The privacy network-capture audit (T146/SC-009) is a developer-side Playwright capture per privacy mode.
**Target Platform**: Chromium (Playwright) for browser E2E; any modern browser (WASM) at runtime; Windows 11 + .NET 10 SDK for build/tests. **Verified at plan time** via a real cross-origin `fetch` from `https://example.com`: Claude (with header) → 401, Gemini → 400, OpenAI → `TypeError` (CORS-blocked), Anthropic-without-header → `TypeError`.
**Project Type**: Feature-build closure over the merged Phase-7 stack. One new shared mapper (`AkmlSql.IntelliSense`); everything else additive in `AkmlSql.Web` (services, Razor, JS) + tests + docs.
**Performance Goals**: First streamed token renders within ~1500 ms on Claude/Gemini (PRD success metric; provider-dependent — recorded, not asserted as a hard gate). Ghost Text debounce ≈350 ms; ≥30 % prompt+prefix cache-hit rate during sustained typing (SC-006); rate-limited to ≤1 request / 3 s default. No regression to the existing 10 MB document ceiling.
**Constraints**:

- **Browser AI stays engine-bypassed** (PRD §4.1): all provider calls go browser→provider directly; **no new IPC message type**, and the CORS reconciliation deliberately *documents* OpenAI/Azure out rather than relaying them through the engine (which would reintroduce the engine in the AI path).
- **The "no schema" privacy guarantee MUST hold on every code path** (FR-007) — including retries and the fallback provider — verified by network capture (FR-036/SC-003).
- **The `SchemaPhaseRehydrator` MUST stay WASM-safe** (no `System.IO`/SqlClient/native) and byte-compatible with the engine's `DatabaseCache` shape; it lives in `AkmlSql.IntelliSense` beside the parser/schema models it maps onto.
- **Native Claude MUST use Anthropic's exact contract** (`x-api-key`, `anthropic-version: 2023-06-01`, `anthropic-dangerous-direct-browser-access: true`, Messages body + SSE event parsing) — verified to clear CORS at plan time.
- **Streaming MUST use `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync`** (per-request; no DI change to the singleton `HttpClient`); on net10 no `SetBrowserResponseStreamingEnabled` call is needed (flagged as a target-framework-coupled assumption).
- **Ghost Text MUST be opt-in / off by default** (parity with WPF) and must not fight the existing autocomplete/snippet Tab handling (precedence guard via `completionStatus` + active-snippet check).
- **Key vault unchanged** (FR-002): the non-extractable-`CryptoKey` scheme is retained; no passphrase/PBKDF2.
- **E2E + audits run developer-side** (mock provider + interactive workstation), matching specs 024/025/027.

**Scale/Scope**: Seven user stories; 39 functional requirements (one reconciled this phase). Deltas: **1 new shared class** (`SchemaPhaseRehydrator`); **~4 new web services** (`IAiFeatureSettings`/privacy, `IChatHistoryStore`, `IAiGhostTextService`, a per-surface streaming controller + token counter); **extend 2 web services** (`IAiClientFactory` → 3-axis provider abstraction + streaming `IAsyncEnumerable`; `IAiPromptService` → IndexAnalysis + privacy/schema wiring + streaming); **3 extended Razor surfaces** (`AiPanel`, `AiChatPanel`, `SettingsAi`) + `Editor` ghost-text wiring + a privacy-mode indicator component; **~100 lines new JS** in `akml-editor.js`; **2 new IndexedDB stores** (DB_VERSION bump); **~7 new test classes** + the deferred E2E/bUnit; **3 docs** (privacy commitment, Ollama CORS, parity audit) + quickstart/progress updates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists in this repository (confirmed — the glob finds none), so no formal constitution gates apply. The closure adopts the same self-imposed gates the spec-025/027 closures used; all hold here:

- **No new IPC message types.** Browser AI is engine-bypassed (PRD §4.1) — every provider call is a direct browser fetch. The CORS reconciliation keeps this true by documenting OpenAI/Azure out rather than relaying them through the engine. No wire envelope is added.
- **No new test framework.** The existing xUnit + bUnit + Playwright .NET stacks are extended; the mock-provider harness is a test fixture, not a new framework.
- **No new package dependencies.** Ghost Text is hand-rolled from CM6 primitives already in the loaded bundle (no new CDN/npm package); streaming uses the framework `HttpClient`; the rehydrator uses existing models. The AI provider SDKs stay engine-side; the browser fetches REST directly (as it already does).
- **Shared logic stays shared; the engine keeps consuming it.** The one new shared type (`SchemaPhaseRehydrator`) goes in `AkmlSql.IntelliSense` (where `DatabaseCache` lives) and is WASM-safe; prompt builders / `SchemaContextBuilder` / `PrivacyTransformer` in `AkmlSql.AI` are reused unchanged. The three-axis provider abstraction is web-only (the engine uses its SDKs) — it does not touch engine behaviour.

A fourth, scope-specific gate: **the four reconciliations only ever narrow or redirect scope, never silently** — each is recorded in research.md with rationale, reflected in the revised FRs/SCs, and (for the CORS one) listed as a named Out-of-Scope follow-up.

These are re-checked in the Post-Design re-evaluation (Complexity Tracking).

## Project Structure

### Documentation (this feature)

```text
specs/028-m6-ai-browser-closure/
├── plan.md                              # This file
├── spec.md                              # /speckit.specify output (FR-013/FR-015/SC-002/US3 reconciled this phase)
├── research.md                          # 7 decisions + 4 reconciliations (CORS empirically verified)
├── data-model.md                        # conceptual + new entities (privacy mode, rehydrator, provider profile, chat store, ghost request)
├── quickstart.md                        # per-user-story build + verify walkthrough
├── contracts/
│   ├── privacy-and-schema-contract.md   # US1 (4 disclosure modes + SchemaPhaseRehydrator + per-feature/global storage)
│   ├── streaming-contract.md            # US2 (ResponseHeadersRead + per-provider SSE parsers + per-surface controller)
│   ├── providers-contract.md            # US3 (3-axis abstraction; Claude native; Gemini/local; OpenAI/Azure documented-out + CORS matrix)
│   ├── index-analysis-contract.md       # US4 (5th action via IndexAnalysisPrompt)
│   ├── ghost-text-contract.md           # US5 (CM6 StateField/widget/keymap + IAiGhostTextService + cache/rate-limit)
│   ├── chat-persistence-contract.md     # US6 (chatHistory store + markdown export)
│   └── verification-and-audit-contract.md # US7 (E2E/bUnit/privacy-capture/parity/docs)
├── checklists/
│   └── requirements.md                  # /speckit.specify output; passes (closure-convention note)
├── M6-PARITY-AUDIT.md                   # US7 output (created during implementation)
└── tasks.md                             # /speckit.tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.IntelliSense/
│   └── Schema/
│       └── SchemaPhaseRehydrator.cs     # ← NEW; SchemaPhasePayload → DatabaseCache (reverse of SchemaPhaseSerializer); WASM-safe (US1)
│
├── AkmlSql.AI/                          # ← REUSED UNCHANGED: SchemaContextBuilder, SchemaContextFormatter, IndexAnalysisPrompt, GhostTextPrompt, PrivacyTransformer
│
└── AkmlSql.Web/
    ├── Services/
    │   ├── IAiClientFactory.cs          # ← extend: 3-axis provider abstraction (request-builder × auth × SSE-parser); Claude native; IAsyncEnumerable<string> streaming path (US2/US3)
    │   ├── IAiPromptService.cs          # ← extend: +IndexAnalysisAsync; wire privacy mode + schema-from-cache; streaming overloads (US1/US2/US4)
    │   ├── IAiFeatureSettings.cs        # ← NEW; global default + per-feature privacy mode + ghost-text settings (aiFeatureSettings store) (US1/US5)
    │   ├── IAiSchemaContextProvider.cs  # ← NEW; resolves SchemaText from ISchemaCacheStore via SchemaPhaseRehydrator + SchemaContextBuilder, filtered by mode (US1)
    │   ├── IAiGhostTextService.cs       # ← NEW; direct-to-provider ghost completion via GhostTextPrompt; cache + rate limit + token counter (US5)
    │   ├── IChatHistoryStore.cs         # ← NEW; persisted conversations (chatHistory store) (US6)
    │   └── JsIndexedDbAdapter.cs        # ← extend StoreNames: +aiFeatureSettings, +chatHistory
    ├── Pages/
    │   └── SettingsAi.razor             # ← privacy modes (global + per-feature), ghost-text settings, OpenAI/Azure not-browser-direct notice (US1/US3/US5)
    ├── Shared/
    │   ├── AiPanel.razor                # ← +Index Analysis (5th action); privacy-mode indicator; streaming render (US1/US2/US4)
    │   ├── AiChatPanel.razor            # ← persistence + markdown export + token counter; streaming render (US2/US6)
    │   ├── EditorComponent.razor        # ← +RequestGhostTextFromJs callback (US5)
    │   ├── AiPrivacyModeBadge.razor     # ← NEW; the "active mode" indicator shown next to each AI control (US1)
    │   └── CapabilityNotice.razor       # ← reused for "fully local needs a local provider" + OpenAI/Azure CORS notice (US1/US3)
    └── wwwroot/
        ├── js/akml-editor.js            # ← +setGhostText/clearGhostText/triggerGhostText + debounced change hook + Prec.highest Tab/Esc keymap (US5)
        ├── js/akml-indexeddb.js         # ← DB_VERSION 1→2; +aiFeatureSettings,+chatHistory in STORES
        └── js/akml-download.js          # ← REUSED for chat markdown export (US6)

tests/
├── AkmlSql.IntelliSense.Tests/         # ← SchemaPhaseRehydrator round-trip vs a known DatabaseCache (US1)
├── AkmlSql.Web.Tests/
│   ├── Ai/PrivacyModeTests.cs          # ← per-mode schema disclosure (full/names/none/local) (US1)
│   ├── Ai/AnthropicWireTests.cs        # ← request builder + SSE parser for native Claude (US3)
│   ├── Ai/StreamingParserTests.cs      # ← OpenAI + Anthropic SSE delta parsers; cancellation; cross-surface isolation (US2)
│   ├── Ai/GhostTextControllerTests.cs  # ← debounce/suppression/cache/rate-limit logic (US5)
│   ├── Ai/ChatHistoryStoreTests.cs     # ← persist/restore/clear; export markdown shape (US6)
│   └── Ai/AiPanelTests.cs              # ← bUnit (the deferred T134): action wiring, no-key, error render, key-never-in-DOM (US7)
└── AkmlSql.Web.E2E.Tests/
    └── UserStory5AiTests.cs            # ← the deferred T137: add key → run feature → response renders; privacy capture; key never plaintext (US7)

doc/WEB/quickstart-m6.md                 # ← update: remove "what's deferred" caveats now closed
doc/WEB/ai-privacy-commitment.md         # ← NEW; threat model + privacy commitment (US7)
doc/WEB/ai-local-provider-cors.md        # ← NEW; OLLAMA_ORIGINS / LM Studio CORS setup (US7)  [or a section in quickstart-m6]
doc/progress.md                          # ← spec-028 closure summary
```

**Structure Decision**: Feature build over the merged Phase-7 stack. Exactly one new *shared* type (`SchemaPhaseRehydrator` in `AkmlSql.IntelliSense`, the M5-deferred reverse mapper, WASM-safe, gated by a round-trip test). Everything else is additive in `AkmlSql.Web` (4 new services, 2 extended services, 3 extended Razor surfaces + 1 new indicator component + editor wiring, ~100 lines of CM6 JS, 2 new IndexedDB stores) plus test classes and three docs. No new csproj, no new NuGet/CDN package, no new IPC message type, and no engine behavioural change.

## Phase 0: Research

Seven decisions (≈ one per user story) + four reconciliations, in [research.md](./research.md). Every decision was checked against current source and (for the provider/CORS decision) **empirically verified by a live cross-origin fetch test**, not the PRD's stale assumptions:

1. **Privacy + schema-from-cache** (US1): build the M5-deferred `SchemaPhasePayload → DatabaseCache` rehydrator (~120 lines, `AkmlSql.IntelliSense`) so the canonical `SchemaContextBuilder` can run in the browser; map the four disclosure modes to (include-schema, compression level, force-local); persist global + per-feature modes in a new `aiFeatureSettings` store.
2. **Streaming** (US2): `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` (net10 default-on streaming) + per-provider SSE delta parser; `IAsyncEnumerable<string>` token stream; one streaming controller per surface, cancellation bound to its lifetime; buffered fallback.
3. **Providers / CORS** (US3) — RECONCILIATION, empirically verified: Claude native + Gemini OpenAI-compat + local work browser-direct; OpenAI/Azure are CORS-blocked → documented out. Three-axis abstraction (request-builder × auth × SSE-parser).
4. **Index Analysis** (US4): add `IndexAnalysisAsync` over the existing `IndexAnalysisPrompt.Build(schemaText, sql, executionPlanXml)`; fifth panel action; honours privacy mode.
5. **Ghost Text** (US5): hand-roll ~100 lines of CM6 (StateField + WidgetType + `Prec.highest` Tab/Esc keymap) using already-loaded bundle modules; new `IAiGhostTextService` direct-to-provider reusing `GhostTextPrompt`; `RequestGhostTextFromJs` parallels `RequestCompletionsFromJs`; suppression via `syntaxTree` node names; prefix cache + rate limit + token counter.
6. **Chat persistence + export** (US6): new `chatHistory` store + `IChatHistoryStore`; markdown export via existing `akml-download.js`; record originating provider per turn.
7. **Verification & audit** (US7): reuse the Playwright .NET / bUnit harness + a mock-provider fixture; privacy network-capture audit; `M6-PARITY-AUDIT.md`; privacy-commitment + Ollama-CORS docs.

## Phase 1: Design & Contracts

- **[data-model.md](./data-model.md)**: the new/extended entities — `AiPrivacyMode` (4 disclosure modes) + `AiFeatureSettings` (global + per-feature, ghost settings), `SchemaPhaseRehydrator` mapping, `ProviderProfile` (request-builder/auth/SSE-parser triple per provider), `StreamingController`, `GhostTextRequest` (debounce/cache/rate-limit), `ChatConversation`/`ChatTurn` (+provider), and the two audit docs.
- **[contracts/](./contracts/)**: seven contracts (privacy+schema, streaming, providers, index-analysis, ghost-text, chat-persistence, verification+audit), each binding its FRs to verified current-source facts, the exact wire/CORS facts from the plan-time test, and a test contract.
- **Agent context**: run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` to record the new surfaces (browser privacy modes + schema rehydrator, streaming provider abstraction incl. native Claude, CM6 ghost text, chat persistence).

## Phase 2 planning note

`/speckit.tasks` generates `tasks.md`. Expected shape, priority order:

- **US1 first** — the `SchemaPhaseRehydrator` + round-trip test is the first task (it gates schema-aware prompting and is the M5-deferred reverse mapper); then `IAiFeatureSettings` store + the 4-mode mapping + `IAiSchemaContextProvider` + the privacy-mode badge + wiring every existing action through the mode; privacy network-capture test.
- **US2** — extend `IAiClientFactory` to stream (`ResponseHeadersRead` + SSE parser) returning `IAsyncEnumerable<string>`; per-surface streaming controller + cancellation; render incrementally in AiPanel + chat; buffered fallback.
- **US3** — the three-axis provider abstraction (refactor the current OpenAI-only `SendAsync`); Claude native request-builder + auth + SSE parser; Gemini verify; OpenAI/Azure not-available notice; local-CORS doc.
- **US4** — `IndexAnalysisAsync` + the fifth AiPanel action.
- **US5** — CM6 ghost-text JS (StateField/widget/keymap) → `RequestGhostTextFromJs` → `IAiGhostTextService` (cache/rate-limit/token-counter) → settings toggle.
- **US6** — `chatHistory` store + `IChatHistoryStore` + persist/restore/clear + markdown export button.
- **US7** — AiPanel bUnit (T134), US5 E2E on the mock-provider fixture (T137), privacy network-capture audit (T146), parity audit doc, privacy-commitment + CORS docs.

Each story is independently demoable; US2 and US3 share the client refactor, so the tasks sequence the 3-axis abstraction once and layer streaming + Claude on it.

## Complexity Tracking

No constitution gate violations (no constitution). The self-imposed gates hold post-design:

- **No new IPC message types** — browser AI is engine-bypassed; the CORS reconciliation documents OpenAI/Azure out rather than relaying them through the engine.
- **No new test framework / no new package** — xUnit/bUnit/Playwright extended; ghost text hand-rolled from already-loaded CM6 primitives; streaming uses the framework `HttpClient`.
- **Shared logic stays shared** — `SchemaPhaseRehydrator` in `AkmlSql.IntelliSense` (WASM-safe); `AkmlSql.AI` builders reused unchanged; the provider abstraction is web-only and does not touch engine behaviour.
- **Reconciliations narrow/redirect scope explicitly** — all four recorded in research.md, reflected in revised FRs/SCs; OpenAI/Azure browser-direct is a named Out-of-Scope follow-up.

The two non-trivial risks, both mitigated:

1. **The `SchemaPhaseRehydrator` is the path M5 deliberately deferred** (research Decision 3 of spec 027 called it "a permanent second deserialization path that must stay byte-compatible forever"). Mitigation: it is a pure reverse of the existing `SchemaPhaseSerializer` over existing models, lives next to `DatabaseCache`, and is gated by a round-trip test (`DatabaseCache → payload → DatabaseCache` equality). It is reused — not duplicated — so it cannot diverge per-feature, and it unblocks the M5 cached-heavyweight follow-up.
2. **Native Claude + streaming wire correctness** — mitigated by the plan-time empirical verification (Claude-with-header → 401, confirming CORS + contract) and unit tests for each provider's SSE parser; the three-axis abstraction isolates per-provider differences so a wire bug is contained to one builder/parser.
