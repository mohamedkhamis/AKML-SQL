# Data Model: spec 036

**Date**: 2026-09-02 | **Source**: `spec.md` Key Entities + `research.md` R1–R12

Six entities. Four already exist and are extended; two are new shapes (one class, one JSON document). Validation rules are traced to the FR they satisfy.

---

## 1. AI Provider Profile

**Where it lives**: `AiSettings` in `src/AkmlSql.Core/Config/AppSettings.cs:1126` (the `ai` section of `config.json`). Serialised by System.Text.Json with explicit `[JsonPropertyName]` on every member.

**Existing fields used by this feature**

| Field | JSON | Type | Notes |
|---|---|---|---|
| `Enabled` | `enabled` | bool | Master switch; `AiChatHandler` throws when false |
| `Provider` | `provider` | string | **Now a canonical id** — see validation below |
| `Model` | `model` | string | Model identifier |
| `ApiKey` | `apiKey` | string | **Now DPAPI-wrapped** — `dpapi:`-prefixed Base64 |
| `Endpoint` | `endpoint` | string | Base URL; required for azure, defaulted for kimi/ollama |
| `MaxTokens` | `maxTokens` | int | Default 4096 |
| `Temperature` | `temperature` | double | Default 0.2 |
| `Timeout` | `timeout` | int (s) | Default 30; bounds the provider test too |
| `PrivacyMode` | `privacyMode` | string | `schemaOnly` (default) / `full` / `anonymous` / `offline` / `disabled` |
| `OfflineProvider` / `OfflineModel` / `OfflineEndpoint` | — | string | Fallback profile |

**New field**

| Field | JSON | Type | Default | FR |
|---|---|---|---|---|
| `SchemaContextMaxObjects` | `schemaContextMaxObjects` | int | 500 | FR-026 — the explicit, documented, configurable budget |

**Canonical provider ids** (FR-013, R8). Stored value is always one of:
`anthropic`, `openai`, `azure`, `gemini`, `kimi`, `ollama`, `lmstudio`, `custom`, or `""` (none).

**Alias table** — accepted on read, normalised to canonical, never written:

| Legacy / display form | Canonical |
|---|---|
| `AzureOpenAI`, `Azure OpenAI`, `azureopenai` | `azure` |
| `LMStudio`, `LM Studio` | `lmstudio` |
| `Moonshot`, `Kimi (Moonshot)` | `kimi` |
| any of the canonical ids in any casing | itself |

**Validation rules**

- V1 (FR-013): a `Provider` that resolves through the alias table to no canonical id is rejected by the factory with the existing "Unknown AI provider" message, which must list the canonical ids.
- V2 (FR-012): when `Provider` is a first-party cloud (`anthropic`, `openai`, `gemini`, `kimi`) and `AiModelFamily.Detect(Model)` returns a *different* non-null family, the request is refused before any network call, naming both vendors.
- V3 (FR-008): `ApiKey` is written only via `ApiKeyProtector.Protect`. Reads accept both wrapped and legacy plaintext (`ApiKeyProtector.Unprotect` passes unprefixed values through), so no migration step is required.
- V4: `ApiKey` must never appear in a log statement or a diagnostics bundle. The existing `Log.Debug` in `AiProviderTestHandler` logs provider/model/`hasEndpoint` only — that shape is the standard to follow.
- V5 (FR-007): selecting a provider in the UI fills `Model` and `Endpoint` with that provider's defaults **only** when the current value is empty or belongs to a foreign family; a user's unrecognised value is never overwritten (existing behaviour at `AiAssistancePage.cs:36-44`).

---

## 2. Schema Context

**Where it lives**: `SchemaContext` in `AkmlSql.Core.Models.Ai`, built by `src/AkmlSql.AI/Context/SchemaContextBuilder.cs`, rendered by `SchemaContextFormatter`.

| Field | Type | Notes |
|---|---|---|
| `DatabaseName` | string | Empty when unbound — the "no connection" signal |
| `CompressionLevel` | int 1–4 | Selects the formatter's rendering |
| `Objects` | list of object summaries | Schema, name, type, row count, and — at level ≥ 2 — columns |
| `ForeignKeys` | list | Relationships **between included objects only** |

