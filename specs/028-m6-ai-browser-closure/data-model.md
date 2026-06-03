# Data Model: M6 — AI Parity Closure

**Branch**: `028-m6-ai-browser-closure` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)

This closure adds **two new IndexedDB store names** (`aiFeatureSettings`, `chatHistory`) and **one new shared runtime mapper** (`SchemaPhaseRehydrator`); the rest are conceptual in-memory/runtime entities and extensions to shipped POCOs. The existing AI stores (`aiKeys`, `keyMaterial`) and the M5 `schemaEntries` store are **unchanged**. Entities exist so `tasks.md` can name them without ambiguity.

---

## E1 — AiPrivacyMode + AiFeatureSettings (new store)

**Owner**: `Services/IAiFeatureSettings.cs` (**new**, singleton, mirrors `IAnalysisSettingsStore`); persisted in the **new `aiFeatureSettings` IndexedDB store** under key `"current"`.

**Purpose**: The global-default + per-feature **disclosure** privacy mode (FR-001/FR-005) and ghost-text settings (FR-027/FR-028).

**`AiPrivacyMode` (enum, 4 disclosure modes)**: `FullSchema` | `SchemaNamesOnly` | `NoSchema` | `FullyLocal`. (Distinct from the engine's redaction enum `full`/`schemaOnly`/`anonymous` — Reconciliation 2.)

**Fields**:

| Field | Type | Meaning |
|---|---|---|
| `GlobalDefaultMode` | `AiPrivacyMode` | Default for any feature without an override. Default `FullSchema`. |
| `FeatureModeOverrides` | `Dictionary<string, AiPrivacyMode>` | Per-feature override keyed by feature id (`"explain"`, `"fix"`, `"optimize"`, `"texttosql"`, `"indexanalysis"`, `"chat"`, `"ghosttext"`); absent ⇒ use global. |
| `GhostTextEnabled` | `bool` | Opt-in master switch; **default `false`** (FR-027). |
| `GhostTextDelayMs` | `int` | Debounce; default `350` (FR-022). |
| `GhostTextMaxRequestsPer3s` | `int` | Rate limit; default `1` (FR-027). |

**Validation rules**: mode resolution = `FeatureModeOverrides[feature] ?? GlobalDefaultMode`. Setting `FullyLocal` (global or per-feature) constrains provider selection (E5). Changing settings invalidates the in-memory cache so the next call reflects it.

**Mode → context mapping** (consumed by E3):

| Mode | includeSchema | compressionLevel | forceLocalProvider |
|---|---|---|---|
| `FullSchema` | yes | 4 | no |
| `SchemaNamesOnly` | yes | 1 | no |
| `NoSchema` | no | — | no |
| `FullyLocal` | yes | 4 | **yes** |

---

## E2 — SchemaPhaseRehydrator (new shared mapper)

**Owner**: `AkmlSql.IntelliSense/Schema/SchemaPhaseRehydrator.cs` (**new**; namespace `AkmlSql.Engine.Schema` to sit beside `DatabaseCache`). WASM-safe.

**Purpose**: The reverse of `SchemaPhaseSerializer` — reconstruct a `DatabaseCache` from a cached `SchemaPhasePayload` so the canonical `SchemaContextBuilder` can run in the browser (FR-003). **This is the path spec 027 deliberately deferred** (research Decision 3 there); M6 builds it because AI prompting needs the canonical builder.

**Contract**: `static DatabaseCache Rehydrate(string cacheKey, SchemaPhasePayload? phaseA, SchemaPhasePayload? phaseB)` — maps `Schemas[] → SchemaEntry`, `Objects[] → DatabaseObject` (+`ObjectType`, `Description`), `Columns[] → Column` (+`TypeName`, `IsNullable`, `IsPrimaryKey`, `Description`), `Parameters[] → Parameter`, `ForeignKeys[] → ForeignKey`; sets `Phase`, then calls `cache.RebuildFkIndex()`.

**Validation rules**:

- Phase B (if present) supersedes Phase A for column data; Phase A alone suffices for `SchemaNamesOnly` (no columns needed).
- MUST NOT introduce any `System.IO` / SqlClient / native dependency (WASM-loadable; uses only existing models).
- **Round-trip invariant** (the gate test): for a known `DatabaseCache`, `Rehydrate(serialize(cache))` reproduces the same objects/columns/FKs the engine would expose via `GetAllObjects()` / `GetForeignKeysForTable()`.

---

## E3 — AiSchemaContextProvider (in-memory; derived)

**Owner**: `Services/IAiSchemaContextProvider.cs` (**new**, singleton).

**Purpose**: Resolve the schema text a prompt needs, for the active database, filtered by the active privacy mode (FR-003/FR-006/FR-007).

**Flow**: read the active `(server, db)` snapshot from `ISchemaCacheStore`; if the resolved mode `includeSchema == false` ⇒ return empty string (the no-schema guarantee); else deserialize `PhaseA`/`PhaseB` → `SchemaPhaseRehydrator.Rehydrate` → `SchemaContextBuilder.BuildAsync(..., compressionLevel, maxObjects)` → `SchemaContextFormatter.Format(...)`, truncated to the provider's budget.

**Validation rules**:

- `NoSchema` ⇒ empty `schemaText` on **every** code path (incl. retries/fallback) — FR-007.
- No cached snapshot ⇒ schema-bearing modes degrade to empty `schemaText` (edge case), never throw.
- Output is identical in shape to what the engine's `SchemaContextBuilder` produces (same canonical builder, no fork).

---

## E4 — ProviderProfile (in-memory; per-provider strategy triple)

**Owner**: `Services/IAiClientFactory.cs` (**extended**).

**Purpose**: Encapsulate the three per-provider wire differences (Decision 3 / FR-013/FR-014) so the OpenAI shape, native Claude, and local providers coexist.

**Shape** — one profile per `providerId`, selecting:

| Axis | Type | Variants |
|---|---|---|
| Request builder | `IAiRequestBuilder` | `OpenAiRequestBuilder` (openai-compat: gemini/ollama/lmstudio) · `AnthropicRequestBuilder` (system top-level, `max_tokens` required) |
| Auth applier | `IAuthApplier` | `BearerAuth` (gemini/ollama/lmstudio) · `AnthropicAuth` (`x-api-key` + `anthropic-version` + `anthropic-dangerous-direct-browser-access`) · `AzureApiKeyAuth` (`api-key`) — Azure present for completeness though documented-out |
| SSE parser | `ISseDeltaParser` | `OpenAiSseParser` (`data:` lines, `choices[0].delta.content`, `[DONE]`) · `AnthropicSseParser` (`content_block_delta`/`delta.text`, end on `message_stop`) |

**Validation rules**:

- `openai` and `azure` profiles exist but their **call path is gated to the not-available notice** (E5) — they are CORS-blocked browser-direct (Decision 3); they are never relayed through an AKML host or engine.
- The origin allow-list (already shipped) still refuses any non-allow-listed origin before a request leaves the browser (FR-016); it covers the native Claude origin.

---

## E5 — Provider availability + "fully local" gating (in-memory; derived)

**Owner**: `Pages/SettingsAi.razor` + the provider picker; consumes E1 + `IAiPreference` + `AiProviderConfig`.

**Purpose**: Decide which providers are selectable/usable (FR-004/FR-013/FR-017/FR-018).

**Rules**:

- `IsLocal(providerId)` ⇔ `providerId ∈ { "ollama", "lmstudio" }`.
- `BrowserDirectCapable(providerId)` ⇔ `providerId ∈ { "anthropic", "gemini", "ollama", "lmstudio" }`. `openai`/`azure` ⇒ **not** capable.
- Active mode `FullyLocal` ⇒ only `IsLocal` providers selectable; a cloud provider is blocked with a `CapabilityNotice`-style explanation (FR-004).
- Selecting `openai`/`azure` ⇒ show the not-available-browser-direct notice (FR-013/US3 scenario 5); never attempt a call that would CORS-fail; never relay.

---

## E6 — StreamingController (in-memory; per-surface)

**Owner**: each AI surface (`AiPanel`, `AiChatPanel`, ghost-text path).

**Purpose**: Own one in-flight provider stream and render its tokens; cancel cleanly (FR-009/FR-010/FR-011).

**Shape**: holds a `CancellationTokenSource` bound to the surface lifetime + the consuming `IAsyncEnumerable<string>` loop. Starting a new action on the same surface cancels the prior CTS (aborting the HTTP request) before starting the next. Mid-stream error ⇒ keep partial text + show mapped error (FR-011).

**State transitions**: idle → streaming (tokens append) → {complete | cancelled | errored}. A cancelled/errored stream never resumes; partial text persists per FR-011.

---

## E7 — GhostTextRequest (in-memory; debounced/cached/rate-limited)

**Owner**: `wwwroot/js/akml-editor.js` (the StateField/widget/keymap + debounced hook) + `Services/IAiGhostTextService.cs` (**new**, the C# direct-to-provider caller).

**Purpose**: One inline grey-text completion attempt (FR-022 … FR-029).

**Fields / behaviour**:

| Aspect | Value |
|---|---|
| Trigger | cursor at end-of-line OR after a keyword, after the debounce (E1 `GhostTextDelayMs`, default 350 ms). |
| Suppression | inside `LineComment`/`BlockComment`/`String`/`QuotedIdentifier` (via `syntaxTree`), empty line, autocomplete popup open (`completionStatus !== null`), active snippet. |
| Accept / dismiss | Tab commits (single edit); Escape / continued typing dismisses. Tab handler at `Prec.highest` returns `false` (falls through) unless ghost text is the sole active affordance. |
| Cache | keyed by prompt + prefix (FR-025); identical request reuses the cached completion (≥30 % hit target, SC-006). |
| Cancellation | a new keystroke cancels the in-flight request before issuing a new one (FR-026). |
| Rate limit | ≤ E1 `GhostTextMaxRequestsPer3s` (default 1 / 3 s), user-configurable (FR-027). |
| Privacy | honours the resolved mode for `"ghosttext"`; schema-bearing modes use a minimal/most-relevant slice (FR-029). |
| Token counter | per-session usage counter incremented per request (FR-028). |
| Prompt | reuses the shared `GhostTextPrompt` (no engine round-trip — keys are browser-side). |

---

## E8 — ChatConversation / ChatTurn (new store)

**Owner**: `Services/IChatHistoryStore.cs` (**new**, singleton); persisted in the **new `chatHistory` IndexedDB store**.

**Purpose**: Persisted, exportable conversations (FR-030 … FR-033).

**Shape**:

- `ChatConversation { Id, Title, CreatedAt, UpdatedAt, Turns: List<ChatTurn> }`
- `ChatTurn { Role ("user"|"assistant"), Content, ProviderId, Timestamp }` — `ProviderId` records which provider produced the turn (FR-033).

**Validation rules**:

- Persist on each completed turn (user + assistant); restore the active conversation on load (FR-030).
- Clear removes the conversation from storage and does not reappear after reload (FR-032); chat storage is independent of `schemaEntries` and `aiKeys` (clearing one MUST NOT affect the others).
- Export (FR-031): a Markdown download via `akml-download.js` (`text/markdown`) preserving turn order + roles, with code-fence-safe escaping; local-only, no network egress (FR-033).

---

## E9 — Audit & docs artifacts

**Owner**: checked-in markdown.

- **`specs/028-m6-ai-browser-closure/M6-PARITY-AUDIT.md`** (FR-037): paired web-vs-WPF screenshots for each AI surface (panel actions incl. Index Analysis, chat, settings, privacy-mode badge, ghost text), a deltas table (element / WPF / web / disposition), closed vs accepted-with-reason, host OS/theme/DPI metadata; ≤ 3 deltas open (SC-009). Follows the `M5-PARITY-AUDIT.md` shape.
- **Privacy audit** (FR-036/SC-003): captured outbound requests per privacy mode (full/names/none) demonstrating disclosure + the no-AKML-host result. May live in the parity-audit doc or `SC-009-EVIDENCE/`.
- **`doc/WEB/ai-privacy-commitment.md`** (FR-038): the threat model + commitment ("data goes only to your provider; no AKML host in the path; minimum per the privacy mode; fully usable with local providers"), including the FR-002 key-storage tradeoff note.
- **`doc/WEB/ai-local-provider-cors.md`** (FR-017): the exact `OLLAMA_ORIGINS` / LM Studio CORS setup (may be a section of `quickstart-m6.md`).
