<!-- Suggested PR title: Spec 028 — M6: AI Assistance in the Browser (closure) -->
<!-- Branch: 028-m6-ai-browser-closure -> master | Use: gh pr create --title "..." --body-file specs/028-m6-ai-browser-closure/PR-DESCRIPTION.md -->

## M6 — AI Assistance in the Browser (closure)

Closes the M6 milestone: the web edition gains the AI feature set, browser-direct (bring-your-own-key), at parity with the WPF surface where the browser sandbox allows.

This is a **closure spec** (the 022–027 pattern). The M6 PRD reads greenfield, but the scaffold already shipped under spec 021 Phase 7 (`AkmlSql.AI` lib, key vault, provider client, panel, chat, settings). This PR builds the genuinely-unmet work on top of it.

### Scope-shaping reconciliations (all user-confirmed; see `research.md`)
1. **Key storage** keeps the shipped per-profile **non-extractable Web-Crypto key** — *not* the PRD's passphrase/PBKDF2. The DoD "passphrase-protected" item is revised accordingly (threat model in `doc/WEB/ai-privacy-commitment.md`).
2. **Privacy modes** follow the PRD's 4 **disclosure** modes (full / names-only / no-schema / fully-local), a different axis than the engine's redaction modes; the engine's `anonymous` mode is out of scope for the browser.
3. **Providers**: a live cross-origin fetch test proved **Claude (native), Gemini, Ollama, LM Studio work browser-direct, but OpenAI and Azure are CORS-blocked** by their own APIs. They're documented-out (in-app notice → desktop edition / OpenAI-compatible endpoint); **no proxy, no engine relay** (PRD §10). "All 5 providers" is revised to "every provider whose API permits browser-direct."
4. **Schema-aware prompting** required building the `SchemaPhasePayload → DatabaseCache` rehydrator that M5 deliberately deferred (so the browser can feed the canonical `SchemaContextBuilder`).

### What's implemented (build- + test-verified)
- **Privacy modes & schema-from-cache** — `IAiFeatureSettings` (new IndexedDB store), `IAiSchemaContextProvider`, `SchemaPhaseRehydrator` (+ type-facet carry-through), per-action privacy badge; fully-local guard enforced **at the send path** (panel + chat + ghost), not just the picker.
- **Streaming / typewriter** — `IAiClientFactory` refactored to a 3-axis provider abstraction (request-builder × auth × SSE-parser) + `StreamAsync`; per-surface cancellation; throttled render; buffered fallback.
- **Providers** — native Claude (`x-api-key` + `anthropic-version` + `anthropic-dangerous-direct-browser-access` + Messages SSE); Gemini (OpenAI-compat); local (Ollama / LM Studio) with documented CORS (`doc/WEB/ai-local-provider-cors.md`); OpenAI/Azure not-available notice.
- **Index Analysis** — 5th panel action.
- **Ghost Text** — CodeMirror grey-text decorator (StateField + widget + `Prec.highest` keymap + debounced/suppression-gated hook) + `IAiGhostTextService` (cache, rate-limit, session counter); opt-in.
- **Chat persistence + Markdown export** — `IChatHistoryStore` (local-only, restore on reload, clear isolated from schema/keys).

### Verification
- **65 AI unit/bUnit tests + 4 rehydrator tests pass.** Full web suite: no new failures (the 26 failing tests are pre-existing formatter-parity, unrelated to M6). Engine + E2E projects build clean.
- **Two rounds of multi-agent adversarial review** ran on the diff; every finding fixed — a fully-local privacy leak, a wire `null`-serialization bug that would've 400'd Claude/local at runtime, and a chat history-poisoning bug — all caught before merge.

### Not in this PR — interactive verification only (tracked in `M6-PARITY-AUDIT.md`)
These need a running app / both surfaces and can't run headlessly: US5 E2E run + mock-provider harness (a skip-flagged scaffold is committed), the privacy network-capture audit, web-vs-WPF parity screenshots, and first-token latency. The deterministic substrate for each is already unit-covered.

### Test plan
- `dotnet test tests/AkmlSql.Web.Tests` (AI suite) and `dotnet test tests/AkmlSql.IntelliSense.Tests` — green.
- Manual (post-merge, per `quickstart-m6.md`): add a Claude/Gemini/local key → run the 5 actions (streamed) → verify per privacy mode via DevTools that "no schema" sends no schema and no AKML host is contacted → enable Ghost Text → persist/export a chat.

### Risk / notes
- Additive `SchemaPhaseColumn` MessagePack keys (backward-compatible) — also improves offline completion type rendering.
- No new IPC message types; browser AI stays engine-bypassed. No new NuGet/CDN packages (ghost text hand-rolled from loaded CM6 primitives).
- Engine AI path unchanged (SSMS/VS plugins unaffected).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