**New fields**

| Field | Type | FR | Notes |
|---|---|---|---|
| `Truncated` | bool | FR-026 | True when the inventory did not fit the budget |
| `TotalObjectCount` | int | FR-026 | The full count, so the model can say "showing 500 of 1,842" |
| `DetailedObjectNames` | set of string | FR-025 | Which objects were promoted to full detail |

**Assembly rule** (FR-022 → FR-026, replaces the filter-then-cap behaviour):

```
1. inventory  := every object in the cache            (level 1 detail: schema, name, type, rows)
2. named      := objects whose name a prompt token matches
3. expanded   := named ∪ (1-hop FK neighbours of named)
4. promote    := expanded, rendered at level 3        (columns, PK, indexes, FK lines)
5. if |inventory| > budget:
       keep all of `promote`, fill the remainder from `inventory`
       set Truncated = true, TotalObjectCount = |inventory|
6. render: level-1 lines for inventory, level-3 blocks for promote
```

**Validation rules**

- V6 (FR-024/FR-025): step 2 may only *promote*. There is no path by which a non-empty `named` set removes an object from `inventory`. This is the inversion of today's `FilterByRelevance`.
- V7 (FR-023): every object in `promote` carries columns with type and nullability, its PK column list, and its FK relationships.
- V8 (FR-028): when `DatabaseName` is empty the formatter must emit an explicit "no database connection" statement, not the current ambiguous `"(No schema objects available)"` — the two states must be distinguishable by the model and by the user.
- V9 (FR-032): no row data. Only `sys.*` metadata reaches the context.
- V10 (FR-030): privacy transformation is applied *after* assembly, unchanged. When `IdentifierMap` is non-empty the panel surfaces the reason and names `privacyMode`.

**Per-feature detail levels** (FR-031) — replaces the hardcoded `2` in seven handlers:

| Feature | Level | Reason |
|---|---|---|
| Chat, Text-to-SQL, Optimize, Index analysis | 3 | Need keys and relationships to produce correct joins and index advice |
| Explain, Fix | 3 | Need column types to explain and correct predicates |
| Ghost text | 1 | Latency-critical inline completion; names are enough |

---

## 3. Editor Session Binding

**Not a persisted entity** — the runtime link that R1 identifies as broken. Recorded here because every FR-021 test asserts against it.

| Element | Owner | Value |
|---|---|---|
| `AkmlSqlSessionId` | Editor text buffer property | Set once per view by `TextViewCreationListener.cs:37` |
| `ConnectionInfo{SessionId, ConnectionString, DatabaseName, ServerVersion, EngineEdition}` | Shell → Engine notification | Sent by `ConnectionWiringHelper.cs:294-303` |
| `SessionState{SessionId, ConnectionString, DatabaseName, IsConnected}` | Engine `SessionManager` | Created with `IsConnected = true` at `SessionManager.cs:29-40` |

**State**

| State | Meaning | Required behaviour |
|---|---|---|
| **Unbound** | No active managed editor view | FR-028: assistant states it has no connection; no request carries a fabricated id |
| **Bound, loading** | Session exists; cache phase is A or in-progress B | FR-029: user told schema is still loading; answer uses it once available |
| **Bound, ready** | Session connected; cache populated | Normal operation |
| **Rebound** | Active window or database changed | FR-027: header updates; the next request uses the new id |

**Validation rules**

- V11 (FR-021): a schema-aware request must carry an id that came from a buffer property. Generating one is prohibited on the AI path. `RefactorCommandHelper`'s existing `Guid.NewGuid()` fallback stays for refactoring but must not be reachable from AI callers (R2).
- V12: rebinding is evaluated at send time, not cached at panel construction.

---

## 4. Chat Message

**Where it lives**: `ChatTurnDto{Role, Content}` in the panel's `_history` list; rendered as a bubble by `AiChatPanel.CreateMessageBubble`.

