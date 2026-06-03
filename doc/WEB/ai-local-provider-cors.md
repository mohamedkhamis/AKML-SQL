# Web edition — AI with a local provider (Ollama / LM Studio): CORS setup

**Spec 028 (M6) task T024 / FR-017.**

The web edition calls your AI provider **directly from the browser** — there is no AKML
server in the path. For a *local* provider (Ollama or LM Studio) that means the browser, served
from the AKML SQL web origin, makes a cross-origin request to `http://localhost:11434`
(Ollama) or `http://localhost:1234` (LM Studio). The local server must be told to allow that
origin, or the browser blocks the request.

## Which providers work browser-direct

| Provider | Browser-direct | Notes |
|---|---|---|
| **Claude (Anthropic)** | ✅ | Uses the official `anthropic-dangerous-direct-browser-access` header (handled for you). |
| **Gemini** | ✅ | Via its OpenAI-compatible endpoint. |
| **Ollama** | ✅ *with CORS config* | See below. |
| **LM Studio** | ✅ *with CORS config* | See below. |
| **OpenAI** | ❌ | `api.openai.com` sends no CORS headers — not reachable from a browser. Use the desktop edition or an OpenAI-compatible endpoint. |
| **Azure OpenAI** | ❌ | Same as OpenAI. |

## Ollama

Set the `OLLAMA_ORIGINS` environment variable to allow the AKML SQL web origin, then restart
Ollama.

- **Allow a specific origin (recommended):**
  - Windows (PowerShell): `setx OLLAMA_ORIGINS "https://your-akml-web-origin"` then restart Ollama.
  - macOS/Linux: `export OLLAMA_ORIGINS="https://your-akml-web-origin"` (or set it in the Ollama service environment).
- **Allow all origins (convenient, less strict):** `OLLAMA_ORIGINS=*`.

Default endpoint: `http://localhost:11434` (configurable in Settings → AI).

## LM Studio

In LM Studio, start the **Local Server** and enable **CORS** in the server settings (the
"Enable CORS" toggle). Default endpoint: `http://localhost:1234/v1`.

## How AKML SQL surfaces a CORS failure

If a local call fails because CORS isn't configured, the AI panel shows an actionable error
(rather than failing silently). Apply the setting above and retry. The browser also enforces an
**origin allow-list** before any request leaves it — only the documented provider origins are
ever contacted, and never an AKML-owned host.

## Mixed content note

If the AKML SQL web app is served over **https**, some browsers block requests to a plain
`http://localhost` endpoint (mixed content). `localhost` is usually exempted, but if you hit
this, serve the web app over http for local development or use a provider that supports https.
