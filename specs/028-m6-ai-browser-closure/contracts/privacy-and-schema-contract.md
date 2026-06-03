# Contract: Privacy disclosure modes + schema-aware prompting from the cache (US1)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 1 (+ Reconciliation 2) · **FRs**: FR-001 … FR-007

## Privacy modes (FR-001, FR-005)

Four **disclosure** modes (distinct from the engine's redaction enum — Reconciliation 2): `FullSchema`, `SchemaNamesOnly`, `NoSchema`, `FullyLocal`. Stored in the **new `aiFeatureSettings` store** via `IAiFeatureSettings` (global default + per-feature override). Resolution: `FeatureModeOverrides[feature] ?? GlobalDefaultMode`. The resolved mode is displayed next to every AI control via `AiPrivacyModeBadge.razor` (FR-005).

Feature ids: `explain`, `fix`, `optimize`, `texttosql`, `indexanalysis`, `chat`, `ghosttext`.

## Schema resolution (FR-003, FR-006, FR-007)

`IAiSchemaContextProvider.GetSchemaTextAsync(featureId, ct)`:

1. Resolve mode → `(includeSchema, compressionLevel, forceLocal)` (data-model E1 table).
2. `includeSchema == false` (`NoSchema`) ⇒ **return empty string** — the no-schema guarantee (FR-007), enforced on every path including retries/fallback.
3. Else: read active `(server, db)` `SchemaSnapshot` from `ISchemaCacheStore`; `SchemaPhaseRehydrator.Rehydrate(cacheKey, phaseA, phaseB)` → `DatabaseCache`; `SchemaContextBuilder.BuildAsync(sessionId, sessionLookup, prompt, compressionLevel, maxObjects)` → `SchemaContextFormatter.Format(...)`; truncate to the provider's budget (FR-006, same policy the WPF surface applies via `SchemaContextBuilder`/`TokenEstimator`).
4. No cached snapshot ⇒ degrade to empty `schemaText` (edge case), never throw.

`SchemaNamesOnly` uses compression level 1 (names + row counts, no types/FKs) and can use Phase A alone; `FullSchema`/`FullyLocal` use level 4 and need Phase B.

## SchemaPhaseRehydrator (FR-003) — the M5-deferred reverse mapper

`AkmlSql.IntelliSense/Schema/SchemaPhaseRehydrator.cs`, namespace `AkmlSql.Engine.Schema`. Pure reverse of `SchemaPhaseSerializer`: `SchemaPhaseSchema[] → SchemaEntry`, `SchemaPhaseObject[] → DatabaseObject` (+`ObjectType`,`Description`), `SchemaPhaseColumn[] → Column`, `SchemaPhaseParameter[] → Parameter`, `SchemaPhaseForeignKey[] → ForeignKey`; then `RebuildFkIndex()`. **WASM-safe**: no `System.IO`/SqlClient/native; existing models only.

> This is the path spec 027 research Decision 3 deferred. M6 builds it because AI prompting needs the canonical `SchemaContextBuilder` (a `DatabaseCache`), not because of heavyweight refactoring. It is reused, not forked — so it cannot diverge per-feature, and it unblocks the M5 cached-heavyweight follow-up.

## Key storage unchanged (FR-002)

Keys stay wrapped by the shipped per-profile **non-extractable AES-GCM-256 `CryptoKey`** (`IAiKeyVault` / `akml-crypto.js`), AAD-bound to `providerId`. No passphrase/PBKDF2 (Reconciliation 1). The threat-model note (strong at rest; no "something you know" factor) goes in the privacy-commitment doc (US7).

## Test contract

- `tests/AkmlSql.IntelliSense.Tests/` — `SchemaPhaseRehydrator` round-trip: a known `DatabaseCache` → serialize → rehydrate reproduces `GetAllObjects()` / column / `GetForeignKeysForTable()` results (the gate invariant).
- `tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs` — for each mode, assert the `schemaText` the provider builds: `FullSchema` contains tables+columns+FKs; `SchemaNamesOnly` contains names but no data types/FKs; `NoSchema` is empty; per-feature override beats global default; `FullyLocal` restricts provider selection.
- The end-to-end "no schema in the wire" proof is the US7 privacy network-capture audit (FR-036/SC-003).

## Out of scope

- Engine's `anonymous` identifier-hashing redaction mode in the browser (Reconciliation 2).
