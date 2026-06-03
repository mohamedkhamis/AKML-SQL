# Feature Specification: M6 — AI Parity Closure (Privacy Modes, Streaming, All Providers, Index Analysis, Ghost Text)

**Feature Branch**: `028-m6-ai-browser-closure`
**Created**: 2026-06-02
**Status**: Draft
**Input**: User description: PRD `doc/WEB/M6-ai-browser.md` ("M6 — AI Assistance in the Browser (Bring Your Own Key)"; Status: Draft; Estimated effort 2 weeks)

## Overview

The M6 PRD reads as greenfield — its §3 "Current state" asserts "**Browser has nothing AI-related**" and its §4.2 proposes creating a **new** `AkmlSql.AI` (netstandard2.0) library. Both claims are **stale**. Like every web-edition milestone before it (the M0–M5 closures, specs 022–027), the M6 scaffold was already merged: the shared AI library was extracted and the browser-direct AI surface was built under spec 021 Phase 7 (tasks **T121–T138**). This is therefore a **closure spec** — and, like the M5 closure, it is honest that the genuine remaining gap is **mostly new feature build plus verification**, not "prove it's done."

The shipped foundation already lets a user add a provider key in the browser, pick an active provider, and run **Explain / Fix / Optimize / Text-to-SQL** against any OpenAI-wire-compatible provider, entirely browser-direct (no engine, no AKML server), with the key wrapped at rest. What is **not** done: the privacy modes the PRD makes central, schema-aware prompting fed from the M5 cache, streaming responses, native Claude, Index Analysis, Ghost Text, chat persistence/export, and the privacy/parity verification. This spec covers exactly that remainder.

> **Planning reconciliations (2026-06-02).** Three decisions were settled with the user before writing, because each materially changes scope:
> 1. **Key storage keeps the shipped model, not the PRD's passphrase scheme.** The browser vault wraps keys with a per-profile **non-extractable AES-GCM-256 `CryptoKey`** (AAD-bound to `providerId`, ~31 tests, documented in `contracts/ai-key-wrapping.md`) — *not* the PRD §4.3 passphrase → PBKDF2-SHA256-600k → AES-GCM design. M6 keeps the shipped vault; the PRD's passphrase requirement and the DoD's "passphrase-protected" wording are **revised**, and the spec documents the threat-model difference (the non-extractable key is strong at rest, but there is no "something you know" factor — browser-profile access implies usable keys). FR-002 records this.
> 2. **Privacy modes follow the PRD's 4-mode *disclosure* axis, not the engine's *redaction* axis.** The engine's modes (`full` / `schemaOnly` / `anonymous`) redact literals and hash identifiers; the PRD's modes (full schema / schema names only / no schema / fully local) control *how much schema* is disclosed. M6 implements the PRD's four browser-side; the engine's identifier-hashing `anonymous` mode is **out of scope** for the browser. FR-001/FR-003/FR-004 record this.
> 3. **Browser-direct works only for the providers whose APIs permit it; OpenAI and Azure are CORS-blocked (plan-phase empirical finding).** The spec-phase choice was "make all five work browser-direct." A plan-phase cross-origin `fetch` test settled the reality: **Claude works** (native `/v1/messages` + the `anthropic-dangerous-direct-browser-access` header → 401 with a dummy key), **Gemini works** (OpenAI-compatible endpoint + `Bearer` → 400), **Ollama / LM Studio work** (local + documented CORS) — but **OpenAI is CORS-blocked** (`api.openai.com` returns no `Access-Control-Allow-Origin` → `TypeError: Failed to fetch`), and **Azure OpenAI** almost certainly likewise. Per the user's plan-phase decision, M6 ships browser-direct for the providers that permit it and **documents OpenAI/Azure as not-available-browser-direct** (in-app notice + a pointer to the desktop edition or an OpenAI-compatible endpoint); **no AKML proxy and no engine relay** (PRD §10). FR-013/FR-015/SC-002 are revised; OpenAI/Azure browser-direct is a named Out-of-Scope follow-up.

### Reality table — what already exists vs what this spec builds

