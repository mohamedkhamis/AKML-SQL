# Research: M6 — AI Parity Closure

**Branch**: `028-m6-ai-browser-closure` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)

Seven technical decisions (≈ one per user story) plus four **scope reconciliations**. Reconciliations 1–3 were settled with the user at `/speckit.specify`; reconciliation 4 (CORS) was settled during this plan after an **empirical cross-origin fetch test** corrected a stale assumption that both the source PRD and the planning sub-agents got wrong. No open `NEEDS CLARIFICATION` items. Every decision was checked against current source — not the M6 PRD's stale "Browser has nothing AI-related" paragraph.

---

## Decision 1 — Privacy disclosure modes + schema-from-cache via the M5-deferred rehydrator (US1)

**Decision**: Implement the PRD's **four schema-*disclosure* modes** browser-side — **full schema / schema names only / no schema / fully local** — as a global default plus per-feature override, persisted in a **new `aiFeatureSettings` IndexedDB store** (`IAiFeatureSettings`, singleton, mirroring `IAnalysisSettingsStore`). To feed the canonical `SchemaContextBuilder` (in `AkmlSql.AI`, which requires a `DatabaseCache`), **build the `SchemaPhasePayload → DatabaseCache` rehydrator that spec 027 deliberately deferred** — a ~120-line reverse of `SchemaPhaseSerializer` over existing models, placed in `AkmlSql.IntelliSense/Schema/` (WASM-safe). A new `IAiSchemaContextProvider` resolves the active database's `SchemaSnapshot` from `ISchemaCacheStore`, rehydrates a `DatabaseCache`, and calls `SchemaContextBuilder` + `SchemaContextFormatter`. The four modes map to a `(includeSchema, compressionLevel, forceLocalProvider)` triple:

| Mode | includeSchema | compression | forceLocal |
|---|---|---|---|
| full schema | yes | level 4 (cols + FKs + descriptions) | no |
| schema names only | yes | level 1 (table/column names, no types/FKs) | no |
| no schema | **no** (empty `schemaText`) | — | no |
| fully local | yes | level 4 | **yes** (only Ollama/LM Studio selectable) |

**Rationale**:

1. **The PRD's modes are a different axis than the engine's** (Reconciliation 2, user-confirmed). The engine's `PrivacyTransformer` modes (`full`/`schemaOnly`/`anonymous`) *redact literals* and *hash identifiers*; the PRD's four modes control *how much schema is disclosed*. They don't map cleanly, so the browser implements the disclosure axis directly (gating *what* `SchemaContextBuilder` is given), not the engine's redaction enum. `anonymous` identifier-hashing is out of scope for the browser.
2. **The rehydrator is genuinely required and is the M5-deferred path.** Verified: the offline completion path (`ICompletionService.BuildFromCacheAsync`) deserializes `SchemaPhasePayload` and iterates it **directly** — it never builds a `DatabaseCache`, and **no `SchemaPhasePayload → DatabaseCache` rehydrator exists anywhere** (grep-confirmed; spec 027 research Decision 3 named this exact gap). `SchemaContextBuilder.BuildAsync` is tightly coupled to `DatabaseCache`/`DatabaseObject` (`GetAllObjects()`, `GetForeignKeysForTable()`). To reuse the **canonical** builder (and not fork a second schema-text generator), the browser must hand it a `DatabaseCache` — hence the rehydrator.
3. **Building it is a conscious reversal of the M5 deferral, now justified.** M5 deferred it because the only consumer then (cached-heavyweight refactoring) was the riskiest/least-certain piece. M6's schema-aware prompting needs the canonical builder, which flips the cost/benefit. The rehydrator is a pure reverse of `SchemaPhaseSerializer` over existing models (`DatabaseCache`, `SchemaEntry`, `DatabaseObject`, `Column`, `Parameter`, `ForeignKey`) → ~120 lines, no new model, gated by a round-trip test. **Bonus**: it unblocks the M5 cached-heavyweight-refactoring follow-up for free.
4. **`SchemaPhasePayload` carries everything the modes need** — schemas, objects (+type), columns (+type, nullable, PK, description), parameters, FKs, descriptions (verified shape). "Names only" can even use Phase A alone (no columns); "full" needs Phase B.

