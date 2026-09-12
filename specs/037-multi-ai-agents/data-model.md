# Phase 1 Data Model: Multiple AI Agents with Guided Setup

**Feature**: `037-multi-ai-agents` | **Date**: 2026-09-07

All types are `netstandard2.0`-safe: plain `{ get; set; }` properties, no `init` accessors, no
records, no nullable-reference-only constructs that the net472 shell cannot compile. They follow
`AppSettings.cs` conventions — `[JsonPropertyName]` on every property, defaults in the initialiser.

---

## E1 — `AiAgent`

**Assembly**: `AkmlSql.Core`, namespace `AkmlSql.Core.Config`, file `Config/AiAgent.cs`
**Persisted at**: `ai.agents[]`
**Represents**: one saved AI configuration the user can name, pick, and test.

| Property | JSON | Type | Default | Notes |
|---|---|---|---|---|
| `Id` | `id` | `string` | `""` (assigned at creation) | 32 lowercase hex, `Guid.NewGuid().ToString("N")`. Immutable. |
| `Name` | `name` | `string` | `""` | User-facing. 1–40 chars after trim, unique case-insensitively. |
| `Provider` | `provider` | `string` | `""` | Canonical id from `AiProviderIds.CanonicalIds`. |
| `Model` | `model` | `string` | `""` | Free text. |
| `ApiKey` | `apiKey` | `string` | `""` | `dpapi:`-wrapped, or legacy plaintext on read. |
| `Endpoint` | `endpoint` | `string` | `""` | Absolute URL when present. |
| `MaxTokens` | `maxTokens` | `int` | `4096` | 256 … 32768. |
| `Temperature` | `temperature` | `double` | `0.2` | 0.0 … 2.0. |
| `Timeout` | `timeout` | `int` | `30` | 5 … 300 seconds. |
| `Retries` | `retries` | `int` | `2` | 0 … 5. |
| `Enabled` | `enabled` | `bool` | `true` | Disabled agents are hidden from the picker and treated as absent by assignments and the fallback order. |
| `CreatedUtc` | `createdUtc` | `string` | `""` | ISO 8601 UTC. Informational; ties broken by list order. |
| `Health` | `health` | `AgentHealth?` | `null` | `null` ≡ never tested. |

**Relationships**: referenced by `AiSettings.ActiveAgentId`, by each field of
`FeatureAgentAssignments`, and by entries of `AiSettings.FallbackOrder` — always by `Id`, never by
`Name` (V5).

---

## E2 — `AgentHealth`

**Persisted at**: `ai.agents[].health`
**Represents**: the last recorded outcome of checking an agent. Advisory only — never gates a request.

| Property | JSON | Type | Default | Notes |
|---|---|---|---|---|
| `Status` | `status` | `string` | `"unknown"` | One of `unknown`, `ready`, `needsKey`, `failed`. Compared case-insensitively on read; written lowercase. |
| `CheckedUtc` | `checkedUtc` | `string?` | `null` | ISO 8601 UTC of the last check. |
| `LatencyMs` | `latencyMs` | `int` | `0` | Round trip of the last **successful** check. |
| `Message` | `message` | `string` | `""` | User-facing summary, ≤ 500 chars, never contains a key. |

`AgentHealthStatus` is a `static class` of string constants rather than an `enum`, matching
`AiProviderIds` — it round-trips through JSON as text and tolerates unknown values by falling back
to `unknown`.

---

## E3 — `FeatureAgentAssignments`

**Persisted at**: `ai.featureAgents`
**Represents**: which agent serves each AI feature. Every field holds an agent `Id`, or `""` meaning
"follow the active agent".

| Property | JSON | Default |
|---|---|---|
| `Chat` | `chat` | `""` |
| `TextToSql` | `textToSql` | `""` |
| `Explain` | `explain` | `""` |
| `Fix` | `fix` | `""` |
| `Optimize` | `optimize` | `""` |
| `IndexSuggestions` | `indexSuggestions` | `""` |
| `GhostText` | `ghostText` | `""` |

---

## E4 — `AiFeature`

**Assembly**: `AkmlSql.Core`, file `Config/AiFeature.cs`
**Represents**: the seven assignable features, as an `enum` used only in memory (never serialised).

```
Chat, TextToSql, Explain, Fix, Optimize, IndexSuggestions, GhostText
```

Each engine handler declares its own value; `AiAgentResolver.ResolveFor` maps it to the matching
field of `FeatureAgentAssignments`.

---

## E5 — `AiSettings` (extended)

**Persisted at**: `ai`

**Added**:

| Property | JSON | Type | Default | Notes |
|---|---|---|---|---|
| `Agents` | `agents` | `List<AiAgent>` | `[]` | 0 … 20. List order is display order. |
| `ActiveAgentId` | `activeAgentId` | `string` | `""` | Resolved by V13 when it names no agent. |
| `FeatureAgents` | `featureAgents` | `FeatureAgentAssignments` | all `""` | |
| `FallbackOrder` | `fallbackOrder` | `List<string>` | `[]` | Agent ids, tried in order. |

**Kept and now derived**:

