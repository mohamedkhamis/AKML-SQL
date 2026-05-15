# M5 — IndexedDB Schema Cache, Offline IntelliSense, Snippets & Refactoring

**Status**: Draft
**Phase**: M5 (browser feature parity push)
**Estimated effort**: 2–3 weeks
**Branch prefix**: `m5-offline-parity`
**Depends on**: M3 shipped

---

## 1. Executive summary

M3 connected the browser to a live engine for schema and IntelliSense. M5 closes most of the remaining gap between the WPF surface and the web surface for offline-capable features:

1. **IndexedDB schema cache** — once the engine has populated Phase A+B for a database, the browser caches it locally so subsequent visits don't refetch
2. **Offline IntelliSense** — when the engine isn't reachable, completion / QuickInfo / signature help still work for any cached database
3. **Snippets** — full personal + built-in snippet library in the browser; expand, list, save, delete
4. **Refactoring** — both lightweight (text-level) and heavyweight (schema-aware) refactorings, gated on schema availability

After M5 the browser has approximate feature parity with the WPF surface for everything except AI (M6) and Git integration (out of scope for the web track entirely).

---

## 2. Why now

M3 made the browser useful with a database, but the experience degrades sharply if the engine becomes unreachable mid-session. M5 makes the browser resilient: cached schema works offline, snippets are local-first, lightweight refactorings need no engine round-trip at all. This also dramatically reduces engine load — the engine becomes the source of truth, the browser handles steady-state interactive work.

The reason to bundle snippets + refactoring with the schema cache is that they share the same offline-vs-online behaviour pattern: features that can work offline using cached state, features that must round-trip to the engine. Designing them together produces a coherent offline model.

---

## 3. Current state

End of M3 / M4:

- Browser fetches schema fresh on every connection
- IndexedDB used only for theme preference + per-rule override storage
- Snippets: not supported at all in the browser
- Refactoring: not supported at all in the browser
- "Engine unreachable" = "browser is mostly useless"

---

## 4. Proposed architecture

### 4.1 IndexedDB schema cache

```
IndexedDB database: AkmlSqlSchemaCache
├── object store: connections     (key: connectionId, value: { name, host, port, lastUsed })
├── object store: databases       (key: connectionId+dbName, value: { phaseA, phaseB, checksum, fetchedAt })
└── object store: changeLog       (key: timestamp, value: { dbKey, change, source })
```

Cache lifecycle:

1. On first IntelliSense request after connection, browser asks engine `SchemaRefresh` and stores result
2. On subsequent loads, browser checks IndexedDB first; if present, IntelliSense runs from cache
3. Browser periodically (every 30 seconds while editor is active) asks engine for current `CHECKSUM_AGG` value; if different, triggers a refresh
4. LRU eviction when storage quota approached (typically 50 MB+ for Origin Private File System; well within typical limits)

The cache mirrors the engine's `DatabaseCache` data shape (including the FK index), so the same `CompletionEngine` logic runs against either.

### 4.2 Running the completion engine in the browser

This is the key architectural piece. The engine's `CompletionEngine` is C# in `AkmlSql.Engine` — not currently referenced by `AkmlSql.Web`. M5 extracts the completion logic into a new project:

```
src/
  AkmlSql.IntelliSense/          ← NEW; netstandard2.0
    CompletionEngine.cs           ← moved from AkmlSql.Engine
    QuickInfoEngine.cs            ← moved from AkmlSql.Engine
    SignatureHelpEngine.cs        ← moved from AkmlSql.Engine
    DatabaseCache.cs              ← shape-compatible with engine's version
    AkmlSql.IntelliSense.csproj
```

Both `AkmlSql.Engine` and `AkmlSql.Web` reference `AkmlSql.IntelliSense`. The engine continues to expose IntelliSense over WebSocket for the "live, no cache" path; the browser uses the same code with the IndexedDB-backed cache for the offline path.

This is the second instance of the pattern M0 established: shared logic in a library, transport adapters at the edges.

### 4.3 Online vs offline behaviour

| Engine state | Completion source | QuickInfo source | Schema refresh |
|--------------|-------------------|------------------|-----------------|
| **Reachable, cache fresh** | Local cache (fastest) | Local cache | Background; opportunistic |
| **Reachable, cache stale** | Local cache, refresh starts | Local cache | Immediate background refresh |
| **Reachable, no cache** | Engine (live) | Engine | Phase A → Phase B sequence |
| **Unreachable, cache present** | Local cache | Local cache | None; show indicator |
| **Unreachable, no cache** | Keywords + snippets only | Keywords only | None; prompt to reconnect |

A status badge in the editor footer shows the current state: "Live", "Cached", "Offline", "Disconnected."

### 4.4 Snippets

```
AkmlSql.Web
└── Services/
    └── SnippetStore.cs
        ├── Built-in snippets   (embedded JSON resource — same files as the engine ships)
        ├── User snippets       (IndexedDB)
        └── Import/export       (.akmlsnippet files via <InputFile> / download)
```

Snippet expansion runs entirely in the browser — pure text manipulation, no engine round-trip needed.

### 4.5 Refactoring split

The engine distinguishes lightweight (text-level) and heavyweight (schema-aware) refactorings. The split maps cleanly onto online/offline:

| Refactoring | Light/heavy | Runs offline? |
|-------------|-------------|----------------|
| Expand INSERT Columns | Light | Yes |
| Convert Old-Style Joins | Light | Yes |
| Encapsulate BEGIN/END | Light | Yes |
| Add/Remove Square Brackets | Light | Yes |
| Convert Temp Table | Light | Yes |
| (5 more lightweight ops) | Light | Yes |
| Smart Rename | Heavy | Only if schema cached |
| Parameterize Values | Heavy | Only if schema cached |
| Extract Procedure | Heavy | Only if schema cached |