**Alternatives considered**:

- **Fork a payload-direct schema-text builder** (like `ICompletionService` does for completions): Rejected. Duplicates `SchemaContextBuilder`'s relevance-filtering + FK-expansion logic into a second path that must stay in sync with the engine's prompt context forever — the exact divergence the shared-lib pattern exists to prevent.
- **Reuse the engine's `full`/`schemaOnly`/`anonymous` redaction modes** (Reconciliation 2 alternative): Rejected by the user — wrong axis; doesn't match the PRD's disclosure wording.
- **Passphrase/PBKDF2 key storage** (Reconciliation 1 alternative): Rejected by the user — see Decision 7 / FR-002; the shipped non-extractable-`CryptoKey` vault is retained.

**Consumer**: US1 / FR-001 … FR-007.

---

## Decision 2 — Streaming via .NET 10 default-on response streaming + per-provider SSE parser (US2)

**Decision**: Add a streaming path to `IAiClientFactory`: `SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)` → `response.Content.ReadAsStreamAsync(ct)` (a `BrowserHttpReadStream` on net10) → `StreamReader.ReadLineAsync` → a **per-provider `ISseDeltaParser`** that yields incremental text tokens as `IAsyncEnumerable<string>`. `IAiPromptService` gains streaming overloads returning the token stream; the existing `Task<string>` methods buffer it for non-streaming callers. Each AI surface (panel, chat, ghost text) owns **one streaming controller** whose `CancellationToken` is bound to its lifetime; starting a new action cancels the prior stream and aborts its request.

**Rationale**:

1. **.NET 10 streams browser responses by default** (verified: `AkmlSql.Web` is `net10.0`; documented breaking change). `ReadAsStreamAsync` returns an incremental `BrowserHttpReadStream` — **no `SetBrowserResponseStreamingEnabled(true)` call needed**. The one load-bearing call is `HttpCompletionOption.ResponseHeadersRead` so `SendAsync` returns before the body completes (the default `ResponseContentRead` would buffer the whole body and defeat SSE). This is per-`HttpRequestMessage`, so the singleton `HttpClient` (Program.cs) needs no DI change.
2. **Two SSE shapes cover all working providers.** OpenAI-shape (`data: {json}` lines, `choices[0].delta.content`, `data: [DONE]` terminator) is shared by OpenAI-compat providers (Gemini/Ollama/LM Studio); Anthropic uses named events (`content_block_delta` with `delta.text`, terminate on `message_stop`). One parser each; selected by `ProviderProfile` (Decision 3).
3. **Per-surface controller prevents cross-panel bleed** (PRD risk row, FR-009). Binding the token to the surface lifetime makes "type → cancel previous" (chat, ghost text) and "switch action → cancel" fall out naturally.
4. **Buffered fallback keeps every feature working** (FR-012) when a provider/mode doesn't stream — the async-enumerable is just awaited to completion.

**Alternatives considered**:

- **JS-interop `fetch` + `ReadableStream`**: Rejected. Unnecessary on net10 — the framework `HttpClient` streams natively; staying in C# keeps the parser testable and avoids an interop boundary per token.
- **Keep buffered-only and fake a typewriter client-side**: Rejected. Defeats the success metric ("begins streaming within ~1500 ms") and the PRD §4.5 UX; a fake typewriter still waits for the full response.

**Consumer**: US2 / FR-008 … FR-012.

---

## Decision 3 — Provider coverage: ship what CORS permits; document OpenAI/Azure out (US3) — RECONCILIATION (empirically verified)

