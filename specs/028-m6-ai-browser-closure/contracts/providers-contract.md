# Contract: Providers, browser-direct — three-axis abstraction + CORS reality (US3)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 3 (RECONCILIATION, empirically verified) · **FRs**: FR-013 … FR-018

## CORS reality (plan-time empirical test, from `https://example.com`)

| Provider | Endpoint | Auth | Cross-origin fetch result | Browser-direct |
|---|---|---|---|---|
| **Anthropic** | `/v1/messages` | `x-api-key` + `anthropic-version` + `anthropic-dangerous-direct-browser-access: true` | 401 (dummy key) | ✅ **yes** |
| Anthropic (no header) | `/v1/messages` | — | `TypeError: Failed to fetch` | ❌ (confirms header is the enabler) |
| **Gemini** | `…/v1beta/openai/chat/completions` | `Authorization: Bearer` | 400 | ✅ **yes** |
| **OpenAI** | `/v1/chat/completions` | `Bearer` | `TypeError: Failed to fetch` | ❌ **CORS-blocked** |
| **Azure** | `/openai/deployments/{d}/…` | `api-key` (not Bearer) | unverifiable w/o a resource; behaves like OpenAI | ❌ assume blocked |
| **Ollama / LM Studio** | local `/v1/…` | none/`Bearer` | works with server CORS (`OLLAMA_ORIGINS`) | ✅ with config |

## Three-axis provider abstraction (FR-013, FR-014) — `ProviderProfile`

Refactor the OpenAI-only `AiClientFactory.SendAsync` into a `ProviderProfile` per `providerId` (data-model E4):

- **`IAiRequestBuilder`** — `OpenAiRequestBuilder` (gemini/ollama/lmstudio): `{model, messages:[system,...history,user], max_tokens, temperature, stream}`. `AnthropicRequestBuilder`: `{model, system (top-level), messages, max_tokens (REQUIRED), stream}`.
- **`IAuthApplier`** — `BearerAuth` (gemini/ollama/lmstudio) · `AnthropicAuth` (`x-api-key`, `anthropic-version: 2023-06-01`, `anthropic-dangerous-direct-browser-access: true`) · `AzureApiKeyAuth` (`api-key`). The shipped hardcoded `Bearer` is replaced.
- **`ISseDeltaParser`** — `OpenAiSseParser` · `AnthropicSseParser` (streaming-contract).

## Native Claude (FR-014)

Verified to clear CORS at plan time. Request: `POST https://api.anthropic.com/v1/messages` with the three headers above + the Messages body; streaming uses the named-event SSE (`content_block_delta`/`delta.text`, end on `message_stop`).

## OpenAI / Azure — documented out (FR-013, FR-018; Reconciliation)

`BrowserDirectCapable(providerId)` is false for `openai`/`azure`. Selecting them shows a `CapabilityNotice`-style "not available in the browser (CORS) — use the desktop edition or an OpenAI-compatible endpoint" message (US3 scenario 5). They are **never** routed through an AKML host or an engine relay (PRD §10). A profile still exists for them so a future change (provider adds CORS, or a user proxy) is a one-line capability flip.

## Origin allow-list + local CORS (FR-016, FR-017)

The shipped allow-list still refuses any non-allow-listed origin before the request leaves the browser; it covers `api.anthropic.com`. Local providers (Ollama/LM Studio) require the documented CORS env/setting (`OLLAMA_ORIGINS`, LM Studio toggle); a CORS/connection failure surfaces an actionable message naming the setting (FR-017). A browser-unreachable provider surfaces an explanation, never a silent reroute (FR-018).

## Test contract

- `tests/AkmlSql.Web.Tests/Ai/AnthropicWireTests.cs` — `AnthropicRequestBuilder` emits the correct headers + body (system top-level, `max_tokens` present); `AnthropicSseParser` extracts `text_delta` tokens and terminates on `message_stop`.
- Existing allow-list tests (18) retained; add cases asserting `openai`/`azure` resolve to "not browser-direct" (notice path) and `anthropic` origin is allow-listed.
- Real-provider reachability is exercised in the US7 E2E against the mock-provider harness (and the plan-time live test is the recorded primary-source evidence).

## Out of scope (named follow-up)

- **OpenAI / Azure browser-direct** — CORS-blocked; revisit only if those APIs add browser CORS, or the user uses a local OpenAI-compatible proxy / the desktop edition. Native Gemini wire is unneeded.
