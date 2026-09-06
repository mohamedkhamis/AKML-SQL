# Contract: which agent serves a request, and what happens when it fails

Satisfies **FR-046 – FR-052**. Engine-side. This is the contract that makes per-feature assignment
real without touching seven handlers' logic.

**Anchors**: `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs:52` · `src/AkmlSql.Engine/Ai/AiPipelineServices.cs:113` · `src/AkmlSql.AI/Providers/AiProviderFactory.cs:59,90`

---

## Part 1 — Resolution

**The rule**: resolution happens **once**, in the handler base, **before** the privacy-consent gate.

### Where

`AiHandlerBase.HandleAsync` already does this (`AiHandlerBase.cs:52`):

```
settings = Services.SettingsProvider()
CheckPrivacyConsent(settings)
...
InvokeAsync(request, ctx, settings, sw, ct)
```

It becomes:

```
global   = Services.SettingsProvider()
agent    = AiAgentResolver.ResolveFor(global, this.Feature)      // NEW
settings = agent == null ? global : AiAgentResolver.Project(global, agent)
CheckPrivacyConsent(settings)                                    // ← now sees the RIGHT provider
...
InvokeAsync(request, ctx, settings, sw, ct)                      // subclasses unchanged
```

Each concrete handler adds one line — `public override AiFeature Feature => AiFeature.Chat;` — and
nothing else.

### The ordering is load-bearing

`CheckPrivacyConsent` allow-lists the local providers (`ollama`, `lmstudio`) and otherwise honours
`PrivacyConsentRequired`. Consent must be evaluated against **the provider that will actually be
called**. Projecting after the gate would let a cloud agent assigned to ghost text slip through a
consent check performed against a local active agent. There is a test for exactly this.

### The algorithm

```
ResolveFor(settings, feature):
    assigned := settings.FeatureAgents[feature]
    if assigned != "" and agent(assigned) is usable   -> agent(assigned)
    if Active(settings) is usable                     -> Active(settings)
    else                                              -> null
```

`null` means "no usable agent". The handler then runs against the unprojected global settings, whose
`Enabled` flag is `false` (V19), so `AiChatHandler`'s existing
`if (!settings.Enabled) throw new InvalidOperationException("AI assistance is disabled")` fires and
the shell surfaces the empty state. No new error path.

### Falling back to the active agent (FR-049, V22)

When `assigned != ""` but that agent is missing or unusable, the resolver returns the active agent
and the engine logs it at `Information` **once per feature per engine process** — not per request. A
user with ghost text assigned to a deleted agent must not get a notification storm at every
keystroke. The shell surfaces this the next time it reads settings, because `Normalize` clears the
dangling assignment on load (V16).

---

## Part 2 — The fallback chain

**The rule**: relevance of order is the user's; the engine only walks the list.

### Today

`AiPipelineServices.ExecuteWithFallbackAsync` (`:113`) tries the primary, and on a non-cancellation,
non-consent exception tries `CreateFromFallback` if `OfflineProvider` is set.

### Required

```
attempt(agent):
    client   := AiProviderFactory.Create(Project(global, agent))
    response := ExecuteWithBackoffAsync(client, agent.Retries,
                                        retryBudget = max(30s, agent.Timeout))
    return response

chain := [selected agent] + [usable agents named by settings.FallbackOrder, in order]

for each candidate in chain:
    try:  return (attempt(candidate), usedFallback: candidate != chain[0], agentName: candidate.Name)
    catch OperationCanceledException:            rethrow          // never falls back
    catch PrivacyConsentRequiredException:       rethrow          // never falls back
    catch:                                       log warning, continue

if settings.OfflineProvider is set:
    return (CreateFromFallback(global), usedFallback: true, agentName: settings.OfflineProvider)

throw AggregateException naming every attempt and its failure
```

### Invariants

1. **Cancellation and consent never trigger a fallback.** The existing `catch … when` filter already
   encodes this; it must survive verbatim.
2. **The retry budget stays per attempt**, bounded by that agent's `Timeout`. Retries must never let
   one logical request outlive the deadline the shell's IPC wait is built around.
3. **The chain stops at the first success.**
4. **The offline provider stays last**, so a configuration that sets only `offlineProvider` behaves
   exactly as it does today (FR-051). `FallbackOrder` defaults to empty, so nothing changes for
   existing users.
5. **The selected agent never appears in its own fallback chain** — `Normalize` removes the active
   agent's id from `FallbackOrder` (V17), and the loop skips a candidate equal to `chain[0]`.
6. **A failing candidate's exception is logged, never surfaced alone.** The user sees the outcome of
   the chain, not a stack of intermediate failures.

---

## Part 3 — Attribution (FR-052)

`ExecuteWithFallbackAsync` returns the name of the agent that produced the response. `AiChatHandler`
puts it on the response:

```
AiChatResponse:
    [Key(6)] public string? AgentName { get; set; }
```

MessagePack with explicit keys is additive-safe: an older shell ignores key 6; a newer shell reading
an older engine gets `null` and falls back to showing no attribution rather than a wrong one.

Only `AiChatResponse` carries this. The other six features render into surfaces with no attribution
line, and adding an unused key to seven DTOs would be speculative.

When `usedFallback` is true, the shell states it plainly — "answered by *Kimi* because *Claude
(work)* was unavailable" — rather than silently showing a different name than the picker.

---

## What must not change

| Thing | Why |
|---|---|
| `AiHandlerBase`'s three catch blocks | Consent / cancellation / generic error mapping is shared by all seven handlers |
| `SettingsProvider()` being called per request | The "settings read fresh every call" invariant is what makes a settings change take effect with no restart |
| `ExecuteWithBackoffAsync`'s rate-limit handling | Orthogonal to agent choice; a 429 is a retry, not a fallback |
| `AiProviderFactory.Create`'s family guard | It is the last line of defence against a Claude model reaching Google's API |

---

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| Each feature uses its assigned agent | `tests/AkmlSql.Engine.Tests/Ai/AgentFeatureResolutionTests.cs` | All seven features, each with a distinct assigned agent, resolve to it (FR-048) |
| Unassigned features follow the active agent | same | FR-047 |
| Changing the active agent moves unassigned features | same | FR-047, no restart |
| An assigned agent that is deleted falls back | same | FR-049 — active agent used |
| An assigned agent that is disabled falls back | same | FR-049 — same treatment as deleted |
| Consent is checked against the resolved provider | same | A cloud agent assigned to a feature trips consent even when the active agent is local — the ordering test |
| No usable agent ⇒ "AI assistance is disabled" | same | Existing error path, not a new one |
| Fallback chain order is honoured | `AgentFallbackChainTests.cs` | Primary fails ⇒ second tried ⇒ third tried, in `FallbackOrder` order |
| Chain stops at first success | same | Later candidates are never constructed |
| Cancellation does not fall back | same | Existing filter preserved |
| Consent exception does not fall back | same | Existing filter preserved |
| Offline provider remains the last resort | same | FR-051 — a config with only `offlineProvider` behaves as today |
| Response carries the answering agent's name | same | FR-052, including the fallback case |
| Each attempt is bounded by its own agent's timeout | same | A 5 s agent does not inherit a 300 s agent's budget |
