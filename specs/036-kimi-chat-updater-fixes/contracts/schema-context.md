# Contract: schema context for AI requests

Satisfies **FR-021 – FR-032**. This is the contract that makes the assistant actually know the database.

**Anchors**: `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs:236` · `src/AkmlSql.Shell.Shared/Refactoring/RefactorCommandHelper.cs:45-91` · `src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs:36-43` · `src/AkmlSql.AI/Context/SchemaContextBuilder.cs` · `src/AkmlSql.AI/Context/SchemaContextFormatter.cs`

---

## Part 1 — Session binding (shell side)

**The rule**: a schema-aware AI request carries a session id that came from an editor buffer. It is never generated.

### Resolution

```
resolve():
    ctx := RefactorCommandHelper.TryGetActiveEditor()
    if ctx is null                       -> UNBOUND
    if ctx has no real AkmlSqlSessionId  -> UNBOUND     // see note below
    else                                 -> BOUND(ctx.SessionId)
```

**Note on the existing fallback**: `TryGetActiveEditor` substitutes `Guid.NewGuid().ToString("N")` when the buffer property is absent (`RefactorCommandHelper.cs:70-74`), because pure-text refactors work without a live session. That behaviour must stay for refactoring, but AI callers must be able to tell the two apart — expose the real/fabricated distinction (e.g. a `HasRealSession` flag on the returned context) rather than changing the fallback.

### Where each caller resolves

| Caller | Today | Required |
|---|---|---|
| `AiChatPanel.SendMessageAsync` | `Guid.NewGuid()` (`:236`) | resolve at send time, every message |
| `GhostTextAdornment` | `Guid.NewGuid()` (`:134`) | resolve from its own text view's buffer — it has one |
| `TextToSqlCommand` | `Guid.NewGuid()` (`:236`, `:137`) | resolve from the active editor |
| `AiExplain/Fix/Optimize/IndexAnalysis` commands | pass a `sessionId` variable | **audit each** — a variable is not proof it holds a real id |

### Unbound behaviour (FR-028)

The request is not sent. The panel states plainly that there is no database connection and how to get one. It must not send a request that will silently produce an empty-schema answer.

### Rebinding (FR-027)

The chat header shows `server.database` for the current binding and updates when the active window or its database changes. `AiChatPanel.SetDatabaseContext(string)` already exists and is called by nothing (`AiChatPanel.cs:185-192`) — that is the hook. The current header shows only the provider name, which is not the required information.

### Loading (FR-029)

When the binding is live but the cache is not ready, tell the user the schema is still loading. Read the same signal the editor margin polls (`MessageTypes.SchemaStatusRequest = 80`, consumed by `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`). Do not invent a second progress mechanism.

---

## Part 2 — Context assembly (engine side)

**The rule**: relevance may only *promote detail*. It may never *remove inventory*.

### Algorithm

```
inventory := dbCache.GetAllObjects()                    // level 1: schema, name, type, row count
if inventory is empty          -> empty context, DatabaseName set
named    := objects whose name matches a prompt token   // existing IsObjectRelevant
expanded := named ∪ FK-1-hop(named)                     // existing ExpandFkConnections
promote  := expanded                                    // level 3: columns, PK, indexes, FKs

budget := settings.Ai.SchemaContextMaxObjects           // default 500
if |inventory| > budget:
    keep all of promote
    fill remaining budget from inventory (stable order)
    Truncated        := true
    TotalObjectCount := |inventory|

render:
    level-1 line for every object in the kept inventory
    level-3 block for every object in promote
    truncation notice when Truncated
```

### What changes from today

| Today | Required |
|---|---|
| `FilterByRelevance` returns `matched` and falls back to all **only at zero matches** (`SchemaContextBuilder.cs:138`) | Inventory is always complete up to the budget; matching only promotes |
| Noise tokens (`my`, `do`, `in`) incidentally match and suppress the fallback | Impossible — there is no fallback to suppress |
| `compressionLevel: 2` hardcoded in seven handlers | Per-feature level (see table below) |
| `expandedSet.Take(maxObjects)` silently truncates (`:87-89`) | Truncation is explicit and signalled |
| Empty context and no-connection render identically | Two distinguishable messages (FR-028) |

### Detail level per feature (FR-031)

| Feature | Level | Reason |
|---|---|---|
| Chat | 3 | Keys and relationships needed for correct joins |
| Text-to-SQL | 3 | Generated SQL must reference real columns (SC-004) |
| Optimize, Index analysis | 3 | Index advice needs existing indexes and PKs |
| Explain, Fix | 3 | Column types drive both |
| Ghost text | 1 | Latency-critical; names suffice |

### Truncation signal (FR-026)

When `Truncated` is true the rendered prompt must carry a line the model can quote, e.g.:

```
NOTE: showing 500 of 1,842 objects in this database. The inventory below is incomplete.
```

The panel shows the user an equivalent note. Silent truncation is a defect.

### Privacy (FR-030)

Unchanged: `PrivacyTransformer.Transform` runs *after* assembly. When `transformation.IdentifierMap` is non-empty, real names cannot appear in the answer by design — the panel must say so and name the `privacyMode` setting rather than letting the user think the assistant is confused.

### Boundaries (FR-032)

Metadata only. No row data reaches any provider. The context is built entirely from the schema cache, which is populated from `sys.*` views — this is already true and must stay true.

---

## Performance

Assembly must add **< 200 ms** to a request on a 500-object database. Level-1 rendering of the full inventory is string work over an in-memory `ConcurrentDictionary`; the expensive part is level-3 detail, which is bounded by `promote`, not by the inventory.

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| General prompt yields full inventory | `tests/AkmlSql.AI.Tests/` | "what tables do I have" → every object present (FR-024) |
| Noise tokens do not shrink the inventory | `tests/AkmlSql.AI.Tests/` | a prompt matching one object incidentally still yields all (R6, FR-025) |
| Named object promoted to level 3 | `tests/AkmlSql.AI.Tests/` | columns, PK, FK lines present for it (FR-023) |
| FK 1-hop neighbours promoted | `tests/AkmlSql.AI.Tests/` | relationship rendered on both sides |
| Budget exceeded sets truncation | `tests/AkmlSql.AI.Tests/` | `Truncated`, `TotalObjectCount`, notice in text (FR-026) |
| Unbound renders distinctly from empty database | `tests/AkmlSql.AI.Tests/` | two different strings (FR-028) |
| Handler passes the real session id through | `tests/AkmlSql.Engine.Tests/` | connected session resolves to its cache (FR-021) |
| Panel refuses to send when unbound | `tests/AkmlSql.Shell.Shared.Tests/` | no request issued; message shown |
| Header reflects the bound database | `tests/AkmlSql.Shell.Shared.Tests/` | `SetDatabaseContext` drives the header (FR-027) |
| Ghost text stays at level 1 | `tests/AkmlSql.AI.Tests/` | latency path unchanged |
