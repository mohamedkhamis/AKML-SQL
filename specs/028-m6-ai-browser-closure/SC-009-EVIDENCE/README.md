# SC-009 / T041 / T043 / T047 — Live interactive evidence

**Captured 2026-06-03** by driving the running web edition (`dotnet run` → `http://localhost:5000`)
with a real Chromium browser, against a **local mock AI provider** shaped as Ollama
(`http://127.0.0.1:11434/v1/chat/completions`, permissive CORS, canned buffered + SSE responses,
no real key). This is the interactive verification that specs 024/025/027 deferred — now runnable
because the session had a browser that could reach a localhost dev server.

> **Prerequisite finding (fixed in this branch):** the AI action panel + chat panel were built
> under 021 T131 / 028 but **wired into no reachable page** — `Editor.razor` had no AI affordance,
> no `/ai` or `/chat` route. They are now hosted in an editor-adjacent collapsible **AI dock**
> (`AI ▾` toolbar toggle → `[Actions] [Chat]` tabs). Every capture below is against that dock.

## Method

- Provider: Ollama (local), model `mock`, endpoint `http://localhost:11434/v1/chat/completions`,
  no API key (local providers are key-less). Marked active.
- Schema: a `SchemaSnapshot` identical to the one `PrivacyModeTests` proves
  (`dbo.Orders` + `dbo.Customers`, FK `FK_Orders_Customers`) seeded into IndexedDB
  (`schemaEntries`) so "Full schema" / "Names only" have real identifiers to disclose. The
  `PhaseB` MessagePack bytes were produced by the same builder the unit test uses.
- Editor SQL: `SELECT * FROM Orders WHERE CustomerId = 1`.
- For each privacy mode: set the global default in Settings → AI, run **Explain** from the dock,
  and read the exact outbound request body the mock received (`GET /__captures`) plus the
  browser's own network panel.

## T041 / SC-003 — Per-mode schema disclosure on the wire

The outbound system prompt's schema block, captured verbatim from the mock:

### Full schema
```
The user's database has the following schema:

Database: SalesDb

dbo.Orders(OrderId int PK, CustomerId int NOT NULL FK->dbo.Customers, Notes nvarchar(100) NOT NULL, Total decimal(18,2) NOT NULL)
  PK: OrderId
  FK: CustomerId -> dbo.Customers.CustomerId
  Desc: Orders table description
dbo.Customers(CustomerId int PK)
  PK: CustomerId

Relationships:
  dbo.Orders.CustomerId -> dbo.Customers.CustomerId
```
→ tables + columns + **types** (`nvarchar(100)`, `decimal(18,2)`) + **FKs** + **descriptions**.

### Schema names only
```
Database schema (names only):
- dbo.Orders (OrderId, CustomerId, Notes, Total)
- dbo.Customers (CustomerId)
```
→ table + column **names only**. No types (`nvarchar`/`decimal` absent), no FK name
(`FK_Orders_Customers` absent), no description (`Orders table description` absent).

### No schema
```
The user's database has the following schema:


```
→ **empty**. Automated leak-check over the captured system prompt: `Orders`, `Customers`,
`Notes`, `Total`, `CustomerId`, `nvarchar`, `decimal`, `FK_Orders_Customers`, `SalesDb` — **all
absent**. Only the SQL itself (in the user turn) leaves the browser.

This matches `tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs` exactly, now confirmed on the wire.

## SC-009 — No AKML-owned host

The browser network panel showed the **only** AI requests went to
`http://localhost:11434/v1/chat/completions` (3 POSTs, one per mode). Zero requests to
`api.anthropic.com`, `api.openai.com`, `*.openai.azure.com`, or any AKML-owned host. Request
headers carried `content-type: application/json` and **no `Authorization` header** (local
provider is key-less, browser-direct).

## FR-002 — API key never plaintext

Added an Anthropic provider with the recognizable fake key
`sk-ant-PLAINTEXT-NEVER-STORED-9k7x2`, then scanned **all 13 IndexedDB stores**:
- The plaintext key string was found in **zero** stores.
- The `aiKeys/anthropic` record holds only `Ciphertext` (AES-GCM, base64), `Iv` (base64), and
  `Aad` (base64 `akmlsql.aikey.anthropic` — the per-provider AAD binding). `HasKey: true`,
  no plaintext.
- The local `aiKeys/ollama` record has empty `Ciphertext`/`Iv`/`Aad` and `HasKey: false`.

## Streaming, chat, persistence

- **Explain** streamed token-by-token (mock SSE) into the result pane with Accept/Discard.
- **Chat**: a turn (`You` / `Assistant`) streamed in; **survived a full page reload** (restored
  from IndexedDB `chatHistory`), with Export / Clear available. (US2 + US6.)

## T047 — Ghost text (SC-006 cache-hit ≥ 30 %)

Enabled ghost text in Settings → AI, typed at end of line in the editor:
- A grey `.akml-ghost-text` widget appeared after the debounce, text
  `Customers c ON o.CustomerId = c.CustomerId [MOCK]`. (Screenshot `m6-ghost-text.png`.)
- **Tab accepted** it — committed into the document (`getText` confirms real text, widget gone).
- The ghost request carried `stream:false, max_tokens:150, temperature:0.2` (matches
  `IAiGhostTextService`).
- **Cache hit**: re-triggering the identical prefix returned the suggestion with **no new mock
  request** (capture count stayed 1) → cache-hit rate 1/2 = **50 % ≥ 30 %** (SC-006).

## Screenshots (T043, web half)

| File | Surface |
|---|---|
| `m6-ai-dock-no-provider.png` | AI dock, no provider configured (Actions/Chat tabs + "Add one in Settings") |
| `m6-ai-actions-full-schema.png` | 5 action buttons, "Full schema" privacy badges |
| `m6-ai-actions-no-schema.png` | 5 action buttons, "No schema" privacy badges |
| `m6-ai-chat.png` | Chat tab — streamed You/Assistant turns + Export/Clear |
| `m6-ghost-text.png` | Inline grey ghost-text suggestion in the editor |

## What is NOT closed here (honest disposition)

- **WPF-half parity screenshots**: no SSMS/VS host runs the WPF AI surface in this environment.
  Recorded as accepted-pending in `M6-PARITY-AUDIT.md`.
- **Real-provider first-token latency (PRD metric)**: requires a real Claude/Gemini key; mock
  latency is synthetic and meaningless for the metric. Recorded as accepted-pending.
