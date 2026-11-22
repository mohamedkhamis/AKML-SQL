# Data Model: QA Audit Remediation (spec 047)

Phase 1. Only the shapes this feature adds or changes. Every wire and storage change is **append-only**: older
readers ignore the new members, and newer readers treat a missing member as the old behaviour. Nothing here holds a
password; rule 10 of the spec, FR-010, is restated per entity where it matters.

## 1. Engine install manifest: `akml-engine.json` (new file, WEB-001)

A static JSON file the installer writes next to the web bundle (`{app}\Web\akml-engine.json`), served by IIS (or any
other host of the files) from the page's own origin.

| Field | Type | Rule |
| --- | --- | --- |
| `bridgePort` | int | 1024–65535; the installer's `WebBridgePort`. Required. |
| `scheme` | string | `"ws"` (Localhost exposure) or `"wss"` (Lan exposure). Required. |
| `manifestVersion` | int | `1`. A page ignores a file with a higher major it does not know. |

- **Written by** `web-engine-manifest.ps1` (Web_PostInstall for IIS and None hosting, and both repair scripts on a
  port change). It is not written for Service hosting, where the engine serves the page itself.
- **Read by** `PageEngine.GetAsync`, only after `HEAD /` reported a static page. A missing, unreadable or invalid file
  means "no declared engine": behaviour as today, falling back to localhost:47291.
- **Never contains** a host name, PIN, token or path. Host = the page's host.

## 2. Page engine result (changed, WEB-001)

`PageEngineInfo(Host, Port, Scheme)` is unchanged. `PageEngineKind.Engine` now means "the engine that serves this page
**or that the page's installer declared**". A new read-only `PageEngineResult.Source`
(`ServedPage` | `InstallManifest`) lets diagnostics say which applied.

`PairLocalEngineAsync` returns `LocalPairResult(bool Ok, string TriedAddress, LocalPairFailure? Failure)`.

| `Failure` | When |
| --- | --- |
| `NotReachable` | Nothing answered at `TriedAddress`. |
| `PinRequired` | The engine requires pairing. The page prefill is set, as today. |
| `Rejected` | Any other handshake status; the existing wording is used. |

## 3. Pairing entry (new wire shape, ENG-001)

New MessagePack messages (append-only IPC numbers, capability `pairing.manage.v1`).

`PairingListRequest` (227) has no fields. `PairingListResponse` (228):

| Key | Field | Type | Rule |
| --- | --- | --- | --- |
| 0 | `PairingUsed` | bool | False when the engine does not require pairing (no tokens exist). |
| 1 | `Entries` | `PairingEntry[]` | Every stored token, newest first. |

`PairingEntry`:

| Key | Field | Type | Rule |
| --- | --- | --- | --- |
| 0 | `Id` | string | The token's SHA-256 hash (hex). The only identifier ever exposed; never the token. |
| 1 | `Label` | string | As minted. New mints: `"{Browser} on {OS} · {origin}"`, ≤ 80 chars. Old rows: their stored text, or "Web edition". |
| 2 | `MintedAt` | DateTimeOffset | |
| 3 | `LastUsedAt` | DateTimeOffset | |
| 4 | `ExpiresAt` | DateTimeOffset | Past dates are shown as "expired" (they already fail `Validate`). |
| 5 | `IsCurrent` | bool | True for the pairing the calling connection authenticated with. |

`PairingRevokeRequest` (229) is `{ 0: Id }`. `PairingRevokeResponse` (230) is
`{ 0: Ok, 1: Message?, 2: ClosedConnections:int }`.

**State transitions of a pairing:**

```
minted (PIN handshake) ──validate──▶ used (LastUsedAt bumped) ──TTL──▶ expired (kept, shown as expired)
        └──────────────────── Revoke ────────────────────────────▶ removed + its open sockets closed
```

Nothing removes a pairing except Revoke (clarification Q5). `RevokeAll` is unchanged and remains engine-local.

**Per-connection state:** `BridgeConnection`'s holder gains `TokenHash` (string?). It is set by `HandshakeHandler` on
a successful bearer validation or a PIN mint, read by the list (`IsCurrent`), and matched by Revoke to close sockets.

## 4. Saved SQL connection (changed, WEB-002, WEB-004, WEB-010)

`SavedSqlConnection` (IndexedDB `savedSqlConnections`):

| Member | Change |
| --- | --- |
| `EffectivePort` | `[JsonIgnore]`: no longer written. An existing stored value is ignored on read. |
| `RememberPassword` | Meaning tightened. For a SQL login it is true only after the engine **confirmed** it keeps the password: a successful Save test (`ConnectResult.Remembered`) or a successful Connect. Unticking it on a row calls Forget on the engine. |
| `Name` | Unchanged. A form opened for a tab with no saved row now starts with the default `"{server} / {database}"`. |

**Save with Remember (SQL login, password typed), as a state flow:**

```
Save ─▶ test login on engine (TestSqlConnection, RememberPassword=true)
          ├─ Ok, Remembered ─▶ row saved, RememberPassword=true,  "Saved. The engine machine keeps the password for {login}."
          ├─ Ok, not kept    ─▶ row saved, RememberPassword=false, "Saved, but the engine could not keep the password."
          ├─ login failed    ─▶ row saved, RememberPassword=false, "Saved without the password: the login test failed — {error}."
          └─ no engine       ─▶ row saved, RememberPassword=false, "Saved without the password: no engine is connected."
```

FR-010: the password exists only in the form field and in the one test request. It is never in the row, the log or
diagnostics.

## 5. Browser storage state (new, WEB-003)

`IBrowserStorageState`:

