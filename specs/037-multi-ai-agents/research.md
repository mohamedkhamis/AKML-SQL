# Phase 0 Research: Multiple AI Agents with Guided Setup

**Feature**: `037-multi-ai-agents` | **Date**: 2026-09-07 | **Spec**: [spec.md](./spec.md)

Every decision below was verified against the repository at the anchors given. Where the spec's
appendix and this document differ, this document is the later reading.

---

## R1 — Where the agent list lives

**Decision**: A new `AiAgent` class in `src/AkmlSql.Core/Config/`, held as `List<AiAgent> Agents`
on the existing `AiSettings` (`AppSettings.cs:1126`), serialised inside the existing
`config.json` under `ai.agents`.

**Rationale**: `ConfigManager` already writes `AppSettings` atomically (temp file + `File.Replace`
on netstandard2.0, `File.Move(overwrite:true)` on net10 — `ConfigManager.cs:88`). `AiSettings` is
already the single AI section read by the shell, the engine, and the web service. A second store
would violate Constitution V and would need its own atomicity, its own migration, and its own
concurrency story.

**Alternatives considered**:

- *A separate `ai-agents.json`, like `sql-credentials.json`*. Rejected: `SqlCredentialStore` is a
  separate file because credentials are keyed by server and are written outside the settings
  dialog's save cycle. Agents are edited exclusively in the Options dialog and saved with
  everything else — splitting them buys nothing and creates a two-file consistency problem for
  `activeAgentId`.
- *Reusing the web edition's `AiProviderConfig`* (`src/AkmlSql.Web/Services/IAiKeyVault.cs:56`).
  Rejected, and the reason is structural: that store is **keyed by `providerId`**
  (`AiKeyVault.SetKeyAsync` writes to `StoreNames.AiKeys` with `providerId` as the key), so it can
  hold at most one configuration per provider. A user cannot have "Claude (work)" and
  "Claude (personal)". It also lives in browser IndexedDB with Web Crypto AES-GCM wrapping, not in
  `config.json` with DPAPI. The desktop model needs a synthetic `id` plus a user-chosen `name`.

**Constraint discovered**: `AkmlSql.Core` dual-targets `netstandard2.0` + `net10.0`, and every
property in `AppSettings.cs` is a plain `{ get; set; }`. `AiAgent` must follow — `init` accessors
need `IsExternalInit`, which this assembly does not carry.

---

## R2 — Keeping existing consumers working (FR-013)

**Decision**: `AiSettings` keeps its flat `Provider` / `Model` / `ApiKey` / `Endpoint` /
`MaxTokens` / `Temperature` / `Timeout` / `Retries` fields, and they are **mirrored from the active
agent** on every save. Normalisation, migration and mirroring live in one new static helper,
`AiAgentResolver` (Core), invoked from `ConfigManager.Load()` and `ConfigManager.Load(string)`.

**Rationale**: `AiProviderFactory.Create` takes an `AiSettings`, not a provider name
(`AiProviderFactory.cs:59`). Every engine handler, the web service, and `AiProviderTestHandler`
read those flat fields. Mirroring means **zero** changes to provider construction, and an older
build opening a newer `config.json` still works because it simply ignores `ai.agents`.

Putting normalisation inside `ConfigManager.Load` gives exactly two call sites and guarantees no
reader ever sees a half-migrated `AiSettings` — including the web service, which uses the
`Load(string path)` overload (`ConfigManager.cs:60`).

**Alternatives considered**:

- *Delete the flat fields and change every consumer*. Rejected: it forces a coordinated change
  across the shell, the engine, the web service and the installer's default config, for no user
  benefit, and it breaks downgrade.
- *Normalise lazily at each read site*. Rejected: seven handlers plus two shell surfaces, each
  able to disagree.

**Migration is idempotent**: `Normalize` is safe to run on already-normalised settings, which it
must be — `ConfigManager.Load()` is called many times per session.

---

## R3 — Turning an agent into something the provider factory accepts

**Decision**: `AiAgentResolver.Project(AiSettings global, AiAgent agent) → AiSettings` returns a
copy of the global settings with the agent's provider, model, key, endpoint, and the four request
parameters substituted, and with every global concern (privacy mode, consent, schema budget,
feature switches) preserved.

