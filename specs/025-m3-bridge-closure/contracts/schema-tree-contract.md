# Contract: Schema object tree component

**Spec**: 025-m3-bridge-closure
**Consumers**: US4 (FR-017 / FR-018 / FR-019 / FR-020 / FR-021 / FR-022)
**Related**: spec 021 T108 (`ISchemaSync` / `ISchemaCacheStore`); Research Decision 3; data-model.md E2

## Node hierarchy

The tree renders a single connected database (the active connection's current database) with this fixed shape:

```
Database "<name>"
├── Schema "dbo"
│   ├── Tables (N)
│   │   ├── [dbo].[Customer]
│   │   │   ├── Id  (int)
│   │   │   ├── Name (nvarchar(100))
│   │   │   └── ...
│   │   ├── [dbo].[Order]
│   │   └── ...
│   ├── Views (N)
│   │   └── ...
│   ├── Stored Procedures (N)
│   │   └── ...
│   └── Functions (N)
│       └── ...
└── Schema "<other>"
    └── ...
```

Object-Kind nodes are headers; clicking them only expands/collapses. Object nodes (`[schema].[name]`) are clickable for insert. Column nodes are non-clickable (display only).

## Data source

The component MUST read from `ISchemaCacheStore.GetAsync(serverCanonicalIdentity, databaseName)` returning a `SchemaSnapshot` per spec 021 contracts/schema-cache-shape.md. It MUST NOT issue any `IEngineBridge.SendAsync` calls — every render path goes through the cache. (Refresh is signalled by `ISchemaSync.ChecksumDrifted`, which writes a new snapshot into the cache before raising; the component reacts by re-reading.)

## Rendering rules

- **Phase A first**: when only `SchemaSnapshot.PhaseA` is populated, render Database → Schema → Object-Kind → Object nodes; Object nodes appear with no expansion arrow (no Phase B yet).
- **Phase B fills in**: when `SchemaSnapshot.PhaseB` is populated, Object nodes acquire an expand arrow; on expand, their columns render from the cached Phase B blob.
- **Column expansion is local**: no further round-trip required — Phase B is already fetched by `ISchemaSync.FetchPhaseBAsync`.
- **Empty state**: when no snapshot exists in the cache (fresh connection, no Phase A yet), the component renders a single placeholder row: "Schema not yet loaded — waiting for engine."

## Virtualisation

When a node has more than 200 immediate children, the children list MUST render through Blazor's built-in `<Virtualize>` component with a fixed item-height of 24 px. Below the threshold, plain `@foreach` renders (virtualisation overhead isn't worth it for short lists).

Per FR-022, this MUST handle at least 2,000 tables in a single schema without UI jank. Larger snapshots (~tens of thousands) are out of scope for this closure.

## Expansion-state preservation (FR-021)

The component owns a `HashSet<string>` of `Path` keys for currently-expanded nodes. When a snapshot refresh fires:

1. The new snapshot replaces the old in the cache.
2. The component re-binds to the new snapshot.
3. The `HashSet<string>` is **not** cleared — nodes whose `Path` is still present in the new tree stay expanded; nodes whose `Path` disappeared (object dropped from the schema) drop out of the set silently.
4. Newly-added paths render collapsed by default.

## Stale-indicator badge (FR-020)

When `IEngineBridge.State` is `Disconnected` or `Reconnecting`:

- A subtle grey badge appears at the top of the tree: "Stale — last fetched <relative time>".
- Relative time format: "just now" (<60s), "5 minutes ago" (<60min), "2 hours ago" (<24h), "yesterday" (<48h), "<absolute date>" (otherwise).
- Source: `SchemaSnapshot.FetchedAt` (already populated by `ISchemaSync`).

The badge MUST disappear immediately on `State == Open` transition; the badge MUST appear within 1 s of `Open → Reconnecting` or `Open → Disconnected`.

## Click-to-insert (FR-019)

On click of an Object node, the component raises an `EventCallback<string>` with `QualifiedName` (e.g., `[dbo].[Customer]`).

`Editor.razor` subscribes to this callback and inserts the qualifier at the editor caret via the existing CodeMirror JS interop (no new interop function required — `akml-editor.js` already exposes `insertAtCaret(text: string)`).

The insert is undoable via CodeMirror's standard `Ctrl+Z`; the component does not manage undo history itself.

## Tests (`tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs`)

bUnit tests MUST cover:

| Test | Asserts |
|------|---------|
| `RendersDatabaseSchemaTableHierarchyFromPhaseA` | Seed `ISchemaCacheStore` with a Phase-A-only snapshot containing 1 db / 2 schemas / 4 tables; assert the rendered DOM has exactly 1 db node, 2 schema nodes, 2 "Tables" headers, 4 Object nodes. No Column nodes. |
| `ExpandsTableShowsColumnsFromPhaseB` | Seed snapshot with Phase A + Phase B; click a table node; assert 5 Column rows render with the right type strings. |
| `ChecksumDriftRefreshesTreePreservesExpansion` | Seed snapshot; expand "Customer" table; raise `ChecksumDrifted` with a new snapshot that still contains "Customer"; assert "Customer" stays expanded post-refresh. |
| `StaleBadgeAppearsWhenDisconnected` | Seed snapshot with `FetchedAt = -5 minutes`; configure bridge State = `Disconnected`; assert the badge text is "Stale — 5 minutes ago". |
| `StaleBadgeHiddenWhenOpen` | Same snapshot; configure State = `Open`; assert no stale badge. |
| `ClickOnObjectRaisesQualifiedName` | Click `[dbo].[Customer]`; assert the registered `EventCallback` was invoked with payload `"[dbo].[Customer]"`. |
| `EmptyStatePlaceholderWhenNoSnapshot` | `ISchemaCacheStore` returns null; assert the placeholder row text is "Schema not yet loaded — waiting for engine." |
| `VirtualisationKicksInPastThreshold` | Seed a snapshot with 250 tables in one schema; render; assert the rendered DOM contains a `<Virtualize>` element wrapping the table list (not a flat `@foreach`). |

## Theming

The component MUST use the existing CSS custom properties from `src/AkmlSql.Web/wwwroot/css/themes/` (`--akml-surface-base`, `--akml-text-primary`, `--akml-border`, `--akml-accent`, etc.) — no hardcoded hex values. The icon set MUST reuse the existing inline-SVG icons under `src/AkmlSql.Web/Shared/Icons/` (already shipped by spec 021); no new icon files are introduced.
