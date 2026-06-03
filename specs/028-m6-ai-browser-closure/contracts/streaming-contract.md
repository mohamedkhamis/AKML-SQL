# Contract: Streaming / typewriter responses (US2)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 2 · **FRs**: FR-008 … FR-012

## Transport (FR-008)

Blazor WASM on **`net10.0`** streams response bodies by default. The streaming path:

```csharp
using var req = BuildRequest(profile, body /* with "stream": true */);
profile.Auth.Apply(req, apiKey);
using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
resp.EnsureSuccess(...);                      // mid-stream errors → FR-011
using var stream = await resp.Content.ReadAsStreamAsync(ct);   // BrowserHttpReadStream (incremental)
using var reader = new StreamReader(stream);
await foreach (var token in profile.SseParser.ParseAsync(reader, ct)) yield return token;
```

- `HttpCompletionOption.ResponseHeadersRead` is **load-bearing** (default `ResponseContentRead` would buffer the whole body). Per-request — the singleton `HttpClient` needs **no DI change**.
- **No `SetBrowserResponseStreamingEnabled(true)` call** on net10 (default-on). *Target-framework-coupled assumption*: if ever retargeted to net8/9, that opt-in must be added — recorded here.
- `BrowserHttpReadStream` is async-only — async reads only.

## Per-provider SSE parsers (FR-008)

- `OpenAiSseParser` (openai-compat: gemini/ollama/lmstudio): split `data:` lines; ignore `data: [DONE]`; JSON-parse; yield `choices[0].delta.content`.
- `AnthropicSseParser`: parse `event:`/`data:` pairs; yield `delta.text` where `type=="content_block_delta"` && `delta.type=="text_delta"`; skip `ping`/non-text deltas; terminate on `message_stop` (no `[DONE]`).

## Surface controller + cancellation (FR-009, FR-010, FR-011)

- One `StreamingController` (data-model E6) per surface (`AiPanel`, `AiChatPanel`, ghost text); its `CancellationToken` is bound to the surface lifetime. Tokens render only to the owning surface (no cross-panel bleed, FR-009).
- Starting a new action / sending a new chat message / disposing the surface cancels the prior CTS, aborting the HTTP request and stopping rendering (FR-010).
- Mid-stream provider error: preserve already-rendered partial text + show the mapped error (existing 401/429/404/content-policy/network mapping) (FR-011).

## Buffered fallback (FR-012)

`IAiPromptService` keeps its `Task<string>` methods by awaiting the async-enumerable to completion; a provider/mode that doesn't stream renders a complete answer. No feature is broken by absent streaming.

## Test contract

- `tests/AkmlSql.Web.Tests/Ai/StreamingParserTests.cs` — feed recorded OpenAI + Anthropic SSE byte sequences; assert the yielded token sequence; assert `[DONE]`/`message_stop` termination; assert mid-stream error preserves partial text; assert a cancelled controller stops yielding and a second controller's tokens are isolated.
- First-token latency is recorded in the US7 run (PRD metric), not asserted as a hard gate.