**Decision**: Browser-direct is implemented for the providers a real browser can reach: **Claude (Anthropic)** via the native `/v1/messages` contract (`x-api-key`, `anthropic-version: 2023-06-01`, `anthropic-dangerous-direct-browser-access: true`, Messages body + named-event SSE), **Gemini** via its OpenAI-compatible endpoint + `Bearer`, and **Ollama / LM Studio** via their local OpenAI-compatible endpoints (documented CORS config). **OpenAI and Azure OpenAI are CORS-blocked by their own APIs** and are surfaced with a clear not-available-browser-direct notice (pointing to the desktop edition or an OpenAI-compatible endpoint) — **no AKML proxy and no engine relay** (PRD §10). Implemented as a **three-axis abstraction**: `IAiRequestBuilder` (OpenAI-shape vs Anthropic-native) × `IAuthApplier` (`Bearer` vs Anthropic headers vs — for completeness — Azure `api-key`) × `ISseDeltaParser` (OpenAI vs Anthropic), selected by a per-provider `ProviderProfile`.

**Rationale** (user-confirmed after an **empirical cross-origin `fetch` test from `https://example.com`**):

| Provider | Test (POST + dummy key) | Result | Verdict |
|---|---|---|---|
| **Anthropic** + `anthropic-dangerous-direct-browser-access` | `/v1/messages` | reached server → **401** | ✅ browser-direct works |
| **Anthropic** *without* the header | `/v1/messages` | **`TypeError: Failed to fetch`** | ❌ confirms the header is the enabler |
| **Gemini** + `Bearer` | `…/v1beta/openai/chat/completions` | reached server → **400** | ✅ browser-direct works |
| **OpenAI** + `Bearer` | `/v1/chat/completions` | **`TypeError: Failed to fetch`** | ❌ CORS-blocked (no `Access-Control-Allow-Origin`) |