| Property | Derivation | Written by |
|---|---|---|
| `Provider`, `Model`, `ApiKey`, `Endpoint`, `MaxTokens`, `Temperature`, `Timeout`, `Retries` | Copied from the active agent; all `""`/defaults when there is no active agent; **left untouched when the agent list is empty** (V18) | `MirrorActiveAgent` — on **every load and every save**, so the mirror holds on disk as well as in memory |
| `Enabled` | `true` iff at least one **usable** agent exists (S1) | `Normalize` — on **load only** (V19) |

**Kept and unchanged** (global, never per agent — FR-033): `PrivacyMode`,
`PrivacyConsentRequired`, `SchemaContextMaxObjects`, `TextToSql`, `Explain`, `Fix`, `Optimize`,
`IndexSuggestions`, `ChatPanel`, `InlineCompletion`, `AutoFixOnError`, `OfflineProvider`,
`OfflineModel`, `OfflineEndpoint`, and every shortcut / ghost-text field.

---

## E6 — `AiAgentResolver`

**Assembly**: `AkmlSql.Core`, file `Config/AiAgentResolver.cs` — a `static class`, no state.

| Member | Signature | Responsibility |
|---|---|---|
| `Normalize` | `void Normalize(AiSettings)` | Migrate (V14), drop malformed entries (V15), repair `ActiveAgentId` (V13), clear dangling assignments and fallback ids (V16, V17), call `MirrorActiveAgent` (V18), derive `Enabled` (V19). **Idempotent.** Load path only. |
| `MirrorActiveAgent` | `void MirrorActiveAgent(AiSettings)` | The mirroring step of V18 on its own, so the write path can hold the invariant without running load-time repairs. No-op when `Agents` is empty. **Idempotent.** Called by `Normalize` and by `ConfigManager.Save`. |
| `Project` | `AiSettings Project(AiSettings global, AiAgent agent)` | A copy of `global` with the agent's connection fields and request parameters substituted; every global concern preserved. |
| `ResolveFor` | `AiAgent? ResolveFor(AiSettings, AiFeature)` | The assigned agent if usable, else the active agent if usable, else `null`. |
| `Active` | `AiAgent? Active(AiSettings)` | The agent named by `ActiveAgentId`, if usable. |
| `IsUsable` | `bool IsUsable(AiAgent)` | S1 below. |
| `RequiresApiKey` | `bool RequiresApiKey(string providerId)` | `true` for `anthropic`, `openai`, `azure`, `gemini`, `kimi`. |
| `RequiresEndpoint` | `bool RequiresEndpoint(string providerId)` | `true` for `azure`, `custom`. |
| `SuggestName` | `string SuggestName(IEnumerable<AiAgent>)` | `"Agent N"`, lowest free `N ≥ 1`. |
| `SuggestCopyName` | `string SuggestCopyName(IEnumerable<AiAgent>, string)` | `"<name> (copy)"`, then `" (copy 2)"` … |

---

## Validation rules

Rules V1–V12 are enforced by the Options page before OK is accepted (FR-032). Rules V13–V21 are
enforced by `AiAgentResolver.Normalize` on load, silently and idempotently. V22–V24 are runtime.

### Edit-time (dialog refuses OK, naming the agent and the field)

| # | Rule |
|---|---|
| **V1** | `Name` is non-empty after trim. |
| **V2** | `Name` is at most 40 characters after trim. |
| **V3** | `Name` is unique across the list, compared case-insensitively after trim. |
| **V4** | `Provider` is one of `AiProviderIds.CanonicalIds`. |
| **V5** | `Id` is non-empty, 32 hex characters, and unique. Never edited by the user. |
| **V6** | `Model` is non-empty. |
| **V7** | `Model`'s detected family (`AiModelFamily.Detect`) is either `null` or equal to the provider's family, for `anthropic`, `openai`, `gemini`, `kimi`. Local and custom providers are exempt. |
| **V8** | `ApiKey` is non-empty when `RequiresApiKey(Provider)` — **unless** the agent's key failed to decrypt (V23), in which case the stored value stands and the agent is reported `needsKey`. |
| **V9** | `Endpoint` is non-empty when `RequiresEndpoint(Provider)`. |
| **V10** | `Endpoint`, when non-empty, parses as an absolute URI. |
| **V11** | `MaxTokens` ∈ [256, 32768]; `Temperature` ∈ [0.0, 2.0]; `Timeout` ∈ [5, 300]; `Retries` ∈ [0, 5]. |
| **V12** | The list holds at most 20 agents; the 21st `Add`/`Duplicate` is refused with a message naming the limit. |

### Load-time (`Normalize`, silent and idempotent)

