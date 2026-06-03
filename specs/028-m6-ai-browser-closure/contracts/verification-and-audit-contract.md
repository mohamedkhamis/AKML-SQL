# Contract: Verification & audit (US7)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 7 (+ Reconciliation 1) · **FRs**: FR-034 … FR-039

## Mock-provider harness

A test fixture that intercepts the allow-listed origins and returns canned (and canned-streaming) responses, so the E2E runs without real keys/network and without hitting real providers. Reused by both the bUnit and Playwright suites.

## AiPanel component tests (FR-035) — the deferred T134

`tests/AkmlSql.Web.Tests/Ai/AiPanelTests.cs` (bUnit): the five actions wire to the prompt service; the no-provider state shows the "add one in Settings" prompt; provider-error renders the mapped message; the **API key never appears in the DOM**; the privacy-mode badge renders.

## US5 E2E (FR-034) — the deferred T137

`tests/AkmlSql.Web.E2E.Tests/UserStory5AiTests.cs` (Playwright, opt-in trait, excluded from the default run): add a key → run a feature → response renders (streamed); assert the key never appears in plaintext storage or the DOM; drive Ghost Text (type → grey text → Tab accept) against the mock provider.

## Privacy network-capture audit (FR-036 / SC-009) — the deferred T146

Per privacy mode (`FullSchema`/`SchemaNamesOnly`/`NoSchema`), capture an outbound request and assert the expected schema disclosure (present / names-only / **none**); assert **no request in the AI path targets an AKML-owned host**. Evidence recorded in the parity-audit doc (or `SC-009-EVIDENCE/`). This is the hard evidence for the privacy DoD.

## Feature-parity audit (FR-037)

`specs/028-m6-ai-browser-closure/M6-PARITY-AUDIT.md` (shape of `M5-PARITY-AUDIT.md`): paired web-vs-WPF screenshots for each AI surface (panel actions incl. Index Analysis, chat, settings, privacy-mode badge, ghost text); deltas table (element / WPF / web / disposition); closed vs accepted-with-reason; host OS/theme/DPI metadata; ≤ 3 deltas open (SC-009).

## Docs (FR-038, FR-017)

- `doc/WEB/ai-privacy-commitment.md` (FR-038): data goes only to the user-configured provider, minimum per the privacy mode, never through any AKML host, fully usable with local providers; includes the FR-002 key-storage tradeoff note (non-extractable key; no passphrase factor — Reconciliation 1). The in-app privacy-mode tooltip reflects this.
- `doc/WEB/ai-local-provider-cors.md` (FR-017): exact `OLLAMA_ORIGINS` / LM Studio CORS setup (may be a section of `quickstart-m6.md`).
- Update `doc/WEB/quickstart-m6.md` (remove now-closed "what's deferred" caveats) and `doc/progress.md` (spec-028 closure summary).

## DoD reconciliation (FR-039)

Every M6 PRD §12 Definition-of-Done checkbox closes against a shipped feature (reality table) or an FR — with the **"passphrase-protected"** item revised per FR-002 (non-extractable key) and **"all five providers"** revised per FR-013/Reconciliation 3 (browser-direct = the CORS-permitted set; OpenAI/Azure documented out).

## Test contract

The suites above ARE the contract. They run developer-side (mock provider + interactive workstation for screenshots), matching specs 024/025/027.

## Out of scope

- Automated pixel-diff parity (human-reviewed screenshots, per project convention).
