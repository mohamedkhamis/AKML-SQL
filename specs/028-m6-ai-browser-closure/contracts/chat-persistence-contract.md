# Contract: Chat history persistence + markdown export (US6)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 6 · **FRs**: FR-030 … FR-033

> Conscious M6-over-021 reversal: spec 021 made chat in-memory "per spec"; the M6 PRD scope table marks persistence + export **Yes**.

## New store (FR-030, FR-032)

- Add `public const string ChatHistory = "chatHistory";` to `JsIndexedDbAdapter.StoreNames`, add `'chatHistory'` to the `STORES` array in `akml-indexeddb.js`, and **bump `DB_VERSION` 1 → 2** (the upgrade transaction creates any missing stores).
- New `IChatHistoryStore` (singleton): `GetActiveAsync()`, `SaveAsync(ChatConversation)`, `ClearAsync(id)`. JSON-per-conversation.
- Independent of `schemaEntries` and `aiKeys` — clearing chat MUST NOT touch schema or keys, and vice-versa (FR-032).

## Model (FR-033)

- `ChatConversation { Id, Title, CreatedAt, UpdatedAt, Turns: List<ChatTurn> }`
- `ChatTurn { Role ("user"|"assistant"), Content, ProviderId, Timestamp }` — `ProviderId` records which provider produced each turn.

## Panel wiring (FR-030)

`AiChatPanel.razor`: persist each completed turn (user + assistant) via `IChatHistoryStore`; restore the active conversation on init; the existing Clear action also clears the store. Persistence is **local-only — no network egress** (FR-033).

## Export (FR-031)

A new export action builds Markdown from the turns (`## You` / `## Assistant` headings + content, in order) and downloads it via the existing `akml-download.js` `downloadBase64(filename, "text/markdown", base64)`. Content is code-fence-safe (a turn containing ``` does not break the document). Filename `chat-{yyyy-MM-dd-HHmm}.md`.

## Test contract

- `tests/AkmlSql.Web.Tests/Ai/ChatHistoryStoreTests.cs` — save→restore round-trip; clear removes and does not reappear; clearing chat leaves `schemaEntries`/`aiKeys` intact; the markdown builder preserves order/roles and escapes code fences; each turn carries its `ProviderId`.

## Out of scope

- Cloud-synced chat history (PRD open question 4 — SaaS concern).