Lightweight refactorings run in `AkmlSql.IntelliSense` (parser + text rewrite — no schema). Heavyweight ones need the `DatabaseCache`, which the IndexedDB cache provides when present.

---

## 5. Feature scope

| Feature | In M5 |
|---------|-------|
| IndexedDB schema cache | Yes |
| Cache invalidation on CHECKSUM_AGG drift | Yes |
| Offline completion | Yes (for cached DBs) |
| Offline QuickInfo | Yes |
| Offline signature help | Yes |
| Connection status badge | Yes |
| Snippet library — built-in | Yes |
| Snippet library — user | Yes |
| Snippet import/export | Yes |
| Snippet surround-with chord | Yes |
| Lightweight refactorings (9 ops) | Yes |
| Heavyweight refactorings (3 ops) | Yes (cache-gated) |
| Inline suppression editing | Yes (was display-only in M2) |
| Multi-tab editor | **No** — separate spec |
| Multi-connection | **No** — one engine connection per browser |
| Tab colouring | **No** — needs tabs first |
| AI | **No** — M6 |

---

## 6. Milestones

### M5.1 — Extract AkmlSql.IntelliSense (week 1, days 1–2)

Move completion / QuickInfo / signature help into the new shared library. Engine consumes it. Existing engine tests pass. No browser changes yet.

### M5.2 — IndexedDB schema cache (week 1, days 3–5)

`SchemaCacheStore.cs` in the browser. On schema response from engine, persist. Read-back on next session. Visual confirmation in the schema tree.

### M5.3 — Offline IntelliSense (week 2, days 1–3)

Wire browser's `CompletionService` to use `AkmlSql.IntelliSense` against the IndexedDB cache. Connection status badge. Tested with cable yanked mid-session.

### M5.4 — CHECKSUM_AGG drift detection in browser (week 2, day 4)

Periodic background ping to engine for `CHECKSUM_AGG`. On change, refresh in background. Visual indicator when cache is updating.

### M5.5 — Snippets in browser (week 2, day 5 – week 3, day 1)

Built-in snippets embedded. User snippet store. Import/export. Editor integration for tab expansion. Surround-with shortcut.

### M5.6 — Refactoring in browser (week 3, days 2–4)

All 9 lightweight refactorings. Heavyweight refactorings gated on cache presence. Preview pane.

### M5.7 — Inline suppression editing + polish (week 3, day 5)

Suppression UI moves from display-only to editable: hover a problem, get a "Suppress on this line / file / globally" menu. Polish round across all M5 features. Visual parity audit against WPF surface.

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Extracting `AkmlSql.IntelliSense` introduces a regression in engine completion | Medium | High | The engine continues to consume the same code; all existing engine tests must pass before merging M5.1 |
| IndexedDB quota exceeded for users with many large schemas | Medium | Medium | LRU eviction; user-visible "Cache: 47 MB used" indicator; manual "Clear cache" button |
| Stale cache after invasive DB changes (DROP + CREATE) | Medium | Medium | Forced refresh on user click ("Reload schema"); DDL regex in browser triggers immediate ping |
| Browser-side parser performance with very long files | Medium | Medium | Same 10 MB limit as engine; warn at 1 MB; document |
| Cache schema migration when we add fields later | Medium | Low | Schema version field on every IndexedDB record; migration on read |
| User runs the same schema cache from two browsers, conflicting drift | Low | Low | IndexedDB is per-origin per-browser; they don't conflict, just refetch independently |

---

## 8. Success metrics

- Cold connection to cached DB: completion responds in < 50 ms (vs. > 200 ms via engine round-trip)
- Engine restart in the middle of a session does not break IntelliSense for cached DBs
- All 9 lightweight refactorings work offline
- All 3 heavyweight refactorings work when cache is present
- Snippet library round-trips between WPF surface and web surface byte-for-byte (export from one, import into the other)
- Cache size stays under 50 MB for the canonical test database (AdventureWorks)
- Visual parity audit: ≤ 3 deltas vs WPF surface (excluding the deferred multi-tab gap)

---

## 9. Out of scope

- Multi-tab editor — separate spec; significant UX design work
- Multi-connection (one browser, two engines) — separate spec
- Schema diff / compare (Phase 11 in the product roadmap) — separate phase
- Git integration in browser — out of scope for the web track
- Engine-resident snippets — snippets stay file-based on the engine side; the browser has its own IndexedDB store

---

## 10. Open questions

1. **Should the engine push schema updates to the browser, or should the browser pull?** — Push is realtime but requires the browser to maintain a listening WebSocket. Pull (with 30-second polling) is simpler and good enough for cache-drift detection. Lean pull for M5; revisit if users complain
2. **Should the IndexedDB cache survive a `Clear browsing data` action?** — No; that's the user explicitly clearing it. We just refetch
3. **Where do snippets sync between browser and engine?** — They don't, per the "independent surfaces" decision. The user can import/export between them manually

---

## 11. Definition of done

- [ ] `AkmlSql.IntelliSense` library exists; engine + browser both consume it
- [ ] Engine test suite green after refactor
- [ ] IndexedDB schema cache works; survives browser restart
- [ ] Offline IntelliSense works with cable yanked
- [ ] CHECKSUM_AGG drift triggers background refresh
- [ ] All 9 lightweight refactorings work in browser
- [ ] All 3 heavyweight refactorings work in browser (with cache)
- [ ] Snippets: built-in + user + import/export + surround-with
- [ ] Inline suppression editing
- [ ] Visual parity audit screenshots
- [ ] Branch `m5-offline-parity` merged to master via PR
