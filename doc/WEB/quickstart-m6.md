# Quickstart — Web edition M6 (User Story 5)

This walks through the BYO-key AI flow: enter a provider key, run Explain / Fix /
Optimize against selected SQL, observe the wrapped key never appears in plaintext
storage.

## Prerequisites

- M2 quickstart completed.
- A key for one of the supported providers: OpenAI, Anthropic, Gemini, Azure
  OpenAI, Ollama (local), or LM Studio (local).

## Add a provider

1. Open the editor, then **Settings → AI providers**.
2. Pick a provider in the dropdown (e.g. `openai`).
3. Display name: `OpenAI`. Model: `gpt-4o`. API key: paste your key.
4. Click **Save**. The key is wrapped with AES-GCM 256 via Web Crypto and
   persisted to IndexedDB. The plaintext is dropped from the form.
5. Toggle the **Active** radio button next to the new provider.

## Verify the key is wrapped

1. Open DevTools → Application → IndexedDB → AkmlSqlWeb → aiKeys.
2. The record for your providerId has `Ciphertext`, `Iv`, `Aad` fields — all
   base64. The plaintext key is not present anywhere.
3. The wrapping key itself lives in the `keyMaterial` store as a CryptoKey
   reference. Its bytes never appear in JS.

## Run an AI action

1. Open the editor, paste a SQL query, select it.
2. Open the **AI** panel (the floating side panel in Editor.razor).
3. Click **Explain**. The browser:
   - Unwraps the key for the duration of one fetch.
   - Sends a POST to `https://api.openai.com/v1/chat/completions` (allow-listed).
   - Drops the unwrapped key as soon as the fetch returns.
4. The response renders in the result pane with **Accept (copy)** / **Discard**.

## Verify origin allow-list

1. With DevTools open, network tab on.
2. Force the provider's endpoint to a different origin by editing the **Endpoint**
   field in Settings → AI to e.g. `https://attacker.example/v1/chat/completions`.
3. Run Explain — the request is **refused at the AiClientFactory** with an
   "Unauthorized origin" error, before any network request is issued.
4. Verify in the network tab: no request to attacker.example was made.

## Spec 028 (M6 closure) — now built

The spec-021 scaffold above is extended by spec 028 (this closure):

- **Privacy disclosure modes** — full schema / schema-names-only / no-schema / fully-local,
  set globally and per feature, shown next to every AI control; schema is resolved from the M5
  IndexedDB cache (no engine round-trip). See `ai-privacy-commitment.md`.
- **Streaming / typewriter** responses across the action panel and chat.
- **All browser-direct providers** — **native Claude** (Anthropic Messages API +
  `anthropic-dangerous-direct-browser-access`), **Gemini** (OpenAI-compatible endpoint),
  **Ollama / LM Studio** (local; see `ai-local-provider-cors.md`). **OpenAI and Azure are
  CORS-blocked browser-direct** and are surfaced as not-available (use the desktop edition or an
  OpenAI-compatible endpoint).
- **Index Analysis** as a fifth panel action.
- **Ghost Text** — inline grey-text completion (opt-in; enable in Settings → AI; Tab to accept).
- **Chat persistence + Markdown export** — conversations persist locally and export to `.md`.

## Remaining (interactive verification only)

The US5 end-to-end run, the privacy network-capture, the web-vs-WPF parity screenshots, and the
first-token latency measurement need a running app / both surfaces — tracked in
`specs/028-m6-ai-browser-closure/M6-PARITY-AUDIT.md`. The deterministic substrate is covered by
the unit/bUnit suite.

## Where to look in the code

| Concern | Path |
|---------|------|
| Key vault (Web Crypto) | `src/AkmlSql.Web/Services/IAiKeyVault.cs` |
| Active provider | `src/AkmlSql.Web/Services/IAiPreference.cs` |
| HTTP client + allow-list | `src/AkmlSql.Web/Services/IAiClientFactory.cs` |
| Prompt builder bridge | `src/AkmlSql.Web/Services/IAiPromptService.cs` |
| Settings page | `src/AkmlSql.Web/Pages/SettingsAi.razor` |
| AI side panel (5 actions, streamed) | `src/AkmlSql.Web/Shared/AiPanel.razor` |
| Privacy modes + schema-from-cache | `IAiFeatureSettings.cs`, `IAiSchemaContextProvider.cs`, `SchemaPhaseRehydrator.cs` |
| Provider wires (OpenAI/Anthropic) + streaming | `src/AkmlSql.Web/Services/IAiClientFactory.cs` |
| Ghost text | `wwwroot/js/akml-editor.js`, `IAiGhostTextService.cs`, `Shared/EditorComponent.razor` |
| Chat persistence + export | `src/AkmlSql.Web/Services/IChatHistoryStore.cs`, `Shared/AiChatPanel.razor` |
| Tests | `tests/AkmlSql.Web.Tests/Ai/` (63 AI unit/bUnit tests) + `tests/AkmlSql.IntelliSense.Tests/SchemaPhaseRehydratorTests.cs` |
