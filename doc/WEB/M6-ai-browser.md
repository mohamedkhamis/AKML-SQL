# M6 — AI Assistance in the Browser (Bring Your Own Key)

**Status**: Draft
**Phase**: M6 (closes the feature parity gap)
**Estimated effort**: 2 weeks
**Branch prefix**: `m6-ai-browser`
**Depends on**: M5 shipped

---

## 1. Executive summary

M5 closed the offline parity gap. M6 closes the AI parity gap. The browser surface gains:

1. **Text-to-SQL** — natural language to SQL
2. **Explain** — pick SQL, get a plain-English explanation
3. **Fix** — auto-correct errors and anti-patterns
4. **Optimize** — rewrite for performance
5. **Index Analysis** — suggest indexes for a query
6. **Chat** — conversational AI panel for free-form questions
7. **Ghost Text** — inline grey-text completion as you type

All five AI providers from the WPF surface are supported: Claude, GPT-4o, Gemini, Azure OpenAI, local Ollama / LM Studio. The four privacy modes (full schema, schema names only, no schema, fully local) are preserved.

**Critical decision baked in: AI calls go directly from the browser to the provider, not via the engine.** This means:

- The provider API key lives in the browser (IndexedDB), encrypted with a user-chosen passphrase
- The user pays for AI usage directly on their own provider account — no proxying, no rate limiting on AKML SQL's side, no privacy concern with the AI traffic going through an intermediate process
- Local providers (Ollama, LM Studio) work the same way — browser talks to `http://localhost:11434/...` directly

Schema-aware prompting still uses the IndexedDB schema cache from M5 — the browser already has the schema; it doesn't need the engine for AI features.

---

## 2. Why now

M5 made the browser self-sufficient for everything except AI. M6 is the closing piece. Doing it last means the AI integration consumes a fully-formed editor + schema cache + connection state — no architectural reshaping needed; just a feature addition.

---

## 3. Current state

End of M5:

- WPF surface has full AI feature set (Text-to-SQL, Explain, Fix, Optimize, Index Analysis, Chat, Ghost Text)
- Engine has AI handlers behind `AiHandlerBase` (consolidated in M0.5)
- Browser has nothing AI-related

---

## 4. Proposed architecture

### 4.1 Architectural pivot from the WPF surface

In the WPF surface, AI requests go shell → engine → AI provider. The engine builds the prompt (it has the schema), calls the provider, returns the response.

