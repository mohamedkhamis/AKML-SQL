# Contract — IndexedDB schema cache shape (M5)

This contract specifies the IndexedDB layout used for offline IntelliSense, the cache key, and the change-detection protocol with the engine.

Cross-references: spec.md FR-024–FR-028; clarification 3; data-model.md E7; M5 PRD.

---

## IndexedDB database

```text
database name:  AkmlSqlSchemaCache
version:        1
```

### Object stores

| Store | Key | Indexes | Purpose |
|-------|-----|---------|---------|
| `schemaEntries` | `[serverCanonicalIdentity, databaseName]` | `lastUsedAt` (for LRU eviction) | One row per (server, db) — the cache itself |
| `changeLog` | `seq` (auto-increment) | `dbKey` | Append-only refresh log for diagnostics |
| `cacheMeta` | `id` (singleton `"meta"`) | — | Total bytes, last-evict-at, schema-version |

---

## `schemaEntries` record shape

```typescript
interface SchemaEntry {
    // Composite primary key
    serverCanonicalIdentity: string;   // e.g. "PROD-DB01\\SQL2022" or "10.0.0.1,1433"
    databaseName: string;              // e.g. "AdventureWorks2022"

    // Snapshot body — mirrors AkmlSql.IntelliSense.DatabaseCache shape
    phaseA: {
        schemas: SchemaInfo[];
        objects: ObjectInfo[];          // tables, views, procs, functions, etc.
        rowCounts: Record<string, number>;  // keyed by "schema.object"
    };
    phaseB: {
        columns: ColumnInfo[];
        foreignKeys: ForeignKeyInfo[];
        parameters: ParameterInfo[];
        descriptions: Record<string, string>;
    } | null;

    fkIndex: Record<string, ForeignKeyInfo[]>;   // "schema.table" → FK[]
    checksum: string;                            // CHECKSUM_AGG result from engine; opaque blob
    fetchedAt: string;                           // ISO 8601
    lastUsedAt: string;                          // ISO 8601 — LRU driver
    sourceConnectionId: string | null;           // informational, not part of key
}
```

`SchemaInfo`, `ObjectInfo`, `ColumnInfo`, `ForeignKeyInfo`, `ParameterInfo` are the same shapes the engine already uses on the wire; the Blazor side has matching C# DTOs that round-trip through the same MessagePack DTOs to keep the engine code path identical between in-process and WebSocket transports.

---

## Cache key (clarification 3)

The primary key is the **ordered pair** `(serverCanonicalIdentity, databaseName)`. Implications:

1. The browser MUST ask the engine for `serverCanonicalIdentity` via either `HandshakeResponse.ServerCanonicalIdentity` (single-DB engines) or a `SchemaIdentify` request that returns `{ serverCanonicalIdentity }` for a given engine state.
2. The browser MUST NOT key by host:port, by FQDN, or by connection-string string equality. Two different DNS aliases of the same SQL Server resolve to the same `serverCanonicalIdentity` and therefore the same cache entry.
3. Cache entries SURVIVE a connection being unpaired or replaced. They are purged only by explicit user action (FR-028) or by LRU eviction (FR-027).

---

## Change detection protocol

```text
[Browser]                                 [Engine]
   |                                          |
   |-- SchemaChecksumRequest{                |
   |     serverCanonicalIdentity, dbName     |
   |   } ------------------------------------>
   |                                          | CHECKSUM_AGG(BINARY_CHECKSUM(...)) over sys.objects
   |<- SchemaChecksumResponse{ checksum } ----|
   |                                          |
   | if checksum == local: do nothing         |
   | else: trigger Phase A refresh           |
   |                                          |
   |-- SchemaRefreshRequest{                 |
   |     serverCanonicalIdentity, dbName,    |
   |     wantPhaseB: true                    |
   |   } ------------------------------------>
   |                                          | Phase A: < 500 ms
   |<- SchemaPhaseAResponse{ ... } -----------|
   | update phaseA, checksum, fetchedAt      |
   |                                          | Phase B: background
   |<- SchemaPhaseBResponse{ ... } -----------|
   | update phaseB, fkIndex                  |
```

Polling cadence: while editor is active and bridge is reachable, browser polls `SchemaChecksumRequest` every 30 seconds. Polling pauses when the editor is idle (no edit + no keyboard focus) for more than 5 minutes.

---

## Eviction policy (FR-027)

Triggered when:

- An IndexedDB write fails with `QuotaExceededError`, **or**
- The browser reports a storage estimate above 80 % of quota.

Algorithm:

1. Sort `schemaEntries` by `lastUsedAt` ascending.
2. Evict entries one at a time until the write succeeds (post-write quota under 80 %).
3. Emit a single non-blocking notice to the user: *"AKML SQL evicted cached schema for `<dbName>` to make room. You can refresh by reconnecting."*
4. Append a `changeLog` row with `{ action: "evict", dbKey, evictedAt }` for the diagnostics bundle.

Bulk-clear (Settings → Clear schema cache) deletes all `schemaEntries` rows in a single transaction; `cacheMeta` updated.

---

## Online vs offline behaviour matrix

| Bridge state | Cache state | Behaviour |
|--------------|-------------|-----------|
| Reachable + fresh | Present, checksum matches | Serve completions from cache; status badge: **Live** |
| Reachable + stale | Present, checksum differs | Serve from cache while Phase A refresh runs; status badge: **Refreshing** → **Live** |
| Reachable + cold | Absent | Phase A then Phase B from engine; status badge: **Loading** → **Live** |
| Unreachable + warm | Present | Serve from cache; status badge: **Cached** with `fetchedAt` timestamp |
| Unreachable + cold | Absent | Keywords + snippets only; status badge: **Disconnected** |

---

## Test obligations

- Two different connections (different host strings) that point at the same SQL Server (same `serverCanonicalIdentity`) MUST share one cache entry.
- A connection unpair followed by re-pair to the same SQL Server MUST find the existing cache entry warm (no Phase A refresh on first IntelliSense request).
- `QuotaExceededError` during a `SchemaRefresh` write MUST trigger eviction and retry, with the non-blocking notice surfacing exactly once per bulk eviction.
- Polling pause: editor idle for 6 minutes MUST suspend `SchemaChecksumRequest`. Resumption on keystroke MUST issue a checksum request within 1 second.
