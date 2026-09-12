# Contract: agent storage, migration and mirroring

Satisfies **FR-001 – FR-014**. This is the contract that lets a list of agents replace a scalar
configuration without breaking a single existing consumer.

**Anchors**: `src/AkmlSql.Core/Config/AppSettings.cs:1126` · `src/AkmlSql.Core/Config/ConfigManager.cs:28,60,88` · `src/AkmlSql.Core/Config/ApiKeyProtector.cs` · `src/AkmlSql.AI/Providers/AiProviderFactory.cs:59,104`

---

## The rule

> **`ai.agents` is the truth. The flat `ai.provider` / `ai.model` / `ai.apiKey` / `ai.endpoint`
> fields are a derived mirror of the active agent, rewritten on every load and every save.**

Everything else in this contract follows from that sentence. It is what makes the change additive:
no provider code changes, the web service is unaffected, and an older build reads a newer config
correctly.

---

## Where normalisation runs

```
ConfigManager.Load()        ─┐
                             ├─▶ deserialize ─▶ AiAgentResolver.Normalize(settings.Ai) ─▶ return
ConfigManager.Load(path)    ─┘

ConfigManager.Save(settings) ─▶ AiAgentResolver.MirrorActiveAgent(settings.Ai) ─▶ serialize ─▶ atomic write
```

Three call sites. `Normalize` runs on the two reads; `MirrorActiveAgent` — the mirroring step
factored out of `Normalize`, and nothing else — runs on the write.

**Why the write needs it too**: mirroring on load alone leaves the *file* carrying stale flat
fields between a save and the next load. Anything that reads the JSON without going through
`ConfigManager` — an older build after a downgrade, a support engineer reading the file, a second
host that has not reloaded — would see the previously active agent. The mirror is a persisted
invariant, not an in-memory convenience, so it must hold on disk.

**Save must not call `Normalize`.** Save's job is to persist what it was handed. Migration,
malformed-entry dropping and dangling-id repair are load-time concerns; running them on the write
path would silently rewrite a caller's settings and make Save's behaviour depend on state the
caller cannot see.

**No other code may call `Normalize` as a precondition** — if a caller needs it, that caller got
its settings from somewhere other than `ConfigManager`, which is itself the bug.

`Normalize` **must be idempotent**. `ConfigManager.Load()` is called many times per session (the
chat panel, `AiCommandVisibility`, every engine request through `SettingsProvider`). Running it
twice must produce the same result as running it once — the migration branch is guarded on
`Agents.Count == 0`, which is what makes this true.

`Normalize` **must not throw**. `Load` already returns defaults on any failure; normalisation must
not turn a recoverable config into a lost one. A malformed agent is dropped with a warning (V15),
never propagated.

---

## Migration (FR-010, V14)

```
if settings.Agents is empty and settings.Provider is not empty:
    agent := new AiAgent {
        Id         = Guid.NewGuid().ToString("N"),
        Name       = display name for Normalize(settings.Provider),   // "Anthropic", "Kimi (Moonshot)", …
        Provider   = AiProviderIds.Normalize(settings.Provider),
        Model      = settings.Model,
        ApiKey     = settings.ApiKey,        // ← VERBATIM. Never unwrap. Never re-wrap.
        Endpoint   = settings.Endpoint,
        MaxTokens  = settings.MaxTokens,
        Temperature= settings.Temperature,
        Timeout    = settings.Timeout,
        Retries    = settings.Retries,
        Enabled    = true,
        CreatedUtc = now,
        Health     = null,
    }
    settings.Agents = [agent]
    settings.ActiveAgentId = agent.Id
```

**The key is copied as a string and never touched.** Unwrapping it during migration would fail on a
roamed profile and destroy a working key; re-wrapping it would be a no-op at best and a corruption
at worst. This is the single most dangerous line in the feature — `ApiKeyProtector`'s entropy
string `"AkmlSql-ApiKey-v1"` must also stay byte-for-byte identical.

**Name collision**: the display name comes from the provider, so a migration can only ever produce
one agent and cannot collide. If a future migration produces several, `SuggestName` disambiguates.

**No prompt, no dialog, no user action** (FR-010). The user must not learn that migration happened.

---

## Mirroring (FR-013, V18)

`AiAgentResolver.MirrorActiveAgent(AiSettings)` — called by `Normalize` on load and by
`ConfigManager.Save` on write.