| Member | Type | Meaning |
| --- | --- | --- |
| `RequestPersistenceAsync()` | `Task<bool?>` | Calls `navigator.storage.persist()` once per app run, the first time a saved connection or engine record is added. Null when the API is missing. |
| `IsPersistedAsync()` | `Task<bool?>` | `navigator.storage.persisted()`. |

The Diagnostics row `diagnostics-storage` shows persistent, best effort, or "not reported by this browser".

**Origin banner state:** a `localStorage` key `akml.originBanner.dismissed` (per origin, per viewer). All reads and
writes are wrapped in try/catch; an unreadable key shows the banner again, which is harmless.

## 6. Analysis display settings (changed, UX-001, SSMS-001)

**SSMS: `config.json` › `codeAnalysis`:**

| Key | Type | Default | Rule |
| --- | --- | --- | --- |
| `squiggleStyle` | string | `"marker"` (was `"underline"`) | `"marker"`: Information and Hint findings get no text decoration, only the margin glyph. Anything else (`"underline"`, and the never-implemented `"dotted"` and `"solid"`): every finding is underlined. |
| `squiggleStyleVersion` | int? | written on first load | Migration marker. A config without it and with `"underline"` becomes `"marker"` once (`ConfigManager.MigrateSquiggleStyle`, in memory, persisted by the next save). Any other value is kept. |
| `showAdvisoryHints` | bool | `true` | Now read. False means the lightbulb offers no fix actions for findings with no automatic fix. Marker, Error List entry and the glyph menu's suppress actions stay. |

**Web: analysis settings record (IndexedDB `analysisSettings` › `current`):**

| Member | Type | Default | Rule |
| --- | --- | --- | --- |
| `AdvisoryDisplay` | string | `"marker"` | `"marker"` or `"underline"`, with the same meaning as SSMS. Absent in older records, which read as `"marker"`. Included in settings export, import, and the section's Restore defaults. |

**Severity to presentation (both hosts, default settings):**

| Severity | Text decoration | Margin / gutter | Problems / Error List |
| --- | --- | --- | --- |
| Error (3), including `SYNTAX` | red underline | red dot | listed |
| Warning (2) | warning underline | orange dot | listed |
| Information (1) | **none** | blue dot | listed |
| Hint (0) | **none** | hint dot | listed |

## 7. Parse-error finding (new, WEB-005)

It is produced by `AnalysisEngine.AnalyzeAsync(..., reportParseErrors: true)`, used by the web only. Its shape is the
existing `CodeIssueInfo`: RuleId `SYNTAX`, Severity 3 (Error), the ScriptDom message ("Incorrect syntax near '*'."),
the line and column of the offending token, and no fix. It is not a rule: it cannot be turned off from the rules
list, and it does not appear in it.

## 8. Schema payload flag (new key, WEB-013)

`SchemaPhasePayload` gains key `8`, `MetadataLimited` (bool). It is true when the engine's login lacks VIEW
DEFINITION on the database (`HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') = 0`). It is read in Phase A
on the existing connection, in one query, and stored as `DatabaseCache.MetadataLimited`. Older engines don't send it,
which reads as false: no notice, the behaviour of today. It is always false for PostgreSQL.

## 9. Find Invalid Objects count (meaning changed, SSMS-003)

`FindInvalidObjectsResponse.TotalScanned` now means **references checked**: every `sys.sql_expression_dependencies`
row of a user object, resolved or not. Before, it meant the unresolved candidates only. The shell and engine ship in
one installer, so no older peer reads the new meaning. The dialog shows the following.

| Records | `TotalScanned` | Summary text | Grid |
| --- | --- | --- | --- |
| 0 | > 0 | "No invalid objects found. All {n} references resolve." | hidden |
| 0 | 0 | "This database has no object references to check." | hidden |
| > 0 | any | "{k} invalid object(s) — references that can no longer be resolved." (as today) | shown |

As built: one reference reads "No invalid objects found. The 1 reference resolves.". Without the grid, the window is
just the summary and Close.

## 10. User snippet as a completion item (new mapping, SN-001)

| `CompletionItem` member | From `WebSnippet` |
| --- | --- |
| `DisplayText` | `Metadata.Shortcode` |
| `InsertText` | `Body` joined with `\n` (`$CURSOR$` and `$Name$` stops as authored) |
| `ObjectType` | `Snippet` |
| `SecondaryText` | `Metadata.Title` |
| `SortPriority` | 500 (as built-ins) |
| `SourceObject` | `""` |

Only non-built-in snippets of the active dialect (`ListAsync(kind)`) are added. A user snippet whose shortcode equals
a built-in's appears once, the user's, which matches the expansion lookup order.

## 11. In-browser schema cache for refactorings (new conversion, WEB-011)

`SchemaSnapshotCache.ToDatabaseCache(SchemaPhasePayload payload)` produces a `DatabaseCache`:

- schemas are taken from `payload.Schemas`;
- each `SchemaPhaseObject` becomes a `DatabaseObject` (type, name, columns from Phase B, parameters from Phase B);
- foreign keys are taken from `payload.ForeignKeys`, followed by `RebuildFkIndex()`.

Phase A input produces objects without columns. The three Expand items treat that as "Still loading the column
list".

As built (spec amendment A7): the conversion is the existing `SchemaPhaseRehydrator.Rehydrate` (AkmlSql.IntelliSense). It
already did all of the above for the AI schema context, and `SchemaPhaseRehydratorTests` cover it. `RefactorSchema.From`
picks the state from it: Phase B gives Ready, Phase A only gives StillLoading, nothing gives None.

## 12. Line-ending preservation (new pipeline step, SSMS-004)

The input's **dominant line ending** is CRLF when `\r\n` occurs at least as often as lone `\n`; otherwise it is LF.
The output is emitted in that convention after validation. A lone `\r` is left untouched.
