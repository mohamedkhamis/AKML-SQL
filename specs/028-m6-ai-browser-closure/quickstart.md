# Quickstart: M6 — AI Parity Closure

**Branch**: `028-m6-ai-browser-closure` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md) · [plan.md](./plan.md)

A per-user-story build + verify walkthrough. Build the web edition and run the AI features browser-direct; no engine is needed for the browser AI path (keys live in the browser). Priority order — each story is independently demoable.

## Prerequisites

- .NET 10 SDK; the web edition builds/runs (`dotnet run --project src/AkmlSql.Web` or the established serve task).
- A provider key for at least one **browser-direct-capable** provider: **Claude (Anthropic)**, **Gemini**, or a local **Ollama / LM Studio**. (OpenAI/Azure are CORS-blocked browser-direct — see US3.)
- For local providers: set the CORS origin (`OLLAMA_ORIGINS=<app origin>` for Ollama; the LM Studio CORS toggle) per `doc/WEB/ai-local-provider-cors.md`.

## US1 — Privacy modes + schema from the cache (P1)

**Build**: `SchemaPhaseRehydrator` (`AkmlSql.IntelliSense/Schema/`) + round-trip test → `IAiFeatureSettings` (`aiFeatureSettings` store) → `IAiSchemaContextProvider` → `AiPrivacyModeBadge` → wire every action's `schemaText` through the provider.

**Verify**: With a schema cached for the active DB and a working provider, set global mode **Full schema** and run Explain — open DevTools → Network → confirm the request body carries table/column names. Switch to **No schema** → the request carries only the SQL, **no** schema identifiers. Set Text-to-SQL's per-feature mode to **Schema names only** while global stays Full → its request has names but no types/FKs; Explain still carries full schema. Set **Fully local** → only Ollama/LM Studio are selectable; a cloud provider is blocked with a notice. Each control shows its active mode badge.

## US2 — Streaming / typewriter (P1)

**Build**: extend `IAiClientFactory` with the `ResponseHeadersRead` + `ReadAsStreamAsync` + per-provider SSE-parser path returning `IAsyncEnumerable<string>`; per-surface `StreamingController`; render incrementally in `AiPanel` + `AiChatPanel`; buffered fallback.

**Verify**: Run Explain on a non-trivial query against Claude or Gemini → text renders incrementally (typewriter), not in one jump. Mid-stream, click Optimize → the Explain stream stops, Optimize starts in its pane, no Explain tokens bleed in. In chat, send a second message before the first finishes → the prior stream cancels. Point at a non-streaming mock → a complete answer still renders.

## US3 — Every provider that permits browser-direct, incl. native Claude (P2)

**Build**: refactor `SendAsync` into the three-axis `ProviderProfile` (request-builder × auth × SSE-parser); add `AnthropicRequestBuilder`/`AnthropicAuth`/`AnthropicSseParser`; mark `openai`/`azure` not-browser-direct; the OpenAI/Azure notice; the local-CORS doc.

**Verify**: With an Anthropic key, run Explain → Network shows the request to `api.anthropic.com` with `x-api-key` + `anthropic-dangerous-direct-browser-access`, response renders. Gemini → its OpenAI-compat endpoint returns a response. Ollama with the documented CORS set → local call succeeds; unset it → the failure names the fix. Select **OpenAI** → the UI shows "not available in the browser (CORS)" with the desktop/proxy pointer; no request is attempted and nothing is relayed.

## US4 — Index Analysis (P2)

**Build**: `IAiPromptService.IndexAnalysisAsync(...)` over `IndexAnalysisPrompt`; fifth `AiPanel` button.

**Verify**: Select a query, click **Index Analysis** → `CREATE INDEX` suggestions + rationale render with Accept/Discard. With the feature's mode = No schema, the request carries only the query.

## US5 — Ghost Text (P2)

**Build**: CM6 `StateField` + `WidgetType` + `Prec.highest` Tab/Esc keymap + debounced hook in `akml-editor.js`; `RequestGhostTextFromJs` in `EditorComponent`; `IAiGhostTextService` (cache/rate-limit/token-counter); settings toggle (default off).

**Verify**: Enable Ghost Text. Type at end of a line, pause ~350 ms → grey suggestion appears; Tab accepts; Escape dismisses. Type inside a comment/string/empty line → nothing fires. Type the same prefix twice → the second is cache-served (no second request in Network). Type continuously → requests are throttled and the in-flight one cancels; the session token counter increments.

## US6 — Chat persistence + export (P3)

**Build**: `chatHistory` store (DB_VERSION 1→2) + `IChatHistoryStore`; persist/restore/clear in `AiChatPanel`; markdown export via `akml-download.js`.

**Verify**: Hold a multi-turn chat, reload → the conversation is restored. Export → a `.md` downloads preserving turns/roles (a message with ``` stays intact). Clear → gone after reload; the schema cache and keys are untouched.

## US7 — Verify & audit (P3)

**Build/Run**: AiPanel bUnit (T134); US5 E2E on the mock-provider harness (T137); the privacy network-capture audit (T146); `M6-PARITY-AUDIT.md`; privacy-commitment + local-CORS docs.

**Verify**: `dotnet test` green (incl. the rehydrator round-trip, privacy-mode, Anthropic-wire, streaming-parser, ghost-text, chat-store, AiPanel tests). Run the opt-in E2E → add key → run feature → response renders → key never plaintext. Open the privacy audit → per-mode captures show the expected disclosure and no AKML host. Open `M6-PARITY-AUDIT.md` → paired web-vs-WPF screenshots, deltas table, dispositions (≤ 3 open).

## Build / test commands

```bash
# Web edition
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release

# Unit + component tests (rehydrator, privacy, wire, streaming, ghost text, chat, AiPanel)
dotnet test tests/AkmlSql.IntelliSense.Tests/AkmlSql.IntelliSense.Tests.csproj
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj

# Opt-in browser E2E (mock provider; excluded from the default run)
dotnet test tests/AkmlSql.Web.E2E.Tests/ --filter Category=BridgeE2E
```