**Rationale**: This is exactly the trick `AiProviderFactory.CreateFromFallback` already uses — it
builds a throwaway `AiSettings` from the offline fields and calls `Create` with it
(`AiProviderFactory.cs:104-115`). Following the established idiom means no provider code changes
and no new abstraction.

**Critical invariant**: privacy mode and `PrivacyConsentRequired` are copied from the **global**
settings, never from the agent. FR-033 and Assumption 4 exist because per-agent consent would let
a user grant cloud consent by accident while adding an agent.

---

## R4 — Which agent serves which request

**Decision**: Resolution happens once, in `AiHandlerBase.HandleAsync`
(`src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs:52`). The base already calls
`Services.SettingsProvider()` and hands the resulting `AiSettings` to `InvokeAsync`. It gains an
abstract `AiFeature Feature { get; }` and projects the assigned agent before the consent gate, so
all seven handlers inherit agent resolution with a one-line addition each.

**Rationale**: One seam, seven beneficiaries, no per-handler logic. The base class was created for
precisely this kind of shared concern (its own XML doc: "lifts ALL the boilerplate shared across
the pre-closure `HandleXxxAsync` methods").

**Alternatives considered**:

- *An `AgentId` field on every AI request DTO*. Rejected: eight DTO changes, and it makes the
  shell responsible for a decision the engine can make from settings both sides already share.
  It also breaks the "engine reads settings fresh on every call" invariant the base documents.
- *Resolution in `AiPipelineServices`*. Rejected: `AiPipelineServices` does not know which feature
  is calling; the handler does.

**Ordering matters**: the projection must run **before** `CheckPrivacyConsent`, because consent is
evaluated against the provider (local providers `ollama`/`lmstudio` are allow-listed). Projecting
afterwards would consent-check the wrong provider.

---

## R5 — The fallback chain

**Decision**: Extend `AiPipelineServices.ExecuteWithFallbackAsync` (`AiPipelineServices.cs:113`)
to walk `ai.fallbackOrder` in order after the primary agent fails transiently, and to fall through
to the existing `OfflineProvider` behaviour last. Its existing catch filter is preserved verbatim:
cancellation and `PrivacyConsentRequiredException` never trigger a fallback.

**Rationale**: FR-050 and FR-051 together mean "add a list in front of the existing single
fallback", not "replace it". Configurations that set only `offlineProvider` keep behaving exactly
as they do today; the new list is empty by default.

**Bound**: each fallback attempt gets its own agent's `Timeout`, and the chain stops at the first
success. Retry-on-rate-limit stays inside the primary attempt where it already is.

---

## R6 — Telling the user which agent answered

**Decision**: Add `[Key(6)] public string? AgentName { get; set; }` to `AiChatResponse`
(`src/AkmlSql.Core/Ipc/Messages/AiChatResponse.cs`). The engine sets it to the agent that actually
produced the answer — the fallback agent when a fallback was used.

**Rationale**: MessagePack with explicit `[Key(n)]` attributes is additive-safe: a peer that does
not know key 6 ignores it, and a peer expecting it gets `null` from an older peer. Keys 0–5 are
taken; 6 is the next free slot. The shell cannot derive attribution itself because it does not
know whether a fallback fired (FR-052).

**Scope**: only `AiChatResponse` needs it. The other six features render into surfaces that do not
show attribution, and adding an unused field to seven DTOs would be speculative generality.

---

## R7 — What the chat picker actually changes

**Decision**: The chat panel's picker writes `ai.featureAgents.chat`. Selecting the agent that is
already active clears the assignment back to `""` (follow the active agent) rather than pinning it.

**Rationale**: This is the GitHub Copilot behaviour the user named — the chat model picker changes
the model **for chat**; inline completions have their own setting. A picker that silently repointed
ghost text, Explain, Fix, Optimize and Text-to-SQL would be a surprise, and it would make US4
(per-feature assignment) unusable: assigning ghost text to a local model would be undone the moment
the user tried a different chat model.

**Alternatives considered**:

- *The picker sets `activeAgentId`*. Rejected for the reason above. It is the more "global" reading
  of the user's words but contradicts the Copilot comparison they made and fights US4.
- *Two controls in chat (an agent picker and a "make active" toggle)*. Rejected: the user asked for
  simple UI. Setting the active agent stays in Options, where it is a single click.

**Consequence for FR-037**: the picker's displayed selection is the *resolved* chat agent —
`featureAgents.chat` when set, otherwise the active agent — so a user who never touches it still
sees the correct name.

---

## R8 — Opening Options on the AI page from the chat panel

**Decision**: Two additions, both on existing types:

1. `SettingsWindow.ShowDialog(string? initialPageKey)` — an overload that calls the existing
   private `SelectTreeLeafByPageKey` (`SettingsWindow.cs:1023`) instead of selecting the first tree
   item, plus an `InitialAgentId` property so FR-021 can open on the offending agent.
2. `OptionsCommand.ShowOptions(string? pageKey, string? agentId)` — a static entry point holding
   the save-and-notify body that `Execute` already runs (`OptionsCommand.cs:44-72`), so the chat
   panel reuses it rather than duplicating `ConfigManager.Save` + the `AnalysisSettingsChanged`
   notification.

**Rationale**: `BuildWindowInner` (`SettingsWindow.cs:206`) unconditionally selects
`_navTree.Items[0]`, which is why no caller can currently deep-link. `SelectTreeLeafByPageKey`
already exists for the settings search box and does exactly the right thing including expanding the
parent group. This is reuse, not new machinery.

**Why the shared entry point matters**: if the chat panel called `ConfigManager.Save` itself and
skipped the `AnalysisSettingsChanged` notification (`OptionsCommand.cs:69`), the engine would keep
serving the old configuration and FR-019's "no restart" would silently fail.

---

## R9 — How the chat panel notices that configuration changed

**Decision**: The panel reads AI configuration through a 5-second cached read, refreshed inside the
existing `_bindingTimer` tick (`AiChatPanel.cs:243`, 2 s interval), plus an immediate refresh when
the panel loads and when an Options dialog it opened returns.

**Rationale**: The panel already polls every 2 seconds for its editor binding and already reads
`ConfigManager.Load()` on a 30-second cache for the privacy note (`AiChatPanel.cs:353`). A 5-second
cache is the same idiom `AiCommandVisibility` uses for exactly this problem
(`AiCommandVisibility.cs:17`, `CacheDurationMs = 5000`). No new timer, no file watcher, no
cross-component event.

**Alternatives considered**:

- *A `FileSystemWatcher` on config.json*. Rejected: a new mechanism for a problem two existing
  mechanisms already solve, and it fires on the temp-file rename of every unrelated settings save.
- *Reading config on every 2-second tick*. Rejected: unnecessary disk I/O in the host process.

**Worst case**: a user who changes agents from the Tools menu while the chat panel is open waits up
to 5 seconds. Opening Options *from* the panel is instant, which is the path FR-019 measures.

---

## R10 — Per-agent health and testing

**Decision**: Health persists on the agent as a nested `health` object. `Test connection` keeps
using the existing `AiProviderTest` (77/177) pair through `AiProviderTestRunner`
(`src/AkmlSql.Shell.Shared/Ai/AiProviderTestRunner.cs`), passing the **editor's current field
values** for the selected agent. Status resets to `unknown` whenever provider, model, key or
endpoint is edited.

**Rationale**: No new IPC. `AiProviderTestRunner.BuildRequest` already takes raw dialog values and
normalises the provider — it needs no change at all, only a caller that passes the selected agent's
working copy. `needsKey` is computed locally without a request (FR-055), which also keeps the list
responsive with 20 agents.

**Why reset on edit**: a `ready` badge next to a key the user just replaced is worse than no badge.

---

## R11 — Per-agent undecryptable keys

**Decision**: The decrypt-failure contract established by the PR #251 review generalises per agent:
each agent's working copy carries its own `KeyDecryptFailed` flag; an agent whose key will not
unwrap shows the notice, is reported as `needsKey`, and its stored key is never overwritten by a
save in which the user did not type into that agent's key box. Typing clears both the flag and the
notice.

**Rationale**: This is FR-009 and it is not a new idea — it is the existing behaviour at
`AiAssistancePage.cs:79`, `:329` and `:369`, applied per agent instead of once. Getting it wrong
destroys keys, which is why it is called out separately rather than left implicit.

**Ordering trap to preserve**: `Load` assigns the key text **before** setting the failure flag,
because the assignment fires `TextChanged` which clears it (`AiAssistancePage.cs:329-332`). The
per-agent version must keep that ordering when it binds the editor to a newly selected agent.

---

## R12 — The Options page layout

**Decision**: A `ListBox` of agents with an `Add` / `Duplicate` / `Remove` / `Set active` button
row above the existing provider-configuration rows, which become the editor for the selected agent.
The editor stays **inline on the page** — no modal.

**Rationale**: `TabsPage` is the in-repo precedent for a list with CRUD buttons on a settings page
(`TabsPage.cs:49-71`), and it is the pattern to follow for the list. But `TabsPage` pushes its
editing into a host-owned modal (`SettingsWindow.ShowRuleEditor`, `:1936`) because a coloring rule
is three fields. An agent is provider, model, key, endpoint and four sliders — rows the AI page
already builds. Rendering them inline reuses `RowFactory` (`AddDropdown`, `AddTextInput`,
`AddSlider`, `AddButton`) and avoids adding agent CRUD to `SettingsWindow`, which would couple the
host to this feature the way the coloring-rule handlers do.

**Consequence**: `AiAssistancePage.Build` returns controls that include the list and the buttons;
the CRUD handlers live in the page's own controls class, not in `SettingsWindow`.

**Theme obligation**: the list must use theme tokens and stay legible hovered and selected — the
Options hover-contrast regression is already pinned by
`tests/AkmlSql.Shell.Shared.Tests/OptionsHoverContrastTests.cs` and spec 036 fixed exactly this
class of bug.

---

## R13 — The editing model inside the dialog

**Decision**: The page holds a **working copy** — a deep copy of `settings.Ai.Agents` made in
`Load`, mutated by the editor and the CRUD buttons, and written back wholesale in `Save`.

**Rationale**: FR-031 requires unsaved edits to survive switching selection, which is impossible if
the editor writes straight through to the live `AppSettings`. It also makes Cancel correct for
free: `SettingsWindow.GetSettings()` is only called on OK (`SettingsWindow.cs:221`), so a discarded
working copy is a discarded edit.

**Field-to-agent sync point**: the editor commits the visible fields into the selected working-copy
agent on every selection change, on every CRUD action, and in `Save` — three call sites, one
private method.

---

## R14 — Identifiers, names and limits

**Decision**:

| Concern | Choice |
|---|---|
| Agent id | `Guid.NewGuid().ToString("N")` — 32 lowercase hex characters |
| Name uniqueness | case-insensitive, after `Trim()` |
| Suggested name | `"Agent N"` for the lowest `N ≥ 1` not in use |
| Duplicate name | `"<name> (copy)"`, then `"<name> (copy 2)"` … |
| Ceiling | 20 agents |

**Rationale**: `Guid.NewGuid().ToString("N")` is the id idiom already used for session ids
throughout the shell. Case-insensitive comparison prevents "Kimi" and "kimi " coexisting, which
would make the chat picker ambiguous. Twenty is far past realistic use and bounds the list height,
the picker, and the config file.

**Why an id at all, rather than keying by name**: FR-005 — renaming an agent must not break the
feature assignments, the active selection, or the fallback order that point at it.

---

## R15 — What this feature does *not* touch

- **The web edition.** `SettingsAi.razor` and `AiKeyVault` read their own IndexedDB store, not
  `ai.agents`. FR-013's mirroring keeps `ai.provider` valid for the web service's engine, so the
  web edition needs no change and gets none.
- **Provider construction.** `AiProviderFactory` is unchanged: it keeps taking an `AiSettings`.
- **Schema context, privacy transformation, prompt building.** Untouched — spec 036 territory.
- **The installer and the update channel.** Untouched.
- **`AiCommandVisibility`.** It reads `settings.Ai.Enabled`, which continues to mean "AI is usable";
  the derivation of that flag moves into `AiAgentResolver.Normalize` but the flag and its readers
  stay.

---

## Open risks carried into the plan

| Risk | Where it bites | Mitigation |
|---|---|---|
| Key destruction on save | FR-009, R11 | Per-agent decrypt-failure guard, tested per agent; the DPAPI entropy string `"AkmlSql-ApiKey-v1"` must not change |
| Consent evaluated against the wrong provider | R4 ordering | Project the agent before `CheckPrivacyConsent`, asserted by a test |
| Two hosts writing config concurrently | Edge case | Existing atomic write; last OK wins; documented, not defended further |
| Options page grows past a reviewable size | R12 | `AiAssistancePage` is 460 lines today; the agent list and CRUD go in the page's controls class, and the editor rows are the ones already there |
| Downgrade | Assumption 9 | Flat fields mirrored, so an older build reads the active agent and ignores `ai.agents` |