| # | Rule |
|---|---|
| **V13** | If `ActiveAgentId` names no agent in the list, it becomes the `Id` of the first usable agent, or `""` when there is none. |
| **V14** | **Migration.** If `Agents` is empty and `Provider` is non-empty, create exactly one agent from the flat fields, named for its provider's display name, `Enabled = true`, `Health = null`, and set `ActiveAgentId` to it. `ApiKey` is carried across **verbatim** — never unwrapped, re-wrapped, or re-encrypted. |
| **V15** | An agent with an empty `Id`, a duplicate `Id`, or an unparseable entry is dropped, with a `Log.Warning` naming the index. The rest load. |
| **V16** | A `FeatureAgents` field naming no usable agent is reset to `""`. |
| **V17** | A `FallbackOrder` entry naming no usable agent is removed. Duplicates are removed, keeping the first. The active agent's own id is removed if present. |
| **V18** | **Mirroring.** The flat `Provider`/`Model`/`ApiKey`/`Endpoint`/`MaxTokens`/`Temperature`/`Timeout`/`Retries` are overwritten from the active agent; blanked when the list is non-empty but no agent is active; **left untouched when `Agents` is empty** (that is the pre-migration shape V14 rescues — blanking it would destroy the configuration). Performed by `MirrorActiveAgent`, which also runs on the **save** path so the invariant holds on disk, not only in memory. |
| **V19** | `Enabled` becomes `true` iff at least one agent is usable (S1). |
| **V20** | A `Health.Status` outside the four known values becomes `unknown`. |
| **V21** | `Agents` beyond the 20th are dropped with a `Log.Warning` (defends a hand-edited file; the dialog cannot produce this). |

### Runtime

| # | Rule |
|---|---|
| **V22** | A feature whose assigned agent has become unusable falls back to the active agent and notifies **once per feature per engine process**, not per request. The engine process is the scope because that is where resolution happens and where the "already told them" flag lives; an engine restart re-notifies, which is correct — the condition is still true and the user has a fresh log. |
| **V23** | An agent whose stored key cannot be unwrapped on this machine keeps its stored value. A save in which the user did not type into that agent's key box must not overwrite it. Typing clears the guard and the notice together. |
| **V24** | No `Message`, log entry, status line, error, or copied text may contain an API key. |

---

## S1 — State: agent usability

An agent is **usable** exactly when all of:

```
Enabled == true
Provider ∈ AiProviderIds.CanonicalIds  and  Provider != ""
Model    != ""
(RequiresApiKey(Provider)  →  ApiKey != "")
(RequiresEndpoint(Provider) →  Endpoint != "")
```

The chat empty state (FR-015) fires exactly when **no** agent is usable. `AiSettings.Enabled` is the
negation of that condition (V19), which keeps `AiCommandVisibility` correct with no change.

```
         ┌───────────────────────────── usable ──────────────────────────────┐
         │  answers requests · appears in the chat picker · assignable       │
         └────────────────────────────────────────────────────────────────────┘
              ▲                                                    │
              │ key/model/endpoint supplied, or re-enabled          │ disabled, or a required
              │                                                     ▼ field cleared
         ┌────────────────────────── not usable ─────────────────────────────┐
         │  hidden from the picker · treated as absent by assignments and    │
         │  the fallback order · shown in Options with its specific reason   │
         └────────────────────────────────────────────────────────────────────┘
```

---

## S2 — State: agent health

```
                    ┌──────────┐
      ┌────────────▶│ unknown  │◀──────── provider / model / key / endpoint edited
      │             └────┬─────┘          (from ANY state — R10)
      │                  │
      │      ┌───────────┼─────────────┐
      │      │           │             │
      │  test ok    cloud agent    test fails
      │      │      with no key        │
      │      ▼           ▼             ▼
      │  ┌───────┐  ┌──────────┐  ┌────────┐
      └──│ ready │  │ needsKey │  │ failed │
         └───┬───┘  └────┬─────┘  └───┬────┘
             │           │            │
             │ key removed│            │ test ok
             └───────────▶│            └──────────▶ ready
             │                                     
             └── live request fails on configuration ──▶ failed
```

`needsKey` is computed locally without any network request (FR-055), so it is correct the moment the
list renders. `ready` is never a precondition for sending — a stale badge informs, it does not gate.

---

## S3 — State: which agent answers a chat message

```
resolve_chat_agent(settings):
    assigned := settings.FeatureAgents.Chat
    if assigned != "" and agent(assigned) is usable  -> agent(assigned)
    else if Active(settings) is usable               -> Active(settings)
    else                                             -> NONE  (empty state, FR-015)
```

The chat picker writes `FeatureAgents.Chat` (research R7). Selecting the agent that is already
active clears it to `""` so the panel keeps following the active agent.

---

## Persistence and concurrency

- Agents are written as part of the existing atomic `AppSettings` save
  (`ConfigManager.Save` — temp file + `File.Replace` / `File.Move(overwrite:true)`).
- `Normalize` runs inside both `ConfigManager.Load()` overloads, so every reader — shell, engine,
  web service — sees a consistent, migrated `AiSettings`.
- `MirrorActiveAgent` runs inside `ConfigManager.Save`, so the flat fields on **disk** match the
  active agent the moment the write completes — not only after the next load. Without this, anything
  reading the JSON directly (an older build after a downgrade, a second host that has not reloaded,
  a support engineer) would see the previously active agent.
- Two hosts saving concurrently: last write wins, whole-section. A conversation already in flight
  keeps the agent it resolved and picks up the change on its next message.
- Downgrade: an older build reads the mirrored flat fields and ignores `ai.agents` entirely.
