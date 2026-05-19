# Contract — AI key wrapping (M6)

This contract specifies the Web Crypto operations that protect AI provider keys at rest in the browser.

Cross-references: spec.md FR-029, FR-030, FR-032; clarification 2; data-model.md E8.

---

## Cryptographic primitives

| Item | Choice |
|------|--------|
| Wrap algorithm | AES-GCM 256-bit |
| IV size | 12 bytes (96 bits) per record; sourced from `crypto.getRandomValues` |
| AAD | UTF-8 of `"akmlsql.aikey." + providerId` |
| Wrapping key | A single `CryptoKey` per browser profile, `extractable: false`, `usages: ["encrypt", "decrypt"]` |
| Wrapping key origin | `crypto.subtle.generateKey({name: "AES-GCM", length: 256}, false, ["encrypt", "decrypt"])` on first use |
| Wrapping key persistence | Stored as a `CryptoKey` reference inside IndexedDB (object store `keyMaterial`, key `"primaryWrap"`). The underlying bytes never leave the browser key store. |

---

## Operations

### Set key (user enters API key in settings)

```javascript
// Inputs: providerId (string), apiKeyPlain (string)
// Outputs: AiProviderConfig record persisted with wrapped form

const wrapKey = await getOrCreateWrapKey();      // non-extractable, lazy-generated once per profile
const iv = crypto.getRandomValues(new Uint8Array(12));
const aad = new TextEncoder().encode("akmlsql.aikey." + providerId);
const ct = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv, additionalData: aad },
    wrapKey,
    new TextEncoder().encode(apiKeyPlain),
);

await indexedDB.put("aiProviders", {
    providerId,
    apiKeyWrapped: new Uint8Array(ct),
    apiKeyIv: iv,
    apiKeyAad: aad,
    // ...other fields per data-model.md E8
});

// apiKeyPlain is no longer referenced; the JS GC reclaims it.
```

### Unwrap key (just before an AI provider call)

```javascript
// Inputs: providerId
// Output: apiKeyPlain (string), held in a local variable only for the duration of the fetch

const rec = await indexedDB.get("aiProviders", providerId);
const wrapKey = await getOrCreateWrapKey();
const pt = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: rec.apiKeyIv, additionalData: rec.apiKeyAad },
    wrapKey,
    rec.apiKeyWrapped,
);
const apiKeyPlain = new TextDecoder().decode(pt);

try {
    await callProvider(providerId, apiKeyPlain, prompt);   // direct fetch, no AKML server
} finally {
    apiKeyPlain = null;   // tighten GC window
}
```

### Remove key

```javascript
// Inputs: providerId
// Output: record removed; bytes zeroised before delete to tighten residual exposure

const rec = await indexedDB.get("aiProviders", providerId);
if (rec) {
    new Uint8Array(rec.apiKeyWrapped).fill(0);
    new Uint8Array(rec.apiKeyIv).fill(0);
    await indexedDB.put("aiProviders", rec);   // overwrite first
    await indexedDB.delete("aiProviders", providerId);
}
```

---

## Invariants

| # | Invariant | Test surface |
|---|-----------|--------------|
| 1 | The wrapping key has `extractable === false`. | Inspect via Web Crypto introspection in the test. |
| 2 | A grep of IndexedDB after `setKey()` MUST NOT find the plaintext key value anywhere. | Test that dumps `aiProviders` and asserts the plain string absent. |
| 3 | Decryption MUST fail (DOMException `OperationError`) if any of `iv`, `aad`, or `wrappedToken` is altered post-storage. | Tamper-and-decrypt test per field. |
| 4 | No AI provider call may receive the plaintext key outside the function-local scope of `Unwrap key` above. | Static analysis + code review; runtime test that the key is null after the `try { ... } finally { ... }` returns. |
| 5 | The `additionalData` field MUST bind the wrapped value to `providerId` — copying a wrapped record to a different `providerId` MUST fail decryption. | Test that copies and asserts failure. |

---

## Provider-call contract

The browser MAY only call the AI provider's documented endpoint directly. The hostname MUST be on a per-provider allow-list compiled into the WASM bundle, e.g.:

| Provider | Allowed origin(s) |
|----------|-------------------|
| Claude | `https://api.anthropic.com` |
| OpenAI | `https://api.openai.com` |
| Gemini | `https://generativelanguage.googleapis.com` |
| Azure OpenAI | `https://*.openai.azure.com` (user-supplied subdomain) |
| Ollama (local) | `http://localhost:11434`, `http://127.0.0.1:11434` |
| LM Studio (local) | `http://localhost:1234`, `http://127.0.0.1:1234` |

A `fetch` to any other origin from AI code paths MUST be refused at the `AiClientFactory` layer, surfaced as a developer error. This is a defence-in-depth measure against a provider implementation accidentally proxying via a non-listed origin.

---

## Threat model

| Threat | Mitigation |
|--------|------------|
| Malicious browser extension reads `IndexedDB` | Plain key never present; wrapping key is non-extractable |
| Co-resident origin reads `IndexedDB` (same eTLD+1 different subdomain) | Same — wrapped form is useless without the wrap key, which is itself non-extractable |
| Compromised user account on the host | Out of scope — host compromise reads memory; spec accepts this risk under "BYO key" |
| Network interception of the AI request | The provider's own HTTPS endpoint protects in transit — this contract does not introduce additional plaintext |
| Server-side leak by the AI provider | Out of scope — user chose the provider |
| Cross-site-scripting (XSS) into the WASM origin | Mitigated by standard CSP on the served bundle — the installer's IIS config MUST set `Content-Security-Policy: default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self' <allow-list above>` |

---

## Test obligations (M6)

- `set → get → use → remove` happy path round-trip.
- Tamper tests per Invariant 3.
- Wrong-provider unwrap test per Invariant 5.
- Allow-list test: a mocked AI provider whose origin is not on the list MUST be refused at `AiClientFactory`.
- Memory-zeroisation: after `remove`, an IndexedDB dump returns no record and the underlying bytes (where observable from JS) are zero.