| Element | Today | After |
|---|---|---|
| Text host | `TextBlock` (not selectable) | Read-only transparent `TextBox` (FR-017) |
| Per-message copy | Exists (`OnCopyMessageClick`) | Preserved, both roles (FR-016) |
| Per-SQL copy | Exists, from `response.CodeActions` | Preserved, labelled per block (FR-015) |
| Conversation copy | Absent | New, built from `_history` (FR-018) |
| Feedback | "✓ Copied" for 1.5 s | Preserved; failure path added (FR-019) |

**Validation rules**

- V13 (FR-015): SQL copy carries the block body only — no prose, no ``` fences.
- V14 (FR-018): the conversation copy attributes every turn to its speaker and preserves order.
- V15 (FR-019): a clipboard exception leaves the bubble on screen and re-copyable; the current handler already returns early on exception but is silent — it must tell the user.
- V16 (FR-020): every copy control is keyboard-reachable and carries an `AutomationProperties.Name` (the existing per-message button already does).

---

## 5. Release Record

**Where it lives**: `src/AkmlSql.Site/wwwroot/releases.json`, deserialised into `Release` (`src/AkmlSql.Site/Releases/Release.cs`). Written by `scripts/deploy-site-iis.ps1:158-172`. **Shape unchanged by this feature** — it becomes the single source the update manifest is generated from.

| Field | Type | Used by the update channel |
|---|---|---|
| `version` | string, 4-segment | Yes — compared against the installed version |
| `releasedAt` | date | No |
| `supportedHosts` | string[] | No |
| `downloadUrl` | relative path | Fallback when `cdnUrl` is null |
| `sha256Hash` | hex, lowercase | Yes — FR-040 integrity check |
| `releaseNotesUrl` | absolute URL | Yes — FR-038 |
| `notesSummary` | string | Yes — shown in the notification |
| `minimumOsVersion` | string | Yes — carried through |
| `cdnUrl` | absolute URL or null | Yes — preferred download source |

**Invariant** (FR-036): the newest entry in `releases.json` and the generated update manifest describe the same version, file and hash. Enforced by a test in `AkmlSql.Site.Tests`, not by discipline.

---

## 6. Update Outcome

**Where it lives**: `UpdateResult` (`src/AkmlSql.Core/Update/UpdateResult.cs`), written atomically to `%AppData%\AKML SQL\cache\update-available.json`.

| Field | Type | Notes |
|---|---|---|
| `Available` | bool | Newer version found |
| `Version` | string | From the manifest |
| `DownloadUrl` | string | CDN URL when present |
| `ReleaseNotesUrl` | string | FR-038 |
| `Sha256Hash` | string | Present today, **used by nobody** — FR-040 changes that |
| `CheckedAt` | DateTimeOffset | Throttle bookkeeping |

**New fields**

| Field | Type | FR | Notes |
|---|---|---|---|
| `VerifiedInstallerPath` | string? | FR-039/FR-040 | Absolute path, set only after the hash matches |
| `DownloadState` | string | FR-039a | `none` / `downloading` / `verified` / `failed` |
| `FailureReason` | string? | FR-041 | Populated on `failed`; surfaced only for a manual check |

**State machine**

```
        idle ──(--check finds newer)──> available
                                            │
                        (user: Update now)  ▼
                                       downloading ──(cancel)──> available   [no partial file]
                                            │
                                    (bytes complete)
                                            ▼
                                        verifying
                                     ┌──────┴──────┐
                          (hash ok)  ▼             ▼  (hash mismatch)
                                 verified        failed  [file deleted, explicit message]
                                     │
                       (user confirms, hosts named)
                                     ▼
                                 launching ──> installer UI ──> installed
                                     │
                            (user declines)
                                     ▼
                                 verified  [nothing installed, offer retained]
```

**Validation rules**

- V17 (FR-037): `Available` is true only when the manifest version is *strictly* newer. `IsNewerVersion` already strips SemVer pre-release tags before comparison — keep that.
- V18 (FR-040): `VerifiedInstallerPath` is set only after a matching SHA-256, and is canonicalised with `Path.GetFullPath()` before launch.
- V19 (FR-039a): cancel or hash failure deletes the partial file and returns to `available`.
- V20 (FR-041): an automatic check that fails writes nothing user-visible; the reason goes to the log. A manual check surfaces it.
- V21: the result file write stays atomic (temp + `File.Move(overwrite: true)`), as today.