```
if settings.Agents.Count == 0:
    return                                    // ← the empty-list guard, see below

active := Active(settings)                    // usable agent named by ActiveAgentId, or null

if active != null:
    settings.Provider    = active.Provider
    settings.Model       = active.Model
    settings.ApiKey      = active.ApiKey       // still wrapped; the factory's KeyDecryptor unwraps
    settings.Endpoint    = active.Endpoint
    settings.MaxTokens   = active.MaxTokens
    settings.Temperature = active.Temperature
    settings.Timeout     = active.Timeout
    settings.Retries     = active.Retries
else:
    settings.Provider = settings.Model = settings.ApiKey = settings.Endpoint = ""
    // request parameters keep their current values — they are harmless without a provider
```

**The empty-list guard is load-bearing.** With no agents, mirroring would blank a caller's flat
fields — and a caller writing flat fields with no agent list is exactly the pre-migration shape that
`Normalize` converts on the next load (V14). Blanking it on save would destroy the very
configuration migration exists to rescue. So: no agents ⇒ leave the flat fields alone.

**Never mirrored, in either direction**: `PrivacyMode`, `PrivacyConsentRequired`,
`SchemaContextMaxObjects`, the per-feature enable switches, the offline provider fields, and every
shortcut setting. These are global (FR-033). A per-agent consent flag would let a user grant cloud
consent by accident while adding an agent.

**`Enabled` (V19) is derived in `Normalize` only**, not in `MirrorActiveAgent`. Save persists the
flag it was handed; the load pass re-derives it. Keeping the write path to one obligation is what
makes it safe to call on settings the caller assembled by hand.

---

## Projection (FR-048)

```
Project(global, agent) -> AiSettings:
    copy := shallow copy of global          // keeps privacy, consent, feature switches, offline fields
    copy.Provider    = agent.Provider
    copy.Model       = agent.Model
    copy.ApiKey      = agent.ApiKey
    copy.Endpoint    = agent.Endpoint
    copy.MaxTokens   = agent.MaxTokens
    copy.Temperature = agent.Temperature
    copy.Timeout     = agent.Timeout
    copy.Retries     = agent.Retries
    return copy
```

The result is handed to `AiProviderFactory.Create` unchanged. This is the same manoeuvre
`CreateFromFallback` already performs with the offline fields (`AiProviderFactory.cs:104-115`) —
follow it rather than inventing a parallel path.

**The copy must not alias the original's mutable members.** `Project` is called per request; a
handler mutating the projection must never reach the cached global settings.

---

## Derived `Enabled` (V19)

```
settings.Enabled = settings.Agents.Any(IsUsable)
```

`AiCommandVisibility` (`src/AkmlSql.Shell.Shared/Commands/AiCommandVisibility.cs:58`) reads
`settings.Ai.Enabled` to hide AI menu items, and `AiChatHandler` throws
`"AI assistance is disabled"` when it is false. Both stay correct with no change, because the flag's
*meaning* ("AI is usable") is preserved even though its *derivation* moves.

---

## What must not change

| Thing | Why |
|---|---|
| `ApiKeyProtector.EntropySource` = `"AkmlSql-ApiKey-v1"` | Every stored key becomes permanently unreadable if it changes |
| `AiProviderFactory.Create(AiSettings)` signature | Seven handlers, the test handler, and the web edition call it |
| `AiProviderIds.Normalize` alias table | Legacy configs (`AzureOpenAI`, `LMStudio`, `Kimi (Moonshot)`) must keep loading |
| The atomic write in `ConfigManager.Save` | Concurrent hosts; partial writes corrupt every setting, not just AI |
| The flat `ai.*` provider fields | The web service, downgrade, and hand-written configs |

---

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| Legacy config migrates to one agent | `tests/AkmlSql.Core.Tests/Config/AiAgentMigrationTests.cs` | One agent, named for the provider, active, key byte-identical (FR-010) |
| Migration is idempotent | same | Loading twice yields one agent, not two |
| Migration does not touch the key | same | A `dpapi:` blob is equal before and after, and is never passed to `Unprotect` |
| Empty config stays empty | same | No provider and no agents ⇒ no agents, `Enabled == false` (V19) |
| Legacy provider spellings migrate | same | `"AzureOpenAI"` ⇒ agent with `provider == "azure"` |
| Dangling active id repaired | `AiAgentResolverTests.cs` | V13 — first usable agent becomes active |
| Dangling assignments cleared | same | V16, V17 |
| Malformed agent dropped, rest survive | same | V15 — list loads, warning logged |
| Mirroring matches the active agent | same | V18, both branches (active present / absent) |
| Projection preserves global concerns | same | Privacy mode and consent come from global, connection fields from the agent |
| Projection does not alias | same | Mutating the projection leaves the original untouched |
| 21st agent refused | `AiAgentModelTests.cs` | V12 |
| Name uniqueness is case- and trim-insensitive | same | V3 — `"Kimi"` vs `"kimi "` |
| Usability predicate | same | S1 across all eight providers, including no-key-needed locals |
