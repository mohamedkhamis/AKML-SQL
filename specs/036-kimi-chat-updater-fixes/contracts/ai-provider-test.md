# Contract: AI provider connection test (IPC 77 / 177)

**Status**: the message types, DTOs and engine handler **already exist and are registered**. No shell code has ever sent message 77. This contract documents what the shell must honour when it starts.

**Anchors**: `src/AkmlSql.Core/Ipc/RpcMessage.cs:223,263` · `src/AkmlSql.Core/Ipc/Messages/AiProviderTest{Request,Response}.cs` · `src/AkmlSql.Engine/Ai/AiProviderTestHandler.cs` · registered at `src/AkmlSql.Engine/EngineHandlerRegistry.cs:78`

Satisfies **FR-009**, and supplies the taxonomy for **FR-014**.

## Request — Shell → Engine, `MessageTypes.AiProviderTest = 77`

```
AiProviderTestRequest
  [Key(0)] Provider : string   required, canonical id (see kimi-provider.md)
  [Key(1)] ApiKey   : string   see "Key encoding" below
  [Key(2)] Endpoint : string?  optional; required for azure, defaulted for kimi/ollama
  [Key(3)] Model    : string   required
```

## Response — Engine → Shell, `MessageTypes.AiProviderTestResult = 177`

```
AiProviderTestResponse
  [Key(0)] Success         : bool
  [Key(1)] ModelName       : string?   echo of the tested model
  [Key(2)] ProviderVersion : string?   echo of the provider
  [Key(3)] ErrorMessage    : string?   set when Success is false
  [Key(4)] LatencyMs       : int
```

## Caller obligations

1. **Test the dialog's current values, not the saved settings.** The user must be able to verify a key before committing it. Read `Provider`/`Model`/`ApiKey`/`Endpoint` from the live controls on the AI Assistance page.
2. **Canonicalise the provider** through the alias table before sending. Sending the display string (`"Azure OpenAI"`) reproduces the R8 bug.
3. **Key encoding.** The engine runs the received value through `AiProviderFactory.KeyDecryptor` → `CredentialManager.Decrypt`, which passes unprefixed values through unchanged. So both a `dpapi:`-wrapped key and a plaintext key work. Send whatever the field holds; do **not** double-wrap an already-wrapped value.
4. **Timeout**: use `AiIpcTimeouts.ForAiRequestMs(settings)` — the same helper the chat path uses. The handler has no internal deadline beyond the provider SDK's.
5. **Never log the key.** Follow the handler's own precedent: it logs `provider`, `model` and `hasEndpoint` only (`AiProviderTestHandler.cs:54-55`).
6. **Engine not running** is a distinct outcome from a provider failure. Check `manager?.Client?.IsConnected` first and report "engine not connected", as the chat panel already does.
7. **Do not block the UI thread.** `async`/`await` throughout; the button shows a busy state and re-enables in a `finally`.

## Error-cause taxonomy (FR-014)

The handler already maps `InvalidOperationException` verbatim, `HttpRequestException` to HTTP text, and everything else to a generic message (`AiProviderTestHandler.cs:122-129`). FR-014 requires five distinguishable causes; extend that `switch` so each maps to a distinct, actionable message:

| Cause | Detected by | Message must say |
|---|---|---|
| Missing / invalid key | `RequireApiKey` throw, or provider 401/403 | that the key is missing or rejected, and which provider |
| Unknown / unavailable model | provider 404 on the model, or `RequireModelFamily` throw | the model name, the provider, and a valid example |
| Unreachable endpoint | `HttpRequestException` / DNS / connect failure | the endpoint URL that failed |
| Quota or rate limit | provider 429 | that the account is rate-limited or out of quota — **never** "AI is disabled" |
| Timeout | `OperationCanceledException` with the deadline elapsed | the timeout value and where to change it |

A raw provider payload (JSON body, stack trace) must never reach the user. Full detail goes to the log.

## Test coverage

- `tests/AkmlSql.Engine.Tests/` — one case per taxonomy row, asserting the mapped message; existing handler tests extended.
- `tests/AkmlSql.Shell.Shared.Tests/AiFailureMessageTests.cs` — the shell renders each cause distinctly.
- Live provider calls are **not** unit-tested; the taxonomy is asserted against synthesised exceptions.
