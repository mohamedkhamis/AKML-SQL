# Contract: Kimi (Moonshot) provider

Satisfies **FR-006, FR-007, FR-010, FR-011, FR-012, FR-013**. Desktop hosts only (user decision, 2026-09-02).

**Anchors**: `src/AkmlSql.AI/Providers/AiProviderFactory.cs` · `src/AkmlSql.Core/Config/AiModelFamily.cs` · `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs:20-44,225-282`

## Identity

| Aspect | Value |
|---|---|
| Canonical id (stored in `config.json`) | `kimi` |
| Display name (Options list) | `Kimi (Moonshot)` |
| Wire protocol | OpenAI-compatible chat completions |
| Default endpoint | `https://api.moonshot.ai/v1` |
| Default model | `kimi-latest` |
| Requires API key | Yes |
| Requires endpoint | No — defaulted, user-overridable |

**Regional note**: the mainland-China service is reached by overriding the endpoint to `https://api.moonshot.cn/v1`. Both accept the same key format; the accounts are separate. The Options page's endpoint field is the only mechanism — no region dropdown (spec Assumptions).

**Why a rolling model alias**: `AiModelFamily.DefaultModelFor` already uses `gemini-flash-latest` with the comment "pinned Gemini names rot" (`AiModelFamily.cs:57-63`). `kimi-latest` follows that precedent. Pinned names such as `kimi-k2-*` remain valid user input — they are simply not the default.

## Factory case

Add to the switch in `AiProviderFactory.Create`, **before** delegating:

```
"kimi" => CreateKimiClient(apiKey, settings.Model, settings.Endpoint)
```

`CreateKimiClient` must:

1. `RequireApiKey(apiKey, "Kimi")`
2. `RequireModel(model, "Kimi")`
3. `RequireModelFamily(model, "kimi", "Kimi")`
4. delegate to `CreateOpenAiClient(apiKey, model, endpoint ?? "https://api.moonshot.ai/v1")`

**It must not fall through to the generic `custom` case.** That branch deliberately skips `RequireModelFamily` because "custom endpoints legitimately serve foreign model ids". Kimi is a named first-party provider, so FR-012 requires the guard — hence its own case.

## Family detection

Extend `AiModelFamily.Detect` to return `"kimi"` for a model name that, after the existing `models/` prefix strip and lowercasing, starts with:

- `kimi`
- `moonshot`

Everything else keeps today's behaviour, including returning `null` for unrecognised names — local models, fine-tunes and Azure deployment names must never be second-guessed (`AiModelFamily.cs:16-18`).

Extend `DefaultModelFor` with `case "kimi": return "kimi-latest";`.

Extend `FamilyDisplayName` in the factory with `"kimi" => "Moonshot (Kimi)"` so FR-012's message names the vendor in prose.

**Bidirectional guard** (FR-012): `kimi-latest` under provider `openai` must be refused just as `gpt-4o` under provider `kimi` is. Both directions fall out of `Detect` returning a non-null family that differs from the expected one — no extra code, but both directions need a test.

## Provider id normalisation (FR-013)

The Options page currently saves display-ish strings, two of which the factory rejects (R8). Introduce one normalisation point used by both the page and the factory:

| Accepted input (case-insensitive) | Canonical |
|---|---|
| `anthropic` | `anthropic` |
| `openai` | `openai` |
| `azure`, `azureopenai`, `azure openai` | `azure` |
| `gemini` | `gemini` |
| `kimi`, `moonshot`, `kimi (moonshot)` | `kimi` |
| `ollama` | `ollama` |
| `lmstudio`, `lm studio` | `lmstudio` |
| `custom` | `custom` |
| `""`, null, whitespace | `""` (none) |

Rules:

- `Save` writes the **canonical id**, never the display name.
- `Load` normalises before matching, so configs written by earlier builds (`AzureOpenAI`, `LMStudio`) keep working with no migration.
- The factory normalises before its switch, so an unnormalised value from any source still resolves.
- The "Unknown AI provider" message lists the canonical ids.

## Options page

The provider list becomes:

```
(None), Anthropic, OpenAI, Azure OpenAI, Gemini, Kimi (Moonshot), Ollama, LM Studio, Custom
```

Inserting Kimi at index 5 shifts Ollama/LM Studio/Custom by one. **The index→string switches in `Load` and `Save` are positional** (`AiAssistancePage.cs:225-235,270-279`) — both must move together, and the round-trip test must cover every entry, not just the new one. This positional coupling is exactly how the Azure/LM Studio mismatch survived; prefer keying off the canonical id rather than the index if the change stays small.

Also on this page (FR-009): a **Test connection** button beside the API key field, wired per `contracts/ai-provider-test.md`.

## Failure message mapping

Inherits the taxonomy in `contracts/ai-provider-test.md`. Kimi-specific notes:

- A 401 from Moonshot means the key is wrong **or** the key belongs to the other region's service. The message should mention the endpoint alongside the key, because a `.cn` key against the `.ai` endpoint is the likely first-run mistake.
- A 429 must read as quota/rate-limit, never "AI is disabled" (FR-014, and an explicit spec edge case).

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| Factory creates a client for `kimi` with defaults | `tests/AkmlSql.AI.Tests/` | no throw, endpoint applied |
| Factory rejects `kimi` with no key / no model | `tests/AkmlSql.AI.Tests/` | `InvalidOperationException`, message names Kimi |
| Family detection, both directions | `tests/AkmlSql.AI.Tests/ProviderModelMismatchTests.cs` | `kimi-*`/`moonshot-*` → `kimi`; `gpt-4o` under kimi refused; `kimi-latest` under openai refused |
| Unrecognised model still returns null | `tests/AkmlSql.AI.Tests/ProviderModelMismatchTests.cs` | local/fine-tune names untouched |
| Alias normalisation, every row | `tests/AkmlSql.Core.Tests/` | table above |
| Options round-trip, **every** provider | `tests/AkmlSql.Shell.Shared.Tests/AiProviderModelAutofillTests.cs` | select → save → load → same selection, for all 8 |
| Autofill on provider switch | `tests/AkmlSql.Shell.Shared.Tests/AiProviderModelAutofillTests.cs` | empty/foreign model replaced, custom name preserved |
