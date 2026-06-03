# Web edition — AI privacy commitment & threat model

**Spec 028 (M6) task T042 / FR-038.**

The AKML SQL web edition's AI features are **bring-your-own-key** and **browser-direct**. This
document is the privacy commitment the in-app "privacy mode" tooltip points to.

## The commitment

- **Your data goes only to the provider you configured.** AI requests are made directly from
  your browser to the provider's own endpoint (Anthropic, Gemini, or your local Ollama / LM
  Studio). There is **no AKML-operated server in the path** — AKML SQL has no AI proxy in the
  local edition.
- **The minimum data per your privacy mode is sent.** Each AI feature has a disclosure mode:
  - **Full schema** — table + column + foreign-key + description context.
  - **Schema names only** — table and column *names*; no data types, no foreign keys, no descriptions.
  - **No schema** — only your SQL / prompt leaves the browser; **no schema at all**.
  - **Fully local** — full schema, but only a local provider (Ollama / LM Studio) may be used.
  Modes are set globally and overridable per feature; the active mode is shown next to every AI control.
- **No AI prompts or responses are logged by default.** Diagnostics record only metadata
  (feature, latency, char counts), not prompt/response content.
- **Fully usable with local-only providers.** With Ollama or LM Studio, zero data leaves your
  machine.

## How the guarantees are enforced

- **"No schema" is enforced before the cache is touched** — the prompt service returns an empty
  schema context for that feature on every path (including retries/fallback). (FR-007.)
- **An origin allow-list** refuses any request to a non-allow-listed origin before it leaves the
  browser; only the documented provider origins are ever contacted, never an AKML host. (FR-016.)
- **Fully local is gated at the send path**, not just the provider picker — a per-feature
  fully-local override (or flipping the global mode while a cloud provider is active) refuses the
  call rather than sending schema to a cloud provider. (FR-004/FR-012.)
- **CORS-blocked providers (OpenAI, Azure) are not callable browser-direct** and are surfaced as
  not-available rather than silently failing or being relayed.

## Key-storage threat model (FR-002 reconciliation)

API keys are stored **encrypted at rest** in the browser using the Web Crypto API: a
**per-profile, non-extractable AES-GCM-256 key** (generated in the browser, never exportable)
wraps each provider key, bound to the provider id via AAD. The plaintext key is unwrapped only
for the duration of a single provider call.

**What this protects against:** the wrapped key in IndexedDB is useless if copied elsewhere (the
wrapping key is non-extractable and profile-bound); the key never appears in plaintext storage or
the DOM.

**What it does *not* add:** there is **no passphrase** ("something you know") factor. Anyone with
access to your unlocked browser profile can use the stored keys, because the wrapping key lives in
that profile. (M6 deliberately kept this shipped scheme rather than the originally-proposed
passphrase/PBKDF2 design — see spec 028 research Reconciliation 1.) If you need a passphrase
factor, treat the keys as you would any browser-stored credential and lock your OS session.

## Verifying it yourself

Open the browser DevTools → Network panel and run any AI feature. You will see exactly one
request, to the provider's own origin, and none to any AKML domain. Switch the feature's privacy
mode to "No schema" and confirm the outbound request body contains no table/column names. (This is
the basis of the spec-028 privacy network-capture audit, FR-036 / SC-003.)
