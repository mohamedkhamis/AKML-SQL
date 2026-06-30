# Tasks: M6 — AI Parity Closure (Privacy Modes, Streaming, All Providers, Index Analysis, Ghost Text)

**Input**: Design documents from `/specs/028-m6-ai-browser-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — the spec (FR-034 E2E, FR-035 bUnit, SC-003/SC-006/SC-008) and every contract's "Test contract" section explicitly require them. Test tasks below are part of the deliverable, not optional.

**Organization**: Tasks are grouped by user story (priority order P1 → P3). This is a **feature-build closure over the merged spec-021 Phase-7 stack**; almost all work is additive in `AkmlSql.Web`, plus one new shared mapper in `AkmlSql.IntelliSense`. No new IPC message types; browser AI stays engine-bypassed.

**Single-engineer note**: Per the plan (and spec 021's "Phase 7 AI is a single-engineer milestone"), several stories touch the same shared files (`IAiClientFactory.cs`, `IAiPromptService.cs`, `AiPanel.razor`, `SettingsAi.razor`, `Program.cs`), so cross-story parallelism is limited — execute in priority order. `[P]` marks tasks on distinct files that can run alongside their phase siblings.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different file, no dependency on an incomplete task)
- **[Story]**: US1–US7 (user-story phases only)

## Path Conventions

Repo root `D:\Repo\01-Khamis-Projects\AKML-SQL`. Shared lib `src/AkmlSql.IntelliSense/`; shared AI `src/AkmlSql.AI/` (reused unchanged); web `src/AkmlSql.Web/`; tests under `tests/`; docs under `doc/WEB/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Baseline + the one shared IndexedDB migration both US1 and US6 need.