1. **The PRD's premise is only partly physically possible.** PRD §7 listed CORS as the top risk and said "verify"; verification (above) shows only Anthropic + Gemini + local clear browser CORS. OpenAI sends no CORS headers, so a browser fetch never reaches it — this is OpenAI's policy, outside AKML's control. Azure behaves the same and additionally needs an `api-key` header (not `Bearer`).
2. **Both prior research inputs were wrong in opposite directions** — the planning sub-agent claimed Gemini is CORS-blocked (forum thread); the stronger-model advisor hypothesised OpenAI works (from the SDK's `dangerouslyAllowBrowser` flag, which guards *key exposure*, not CORS). The live test overruled both. This is why the decision is anchored to an empirical capture, not a citation.
3. **Documenting OpenAI/Azure out beats a proxy or engine relay** (user choice). A proxy/AKML server is forbidden (PRD §10); an opt-in engine relay would reintroduce the engine in the AI path — the exact privacy concern the PRD §4.1 browser-direct pivot was designed to remove. An honest "use the desktop edition or an OpenAI-compatible endpoint" notice is truthful and zero-architecture.
4. **The three-axis split is needed regardless.** Even for the providers that work, auth is not uniform (the shipped code's hardcoded `Bearer` is wrong for Anthropic and Azure) and SSE differs (OpenAI vs Anthropic). Isolating request-build / auth / parse per provider contains any wire bug to one place.

**What this changes in the spec**: FR-013 narrows from "all five browser-direct" to "every provider whose API permits it" + an OpenAI/Azure not-available notice; FR-015 records Gemini as confirmed-working; SC-002 narrows to the working set + a verified notice for OpenAI/Azure; US3 title/scenarios revised (scenario 5 = the not-available notice). OpenAI/Azure browser-direct moves to **Out of Scope** as a named follow-up.

**Alternatives considered**:

- **Opt-in local-engine relay for OpenAI/Azure**: Rejected by the user — reintroduces the engine in the AI path (PRD §4.1 privacy concern) and adds scope.
- **User-run CORS proxy**: Rejected by the user — pushes setup burden onto the user; still not "all five" cleanly.
- **Trust the sub-agent's "Gemini blocked / OpenAI blocked" summary**: Rejected — superseded by the live test (Gemini works, and the test is ground truth over a forum citation).

**Consumer**: US3 / FR-013 (revised), FR-014, FR-015 (confirmed), FR-016, FR-017, FR-018.

---

## Decision 4 — Index Analysis as the fifth panel action (US4)

**Decision**: Add `Task<string> IndexAnalysisAsync(string schemaText, string selectedSql, string? executionPlanXml, CancellationToken ct)` to `IAiPromptService`, building its prompt from the existing `AkmlSql.AI` `IndexAnalysisPrompt.Build(schemaText, selectedSql, executionPlanXml)` and funnelling through the same `CallAsync` path as the other four actions. Add a fifth button to `AiPanel.razor` rendering the `CREATE INDEX` suggestions with Accept/Discard; it honours the active privacy mode for the Index-Analysis feature.

**Rationale**:

1. **The prompt builder already exists** in the shared lib (`IndexAnalysisPrompt.cs`, same namespace as the other four prompts). The shipped panel deferred the action only because the web prompt service exposed four methods and the schema context wasn't wired — both of which US1 + this decision close.
2. **It is the smallest of the seven gaps** and completes the PRD's "all 7 features," so it slots cleanly after the privacy/streaming foundation.
3. **`executionPlanXml` is optional** — the browser has no execution plan offline, so it passes `null` (the prompt degrades to schema + SQL), matching how the engine handles a missing plan.

**Alternatives considered**:

- **Wait for an engine-side index-analysis context builder** (the T131 deferral reason): Rejected. The prompt builder is self-sufficient given `schemaText`; US1's `IAiSchemaContextProvider` supplies that, so there is nothing engine-side to wait for.

**Consumer**: US4 / FR-019 … FR-021.

---

## Decision 5 — Ghost Text: hand-rolled CodeMirror 6 inline completion (US5)

**Decision**: Hand-roll ~100 lines in `wwwroot/js/akml-editor.js` using **already-loaded CM6 primitives**: a `StateField<DecorationSet>` holding the suggestion (cleared on any `docChanged`), a `WidgetType` rendering a grey/italic inline span via `Decoration.widget({side:1})`, a `StateEffect` to set/clear it, and a **`Prec.highest` keymap** binding Tab→accept / Escape→dismiss. A debounced (≈350 ms) change hook runs suppression checks then calls `dotNetRef.invokeMethodAsync('RequestGhostTextFromJs', pos, docText)` (paralleling the existing `RequestCompletionsFromJs`). C# side: a new `IAiGhostTextService` calls the provider **direct** via `IAiClientFactory` using the existing `GhostTextPrompt` (engine path is unavailable — keys are browser-side), with a prompt+prefix cache, a configurable rate limit, and a per-session token counter. Suppression contexts come from `syntaxTree(state).resolveInner(pos,-1)` node names (`LineComment`/`BlockComment`/`String`/`QuotedIdentifier`) + end-of-line/empty-line checks; the Tab handler returns `false` (falls through to autocomplete/snippet) unless ghost text is the only active affordance (`completionStatus(state)===null` && no active snippet).

**Rationale**:

1. **It's a port, not a greenfield invent.** The shared `GhostTextPrompt` and the WPF reference behaviour (`GhostTextAdornment.cs`: staleness check tied to caret offset, dismiss-on-type, 500-char window, strip fences) define the contract; M6 mirrors it in CM6 + Blazor.
2. **Every primitive is already in the loaded bundle** (`@codemirror/state` `StateField`/`StateEffect`/`Prec`, `@codemirror/view` `Decoration`/`WidgetType`, `@codemirror/autocomplete` `completionStatus`, `@codemirror/language` `syntaxTree`) — so hand-rolling adds **no new package/CDN dependency** (gate-relevant), versus pulling `codemirror-copilot`/`inline-suggestion` which implement the same pattern but add supply-chain surface and don't provide the contract-specific logic (staleness, suppression, opt-in gate, Tab precedence) anyway.
3. **Direct-to-provider is forced** — verified: nothing in `AkmlSql.Web` references `MessageTypes.Ai*`; web keys live in the browser vault and the paired engine doesn't hold them. So ghost text reuses `IAiClientFactory` like the panel does.
4. **The Tab precedence guard is the subtle part** and is solved with `completionStatus` + active-snippet checks so ghost text never fights the autocomplete popup or snippet tab-stops.

**Notes carried to tasks**: the WPF adornment hardcodes 300 ms while `AppSettings.GhostTextDelayMs` defaults to 500 ms; the PRD specifies ≈350 ms. The web reads a configurable value (default 350 ms) from `IAiFeatureSettings` and debounces in JS (passed at editor `create()`) to avoid per-keystroke interop churn. Manual trigger wired to the existing `GhostTextShortcut` ("Ctrl+Alt+Up") parity via `triggerGhostText`.

**Alternatives considered**:

- **Use a community CM6 inline-completion package**: Rejected — adds a dependency for ~100 lines you'd own anyway, and none supply the contract-specific logic.
- **Route ghost text through the engine** (like the WPF surface): Rejected — web keys are browser-side; the engine path is unavailable and would violate the engine-bypass gate.

**Consumer**: US5 / FR-022 … FR-029.

---

## Decision 6 — Chat persistence + markdown export (US6)

**Decision**: Add a `chatHistory` IndexedDB store (bump `DB_VERSION` 1→2; register in `StoreNames` + the JS `STORES` array) and an `IChatHistoryStore` (singleton, JSON-per-conversation). `AiChatPanel` persists each turn (user + assistant) and restores on load; a Clear action removes the conversation. Export reuses the existing `akml-download.js` `downloadBase64(filename, "text/markdown", base64)` to download a `.md` whose turns/roles are preserved (code-fence-safe). Each persisted turn records its originating `providerId`.

**Rationale**:

1. **This is a conscious M6-over-021 reversal.** Spec 021 made chat in-memory "per spec" (T132 comment); the M6 PRD scope table marks persistence + markdown export **Yes**. The reality table records this so it reads as a deliberate scope addition, not an oversight.
2. **The infra already exists.** The `IndexedDb` adapter + a `DB_VERSION`-bump path are established; `akml-download.js` already does file downloads (the M5 snippet export uses it). So this is a new store + a small store class + two UI affordances.
3. **Local-only, no sync** (PRD open question 4 / FR-033) — persistence is a browser store with no network egress; sync is explicitly a SaaS concern, out of scope.

**Alternatives considered**:

- **Keep in-memory (021 behaviour)**: Rejected — the M6 PRD scope table overrides it.
- **Reuse an existing store**: Rejected — a dedicated store keeps chat independent of the schema cache and key vault (FR-032: clearing one must not affect the others).

**Consumer**: US6 / FR-030 … FR-033.

---

## Decision 7 — Verification & audit reuse the established harnesses; key vault unchanged (US7) + RECONCILIATION 1

**Decision**: The deferred T134 (AiPanel bUnit) and T137 (US5 E2E) are built on the existing bUnit + Playwright .NET stacks against a **mock-provider harness** (a test fixture intercepting the allow-listed origins). The privacy network-capture audit (T146/SC-009) captures, per privacy mode, an outbound request showing the expected disclosure (full/names/none) and confirms no AKML-owned host in the AI path. `M6-PARITY-AUDIT.md` follows the `M5-PARITY-AUDIT.md` shape. A privacy-commitment doc and a local-provider-CORS doc are written. **Reconciliation 1**: the **key vault is unchanged** — the shipped per-profile non-extractable-`CryptoKey` scheme is retained (FR-002), and the DoD "passphrase-protected" wording is revised to "encrypted at rest with a non-extractable key."

**Rationale**:

1. **The harnesses exist** (specs 024/025 built Playwright + the E2E trait; bUnit is wired). T134/T137 were deferred precisely awaiting an interactive session — this closure provides it.
2. **The privacy audit is the DoD's hard evidence** ("no schema verified by network capture") and depends on US1's mode wiring, so it lands last.
3. **Keeping the shipped vault** (Reconciliation 1, user-confirmed) avoids regressing 31 tested + contract-documented code; the non-extractable key is strong at rest. The honest tradeoff (no "something you know" factor) is documented in the privacy-commitment doc, and the DoD wording is revised rather than the code rebuilt.

**Alternatives considered**:

- **Rebuild to passphrase/PBKDF2 (PRD §4.3)**: Rejected by the user — discards reviewed code and adds per-session unlock friction.
- **Automated pixel-diff parity**: Rejected — the project's parity audits are human-reviewed screenshot comparisons (DPI/font variance accepted-with-reason), consistent with specs 024/027.

**Consumer**: US7 / FR-034 … FR-039; FR-002 (Reconciliation 1).

---

## Verified against current source

| Decision | Checked file / fact | Result |
|---|---|---|
| 1 — Privacy + rehydrator | `ICompletionService.BuildFromCacheAsync` deserializes `SchemaPhasePayload` and iterates it directly (no `DatabaseCache`); grep finds **no** `SchemaPhasePayload→DatabaseCache` rehydrator; `SchemaContextBuilder.BuildAsync` needs `Func<string,string,DatabaseCache?>`; `SchemaPhasePayload` carries schemas/objects/columns/params/FKs/descriptions; new store added to `JsIndexedDbAdapter.StoreNames` + `akml-indexeddb.js STORES` | ✓ rehydrator genuinely required (M5-deferred); modes map to compression levels |
| 2 — Streaming | `AiClientFactory.SendAsync` buffers (`ReadAsStringAsync`); `HttpClient` is a plain singleton (`Program.cs`), default `BrowserHttpHandler`, no streaming opt set; **`net10.0`** ⇒ default-on response streaming; no existing streaming path in `src` | ✓ `ResponseHeadersRead` + `ReadAsStreamAsync`; no DI change |
| 3 — Providers / CORS | **Live cross-origin fetch from `https://example.com`**: Anthropic+header → 401, Anthropic no-header → TypeError, Gemini → 400, OpenAI → TypeError; `AiClientFactory` is OpenAI-wire only + hardcoded `Bearer` (`:179-182`); allow-list at `:87-95` | ✓ Claude/Gemini/local work; OpenAI/Azure CORS-blocked → documented out |
| 4 — Index Analysis | `IndexAnalysisPrompt.Build(schemaText, selectedSql, executionPlanXml)` exists in `AkmlSql.AI/Prompts`; `IAiPromptService` exposes only 4 actions; `AiPanel` defers Index Analysis (comment) | ✓ add a 5th method + button |
| 5 — Ghost Text | `akml-editor.js` loads `@codemirror/{state,view,autocomplete,language}` (CDN), exposes `RequestCompletionsFromJs` + interop; CM6 `StateField`/`WidgetType`/`Prec`/`completionStatus`/`syntaxTree` all in-bundle; no web ref to `MessageTypes.Ai*`; `GhostTextPrompt` + WPF `GhostTextAdornment` exist | ✓ hand-roll, direct-to-provider, no new package |
| 6 — Chat persistence | `AiChatPanel` history is in-memory (T132 "persistence intentionally absent per spec"); `akml-download.js downloadBase64` exists + used by Snippets export; `DB_VERSION` bump path established | ✓ new store + store class + export reuse |
| 7 — Verification + vault | `AiKeyVault` uses `crypto.subtle.generateKey({extractable:false})` (non-extractable `CryptoKey`), AAD-bound, 31 tests; Playwright/bUnit harnesses present; T134/T137/T146 deferred `[ ]` in 021 `tasks.md` | ✓ reuse harnesses; keep vault (Recon 1) |