| M6 PRD area | Status today | Evidence |
|---|---|---|
| `AkmlSql.AI` shared library (prompts, provider factory, privacy transformer, schema-context builder, stream coalescer) | **Shipped** (T121–T124). Targets **net10.0** (not the PRD's netstandard2.0); engine + web both reference it | `src/AkmlSql.AI/` (csproj line 4; `Prompts/`, `Providers/`, `Privacy/`, `Context/`, `Streaming/`) |
| Browser key vault — encrypted at rest, AAD-bound, zeroise-on-use/delete | **Shipped** (T125). Uses a per-profile **non-extractable CryptoKey**, *not* the PRD passphrase/PBKDF2 scheme | `src/AkmlSql.Web/Services/IAiKeyVault.cs`, `wwwroot/js/akml-crypto.js`, 31 tests (`tests/AkmlSql.Web.Tests/Ai/`) |
| Active-provider preference | **Shipped** (T126) | `Services/IAiPreference.cs` (`aiKeys` store, `_active` sentinel) |
| Direct-to-provider client + origin allow-list | **Shipped** (T128). **OpenAI-wire only, non-streaming**; reads the full response | `Services/IAiClientFactory.cs`, 18 allow-list tests |
| Schema-aware prompt service | **Partial** (T129). Calls the 4 prompt builders; schema text passed in **as a raw string** by the caller — **not auto-resolved from the M5 cache, and no privacy-mode filtering applied** | `Services/IAiPromptService.cs` |
| AI panel — Explain / Fix / Optimize / Text-to-SQL | **Shipped** (T131). 4 actions; **Index Analysis explicitly deferred** | `Shared/AiPanel.razor` |
| Chat panel — multi-turn | **Shipped** (T132). In-memory; **persistence intentionally absent "per spec"** (the M6 PRD scope table overrides this) | `Shared/AiChatPanel.razor` |
| Settings → AI page (add/edit/remove, masked key, per-provider endpoint) | **Shipped** (T135) | `Pages/SettingsAi.razor` |
| Provider error mapping (401 / 429 / 404 / content-policy / network) | **Shipped** (T136). Per-provider docs links are a follow-up | `AiPanel.MapErrorToMessage` |
| **Privacy modes** (4 disclosure modes, per-feature + global, shown next to each action) | **Not built** — no selector exists; the browser sends whatever schema string it is handed (effectively "full") | — |
| **Schema-aware prompting fed from the M5 IndexedDB cache** | **Not built** — `AiPromptService` notes the cache auto-resolve "lands when the cache-backed completion path lights up" | `Services/IAiPromptService.cs` (T129 note) |
| **Streaming / typewriter responses** | **Not built** — `SendAsync` buffers the whole response | `Services/IAiClientFactory.cs` |
| **Native Claude (Anthropic) browser-direct** | **Not built** — OpenAI-wire only; native `/v1/messages` unsupported | `Services/IAiClientFactory.cs` (T128 note) |
| **Index Analysis** action | **Not built** — deferred in T131 (needs the index-analysis context wired to the web library) | `Shared/AiPanel.razor` comment |
| **Ghost Text** inline completion | **Not built** — deferred (T133); needs the CodeMirror grey-text decorator | `tasks.md` T133 `[ ]` |
| **Chat history persistence + markdown export** | **Not built** — 021 descoped persistence; the M6 PRD adds it | `AiChatPanel.razor` (T132 note) |
| US5 E2E + AiPanel bUnit + privacy network-capture audit | **Deferred** (T137, T134, T146) | `tasks.md` |
| Feature-parity audit vs WPF + privacy-commitment doc + local-provider (Ollama) CORS doc | **Not done** | — |

This spec covers the bottom nine rows, framed as seven prioritised user stories. Everything above them is already shipped and is **not** rewritten here (except to extend: privacy filtering and streaming touch the existing prompt service and client; Index Analysis adds a fifth panel action).

### PRD-vs-reality discrepancies that shape the spec

1. **"Browser has nothing AI-related" is false.** The full M6 scaffold — library extraction, key vault, direct-to-provider client, four-action panel, chat panel, settings page, error mapping, and quickstart docs — shipped under spec 021 Phase 7 (T121–T138). This spec is a feature-build + verification closure on top of that substrate, mirroring the M5 closure.
2. **`AkmlSql.AI` already exists and is net10.0, not a new netstandard2.0 project.** The PRD's headline M6.1 milestone ("Extract AkmlSql.AI") is complete; the target was deliberately set to net10.0 because both consumers (engine, web) are already net10. **No extraction work remains.**
3. **Key storage diverges from the PRD by design** (planning reconciliation 1). Non-extractable `CryptoKey`, not passphrase/PBKDF2. The PRD §4.3 and the DoD "passphrase-protected" item are revised.
4. **The PRD's privacy modes are a disclosure axis, distinct from the engine's redaction axis** (planning reconciliation 2). M6 implements the four disclosure modes; the engine's `anonymous` identifier-hashing mode is out of scope for the browser.
5. **"All five providers work browser-direct" is not achievable — OpenAI/Azure are CORS-blocked** (planning reconciliation 3, empirically verified by a cross-origin fetch test). Claude (native + browser-access header), Gemini (OpenAI-compat + Bearer), and local Ollama/LM Studio work browser-direct; OpenAI and Azure send no CORS headers. M6 adds native Claude + Gemini + local and documents OpenAI/Azure as not-available-browser-direct (no proxy, no engine relay).
6. **Chat persistence was deliberately descoped in 021 ("per spec") and the M6 PRD scope table re-adds it.** M6 builds IndexedDB persistence + markdown export, explicitly reversing the 021 descope rather than treating it as never-considered.
7. **The PRD's "consolidated in M0.5 behind `AiHandlerBase`" is a naming approximation.** The 1896-LOC `AiRequestHandler` monolith was split into `AiHandlerBase` + 7 per-message handlers in spec 022 (M0 closure) Phase 3, not a distinct "M0.5". Nothing to build; recorded for accuracy.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Control what schema the AI sees, fed from the local cache (Priority: P1)

A user can choose, per AI feature and as a global default, how much database schema is sent to the provider — **full schema**, **schema names only**, **no schema**, or **fully local** (which also forces a local provider). The active mode is shown next to every AI feature control. When a mode includes schema, the schema is resolved automatically from the M5 IndexedDB cache for the active database (no engine round-trip), and is truncated to fit the provider's context budget. When the mode is "no schema", no schema leaves the browser for that feature.

**Why this priority**: Privacy is the central architectural commitment of M6 (PRD §1, §8) and the basis of a Definition-of-Done item ("privacy mode 'no schema' verified by network capture"). Today the browser has **no mode selector at all** and the prompt service sends whatever schema string the panel hands it — effectively "full schema" with no user control and no cache wiring. Every other AI feature's request must respect the chosen mode, so this is the foundation the rest of the spec sits on. It is independently demonstrable with a network capture.

**Independent Test**: With a provider configured and a schema cached for the active database, set the global mode to "full schema" and run Explain — confirm (via the browser network panel) the outbound request body contains table/column names. Switch to "no schema" and run Explain again — confirm the request contains the SQL but **zero** schema identifiers. Set Text-to-SQL's per-feature mode to "schema names only" while the global default stays "full" — confirm Text-to-SQL requests carry names but no data types/FKs while Explain still carries the full schema. Set "fully local" and confirm a cloud provider is refused/greyed with a notice and only a local provider (Ollama / LM Studio) is selectable.

**Acceptance Scenarios**:

1. **Given** a cached schema for the active database and mode "full schema", **When** the user runs any AI feature, **Then** the outbound provider request includes table, column, FK, and (where available) description context resolved from the M5 cache, and the active mode label is visible next to the feature control.
2. **Given** mode "schema names only", **When** an AI feature runs, **Then** the request includes table and column names but excludes data types and foreign-key relationships.
3. **Given** mode "no schema", **When** an AI feature runs, **Then** the request contains only the user's SQL / prompt and **no** schema identifiers, verifiable by network capture.
4. **Given** mode "fully local", **When** the user opens the provider picker, **Then** only local providers (Ollama / LM Studio) are selectable and any cloud provider is blocked with a notice explaining the mode; full schema is permitted because the provider is local.
5. **Given** a per-feature mode differs from the global default, **When** that feature runs, **Then** the per-feature mode wins for that feature only and the others use the global default.
6. **Given** the cached schema is larger than the provider's context budget, **When** a schema-bearing feature runs, **Then** the schema is truncated to fit (the same size-truncation policy the WPF surface uses) rather than the request failing.

---

### User Story 2 — Watch answers stream in as they are generated (Priority: P1)

A user invoking any AI feature sees the response render token-by-token (typewriter effect) as it arrives, rather than waiting for the whole answer and then seeing it appear at once. Typing or invoking another feature cancels the in-flight stream cleanly, and a stream for one panel never bleeds tokens into another.

**Why this priority**: Streaming is the marquee UX of every AI feature (PRD §4.5) and underpins a success metric ("response begins streaming within 1500 ms"). It is a **cross-cutting transport change** to the single request path (`AiClientFactory`) that every feature — existing and new — uses, so doing it before the per-feature work (providers, Index Analysis, Ghost Text) avoids reworking each feature later. Today `SendAsync` buffers the entire response, so nothing streams.

**Independent Test**: Configure a streaming-capable provider, run Explain on a non-trivial query, and confirm text appears incrementally (not in a single jump). Mid-stream, invoke Optimize and confirm the Explain stream stops and the Optimize stream starts in its own pane without Explain tokens appearing in it. Start a chat response and, before it finishes, send a new chat message — confirm the prior stream is cancelled and the new one begins.

**Acceptance Scenarios**:

1. **Given** a streaming-capable provider, **When** an AI feature runs, **Then** tokens render incrementally as they arrive over the network stream.
2. **Given** a stream is in flight, **When** the user invokes another AI action or the panel closes, **Then** the in-flight request is cancelled (its network request aborts) and no further tokens render for it.
3. **Given** two panels could be active (e.g. the action panel and chat), **When** both have run, **Then** each renders only its own stream — tokens from one never appear in the other.
4. **Given** a provider returns an error mid-stream (e.g. rate limit), **When** the error arrives, **Then** the partial text is preserved and the mapped error message is shown, rather than the partial text being discarded silently.
5. **Given** a provider or mode that does not support streaming, **When** a feature runs, **Then** it falls back to a single buffered response and still renders the full answer (no feature is broken by the absence of streaming).

---

### User Story 3 — Use every provider that permits browser-direct calls, including native Claude (Priority: P2)

A user can configure and use, directly from the browser, every provider whose API allows it: **Claude (Anthropic)**, **Gemini (Google)**, and a **local provider (Ollama / LM Studio)** — each with its own wire format and authentication. For local providers, the docs state exactly which CORS setting to apply. For **OpenAI** and **Azure OpenAI**, which their own APIs block from browser-direct calls (no CORS headers), the UI shows a clear notice that they are not available in the browser and points to the desktop edition or an OpenAI-compatible endpoint — they never fail silently and are never routed through an AKML host or engine relay.

**Why this priority**: The PRD promises "all five providers work in the browser." A plan-phase cross-origin `fetch` test established what is actually possible: Claude works browser-direct via its native API (`x-api-key` + `anthropic-version` + `anthropic-dangerous-direct-browser-access` headers + the Messages body), Gemini works via its OpenAI-compatible endpoint + `Bearer`, and local providers work with the documented CORS config — but **OpenAI and Azure are CORS-blocked by their own APIs and cannot be made to work browser-direct without a proxy/relay** (out of scope per PRD §10). The shipped client speaks OpenAI-wire only and is non-streaming, so native Claude and the streaming parsers are the real build. P2 because the OpenAI-wire providers that *can* work (Gemini, local) already function for non-streaming calls, so the surface is usable.

**Independent Test**: Configure an Anthropic key, run Explain, and confirm a Claude response returns browser-direct (network capture shows the request to `api.anthropic.com` with `x-api-key`, not via any AKML host). Configure Gemini and confirm its OpenAI-compatible path returns a response. Start Ollama with the documented CORS setting and confirm a local call succeeds; remove the setting and confirm the failure names the fix. Select OpenAI (or Azure) and confirm the UI shows the not-available-browser-direct notice rather than attempting a call that would CORS-fail.

**Acceptance Scenarios**:

1. **Given** an Anthropic key, **When** any AI feature runs with Claude active, **Then** the request uses Anthropic's native wire format (`x-api-key`, `anthropic-version`, the `anthropic-dangerous-direct-browser-access` header, and the Messages body) and a response renders.
2. **Given** Gemini is active, **When** an AI feature runs, **Then** the call succeeds against Gemini's OpenAI-compatible endpoint with `Bearer` auth and a response renders.
3. **Given** any working provider, **When** a feature runs, **Then** the request goes **directly** to the provider's allow-listed origin and to no AKML-owned host (the origin allow-list still refuses any non-allow-listed origin before the request leaves the browser).
4. **Given** a local provider (Ollama / LM Studio) and the documented CORS configuration applied, **When** a feature runs, **Then** the local call succeeds; **and** when the CORS configuration is absent, the failure is surfaced with an actionable message that names the setting to apply.
5. **Given** OpenAI or Azure OpenAI is selected, **When** the user attempts an AI feature, **Then** the UI shows a clear "not available in the browser (CORS)" notice pointing to the desktop edition or an OpenAI-compatible endpoint — it does not silently fail and does not route through any AKML host or engine relay.
6. **Given** streaming (US2), **When** Claude or Gemini streams, **Then** the typewriter effect works for both (each provider's stream format is parsed correctly).

---

### User Story 4 — Get index suggestions in the browser (Priority: P2)

A user can select a query and request Index Analysis as a fifth AI action in the browser, receiving suggested `CREATE INDEX` statements (with the AI's rationale) rendered like the other actions, with Accept/Discard.

**Why this priority**: Index Analysis is one of the PRD's seven features and is explicitly deferred in the shipped panel (which has four actions). The prompt template (`IndexAnalysisPrompt`) already exists in `AkmlSql.AI`; the gap is exposing it through the web prompt service and wiring it to the schema context. It is P2 because it is one feature among several and the panel is already useful without it, but it is required for the PRD's "all 7 features" claim.

**Independent Test**: Open the AI panel with a query selected, click Index Analysis, and confirm the response contains one or more `CREATE INDEX` statements with rationale, honouring the active privacy mode (e.g. "no schema" sends only the query). Accept the result and confirm it lands in the editor.

**Acceptance Scenarios**:

1. **Given** a provider is configured and a query is selected, **When** the user invokes Index Analysis, **Then** a fifth action runs and returns index suggestions as `CREATE INDEX` statements with rationale.
2. **Given** the active privacy mode for Index Analysis, **When** it runs, **Then** the schema disclosed in the request matches that mode (e.g. "no schema" sends only the query).
3. **Given** an Index Analysis result, **When** the user clicks Accept, **Then** the suggested statements are inserted into / copied for the editor, consistent with the other actions.

---

### User Story 5 — See inline grey-text completions as you type (Priority: P2)

A user typing in the web editor sees AI-generated grey-text completions appear inline after a short pause, can accept the suggestion with Tab or dismiss it with Escape, and continued typing replaces the pending suggestion. Completions are debounced, cached, cancellable, and rate-limited so the feature does not spam the provider, and a visible per-session usage counter lets the user see how much they are spending.

**Why this priority**: Ghost Text is the seventh PRD feature and the explicitly-deferred "trickiest piece" (T133) — it requires a CodeMirror grey-text decorator integration that the prior session could not verify non-interactively. It is P2 because the editor is fully usable without it and it is opt-in (disabled by default, matching the WPF surface). The prompt side reuses the existing prompt service with a ghost-text prompt; the new work is the editor decorator plus the debounce/cache/cancel/rate-limit controller.

**Independent Test**: Enable Ghost Text. Type a partial statement at the end of a line and, after the debounce, confirm a grey-text suggestion appears; press Tab and confirm it is committed; press Escape on the next and confirm it dismisses. Type inside a comment / string literal / on an empty line and confirm **no** suggestion fires. Type the same prefix twice and confirm the second is served from cache (no second network request). Type continuously and confirm requests are rate-limited and the prior in-flight request is cancelled when a new keystroke arrives.

**Acceptance Scenarios**:

1. **Given** Ghost Text is enabled and the cursor is at the end of a line or after a keyword, **When** the user pauses for the debounce interval (≈350 ms), **Then** a grey-text completion is requested and rendered inline.
2. **Given** the cursor is inside a comment, inside a string literal, or on an empty line, **When** the user pauses, **Then** **no** completion is requested (suppression conditions hold).
3. **Given** a pending grey-text suggestion, **When** the user presses Tab, **Then** it is committed into the document; **when** the user presses Escape or keeps typing, **Then** it is dismissed/replaced.
4. **Given** the same prompt + prefix was requested before, **When** it recurs, **Then** the cached completion is reused and no duplicate provider request is sent.
5. **Given** a request is in flight, **When** the user types again, **Then** the in-flight request is cancelled before a new one is issued, and requests are throttled to no more than the configured rate (default ≤ 1 per 3 s, user-configurable).
6. **Given** Ghost Text is active, **When** completions are requested over a session, **Then** a visible per-session usage/token counter reflects the spend and the active privacy mode is honoured for the ghost-text prompt.

---

### User Story 6 — Keep and export chat conversations (Priority: P3)

A user's chat conversations persist across browser reloads (stored locally in IndexedDB) and can be exported to a Markdown file download. Clearing a conversation removes it from storage.

**Why this priority**: The M6 PRD scope table marks "Conversation history persistence (IndexedDB)" and "Conversation export (markdown download)" as **Yes**, consciously overriding spec 021's deliberate decision to keep chat in-memory only. It is P3 because chat already functions within a session; persistence and export are convenience that build on the working chat panel.

**Independent Test**: Hold a multi-turn chat, reload the browser, and confirm the conversation is restored. Export the conversation and confirm a `.md` file downloads whose content reflects the turns (roles + messages) in order. Clear the conversation and confirm it no longer appears after reload.

**Acceptance Scenarios**:

1. **Given** a multi-turn chat, **When** the user reloads the browser, **Then** the conversation is restored from local storage.
2. **Given** a conversation, **When** the user exports it, **Then** a Markdown file is offered for download whose content preserves the turn order and roles.
3. **Given** a conversation, **When** the user clears it, **Then** it is removed from storage and does not reappear after a reload.
4. **Given** the user has not enabled any cloud sync, **When** conversations are stored, **Then** they remain local-only (no network egress for persistence) — consistent with the privacy commitment.

---

### User Story 7 — Prove M6's privacy and parity against the WPF surface (Priority: P3)

A maintainer can run the browser AI verification suite (the deferred T137 US5 E2E and T134 AiPanel component tests) against mock provider endpoints, and can open a checked-in privacy audit and feature-parity audit that prove (a) the "no schema" mode sends no schema and no AKML-owned host appears in the AI request path (the deferred T146 / SC-009), and (b) the browser AI surface matches the WPF surface to the agreed parity bar. The privacy commitment and the local-provider CORS configuration are documented.

**Why this priority**: Several DoD items ("privacy mode audit: network captures confirm no leakage", "feature parity audit screenshots", "local provider (Ollama) documented with CORS config", "privacy commitment doc written") cannot be retired against evidence today. T137/T134/T146 are explicitly deferred. This is P3 because it verifies what US1–US6 build — it cannot meaningfully run until they land — but it is what converts "we built it" into "we proved it."

**Independent Test**: Run the browser AI E2E suite against the mock-provider harness and confirm it drives the US-level acceptance scenarios (add key → run feature → response renders → key never in plaintext). Open the privacy audit and confirm it contains a captured request set per privacy mode showing schema present/absent as expected and no AKML-owned host in any AI request. Open the parity audit and confirm paired web-vs-WPF screenshots of each AI surface with a deltas table and dispositions.

**Acceptance Scenarios**:

1. **Given** a fresh checkout, **When** the maintainer runs the browser AI E2E + component suite against mock providers, **Then** it exercises the US5 acceptance scenarios and the AiPanel rendering/no-key/error paths and reports pass, and asserts the API key never appears in the DOM or in plaintext storage.
2. **Given** the privacy audit, **When** a reviewer opens it, **Then** it shows, per privacy mode, a captured outbound request demonstrating the expected schema disclosure (full / names-only / none) and confirms no request in the AI path targets an AKML-owned host.
3. **Given** the parity audit, **When** a reviewer opens it without building, **Then** they see paired web-vs-WPF screenshots for each AI surface (panel actions, chat, settings, privacy-mode indicator, ghost text), a deltas table, and each delta's disposition (closed / accepted-with-reason).
4. **Given** the docs, **When** a user reads them, **Then** the privacy commitment ("your data goes only to your provider; no AKML server is in the path") and the exact local-provider CORS configuration (e.g. `OLLAMA_ORIGINS`) are documented.

---

### Edge Cases

- **No schema cached for the active database** — a schema-bearing privacy mode degrades to sending no schema (or a clear "schema unavailable" note) rather than failing; the feature still runs on the SQL alone.
- **"Fully local" selected but no local provider configured** — the AI controls are gated with a notice telling the user to configure Ollama / LM Studio, rather than silently sending to a cloud provider.
- **Privacy mode "no schema" must never leak schema even via error retries or fallback** — the no-schema guarantee holds across the fallback provider path and any retry, not just the first attempt.
- **Streaming abort mid-token** — aborting a stream must not leave a half-written token or a corrupted result pane; the partial text is either kept intact or cleared, never garbled.
- **Provider returns a non-OpenAI-shaped / non-streaming body when streaming was expected** — the client falls back to buffered parsing and still renders the answer.
- **Anthropic CORS preflight blocked** — if the browser cannot reach Anthropic directly (CORS), the failure is surfaced with an explanation, not a silent no-op; the request is never rerouted through an AKML host as a workaround.
- **Ghost Text fires while a modal/menu is open or during an undo/redo** — suppressed; ghost text never interferes with an active edit gesture.
- **Ghost Text suggestion overlaps a snippet expansion or autocomplete popup** — one inline affordance wins deterministically; the two never render stacked.
- **Rate limit / token counter at zero budget** — when a user-configured ghost-text rate or budget is exhausted, further requests are suppressed with a visible indication, not queued indefinitely.
- **Chat persistence across a schema-cache "clear all"** — clearing the schema cache must not delete chat conversations (separate store), and clearing chat must not touch schema or keys.
- **Exported Markdown with code fences in message content** — export escapes/fences correctly so a message containing ``` does not break the document structure.
- **Switching the active provider mid-conversation** — defined behaviour (continue with the new provider; the persisted history records which provider produced each turn) rather than a crash or silent mixing.

## Requirements *(mandatory)*

### Functional Requirements

#### Privacy modes & schema-aware prompting (US1)

- **FR-001**: The web edition MUST implement four user-selectable privacy **disclosure** modes — **full schema**, **schema names only**, **no schema**, and **fully local** — as both a global default and a per-feature override, applied to every AI feature's prompt construction. (The engine's `anonymous` identifier-hashing redaction mode is out of scope for the browser per planning reconciliation 2.)
- **FR-002**: API keys MUST remain encrypted at rest using the shipped browser vault (a per-profile non-extractable AES-GCM-256 `CryptoKey`, AAD-bound to `providerId`); M6 MUST NOT replace this with a passphrase/PBKDF2 scheme. The docs MUST state the threat-model implication: keys are strong at rest and never extractable, but there is no passphrase factor, so access to the browser profile implies usable keys.
- **FR-003**: When a mode includes schema, the prompt service MUST resolve the schema for the active database **from the M5 IndexedDB schema cache** (no engine round-trip) and MUST include exactly the elements the mode permits: full = tables + columns + FKs + descriptions; names only = table + column names without data types or FKs; no schema = none.
- **FR-004**: In "fully local" mode, the web edition MUST restrict the active provider to a local provider (Ollama / LM Studio) and MUST block / gate cloud providers with an explanatory notice; full schema is permitted because the provider is local.
- **FR-005**: The active privacy mode MUST be displayed next to every AI feature control (panel actions, chat, ghost text), so the user can see, before invoking, what will be disclosed.
- **FR-006**: Schema included in a prompt MUST be truncated to fit the provider's context budget using the same size-truncation policy the WPF surface applies, rather than failing the request when the schema is large.
- **FR-007**: The "no schema" guarantee MUST hold across retries and any fallback-provider path — no schema may be sent for a feature whose active mode is "no schema", under any code path.

#### Streaming responses (US2)

- **FR-008**: The provider client MUST support token streaming (read the provider's streamed response incrementally) and render tokens to the requesting surface as they arrive (typewriter effect).
- **FR-009**: Each AI surface (action panel, chat panel, ghost text) MUST own its own streaming controller; tokens from one surface's stream MUST NOT render in another's.
- **FR-010**: An in-flight stream MUST be cancellable — invoking another AI action, sending a new chat message, or closing/disposing the surface MUST abort the underlying network request and stop rendering, with the cancellation token bound to that surface's lifetime.
- **FR-011**: When a provider errors mid-stream, the already-rendered partial text MUST be preserved and the mapped error message (the existing 401/429/404/content-policy/network mapping) MUST be shown.
- **FR-012**: When a provider or mode does not support streaming, the feature MUST fall back to a single buffered response and still render the full answer; no feature may be broken by the absence of streaming.

#### All five providers, browser-direct (US3)

- **FR-013**: The browser MUST support browser-direct calls for every provider whose API permits cross-origin browser access — verified empirically: **Claude (Anthropic)** (native wire, FR-014), **Gemini** (OpenAI-compatible endpoint + `Bearer`), and **Ollama / LM Studio** (local, with documented CORS config). **OpenAI and Azure OpenAI are CORS-blocked browser-direct** (their APIs send no `Access-Control-Allow-Origin`); M6 MUST surface them with an in-app notice that they are unavailable in the browser, pointing to the desktop edition or an OpenAI-compatible endpoint, and MUST NOT route them through any AKML host or engine relay (Reconciliation; PRD §10).
- **FR-014**: Claude MUST be called using Anthropic's native API contract from the browser: `x-api-key` and `anthropic-version` headers, the `anthropic-dangerous-direct-browser-access` header to satisfy CORS, and the Messages request/response body shape (including its streaming format for US2).
- **FR-015**: Gemini browser-direct is **confirmed working** (plan-phase cross-origin test: its OpenAI-compatible endpoint `…/v1beta/openai/chat/completions` accepts `Authorization: Bearer <key>` and returns CORS headers). M6 MUST use that path and MUST verify it end-to-end (including `stream: true`) in the US7 suite; native-Gemini wire is unnecessary.
- **FR-016**: Every AI request MUST go directly to the provider's allow-listed origin and to **no AKML-owned host**; the existing per-provider origin allow-list MUST continue to refuse any non-allow-listed origin before the request leaves the browser, and the allow-list MUST cover the native Claude origin.
- **FR-017**: Local-provider (Ollama / LM Studio) use MUST be documented with the exact CORS configuration required (e.g. `OLLAMA_ORIGINS`), and a CORS/connection failure MUST be surfaced with an actionable message naming the setting to apply.
- **FR-018**: When the browser cannot reach a provider directly (e.g. CORS preflight blocked), the failure MUST be surfaced with an explanation; the request MUST NOT be silently rerouted through any AKML-owned host as a workaround.

#### Index Analysis (US4)

- **FR-019**: The web prompt service MUST expose an Index Analysis action that builds its prompt from the existing `IndexAnalysisPrompt` in `AkmlSql.AI`, wired to the schema context per the active privacy mode.
- **FR-020**: The AI panel MUST present Index Analysis as a fifth action alongside Explain / Fix / Optimize / Text-to-SQL, rendering the suggested `CREATE INDEX` statements with rationale and Accept/Discard, consistent with the other actions.
- **FR-021**: Index Analysis MUST honour the active privacy mode for its feature (e.g. "no schema" sends only the selected query).

#### Ghost Text (US5)

- **FR-022**: The web editor MUST render AI inline grey-text completions, requested after a debounce of ≈350 ms following the last keystroke, when the cursor is at the end of a line or after a keyword.
- **FR-023**: Ghost Text MUST be suppressed when the cursor is inside a comment, inside a string literal, or on an empty line.
- **FR-024**: A pending grey-text suggestion MUST be committable with Tab and dismissable with Escape; continued typing MUST replace or dismiss the pending suggestion.
- **FR-025**: Ghost Text MUST cache responses keyed by prompt + prefix so an identical request reuses the cached completion instead of issuing a duplicate provider request.
- **FR-026**: A new keystroke while a Ghost Text request is in flight MUST cancel the in-flight request before issuing a new one.
- **FR-027**: Ghost Text MUST be rate-limited to a user-configurable maximum (default ≤ 1 request per 3 s of active typing) and MUST be **disabled by default** (opt-in), matching the WPF surface.
- **FR-028**: The UI MUST show a per-session AI usage/token counter reflecting spend (at minimum for Ghost Text), so the user can see the cost of sustained typing.
- **FR-029**: Ghost Text MUST honour the active privacy mode for its prompt (a schema-bearing mode uses a minimal/most-relevant schema slice to stay within latency and token budgets).

#### Chat persistence & export (US6)

- **FR-030**: Chat conversations MUST persist locally (IndexedDB) and be restored across browser reloads.
- **FR-031**: A conversation MUST be exportable to a Markdown file download that preserves turn order and roles, with message content (including code fences) escaped so the document structure is not broken.
- **FR-032**: Clearing a conversation MUST remove it from local storage; chat storage MUST be independent of the schema cache and key store (clearing one MUST NOT affect the others).
- **FR-033**: Conversation persistence MUST be local-only (no network egress for storage or sync) and MUST record which provider produced each turn.

#### Verification & audit (US7)

- **FR-034**: A browser AI end-to-end suite (the deferred T137) MUST exist that drives the US-level acceptance scenarios against mock provider endpoints (add key → run feature → response renders), asserts the key never appears in the DOM or plaintext storage, and is opt-in / excluded from the default test run (matching the established E2E trait pattern).
- **FR-035**: AiPanel component tests (the deferred T134) MUST cover action wiring, the no-provider prompt, provider-error rendering, and the key never being present in the DOM.
- **FR-036**: A privacy audit (the deferred T146 / SC-009) MUST exist that captures, per privacy mode, an outbound request demonstrating the expected schema disclosure (full / names-only / none) and confirms no request in the AI path targets an AKML-owned host.
- **FR-037**: A feature-parity audit document MUST compare each browser AI surface (panel actions, chat, settings, privacy-mode indicator, ghost text) against the WPF surface with paired screenshots, a deltas table, and per-delta dispositions; the highest-impact deltas MUST be closed and the remainder filed as named follow-ups.
- **FR-038**: A privacy-commitment document MUST be written stating that AI data goes only to the user-configured provider, the minimum per the privacy mode, never through any AKML-owned host, and that the web edition is fully usable with local-only providers; the in-app privacy-mode tooltip MUST reflect this.
- **FR-039**: After this spec lands, every M6 PRD §12 Definition-of-Done checkbox MUST be closeable against either an already-shipped feature (per the reality table), one of FR-001 … FR-038, or an explicit revised-with-reason note — specifically the "passphrase-protected" item is **revised** per FR-002 (non-extractable key, no passphrase), and "all five providers work" is **revised, not met as written**: met browser-direct for the CORS-permitting providers (Claude/Gemini/local) and **documented-out for OpenAI/Azure** per FR-013 / Reconciliation 3 (CORS-blocked, no proxy, no relay).

### Key Entities

- **Privacy mode (disclosure)**: one of full schema / schema names only / no schema / fully local; settable globally and per feature; determines what schema (if any) is included in a prompt and, for "fully local", which providers are selectable.
- **AI provider configuration**: the shipped per-provider record (id, display name, model, optional endpoint, wrapped-key indicator) extended so Claude carries its native wire requirements; the active provider is tracked by the existing preference.
- **Wrapped API key**: the at-rest key material, wrapped by the per-profile non-extractable `CryptoKey`, AAD-bound to `providerId`; unwrapped only for the duration of a single provider call.
- **Streaming controller**: a per-surface object that owns one in-flight provider stream, renders its tokens, and is cancelled when the surface's lifetime ends or another action starts.
- **Ghost-text request**: a debounced, prefix-keyed, cancellable inline-completion request, subject to suppression conditions, a response cache, and a rate limit.
- **Chat conversation**: a locally-persisted ordered list of turns (role + content + originating provider), exportable to Markdown.
- **Schema context (browser)**: the table/column/FK/description data resolved from the M5 IndexedDB cache for the active database, filtered by the active privacy mode and truncated to the provider's budget.
- **Privacy audit / parity audit**: the checked-in records proving no-schema/no-AKML-host behaviour and web-vs-WPF feature parity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can run all seven AI features in the browser — Text-to-SQL, Explain, Fix, Optimize, Index Analysis, Chat, Ghost Text — without DevTools, the address bar, or a diagnostic page.
- **SC-002**: Every provider that permits browser-direct access — Claude, Gemini, Ollama/LM Studio — returns a working response browser-direct, and a network capture confirms each request goes to the provider's own origin and to no AKML-owned host. OpenAI and Azure are surfaced with a clear not-available-browser-direct notice (verified present), never a silent failure.
- **SC-003**: With privacy mode "no schema", a network capture of every AI feature's outbound request shows **zero** schema identifiers; with "full schema" the schema is present; with "schema names only" names are present but data types and FKs are absent — verified per feature.
- **SC-004**: Per-feature privacy overrides take effect independently of the global default (one feature can be "full schema" while another is "no schema") and the active mode is visible next to every AI control.
- **SC-005**: AI responses render incrementally (typewriter) for streaming-capable providers, and invoking a second action cancels the first stream with no cross-panel token bleed; a non-streaming provider still renders a complete answer.
- **SC-006**: Ghost Text produces inline grey-text completions under the defined trigger/suppression rules, accepts on Tab, is opt-in, is rate-limited to the configured maximum, and achieves a cache hit rate of ≥ 30 % during sustained typing (recorded), with a visible per-session usage counter.
- **SC-007**: Chat conversations survive a browser reload and export to a Markdown file that preserves turns and roles; clearing chat does not affect the schema cache or keys, and persistence produces no network egress.
- **SC-008**: The browser key vault keeps the API key out of plaintext storage and out of the DOM (asserted by the E2E + component tests), using the shipped non-extractable-key scheme.
- **SC-009**: The privacy audit and parity audit exist and are reviewable without building: the privacy audit shows the per-mode disclosure captures and the no-AKML-host result; the parity audit records every web-vs-WPF delta with a disposition, with ≤ 3 deltas left open.
- **SC-010**: Every M6 PRD Definition-of-Done checkbox is closed against either a shipped feature or a requirement in this spec, **or explicitly revised-with-reason**: the "passphrase-protected" item → non-extractable key (FR-002); "all five providers work browser-direct" → met for the CORS-permitting providers, OpenAI/Azure documented-out (FR-013 / Reconciliation 3).

## Assumptions

- **Shipped scaffold is the baseline.** The library extraction (T121–T124), key vault (T125), preference (T126), client + allow-list (T128, T130), prompt service (T129), panel (T131), chat (T132), settings (T135), and error mapping (T136) are merged and not re-implemented; this spec extends them.
- **Key storage model.** Per planning reconciliation 1, the non-extractable-`CryptoKey` vault is retained; the PRD's passphrase/PBKDF2 design is not implemented. The DoD "passphrase-protected" wording is revised to "encrypted at rest with a non-extractable key."
- **Privacy taxonomy.** Per planning reconciliation 2, the browser uses the PRD's four disclosure modes; the engine's `anonymous` redaction mode is not ported to the browser.
- **Provider coverage.** Per planning reconciliation 3, native Claude is added; Gemini is verified via its OpenAI-compatible endpoint; the OpenAI-wire path remains the shared path for OpenAI / Azure / Ollama / LM Studio.
- **Schema source.** The browser builds prompts from the **M5 IndexedDB schema cache** (`schemaEntries`), not from the engine; AI features need no engine round-trip (PRD §4.1). Where no cache exists, schema-bearing modes degrade to no-schema.
- **Direct-to-provider, no proxy.** Consistent with the PRD and spec 021 FR-030, no AKML-owned host is ever in the AI request path; CORS limitations are surfaced, not worked around via a proxy.
- **Ghost Text is opt-in** and off by default, matching the WPF surface; its prompt reuses the existing prompt builders with a ghost-text-specific prompt.
- **Verification runs developer-side / interactively.** The E2E (T137), component (T134), privacy-capture (T146), and parity audits require a running web app, a mock-provider harness, and (for parity) an interactive workstation running both surfaces at the same theme — the same constraint as the prior closure audits (specs 024/025/027).
- **Engine AI path is unchanged.** Per PRD open question 1, the engine's `AiHandlerBase` handlers and the SSMS/VS plugin AI path coexist and are not modified by this browser-only work.

## Dependencies

- **Spec 021 Phase 7 (T121–T138, merged)** — `AkmlSql.AI` extraction, browser key vault, active-provider preference, direct-to-provider client + origin allow-list, prompt service, AI panel, chat panel, settings page, error mapping, quickstart docs. This spec builds the remaining features and verification on that substrate.
- **Spec 027 (M5 offline closure, merged)** — the IndexedDB schema cache (`schemaEntries`, `ISchemaCacheStore`, `SchemaSnapshot`) US1 reads to build schema-aware prompts; the cache-availability model the privacy/schema piece piggy-backs on.
- **`AkmlSql.AI`** — the shared library that already holds the prompt builders (incl. `IndexAnalysisPrompt`), provider factory, `PrivacyTransformer`, `SchemaContextBuilder`, and `StreamCoalescer`; US1/US2/US4 extend its consumption from the web.
- **`AkmlSql.Web` editor** — CodeMirror 6 (`EditorComponent.razor`, `wwwroot/js/akml-editor.js`); US5's grey-text decorator is the new editor-side integration.
- **`IWebCryptoWrapper` / `akml-crypto.js`** — the shipped Web Crypto layer the key vault uses; unchanged by FR-002.
- **Playwright .NET + bUnit stacks** — already wired into `tests/AkmlSql.Web.E2E.Tests/` and `tests/AkmlSql.Web.Tests/`; reused for US7.
- **`CapabilityNotice.razor`** — the inline-notice pattern reused for the "fully local needs a local provider" and CORS-failure notices.

## Out of Scope (deferred follow-ups)

- **Passphrase / PBKDF2 key storage** (PRD §4.3) — superseded by the shipped non-extractable-key vault per planning reconciliation 1; revisited only if a "something you know" factor is later required.
- **Engine's `anonymous` identifier-hashing redaction mode in the browser** — the browser uses the four disclosure modes only; porting identifier hashing is a named follow-up.
- **OpenAI / Azure OpenAI browser-direct** — CORS-blocked by their own APIs (empirically verified); deferred unless those providers add browser CORS, or the user uses a local OpenAI-compatible proxy or the desktop edition. (Native Gemini wire is likewise unneeded — its OpenAI-compatible endpoint works browser-direct.)
- **Per-provider error-docs deep links** — the error mapping (401/429/404/content-policy/network) is shipped; deep-linking each provider's docs page is a follow-up.
- **Usage cost estimate display beyond a token counter, multi-model output comparison, cost-per-feature dashboard** — PRD §5/§10 explicitly out of scope.
- **AKML-hosted AI proxy** — would change the privacy model; explicitly excluded (PRD §10).
- **Cloud-synced chat history** — local-only in M6; sync is a SaaS concern (PRD open question 4).
- **Fine-tuned models / RAG, voice input, image input, workflow chains** — PRD §10 out of scope.
- **Engine-side streaming handlers** — the engine's AI path is unchanged; the `StreamCoalescer` infra exists but wiring engine handlers to stream is not part of this browser-only closure.