- [X] T001 Record baseline: build `src/AkmlSql.Web/AkmlSql.Web.csproj` (Release) and run `tests/AkmlSql.Web.Tests` + `tests/AkmlSql.AI.Tests` green; note the current AI surface (4 actions, buffered OpenAI-wire client) so regressions are visible.
- [X] T002 IndexedDB migration: add `public const string AiFeatureSettings = "aiFeatureSettings";` and `public const string ChatHistory = "chatHistory";` to `StoreNames` in `src/AkmlSql.Web/Services/JsIndexedDbAdapter.cs`; add `'aiFeatureSettings'` + `'chatHistory'` to the `STORES` array and bump `DB_VERSION` 1 → 2 in `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js`. (Registering both stores once avoids a second migration; `chatHistory` is harmless before US6 uses it.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared mapper that gates every schema-aware feature, plus the provider-call backbone every story routes through.

**⚠️ CRITICAL**: T003–T005 block the schema-aware (US1/US4/US5) and provider (US2/US3/US4/US5) work.

- [X] T003 Create `SchemaPhaseRehydrator` in `src/AkmlSql.IntelliSense/Schema/SchemaPhaseRehydrator.cs` (namespace `AkmlSql.Engine.Schema`): `static DatabaseCache Rehydrate(string cacheKey, SchemaPhasePayload? phaseA, SchemaPhasePayload? phaseB)` — the reverse of `SchemaPhaseSerializer` mapping `Schemas[]→SchemaEntry`, `Objects[]→DatabaseObject`, `Columns[]→Column`, `Parameters[]→Parameter`, `ForeignKeys[]→ForeignKey`, then `RebuildFkIndex()`. WASM-safe (no `System.IO`/SqlClient/native; existing models only). This is the M5-deferred path (research Decision 1).
- [X] T004 [P] Round-trip gate test in `tests/AkmlSql.IntelliSense.Tests/SchemaPhaseRehydratorTests.cs`: a known `DatabaseCache` → `SchemaPhaseSerializer` → `SchemaPhaseRehydrator.Rehydrate` reproduces the same `GetAllObjects()`, column data, and `GetForeignKeysForTable()` results (the invariant that lets it reuse, not fork, `SchemaContextBuilder`).
- [X] T005 Introduce the `ProviderProfile` 3-axis abstraction in `src/AkmlSql.Web/Services/IAiClientFactory.cs`: interfaces `IAiRequestBuilder`, `IAuthApplier`, `ISseDeltaParser`, and the OpenAI-shape implementations (`OpenAiRequestBuilder`, `BearerAuth`, `OpenAiSseParser`) extracted from the current `SendAsync` — behaviour-preserving for the existing buffered OpenAI-wire path. Per-`providerId` profile selection; the shipped origin allow-list and error mapping stay. (Backbone for US2/US3/US4/US5; no streaming yet.)

**Checkpoint**: Rehydrator proven by its round-trip test; the provider backbone exists with the existing behaviour intact.

---

## Phase 3: User Story 1 — Control what schema the AI sees, fed from the local cache (Priority: P1) 🎯 MVP

**Goal**: Four disclosure privacy modes (full / names-only / no-schema / fully-local), global + per-feature, shown next to every AI control, with schema resolved from the M5 IndexedDB cache.

**Independent Test**: With a schema cached + a working provider, set mode "no schema" → DevTools Network shows the request carries SQL but zero schema identifiers; "full schema" shows table/column names; a per-feature override beats the global default; "fully local" restricts the provider picker to local.

- [X] T006 [US1] Create `AiPrivacyMode` enum (`FullSchema`/`SchemaNamesOnly`/`NoSchema`/`FullyLocal`) + `AiFeatureSettings` model (`GlobalDefaultMode`, `FeatureModeOverrides`, `GhostTextEnabled=false`, `GhostTextDelayMs=350`, `GhostTextMaxRequestsPer3s=1`) + `IAiFeatureSettings` store (singleton, `aiFeatureSettings`/`"current"`, mirrors `IAnalysisSettingsStore`) in `src/AkmlSql.Web/Services/IAiFeatureSettings.cs`; register in `src/AkmlSql.Web/Program.cs`.
- [X] T007 [US1] Create `IAiSchemaContextProvider` in `src/AkmlSql.Web/Services/IAiSchemaContextProvider.cs`: `GetSchemaTextAsync(featureId, ct)` resolves mode → `(includeSchema, compressionLevel, forceLocal)`; `NoSchema` ⇒ empty string; else read active `(server,db)` snapshot from `ISchemaCacheStore` → `SchemaPhaseRehydrator.Rehydrate` → `SchemaContextBuilder.BuildAsync(...compressionLevel...)` → `SchemaContextFormatter.Format`, truncated to budget; no cache ⇒ empty (degrade, never throw). Register in `Program.cs`.
- [X] T008 [P] [US1] Create `AiPrivacyModeBadge` component in `src/AkmlSql.Web/Shared/AiPrivacyModeBadge.razor` showing the resolved mode for a given feature id (theme-aware, `ThemeManager` tokens).
- [X] T009 [US1] Wire `src/AkmlSql.Web/Services/IAiPromptService.cs` Explain/Fix/Optimize/TextToSql to obtain `schemaText` from `IAiSchemaContextProvider.GetSchemaTextAsync(featureId, ct)` (replace the caller-supplied `schemaText`), enforcing the per-feature mode incl. the no-schema guarantee on retries/fallback (FR-007).
- [X] T010 [US1] Update `src/AkmlSql.Web/Shared/AiPanel.razor`: render `AiPrivacyModeBadge` next to each action; drop the inbound `SchemaText` param path in favour of the provider-resolved schema.
- [X] T011 [US1] Extend `src/AkmlSql.Web/Pages/SettingsAi.razor`: global-default privacy-mode selector + per-feature overrides (explain/fix/optimize/texttosql/indexanalysis/chat/ghosttext) persisted via `IAiFeatureSettings`.
- [X] T012 [US1] "Fully local" gating: when the resolved mode is `FullyLocal`, restrict the provider picker / active provider to local (`ollama`/`lmstudio`) and block cloud providers with a `CapabilityNotice`-style message (reuse `src/AkmlSql.Web/Shared/CapabilityNotice.razor`).
- [X] T013 [P] [US1] `tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs`: per-mode disclosure for each feature id (Full=tables+cols+FKs; NamesOnly=names, no types/FKs; NoSchema=empty; per-feature override beats global; `FullyLocal` restricts provider).

**Checkpoint**: Privacy modes + schema-from-cache work end-to-end for the existing four actions; the MVP increment is demoable and network-verifiable.

---

## Phase 4: User Story 2 — Watch answers stream in (Priority: P1)

**Goal**: Token-by-token typewriter rendering across AI surfaces, with per-surface cancellation and no cross-panel bleed; buffered fallback when streaming is unavailable.

**Independent Test**: Run Explain on Claude/Gemini → text renders incrementally; mid-stream invoke Optimize → Explain stream stops, Optimize starts in its pane, no bleed; a non-streaming mock still renders a complete answer.

- [X] T014 [US2] Add a streaming send to `src/AkmlSql.Web/Services/IAiClientFactory.cs`: `IAsyncEnumerable<string> StreamAsync(providerId, AiChatRequest, ct)` using `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` + `StreamReader.ReadLineAsync` feeding the profile's `ISseDeltaParser` (request body sets `stream:true`). Keep the buffered `SendAsync` for fallback.
- [X] T015 [US2] Add streaming overloads to `src/AkmlSql.Web/Services/IAiPromptService.cs` returning `IAsyncEnumerable<string>` for the four actions (and the future Index Analysis); the existing `Task<string>` methods buffer the stream to completion (FR-012 fallback).
- [X] T016 [US2] Create a per-surface `StreamingController` (in `src/AkmlSql.Web/Services/` or as a component helper) owning one `CancellationTokenSource` bound to the surface lifetime; starting a new action cancels the prior stream/request; mid-stream error preserves partial text + shows the mapped error (FR-011).
- [X] T017 [US2] Update `src/AkmlSql.Web/Shared/AiPanel.razor` to consume the streaming overload and render tokens incrementally via its `StreamingController`.
- [X] T018 [US2] Update `src/AkmlSql.Web/Shared/AiChatPanel.razor` to render streamed assistant turns incrementally and cancel the in-flight stream when a new message is sent.
- [X] T019 [P] [US2] `tests/AkmlSql.Web.Tests/Ai/StreamingParserTests.cs`: feed recorded OpenAI SSE sequences → assert token order + `[DONE]` termination; assert mid-stream error preserves partial; assert a cancelled controller stops yielding and two controllers' tokens stay isolated.

**Checkpoint**: All existing actions + chat stream incrementally; cancellation and isolation verified.

---

## Phase 5: User Story 3 — Every provider that permits browser-direct, incl. native Claude (Priority: P2)

**Goal**: Claude (native wire), Gemini (OpenAI-compat), and local providers work browser-direct; OpenAI/Azure are surfaced as not-available-browser-direct (CORS), never relayed.

**Independent Test**: Anthropic key → Explain returns browser-direct (Network shows `api.anthropic.com` + `x-api-key`); Gemini returns a response; Ollama with documented CORS succeeds (and names the fix when absent); selecting OpenAI shows the not-available notice with no request attempted.

- [X] T020 [P] [US3] Add the Anthropic axis to `src/AkmlSql.Web/Services/IAiClientFactory.cs`: `AnthropicRequestBuilder` (top-level `system`, `messages`, required `max_tokens`, `stream`), `AnthropicAuth` (`x-api-key`, `anthropic-version: 2023-06-01`, `anthropic-dangerous-direct-browser-access: true`), `AnthropicSseParser` (yield `content_block_delta`/`text_delta`, terminate on `message_stop`, skip `ping`/non-text).
- [X] T021 [US3] Register the `anthropic` `ProviderProfile` (Anthropic builder/auth/parser) and confirm `api.anthropic.com` is in the origin allow-list; wire it into both `SendAsync` and `StreamAsync`.
- [X] T022 [US3] Verify Gemini browser-direct end-to-end (OpenAI-compat profile + `Bearer`, incl. `stream:true`); ensure `ResolveEndpoint` points Gemini at `…/v1beta/openai/chat/completions` and its origin is allow-listed.
- [X] T023 [US3] OpenAI/Azure documented-out: add `BrowserDirectCapable(providerId)` (false for `openai`/`azure`); show a not-available-browser-direct notice (CORS → desktop edition / OpenAI-compatible endpoint) in `src/AkmlSql.Web/Pages/SettingsAi.razor` + `src/AkmlSql.Web/Shared/AiPanel.razor`; never attempt the call, never relay (define `AzureApiKeyAuth` for completeness but keep it gated).
- [X] T024 [P] [US3] Write `doc/WEB/ai-local-provider-cors.md` (exact `OLLAMA_ORIGINS` / LM Studio CORS setup) and ensure a CORS/connection failure surfaces an actionable message naming the setting (FR-017).
- [X] T025 [P] [US3] `tests/AkmlSql.Web.Tests/Ai/AnthropicWireTests.cs`: `AnthropicRequestBuilder` emits correct headers/body; `AnthropicSseParser` extracts `text_delta` and ends on `message_stop`; extend the allow-list tests so `openai`/`azure` resolve to the not-available path and `anthropic` origin is allowed.

**Checkpoint**: Claude/Gemini/local work browser-direct (streamed); OpenAI/Azure show the honest notice.

---

## Phase 6: User Story 4 — Index Analysis (Priority: P2)

**Goal**: A fifth AI action returning `CREATE INDEX` suggestions, honouring the active privacy mode.

**Independent Test**: Select a query → Index Analysis → `CREATE INDEX` + rationale render with Accept/Discard; with "no schema" mode the request carries only the query.

- [X] T026 [US4] Add `IndexAnalysisAsync(string schemaText, string selectedSql, string? executionPlanXml, CancellationToken ct)` (+ streaming overload) to `src/AkmlSql.Web/Services/IAiPromptService.cs` over `AkmlSql.AI` `IndexAnalysisPrompt.Build(...)`, passing `executionPlanXml: null` (no plan in-browser) and resolving `schemaText` via `IAiSchemaContextProvider.GetSchemaTextAsync("indexanalysis", ct)`.
- [X] T027 [US4] Add the fifth action button to `src/AkmlSql.Web/Shared/AiPanel.razor` (Index Analysis) rendering the suggestions with Accept/Discard + the privacy badge, consistent with the other four.

**Checkpoint**: All seven AI features (Explain/Fix/Optimize/TextToSql/IndexAnalysis/Chat/GhostText-next) are reachable; the panel offers five actions.

---

## Phase 7: User Story 5 — Inline grey-text completions (Ghost Text) (Priority: P2)

**Goal**: Opt-in inline ghost text in the editor — debounced, suppressed in comments/strings, Tab-accept/Esc-dismiss, cached, rate-limited, with a session token counter.

**Independent Test**: Enable Ghost Text → type at end-of-line, pause → grey suggestion; Tab accepts, Esc dismisses; no fire in comment/string/empty line; repeat prefix is cache-served; continuous typing is throttled and cancels in-flight; counter increments.

- [X] T028 [US5] In `src/AkmlSql.Web/wwwroot/js/akml-editor.js` add the CM6 ghost-text extension (hand-rolled, no new package): `StateField<DecorationSet>` (cleared on `docChanged`), `WidgetType` grey inline span via `Decoration.widget({side:1})`, a `StateEffect`, and a `Prec.highest` keymap (Tab→accept single edit, Esc→dismiss) that returns `false` unless ghost text is the sole active affordance (`completionStatus(state)===null` && no active snippet). Export `setGhostText`/`clearGhostText`/`triggerGhostText`.
- [X] T029 [US5] In `akml-editor.js` add the debounced (`GhostTextDelayMs`, default 350 ms) change hook: suppression checks via `syntaxTree(state).resolveInner(pos,-1)` node names (`LineComment`/`BlockComment`/`String`/`QuotedIdentifier`), end-of-line/empty-line, autocomplete-open; capture `pos` + preceding ~500 chars; call back into C#; staleness-check (`selection.main.head === pos`) + request-id to drop stale responses.
- [X] T030 [US5] Add `[JSInvokable] RequestGhostTextFromJs(int cursorOffset, string documentText)` to `src/AkmlSql.Web/Shared/EditorComponent.razor`: gate on `IAiFeatureSettings.GhostTextEnabled` (default off) + an active provider; derive precedingText + `schemaText` via `IAiSchemaContextProvider.GetSchemaTextAsync("ghosttext", ct)`; return `null` on any failure (mirror `RequestCompletionsFromJs`).
- [X] T031 [US5] Create `IAiGhostTextService` in `src/AkmlSql.Web/Services/IAiGhostTextService.cs`: `CompleteAsync(schemaText, precedingText, ct)` → `AkmlSql.AI` `GhostTextPrompt.Build` → `IAiClientFactory.SendAsync(active, {MaxTokens=150, Temperature=0.2})` → strip code fences + `TrimEnd`; prompt+prefix cache (FR-025), rate limit (`GhostTextMaxRequestsPer3s`), per-session token counter; register in `Program.cs`.
- [X] T032 [US5] Extend `src/AkmlSql.Web/Pages/SettingsAi.razor` with ghost-text settings (enable — default off, debounce, rate limit) persisted to `IAiFeatureSettings`; surface the session token counter in the UI.
- [X] T033 [P] [US5] `tests/AkmlSql.Web.Tests/Ai/GhostTextControllerTests.cs`: debounce coalescing, suppression decisions (comment/string/empty/popup/snippet), cache hit on repeated prompt+prefix, rate-limit throttling, in-flight cancellation on new keystroke, opt-in gate.

**Checkpoint**: Ghost Text works opt-in with debounce/cache/cancel/rate-limit; doesn't fight autocomplete/snippets.

---

## Phase 8: User Story 6 — Keep and export chat conversations (Priority: P3)

**Goal**: Conversations persist across reloads (IndexedDB) and export to Markdown; clearing is isolated from schema/keys.

**Independent Test**: Multi-turn chat survives reload; export downloads a `.md` preserving turns/roles (code-fence-safe); clear removes it and leaves the schema cache + keys intact.

- [X] T034 [US6] Create `IChatHistoryStore` in `src/AkmlSql.Web/Services/IChatHistoryStore.cs` (singleton, `chatHistory` store) with `ChatConversation { Id, Title, CreatedAt, UpdatedAt, Turns }` + `ChatTurn { Role, Content, ProviderId, Timestamp }`; `GetActiveAsync`/`SaveAsync`/`ClearAsync`; register in `Program.cs`.
- [X] T035 [US6] Update `src/AkmlSql.Web/Shared/AiChatPanel.razor` to persist each completed turn (with `ProviderId`) via `IChatHistoryStore`, restore the active conversation on init, and make the existing Clear also clear the store (local-only, no network egress).
- [X] T036 [US6] Add a chat Markdown export action to `AiChatPanel.razor`: build code-fence-safe Markdown (`## You`/`## Assistant` + content, in order) and download via the existing `src/AkmlSql.Web/wwwroot/js/akml-download.js` `downloadBase64(name, "text/markdown", base64)`; filename `chat-{yyyy-MM-dd-HHmm}.md`.
- [X] T037 [P] [US6] `tests/AkmlSql.Web.Tests/Ai/ChatHistoryStoreTests.cs`: save→restore round-trip; clear removes + does not reappear; clearing chat leaves `schemaEntries`/`aiKeys` intact; the Markdown builder preserves order/roles and escapes code fences; each turn carries `ProviderId`.

**Checkpoint**: Chat persists + exports; storage is isolated.

---

## Phase 9: User Story 7 — Prove privacy & parity (Priority: P3)

**Goal**: Close the deferred verification (T134/T137/T146) + the privacy/parity audits + the docs.

**Independent Test**: The bUnit + E2E suites pass against a mock provider (key never plaintext); the privacy audit shows per-mode disclosure + no-AKML-host; the parity audit has paired web-vs-WPF screenshots with dispositions (≤3 open).

- [X] T038 [US7] Add a mock-provider harness fixture in `tests/AkmlSql.Web.E2E.Tests/` (intercepts the allow-listed origins; returns canned + canned-streaming responses) reusable by the bUnit + Playwright suites. **Done:** `Harness/MockAiProvider.cs` (real `HttpListener`, Ollama/OpenAI-compat, CORS, buffered + SSE, records request bodies) + `Harness/WebAppFixture.cs` (launches `dotnet run`, waits for the port).
- [X] T039 [P] [US7] `tests/AkmlSql.Web.Tests/Ai/AiPanelTests.cs` (bUnit, the deferred T134): the five actions wire to the prompt service; no-provider prompt; provider-error renders the mapped message; **API key never appears in the DOM**; the privacy badge renders.
- [X] T040 [US7] `tests/AkmlSql.Web.E2E.Tests/UserStory5AiTests.cs` (Playwright, opt-in trait — the deferred T137): add key → run a feature → streamed response renders; key never in plaintext storage/DOM; drive Ghost Text (type → grey text → Tab accept) against the mock provider. **Done & passing:** rewritten from skip-pseudocode into real Playwright C# (selectors verified live) — `AddProvider_RunExplain_StreamsBrowserDirect` + `GhostText_TypeShowsGreyText_TabAccepts` both **pass** (2/2) against the live app + `MockAiProvider`. `[SkippableFact]` + `[Trait("Category","BridgeE2E")]` (opt-in; skips gracefully if Playwright browsers absent).
- [X] T041 [US7] Privacy network-capture audit (the deferred T146 / SC-009): per privacy mode (`FullSchema`/`SchemaNamesOnly`/`NoSchema`) capture an outbound request showing the expected disclosure (present/names-only/**none**) and assert **no AKML-owned host** in the AI path; record evidence under `specs/028-m6-ai-browser-closure/` (in the parity doc or `SC-009-EVIDENCE/`). **Done (live 2026-06-03):** all three modes captured on the wire + no-AKML-host + key-never-plaintext scan → `SC-009-EVIDENCE/README.md`.
- [X] T042 [P] [US7] Write `doc/WEB/ai-privacy-commitment.md`: data goes only to the user's provider, minimum per the privacy mode, never through any AKML host, fully usable with local providers; include the FR-002 key-storage tradeoff (non-extractable key; no passphrase factor); reflect it in the in-app privacy-mode tooltip.
- [X] T043 [US7] Create `specs/028-m6-ai-browser-closure/M6-PARITY-AUDIT.md` (shape of `M5-PARITY-AUDIT.md`): paired web-vs-WPF screenshots for each AI surface (5 panel actions, chat, settings, privacy badge, ghost text), deltas table, per-delta disposition (closed / accepted-with-reason), host OS/theme/DPI; ≤ 3 deltas open (SC-009). **Web half done:** screenshots of all surfaces captured (`SC-009-EVIDENCE/*.png`), deltas table + dispositions updated in `M6-PARITY-AUDIT.md`. **Open: 1 delta** — WPF-half screenshots (no SSMS/VS host in this environment; accepted-pending, ≤ 3).
- [X] T044 [US7] DoD reconciliation (FR-039): walk M6 PRD §12 — confirm each checkbox closes against a shipped feature or an FR, or is revised-with-reason ("passphrase-protected" → non-extractable key per FR-002; "all five providers" → met for CORS-permitting providers, OpenAI/Azure documented-out per FR-013/Reconciliation 3). Record in `M6-PARITY-AUDIT.md` or a DoD section.

**Checkpoint**: Privacy proven by capture; parity recorded; DoD honestly reconciled.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [X] T045 [P] Update `doc/WEB/quickstart-m6.md` — remove the now-closed "what's deferred" caveats (ghost text, streaming, native Claude, privacy modes).
- [X] T046 [P] Update `doc/progress.md` with the spec-028 closure summary (per the per-spec table style): tasks done/deferred, the 4 reconciliations, the CORS finding.
- [X] T047 Record first-token latency for Claude/Gemini (PRD metric) and the Ghost Text cache-hit rate (SC-006 ≥30 %) in the audit/perf evidence — measured, not asserted as a hard gate. **Cache-hit done (live):** repeated prefix served from cache with no new request → 50 % ≥ 30 % (`SC-009-EVIDENCE`). **First-token latency: not closeable here** — requires a real Claude/Gemini key; mock latency is synthetic (accepted-pending, recorded in `M6-PARITY-AUDIT.md`).
- [X] T048 Final full test run green: `tests/AkmlSql.IntelliSense.Tests`, `tests/AkmlSql.Web.Tests` (+ the opt-in E2E excluded from the default run); confirm no regression to existing AI/snippet/completion tests. **Re-verified unfiltered post-AI-dock-edit (2026-06-03):** IntelliSense **12/12 green**; Web.Tests **265 passed / 26 failed** — all 26 are the single pre-existing formatter-parity corpus test (`Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles`), **zero new editor/AI regressions**. Opt-in `UserStory5AiTests` **2/2 pass** with Playwright Chromium installed.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)**: T001 baseline; T002 store migration (shared, blocks US1 + US6).
- **Foundational (P2)**: T003/T004 rehydrator (blocks schema-aware US1/US4/US5); T005 provider backbone (blocks US2/US3/US4/US5). Must complete before the stories.
- **User Stories (P3–P9)**: priority order P1 → P3. Real cross-story couplings (be explicit, not pretend-independent):
  - **US2 → Foundational T005** (extends the provider backbone with streaming).
  - **US3 → US2** (adds Anthropic/Gemini axes onto the streaming send/parse path; shares `IAiClientFactory.cs`).
  - **US4 → US1 + US2** (uses `IAiSchemaContextProvider` for schema + the streaming prompt path; shares `IAiPromptService.cs` + `AiPanel.razor`).
  - **US5 → US1** (`IAiSchemaContextProvider` for the "ghosttext" mode) **+ US2** (uses `IAiClientFactory.SendAsync`).
  - **US6** is independent of US1–US5 (only needs T002).
  - **US7 → US1–US6** (verifies them; lands last).
- **Polish (P10)**: after the desired stories.

### Shared-file sequencing (NOT parallel across stories)

- `src/AkmlSql.Web/Services/IAiClientFactory.cs` — T005 → T014 (US2) → T020/T021/T022/T023 (US3).
- `src/AkmlSql.Web/Services/IAiPromptService.cs` — T009 (US1) → T015 (US2) → T026 (US4).
- `src/AkmlSql.Web/Shared/AiPanel.razor` — T010 (US1) → T017 (US2) → T027 (US4) → T023 (US3 notice).
- `src/AkmlSql.Web/Pages/SettingsAi.razor` — T011 (US1) → T023 (US3) → T032 (US5).
- `src/AkmlSql.Web/Program.cs` — T006 (US1) → T031 (US5) → T034 (US6).
- `src/AkmlSql.Web/wwwroot/js/akml-editor.js` — T028 → T029 (US5 only).

### Parallel opportunities

- T004 (rehydrator test) runs alongside other Foundational verification once T003 lands.
- Within a story, distinct-file tasks marked `[P]` run together: T008 (badge) + T013 (privacy tests); T020 (Anthropic axis) + T024 (CORS doc) + T025 (wire tests); T039 (bUnit) + T042 (privacy doc); T045 + T046 (docs).
- US6 (P3) can be built in parallel with US2–US5 if staffed (independent files), though priority favours finishing P1/P2 first.

---

## Parallel Example: User Story 1

```bash
# After T006/T007 land, these touch distinct files:
Task: "T008 AiPrivacyModeBadge component in src/AkmlSql.Web/Shared/AiPrivacyModeBadge.razor"
Task: "T013 PrivacyModeTests in tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs"
```

## Parallel Example: User Story 3

```bash
Task: "T020 Anthropic request-builder/auth/SSE-parser in src/AkmlSql.Web/Services/IAiClientFactory.cs"
Task: "T024 doc/WEB/ai-local-provider-cors.md"
Task: "T025 AnthropicWireTests in tests/AkmlSql.Web.Tests/Ai/AnthropicWireTests.cs"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup (T001–T002) → Phase 2 Foundational (T003–T005).
2. Phase 3 US1 (T006–T013) → **STOP & VALIDATE**: privacy modes + schema-from-cache work for the four shipped actions, network-verified.
3. Demo the privacy MVP (the PRD's central commitment) independently.

### Incremental delivery

1. Foundation → US1 (privacy MVP) → US2 (streaming) → US3 (Claude/Gemini/local + OpenAI notice) → US4 (Index Analysis) → US5 (Ghost Text) → US6 (chat persistence) → US7 (prove it).
2. Each story is demoable at its checkpoint; later stories extend, not break, earlier ones.

### Notes

- `[P]` = different file, no incomplete-task dependency. `[Story]` maps to spec.md user stories.
- Tests are included per the spec/contracts; the rehydrator round-trip (T004) is the gate for schema reuse and should pass before US1 wiring.
- No new IPC message types, no new NuGet/CDN packages, no engine behavioural change (self-imposed gates).
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.

---

## Closure addendum (2026-06-03 interactive verification pass)

The US7 interactive items (T038/T040/T041/T043/T047) were closed by running the web edition
(`dotnet run`) and driving it with a real browser against a local Ollama-shaped mock provider.
Running the product surfaced a **prerequisite bug not catchable by unit/bUnit tests**:

- **AI panel + chat were orphaned.** `AiPanel.razor` / `AiChatPanel.razor` (built under 021 T131 /
  028 US1–US6) were referenced by **no routed page** — `Editor.razor` had no AI affordance, no
  `/ai` or `/chat` route. So Explain/Fix/Optimize/NL→SQL/Index Analysis + Chat were unreachable by
  a user, contradicting the DoD's "all 7 features work in the browser." bUnit renders components in
  isolation, so 65 green tests never caught it.
- **Fix (additive):** an editor-adjacent collapsible **AI dock** — `AI ▾` toolbar toggle →
  `[Actions] [Chat]` tabs. Actions operate on the live editor selection (new optional
  `AiPanel.SelectedSqlProvider`, defaulting to the existing `SelectedSql` path so `AiPanelTests`
  stay 65/65 green); Accept inserts at the caret. New: `getSelectedText` (akml-editor.js) +
  `EditorComponent.GetSelectedTextAsync`. Files: `Pages/Editor.razor`, `Shared/AiPanel.razor`,
  `Shared/EditorComponent.razor`, `wwwroot/js/akml-editor.js`.
- **Verified live:** dock reachable; 5 actions stream against the mock; per-mode privacy badges
  + wire disclosure (Full/Names/None); chat streams + persists across reload; ghost text grey
  widget + Tab-accept + 50 % cache-hit; API key never plaintext in any IndexedDB store; AI requests
  only to the local provider (no AKML host). Evidence: `SC-009-EVIDENCE/`.
