# Phase 1 — Data Model: AKML SQL Web Edition

This document defines the persistent and semi-persistent entities introduced by the web edition. Each entity lists: fields and types, identity, validation rules, persistence location, and state transitions where applicable.

Cross-reference: spec.md § "Key Entities", FR-005a, FR-010, FR-024–FR-029.

---

## Storage map (where each entity lives)

| Entity | Persistence layer | Per-… | Survives reload |
|--------|-------------------|-------|-----------------|
| `EditorSession` | IndexedDB | tab+install | Yes |
| `EngineConnection` | IndexedDB | browser profile | Yes |
| `PairingToken` | IndexedDB (wrapped) | connection | Yes |
| `FormattingProfile` | IndexedDB + embedded WASM resources | browser profile | Yes |
| `AnalysisSettings` | IndexedDB | browser profile | Yes |
| `SchemaCacheEntry` | IndexedDB | browser profile | Yes |
| `AiProviderConfig` | IndexedDB (wrapped) | browser profile | Yes |
| `DiagnosticEntry` (ring buffer) | Memory + IndexedDB | tab | Yes (last N before close) |
| `ThemePreference` | IndexedDB | browser profile | Yes |
| `HandshakeMetadata` | Memory only | connection | No |
| `WebEditionInstall` | Engine-side filesystem (`%AppData%/AKML SQL Web/`) | install | Yes |
| `InstallSummaryFile` | Engine-side filesystem (`%ProgramFiles%/AKML SQL/Web/INSTALL-SUMMARY.txt`) | install | Yes |

---

## E1. WebEditionInstall

Engine-side configuration for a single web-edition install on a host.

| Field | Type | Notes |
|-------|------|-------|
| `installId` | string (GUID) | Generated at install time; identifies this install across re-runs |
| `installRoot` | string (absolute path) | `%ProgramFiles%/AKML SQL/Web/` by default |
| `engineExePath` | string (absolute path) | Resolved at install time; engine binary is shared with the IDE-plugin engine but invoked with a separate `%AppData%` |
| `appDataRoot` | string (absolute path) | `%AppData%/AKML SQL Web/` |
| `transportMode` | enum `"localhost" \| "lan"` | Set at install time; user can flip via re-install |
| `bindAddress` | string | `127.0.0.1` for localhost, `0.0.0.0` for LAN |
| `port` | integer 1024..65535 | Default `47291`; configurable |
| `tlsCertPath` | string (absolute path)\|null | Required when `transportMode == "lan"` |
| `installedVersion` | semver | Engine version delivered with this install |
| `iisSiteName` | string\|null | If installer created an IIS site; `null` if "Don't host" chosen |

**Identity**: `installId`. Two installs on one host (web edition + IDE plugins) have different `installId`s and different `appDataRoot`s.

**Validation**:

- `port` must not collide with the IDE-plugin engine's pipe (the plugin engine does not listen on a WebSocket, so collision is on the IIS site port only). Installer must check before binding.
- `tlsCertPath` required when LAN; installer generates if absent.

**State transitions**: Created on install. Modified on re-run installer (e.g. switch localhost → LAN regenerates cert + token). Removed on uninstall, which clears `appDataRoot` and removes the IIS site / fallback service.

---

## E2. EngineConnection

A browser-side record describing one paired engine.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string (UUID v4) | Browser-generated |
| `name` | string (≤ 64 chars) | User-editable; default `"Local engine"` for localhost, host name for LAN |
| `host` | string | `127.0.0.1` for localhost; FQDN or IP for LAN |
| `port` | integer 1024..65535 | Matches engine `WebEditionInstall.port` |
| `isLocalhost` | boolean | Derived from `host`; affects whether `bearerTokenWrappedRef` is required |
| `bearerTokenWrappedRef` | string\|null | Pointer to a wrapped token record in the `pairingTokens` object store; `null` for localhost |
| `tlsFingerprint` | string\|null | Hex SHA-256 of the engine's TLS cert; pinned after first connect for LAN |
| `lastConnectedAt` | ISO 8601 timestamp\|null | Updated on successful handshake |
| `lastKnownEngineVersion` | semver\|null | Latest value received in `HandshakeResponse.engineVersion` |
| `lastKnownCapabilities` | string[] | Latest `engineCapabilities` from handshake |

**Identity**: `id`.

**Validation**:

- `bearerTokenWrappedRef` MUST be non-null when `!isLocalhost`.
- `tlsFingerprint` set on first successful WSS handshake for LAN; subsequent connections that present a different fingerprint are refused and surface a dialog: "Engine certificate changed — re-pair?".

**State transitions**:

- *Created* on first PIN-based pairing (LAN) or on first localhost connect.
- *Updated* (`lastConnectedAt`, `lastKnownEngineVersion`, `lastKnownCapabilities`) on every successful handshake.
- *Unpaired* when user clicks "Remove" — record deleted, wrapped token discarded, schema-cache entries owned by this connection are retained (they're keyed by server identity, not by connection — see Schema cache identity).

---

## E3. PairingToken (wrapped)

A long-lived 256-bit bearer token, stored only in wrapped form.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string (UUID v4) | Matches `EngineConnection.bearerTokenWrappedRef` |
| `connectionId` | string (UUID v4) | FK to `EngineConnection.id` |
| `wrappedToken` | bytes | AES-GCM ciphertext |
| `iv` | bytes (12) | Per-record IV |
| `aad` | bytes | `utf8("akmlsql.pairing." + connectionId)` |
| `ttlExpiresAt` | ISO 8601 | Engine-side TTL is authoritative; this field is informational |

**Identity**: `id`.

**Validation**: All fields required. `wrappedToken` is unwrappable only via the same browser-profile's non-extractable wrapping key.

**State transitions**:

- *Minted* on first successful PIN handshake; engine sends raw token over WSS, browser wraps and stores; raw token is then dropped from memory.
- *Used* on subsequent handshakes — browser unwraps, sends in `HandshakeRequest.bearerToken`, re-wraps storage on rotation.
- *Revoked* when the user regenerates the token from the engine UI; engine rejects the old token; browser falls back to PIN re-pairing.

---

## E4. FormattingProfile

User-owned or built-in formatting style preset.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | `"builtin:<name>"` for embedded; `"user:<uuid>"` for imported |
| `displayName` | string | User-visible; user-editable for `user:` profiles |
| `kind` | enum `"akmlstyle" \| "sqlpromptstylev2"` | Determines parser path |
| `source` | enum `"builtin" \| "imported" \| "exported"` | Provenance |
| `body` | JSON string | Serialised form; round-trips through `AkmlSql.Formatting` parsers |
| `createdAt` | ISO 8601 | First seen / imported |
| `lastUsedAt` | ISO 8601 | For surfacing recent profiles |

**Identity**: `id`.

**Validation**: `body` must parse via the existing C# round-trip. Parse failure aborts import with a user-facing error.

**State transitions**: Imported, updated (rename), exported (download), deleted.

---

## E5. AnalysisSettings

Per-rule severity overrides and enable/disable toggles. Equivalent to `.casettings` from the IDE plugin.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Singleton `"global"` per browser profile |
| `overrides` | map `<ruleId, { severity?, disabled? }>` | Sparse |
| `lastModifiedAt` | ISO 8601 | |

**Identity**: `id` (singleton).

**Validation**: `ruleId` must match a known `IAnalysisRule` ID (validated at apply time, not at store time, so a forward-compatible cache survives a rule rename).

---

## E6. EditorSession

The currently open document + UI state for restore-on-reload.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string (UUID v4) | Per tab |
| `documentText` | string | ≤ 10 MB |
| `caretLine` | integer | 1-based |
| `caretColumn` | integer | 1-based |
| `selection` | `{ startLine, startCol, endLine, endCol }` \| null | |
| `activeProfileId` | string | FK to `FormattingProfile.id` |
| `activeConnectionId` | string\|null | FK to `EngineConnection.id` |
| `lastFormatAt` | ISO 8601\|null | |
| `lastAnalysisFindingIds` | string[] | UI-only; cleared on reload |

**Identity**: `id`.

**Validation**: `documentText.length ≤ 10_485_760` (10 MiB). Reject paste at the upper bound with a clear error per FR-011.

**State transitions**: Created when user starts editing; updated on debounce after typing; deleted when user clicks "New document".

---

## E7. SchemaCacheEntry

Per-database snapshot used for offline IntelliSense (FR-024–FR-028; clarification 3).

| Field | Type | Notes |
|-------|------|-------|
| `key` | composite `(serverCanonicalIdentity, databaseName)` | **Primary key per clarification 3** |
| `serverCanonicalIdentity` | string | Reported by engine in `SchemaIdentify` |
| `databaseName` | string | |
| `phaseA` | JSON | Object list, schemas, row-counts (mirrors engine `DatabaseCache.PhaseA`) |
| `phaseB` | JSON | Columns, FKs, parameters, descriptions (mirrors engine `DatabaseCache.PhaseB`) |
| `checksum` | string | `CHECKSUM_AGG(BINARY_CHECKSUM(...))` returned by engine; used for change detection |
| `fkIndex` | JSON | Pre-computed `"schema.table"` → `ForeignKey[]` map; mirrors `DatabaseCache.RebuildFkIndex()` |
| `fetchedAt` | ISO 8601 | When the snapshot was taken |
| `lastUsedAt` | ISO 8601 | Drives LRU eviction (FR-027) |
| `sourceConnectionId` | string \| null | Informational only — the connection that fetched this entry. NOT part of identity. |

**Identity**: `(serverCanonicalIdentity, databaseName)`. Re-pairing with a different engine that points at the same SQL Server resolves to the same entry.

**Validation**:

- `phaseA` MUST be present; `phaseB` MAY be `null` if Phase B is mid-fetch.
- Total serialised size: governed by the browser's quota; if quota is approached the system evicts least-recently-used entries.

**State transitions**:

- *Created* on first IntelliSense request for a database via a paired engine.
- *Refreshed* on background poll if `checksum` differs from engine's current value, or on DDL hint.
- *Evicted* by LRU when storage quota is approached (FR-027).
- *Cleared* via Settings → Clear schema cache (FR-028).

---

## E8. AiProviderConfig (wrapped)

| Field | Type | Notes |
|-------|------|-------|
| `providerId` | enum `"claude" \| "openai" \| "gemini" \| "azure-openai" \| "ollama" \| "lmstudio"` | |
| `displayName` | string | User-editable |
| `model` | string \| null | Provider-specific default if `null` |
| `endpoint` | URL \| null | For Azure / Ollama / LM Studio |
| `apiKeyWrapped` | bytes | AES-GCM ciphertext |
| `apiKeyIv` | bytes (12) | Per-record IV |
| `apiKeyAad` | bytes | `utf8("akmlsql.aikey." + providerId)` |
| `addedAt` | ISO 8601 | |
| `lastUsedAt` | ISO 8601 \| null | |

**Identity**: `providerId`.

**Validation**:

- `apiKeyWrapped` non-empty unless `providerId` is a local provider (Ollama, LM Studio) that does not require a key.
- Active provider is selected via a singleton `AiPreference` record (not modelled here; trivial `{ activeProviderId }`).

**State transitions**: Added, updated (rotate key), removed (key bytes are zeroised on delete).

---

## E9. DiagnosticEntry (ring buffer)

| Field | Type | Notes |
|-------|------|-------|
| `seq` | integer monotonic | Wraps when buffer is full |
| `ts` | ISO 8601 | |
| `level` | enum `"trace" \| "info" \| "warn" \| "error"` | |
| `source` | enum `"formatter" \| "analyser" \| "bridge" \| "ai" \| "cache" \| "ui"` | |
| `message` | string ≤ 2 KB | |
| `data` | JSON \| null | Optional structured payload |

**Identity**: `seq` (within a session).

**Validation**: total bytes per entry ≤ 4 KB; ring buffer size default 2 048 entries (~8 MB cap).

**State transitions**: Append-only until ring wraps. Flushed to IndexedDB periodically and on tab close. Cleared on Settings → Clear diagnostics.

---

## E10. ThemePreference

| Field | Type | Notes |
|-------|------|-------|
| `mode` | enum `"system" \| "light" \| "dark" \| "high-contrast"` | Default `"system"` |
| `lastChangedAt` | ISO 8601 | |

Trivial singleton record.

---

## E11. InstallSummaryFile (engine-side)

Plain-text artefact written by the installer. Re-displayable from the engine UI at any time.

```text
AKML SQL — Web Edition install summary
Install date: 2026-05-16 14:22 UTC
Install id:    {GUID}
URL:           https://lan.example.local:47291/akmlsql/
Mode:          LAN
Pairing PIN:   123 456    (one-time; valid for 24 h)
Bearer TTL:    90 days
TLS fingerprint (SHA-256): aa:bb:cc:...
Notes:
  - Trust the TLS cert on each browser you pair from.
  - Regenerate the token any time from "Engine UI → Pairing".
```

No JSON model needed; the file is human-readable and copyable.

---

## E12. HandshakeMetadata (per-connection runtime, not persisted)

Held by `EngineConnection` in memory between connect and disconnect.

| Field | Type | Notes |
|-------|------|-------|
| `engineVersion` | semver | From `HandshakeResponse` |
| `webVersion` | semver | Local |
| `protocolVersion` | integer | Negotiated min of (web, engine); error if disjoint |
| `engineCapabilities` | string[] | Drives feature gating per clarification 5 |
| `connectedAt` | ISO 8601 | Set on first successful frame post-handshake |

---

## Cross-entity invariants

1. **Schema cache identity is independent of the connection that populated it** (R8). Re-pairing with a different engine pointing at the same SQL Server reuses the same `SchemaCacheEntry` record.
2. **Bearer tokens never appear in plain in IndexedDB.** A grep of IndexedDB after a successful pairing must not find the raw token. Tested in `AkmlSql.Web.Tests`.
3. **AI keys never appear in plain in IndexedDB.** Same constraint, same test pattern.
4. **The web-edition install owns its `%AppData%` namespace.** No code in the web edition reads or writes `%AppData%/AKML SQL/...` (the plugin namespace). Enforced by a single allow-list in `AkmlSql.Web.Services.AppDataPaths`.
5. **No PII or schema content leaves the user's machine without an explicit user action.** "Export diagnostics" is the only egress channel and is user-initiated.