In the browser, **the engine is bypassed.** The browser builds the prompt itself (it has the schema from M5's IndexedDB cache) and calls the AI provider directly. Three reasons:

1. **Privacy** — engine logs AI traffic by default; browser-direct lets the user opt out completely
2. **Cost transparency** — user sees their provider's usage dashboard reflect their actual AKML usage
3. **Local provider support** — Ollama / LM Studio run on the user's machine; engine-as-proxy adds latency for no benefit

This means the prompt-construction logic from `AiHandlerBase` needs to be available in the browser. M6 extracts it.

### 4.2 Project additions

```
src/
  AkmlSql.AI/                       ← NEW; netstandard2.0
    Prompts/                         ← prompt templates (TextToSql, Explain, Fix, Optimize, Index)
    Providers/                       ← provider clients
      ClaudeClient.cs
      OpenAIClient.cs
      GeminiClient.cs
      AzureOpenAIClient.cs
      OllamaClient.cs
      LmStudioClient.cs
    PromptBuilder.cs                 ← schema-aware prompt construction
    PrivacyMode.cs                   ← schema visibility filter
    AkmlSql.AI.csproj
```

`AkmlSql.Engine` references `AkmlSql.AI` (replaces the inline boilerplate). `AkmlSql.Web` references `AkmlSql.AI` (new).

This is the third instance of the M0 pattern: shared logic, surface-specific adapters.

### 4.3 API key storage in the browser

```
IndexedDB → AkmlSqlSecrets
└── object store: providers
    └── { providerId, encryptedApiKey, kdfSalt, kdfIterations, encAlgorithm }
```

API keys are encrypted with a passphrase via the Web Crypto API:

- PBKDF2-SHA256 with 600,000 iterations (current OWASP recommendation as of 2025) derives a key from the passphrase
- AES-GCM-256 encrypts the API key
- Salt and IV stored alongside the ciphertext

The passphrase is **not** stored. The browser prompts for it once per session (or once per tab if the user clears session storage). After unlocking, the decrypted key lives only in memory.

Local providers (Ollama, LM Studio) typically don't need an API key — store the URL only, no encryption needed.

### 4.4 Privacy modes

Same four modes as the WPF surface, implemented in `PromptBuilder`:

| Mode | Schema in prompt | Use case |
|------|-------------------|----------|
| **Full schema** | Table + column + FK + comments | Best quality; trust the provider |
| **Schema names only** | Table + column names; no data types, no FKs | Reduced disclosure |
| **No schema** | Selected SQL only | Strong privacy; AI works on syntax alone |
| **Fully local** | Forces a local provider; full schema available | Privacy without quality loss |

Mode is a per-feature setting (text-to-sql can be "full schema" while chat is "no schema") and a global default. The active mode is shown next to every AI feature button.

### 4.5 Streaming

All providers support token streaming. Browser uses `fetch` with `ReadableStream` and renders tokens as they arrive. Same UX as the WPF surface (typewriter effect).

### 4.6 Ghost Text

Ghost Text is the trickiest feature because it fires on keystrokes. Design:

- Debounce: 350 ms after last keystroke
- Trigger conditions: cursor at end of line OR after a keyword
- Suppression conditions: cursor in a comment, cursor in a string literal, line is empty
- Caching: same prompt + same prefix → cached response (avoid double-charging)
- Cancellation: typing while a request is in flight cancels the previous request

This pattern is already established in the engine; M6 ports it.

---

## 5. Feature scope

| Feature | In M6 |
|---------|-------|
| Text-to-SQL | Yes |
| Explain | Yes |
| Fix | Yes |
| Optimize | Yes |
| Index Analysis | Yes |
| Chat panel | Yes |
| Ghost Text | Yes |
| Streaming responses | Yes |
| All 5 providers | Yes |
| All 4 privacy modes | Yes |
| Encrypted key storage | Yes |
| Per-feature provider override | Yes |
| Usage cost estimate display | **No** — separate feature |
| Multi-model output comparison | **No** — separate feature |
| Conversation history persistence | Yes (IndexedDB) |
| Conversation export | Yes (markdown download) |

---

## 6. Milestones

### M6.1 — Extract AkmlSql.AI (week 1, days 1–2)

Move `AiHandlerBase` into `AkmlSql.AI`. Move all 6 provider clients. Engine consumes the library. Engine tests pass.

### M6.2 — Key storage + unlock UX (day 3)

IndexedDB schema. Encryption helpers. Passphrase prompt modal. "Forgot passphrase" path = re-enter API keys.

### M6.3 — Text-to-SQL + Chat (days 4–5)

The two most-used features land first. Streaming. Provider picker. Privacy mode display. Cost-of-keystrokes test: typing a 50-word natural language prompt produces SQL in < 3 seconds (provider-dependent; record actual).

### M6.4 — Explain / Fix / Optimize / Index Analysis (week 2, days 1–3)

Four similar features sharing the "pick SQL → submit → render markdown response" pattern. Each gets a distinct icon in the toolbar.

### M6.5 — Ghost Text (week 2, day 4)

Debouncing, cancellation, caching. The trickiest piece. Tested with 100 keystrokes/minute typing speed.

### M6.6 — Polish, docs, privacy review (week 2, day 5)

Privacy mode audit: prove that "no schema" mode never sends schema. Audit network requests for each mode. Document the threat model: "your data goes to your provider — we are not in the path."

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Provider API CORS blocks browser-direct calls | Medium | Very high | Audit each provider's CORS policy before M6.1; Claude, OpenAI, Gemini all support browser-direct as of mid-2026 (verify); local providers may need a CORS-proxy fallback |
| Encryption implementation bug exposes keys | Medium | Very high | Use Web Crypto API exclusively (no DIY crypto); third-party review of the key storage code before merging M6.2 |
| User loses passphrase → loses API keys | High | Low | This is acceptable; the key is replaceable. Document clearly |
| Ghost Text spam burns through provider tokens | High | Medium | Debounce + cache; per-session token-spent counter visible in UI; user-configurable rate limit |
| Local Ollama / LM Studio CORS rejects browser | High | High | Document the CORS env var to set (`OLLAMA_ORIGINS=*`); installer prompt to configure if local provider chosen |
| Schema in prompt exceeds provider context window | Medium | Medium | Already handled in WPF surface via prompt size truncation; port the same logic to `PromptBuilder` |
| Streaming responses leak across feature boundaries (Chat tokens appear in Explain panel) | Low | Medium | One streaming controller per panel; cancellation token bound to the panel's lifetime |

---

## 8. Privacy commitment (documented)

The web edition's AI features:

- Send user data **only to the provider the user configured**
- Send the **minimum data per the user's privacy mode**
- Do **not** route AI traffic through any AKML SQL server (because there is no such server in the local edition)
- Do **not** log AI prompts or responses by default; opt-in to logging
- Are **fully usable with local-only providers** (Ollama, LM Studio) for users who want zero external data flow

This commitment goes in the privacy policy section of the docs and in the in-app "Privacy mode" tooltip.

---

## 9. Success metrics

- All 7 AI features work in the browser with all 5 providers
- Text-to-SQL response begins streaming within 1500 ms on Claude / GPT-4o (measure baseline; depends on provider)
- Encrypted key storage passes a security review
- Privacy mode "no schema" verified by network capture — no schema in any outgoing request
- Local provider (Ollama) works end-to-end with documented CORS configuration
- Ghost Text cache hit rate ≥ 30% during sustained typing
- Feature parity audit vs. WPF surface: ≥ 95% of WPF AI features work identically

---

## 10. Out of scope

- AKML-hosted AI proxy (would change the privacy model)
- Multi-model output comparison ("show me both Claude and GPT versions")
- Cost-per-feature dashboard
- Fine-tuned models / RAG against user docs
- Voice input
- Image input (screenshot of a query plan, etc.)
- Workflow chains ("write SQL, then explain it, then optimize")

All of these are good ideas; none are M6.

---

## 11. Open questions

1. **Should the engine's AI handlers be deprecated once browser AI works?** — No; the SSMS/VS plugins still use them. Engine and browser AI paths coexist
2. **Default passphrase prompt — every session, or remember within session?** — Remember within session (until tab close); never persist
3. **What's the right rate limit default for Ghost Text?** — Aim for ≤ 1 request per 3 seconds of active typing; user-configurable
4. **Chat history persistence — local only or syncable?** — Local only in M6; sync is a SaaS concern

---

## 12. Definition of done

- [ ] `AkmlSql.AI` library exists; engine and browser both consume it
- [ ] All 7 features work in the browser
- [ ] All 5 providers work
- [ ] All 4 privacy modes work
- [ ] Key storage uses Web Crypto API; passphrase-protected
- [ ] Ghost Text works with debounce + cache + cancellation
- [ ] Privacy mode audit: network captures confirm no leakage
- [ ] Local provider (Ollama) documented with CORS config
- [ ] Privacy commitment doc written
- [ ] Feature parity audit screenshots
- [ ] Branch `m6-ai-browser` merged to master via PR
- [ ] **Web edition is feature-complete for the local-edition track**
