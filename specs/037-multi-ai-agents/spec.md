# Feature Specification: Multiple AI Agents with Guided Setup

**Feature Branch**: `037-multi-ai-agents`
**Created**: 2026-09-07
**Revised**: 2026-09-07 — refinement pass after `/speckit.analyze`: FR-003 (up-to vs at-least), FR-022 (ghost text excluded), FR-052 (attribution scoped to chat), US1 scenario 6–7 and US3 scenario 1 wording. Requirement numbering is unchanged, so every reference in plan.md, tasks.md, data-model.md, contracts/ and quickstart.md remains valid.
**Status**: Draft
**Input**: User description: "please i want to add AI Agent to be multibe like Microsoft github copilot , and please give me UI and UX to be simple for example when there are no AI Agent setup at chat give me to add ai agent and open option page to add it , also write with full data description because there are another model will implement (Kimi)"

---

## Summary

Today AKML SQL holds **one** AI configuration: a single provider, a single model, a single key. Changing from Claude to a local model means retyping the whole configuration and losing the previous one. And a user who has never configured anything sees a friendly "Hello! I'm your AI SQL assistant" greeting that is a lie — the first question they ask returns a raw error.

This feature replaces the single configuration with a **named list of AI agents** (the way GitHub Copilot lets a user keep several models and pick one per conversation), and makes the zero-configuration path lead somewhere: an empty chat offers **Add AI agent**, which opens the Options page ready to receive one.

**Implementation note for the implementing model (Kimi)**: the Key Entities section below carries complete field-level data descriptions (types, defaults, ranges, JSON names, validation rules), and the non-normative **Appendix: Implementation Notes** carries file anchors and the current-state facts each requirement was written against. Requirements FR-001 … FR-058 are the contract; the appendix is orientation, not instruction.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A first-time user is led into setting up an agent (Priority: P1)

A user installs AKML SQL, opens the AI chat panel, and has configured nothing. Instead of a greeting that pretends to work, the panel states plainly that no AI agent is set up and offers a single button: **Add AI agent**. Clicking it opens the AKML SQL Options dialog with the AI Assistance page already selected and a blank agent ready to fill in. The user picks a provider, pastes a key, presses **Test connection**, sees it succeed, and clicks OK. The chat panel — still open behind the dialog — now shows the agent's name and an input box that works. No restart.

**Why this priority**: This is the moment where a user decides the feature exists or doesn't. Today it fails silently, and every other capability in this spec is unreachable until it works.

**Independent Test**: With `config.json` containing no AI agent, open the chat panel. The onboarding card must appear instead of the greeting, the button must open Options on the AI Assistance page, and after saving one agent the chat must become usable without restarting SSMS or Visual Studio. Fully testable with no other story implemented.

**Acceptance Scenarios**:

1. **Given** no AI agent is configured, **When** the user opens the AI chat panel, **Then** the panel shows an onboarding card explaining that no AI agent is set up, with an **Add AI agent** button, and the message input is disabled.
2. **Given** the onboarding card is showing, **When** the user clicks **Add AI agent**, **Then** the Options dialog opens with the AI Assistance page selected and a new, empty agent in edit state.
3. **Given** the user saved a valid agent in Options, **When** the dialog closes with OK, **Then** the chat panel replaces the onboarding card with a normal greeting naming the new agent, and the input becomes enabled — with no restart of the host.
4. **Given** the user opens Options from the card and cancels without adding an agent, **When** the dialog closes, **Then** the chat panel still shows the onboarding card and the input stays disabled.
5. **Given** an agent exists but is unusable (for example a cloud agent with no key), **When** the user opens the chat panel, **Then** the panel shows the same onboarding path with wording naming the specific problem, and the button opens Options on that agent rather than on a blank one.
6. **Given** no AI agent is configured, **When** the user invokes any other AI feature deliberately (Explain, Fix, Optimize, Index Analysis, Text-to-SQL), **Then** that feature reports the same "no AI agent configured" state and offers the same route into Options, rather than a provider error.
7. **Given** no AI agent is configured, **When** the user simply types in a query window, **Then** ghost text stays silent — no error, no prompt, no interruption.

---

### User Story 2 - A user keeps several agents side by side (Priority: P1)

A user keeps three agents: "Claude (work)" pointing at Anthropic, "Kimi" pointing at Moonshot, and "Local Llama" pointing at an Ollama endpoint on their own machine. All three are saved with their own keys and models. Adding a fourth does not disturb the first three; deleting one does not disturb the rest. Renaming "Kimi" to "Kimi K2" does not break anything that referred to it.

**Why this priority**: This is the feature the user asked for. Without it there is nothing to pick between, and story 3 has no content.

**Independent Test**: In Options → AI Assistance, add three agents with different providers, close and reopen the dialog, and confirm all three persist with their own settings and their own keys. Testable without the chat picker existing.

**Acceptance Scenarios**:

1. **Given** the AI Assistance page is open, **When** the user clicks **Add agent**, **Then** a new agent appears in the agent list with a suggested unique name, and the editor beside the list is bound to it.
2. **Given** several agents exist, **When** the user selects a different agent in the list, **Then** the editor shows that agent's provider, model, key, endpoint and request parameters, and any unsaved edits to the previously selected agent are retained in the dialog.
3. **Given** an agent is selected, **When** the user clicks **Duplicate**, **Then** a copy is created with a distinct name and the same settings including the key, and the copy becomes selected.
4. **Given** an agent is selected, **When** the user clicks **Remove**, **Then** the dialog asks for confirmation naming the agent, and on confirmation the agent is deleted and the selection moves to the nearest remaining agent.
5. **Given** the user types a name already used by another agent (ignoring case and surrounding whitespace), **When** focus leaves the name box, **Then** the dialog shows a validation message and OK is refused until the name is unique.
6. **Given** the user renames an agent, **When** the dialog is saved, **Then** every feature assignment, the active-agent selection, and the fallback order continue to point at the same agent.
7. **Given** the user clicks OK, **When** the settings are saved, **Then** every agent's key is stored wrapped, never in plain text, and no key appears in any log.

---

### User Story 3 - A user switches the answering agent from the chat panel (Priority: P2)

Mid-conversation the user wants a second opinion from a different model. A picker in the chat panel lists the configured agents; choosing one makes it the agent that answers from the next message onward. Each answer in the transcript carries the name of the agent that produced it, so a mixed conversation stays readable. The picker's last entry is **Add agent…**, which opens Options — the same route as story 1, reachable at any time.

**Why this priority**: Switching is the visible payoff of story 2, but story 2 is usable without it (switch in Options), so it ranks below.

**Independent Test**: With two agents configured, send a message, switch agents in the chat picker, send another, and confirm the second answer is attributed to the second agent and that the choice survives closing and reopening the panel.

**Acceptance Scenarios**:

1. **Given** two or more agents are configured, **When** the chat panel is open, **Then** a picker shows the agents by name, with **the agent that will answer the next message** selected — which is the agent assigned to chat when one is assigned, and otherwise the active agent (FR-006).
2. **Given** the picker is open, **When** the user chooses a different agent, **Then** the next message is answered by that agent, with no restart and without clearing the conversation.
3. **Given** the user has switched agents, **When** the panel is closed and reopened, or the host is restarted, **Then** the chosen agent is still selected.
4. **Given** a conversation contains answers from two agents, **When** the user reads the transcript, **Then** each assistant answer shows the name of the agent that produced it.
5. **Given** a conversation contains answers from two agents, **When** the user copies the whole conversation, **Then** the copied text attributes each answer to its agent.
6. **Given** the picker is open, **When** the user chooses **Add agent…**, **Then** Options opens on the AI Assistance page with a new empty agent, and on OK the new agent becomes selected in the picker.
7. **Given** exactly one agent is configured, **When** the chat panel is open, **Then** the picker still shows that agent's name (so the user always knows who is answering) and still offers **Add agent…**.

---

### User Story 4 - A user points different features at different agents (Priority: P2)

Ghost-text completions fire constantly and need to be fast and cheap; chat answers need to be good. The user assigns ghost text to "Local Llama" and everything else to "Claude (work)". Each feature then uses the agent assigned to it. Features with no explicit assignment follow whichever agent is active, so a user who never opens this section is unaffected.

**Why this priority**: A real cost and latency win, and the natural consequence of having several agents — but the feature is complete and shippable without it.

**Independent Test**: Assign ghost text to one agent and chat to another, then exercise both and confirm from the engine log which agent served each request.

**Acceptance Scenarios**:

1. **Given** several agents are configured, **When** the user opens the feature-assignment section of the AI Assistance page, **Then** each AI feature (chat, text-to-SQL, explain, fix, optimize, index suggestions, ghost text) offers a choice of agent plus a "Use active agent" default.
2. **Given** a feature is assigned to a specific agent, **When** that feature runs, **Then** the request uses that agent's provider, model, key, endpoint and request parameters.
3. **Given** a feature is left at "Use active agent", **When** the active agent changes, **Then** that feature follows the change with no further configuration.
4. **Given** a feature is assigned to an agent that is later deleted, **When** that feature next runs, **Then** it falls back to the active agent and the user is told once that the assignment was cleared.
5. **Given** a feature is assigned to a disabled agent, **When** that feature runs, **Then** it behaves exactly as if the agent were deleted (falls back to the active agent, tells the user once).

---

### User Story 5 - A user can see, at a glance, which agents actually work (Priority: P3)

The agent list shows each agent's state: **Ready**, **Not tested**, **Needs API key**, or **Failed**, with the time of the last check. **Test connection** tests the selected agent using the values currently in the editor — nothing needs to be saved first. When a live request fails, the error names the agent and offers the same one-click route into Options.

**Why this priority**: Diagnosis quality, not capability. Everything works without it; the user just spends longer finding out why an agent is silent.

**Independent Test**: Configure one agent with a valid key and one with a deliberately wrong key, test both, and confirm the list reflects the two outcomes with distinct, non-technical wording.

**Acceptance Scenarios**:

1. **Given** an agent has never been tested, **When** the agent list is shown, **Then** its status reads **Not tested**.
2. **Given** an agent is selected, **When** the user clicks **Test connection**, **Then** the current editor values are tested (not the last saved ones), the result is shown inline within the request timeout, and the agent's status and last-checked time update.
3. **Given** a cloud agent has no key, **When** the agent list is shown, **Then** its status reads **Needs API key** without any request being sent.
4. **Given** a test fails, **When** the result is shown, **Then** the message distinguishes an authentication failure, an unreachable endpoint, an unknown model, and an engine-not-running condition.
5. **Given** a live AI request fails because of the agent's configuration, **When** the error is surfaced to the user, **Then** it names the agent and offers a route to that agent's settings.
6. **Given** any test or failure is recorded, **When** logs are written, **Then** the API key never appears in them.

---

### User Story 6 - An existing user upgrades and loses nothing (Priority: P3)

A user who already had a provider, model and key configured upgrades. Their configuration appears as an agent named after its provider, already active, already working. They never had to do anything. If they hand-edited `config.json`, it still loads.

**Why this priority**: It protects existing users rather than adding capability, but it is a hard requirement on the change — the feature cannot ship without it.

**Independent Test**: Take a `config.json` from the current release with a configured provider and key, open the new build, and confirm the agent list holds exactly one agent carrying those settings, that it is active, and that chat works without user action.

**Acceptance Scenarios**:

1. **Given** a configuration with a provider and no agent list, **When** settings are loaded, **Then** exactly one agent is created from those values, named for its provider, marked active, and the user is not prompted.
2. **Given** the migrated configuration, **When** it is saved, **Then** the previously stored key keeps working — it is not re-wrapped, re-encrypted, or blanked.
3. **Given** a configuration with no provider and no agents, **When** settings are loaded, **Then** the agent list is empty and the onboarding path of story 1 applies.
4. **Given** a configuration naming an active agent that does not exist, **When** settings are loaded, **Then** the first enabled agent becomes active, and if there is none, the empty-state path applies.
5. **Given** a configuration whose agent list contains a malformed entry, **When** settings are loaded, **Then** the remaining agents load normally and the malformed entry is skipped with a log entry — the dialog must still open.
6. **Given** the offline/fallback provider fields from earlier releases are set, **When** settings are loaded, **Then** that fallback keeps working, expressed as an agent in the fallback order.

---

### Edge Cases

- **No agents at all**: chat, and every other AI surface, shows the onboarding route rather than a provider error. The message input is disabled so the user cannot type a question into a void.
- **Agent count ceiling**: the twenty-first agent is refused with an explanation; the existing twenty are untouched.
- **Duplicate names**: refused at the point of editing, comparing case-insensitively after trimming. Two agents named "Kimi" and "kimi " cannot coexist.
- **Empty name**: refused; a name is how the user picks the agent everywhere else.
- **Deleting the active agent**: the next enabled agent becomes active; if none remains, the empty state applies and features report it.
- **Deleting an agent that a feature is assigned to**: the assignment reverts to "use active agent" and the user is told once.
- **An agent's key cannot be decrypted on this machine** (roamed profile, restored backup, different machine): the agent shows **Needs API key**, the stored value is left untouched, and a save the user never touched the key in must not blank it.
- **Two hosts open at once** (SSMS and VS, or two SSMS windows): the last dialog to click OK wins, writing the whole AI section atomically. A running conversation in the other host keeps using the agent it already resolved and picks up the change on its next message.
- **Agent edited while a request is in flight**: the in-flight request completes against the agent it started with; the change applies from the next request.
- **Local agents (Ollama, LM Studio, custom endpoints)**: valid with no API key. They must never be marked **Needs API key**.
- **Azure and custom agents**: invalid without an endpoint; that is reported as a configuration problem, not a connection failure.
- **A provider/model family mismatch** (a Claude model name on a Gemini agent): reported as a configuration problem naming both, before any request leaves the machine.
- **The chat picker while an answer is streaming**: switching is allowed but takes effect on the next message; the in-flight answer stays attributed to the agent that produced it.
- **All agents disabled**: treated as the empty state.
- **Config file hand-edited to invalid JSON**: existing behaviour applies — defaults load and the user is not blocked from opening Options.

---

## Requirements *(mandatory)*

### Functional Requirements — Agent model and storage

- **FR-001**: The system MUST support multiple saved AI agents, each an independently named and independently configured set of AI connection settings.
- **FR-002**: Each agent MUST carry: a stable identifier, a display name, a provider, a model, an API key, an endpoint, maximum response tokens, temperature, request timeout, retry count, an enabled flag, and its last recorded health result.
- **FR-003**: The system MUST support up to 20 agents, and MUST refuse to create a 21st with a message naming the limit. The existing 20 are left untouched by the refusal.
- **FR-004**: Agent names MUST be unique after trimming and ignoring case, MUST be non-empty, and MUST be at most 40 characters.
- **FR-005**: An agent's identifier MUST be stable across renames, edits, reordering and restarts, and MUST be what every other setting refers to.
- **FR-006**: The system MUST record exactly one active agent, which is the agent used by any feature that has no explicit assignment.
- **FR-007**: Agents MUST persist in the existing AKML SQL configuration file, written atomically as part of the existing settings save — no second settings store.
- **FR-008**: Every agent's API key MUST be stored wrapped at rest using the existing key-protection mechanism and entropy, and MUST never be written to a log, an error message, or a copied conversation.
- **FR-009**: A stored key that cannot be unwrapped on the current machine MUST be reported to the user and MUST NOT be overwritten or cleared by a save in which the user did not edit that key.
- **FR-010**: Loading a configuration that has no agent list but does have a configured provider MUST produce exactly one agent from those values, named for its provider, marked active, with no user prompt and no change to the stored key.
- **FR-011**: Loading a configuration whose active agent identifier matches no agent MUST select the first enabled agent, or leave no agent active if there is none.
- **FR-012**: A malformed agent entry MUST be skipped with a log entry, leaving the remaining agents loadable and the Options dialog openable.
- **FR-013**: The system MUST keep the single-provider settings fields consistent with the active agent, so that any consumer reading them (including the web edition and existing engine paths) behaves as if the active agent were the configured provider.
- **FR-014**: Provider identifiers on agents MUST use the existing canonical identifier set and MUST accept the existing legacy and display spellings when read.

### Functional Requirements — Empty state and guided setup

- **FR-015**: When no usable agent is configured, the AI chat panel MUST show an onboarding card in place of the greeting, stating that no AI agent is set up.
- **FR-016**: The onboarding card MUST offer a single primary action labelled **Add AI agent**.
- **FR-017**: Activating **Add AI agent** MUST open the AKML SQL Options dialog with the AI Assistance page already selected and a new, empty agent ready to edit — the user MUST NOT have to navigate to it.
- **FR-018**: While no usable agent is configured, the chat message input and send action MUST be disabled, and no request may be sent.
- **FR-019**: When the Options dialog closes after an agent has been added, the chat panel MUST become usable immediately, without restarting the host application.
- **FR-020**: When the Options dialog is cancelled without adding an agent, the chat panel MUST remain in the empty state.
- **FR-021**: When agents exist but none is usable, the card MUST name the specific reason (no key, no model, no endpoint, or all agents disabled) and MUST open Options on the offending agent rather than on a blank one.
- **FR-022**: Every other AI feature the user invokes deliberately (text-to-SQL, explain, fix, optimize, index suggestions) MUST report the no-agent state in its own surface and MUST offer the same route into Options, instead of surfacing a provider error. **Ghost text is excluded**: it is a background completion the user never asked for, with no surface to report into, so when no agent is configured it MUST stay silent and MUST NOT show an error, a prompt, or a route into Options.
- **FR-023**: The onboarding card MUST be reachable in at most two interactions from an unconfigured chat panel: open the panel, click the button.

### Functional Requirements — Options page

- **FR-024**: The AI Assistance page MUST present the agents as a list, with the currently selected agent's settings shown in an editor beside it.
- **FR-025**: The page MUST provide **Add agent**, **Duplicate**, **Remove**, and a way to mark an agent active.
- **FR-026**: **Add agent** MUST create an agent with a unique suggested name and select it for editing.
- **FR-027**: **Duplicate** MUST copy every setting of the selected agent including its key, give the copy a unique name, and select the copy.
- **FR-028**: **Remove** MUST confirm by naming the agent, and after removal MUST select the nearest remaining agent.
- **FR-029**: The agent list MUST show each agent's name, provider, model, and health status.
- **FR-030**: Editing an agent's settings MUST NOT alter any other agent.
- **FR-031**: Switching selection in the agent list MUST retain unsaved edits made to the previously selected agent for the lifetime of the dialog.
- **FR-032**: The page MUST refuse OK while any agent fails validation, and MUST show which agent and which field is at fault.
- **FR-033**: The page MUST keep the existing non-agent AI settings (privacy mode, cloud consent, per-feature enable switches, schema context budget, shortcuts) as settings that apply across all agents, not per agent.
- **FR-034**: Switching an agent's provider MUST apply the existing model auto-correction behaviour to that agent's model field only.
- **FR-035**: The page MUST remain usable by keyboard alone, and every control MUST carry an accessible name.
- **FR-036**: The page MUST follow the existing theme token system, and all text MUST remain legible in every supported theme in both hovered and selected states.

### Functional Requirements — Chat panel agent selection

- **FR-037**: The chat panel MUST show which agent will answer the next message, by name.
- **FR-038**: The chat panel MUST let the user change the answering agent without leaving the panel.
- **FR-039**: A change of agent MUST take effect from the next message, MUST NOT clear the conversation, and MUST NOT require a restart.
- **FR-040**: The chosen agent MUST persist across panel close/reopen and host restart.
- **FR-041**: The agent picker MUST offer an **Add agent…** entry that opens Options as in FR-017, and on save MUST select the newly added agent.
- **FR-042**: Each assistant answer in the transcript MUST identify the agent that produced it.
- **FR-043**: Copying the whole conversation MUST preserve agent attribution for each answer.
- **FR-044**: The picker MUST be shown even when only one agent exists.
- **FR-045**: Existing chat behaviour MUST be preserved: database binding to the active editor, the refusal to send when unbound, the schema-loading notice, the privacy-mode notice, copy-SQL and copy-message actions.

### Functional Requirements — Per-feature assignment and fallback

- **FR-046**: The system MUST allow each AI feature — chat, text-to-SQL, explain, fix, optimize, index suggestions, ghost text — to be assigned to a specific agent or to follow the active agent.
- **FR-047**: Following the active agent MUST be the default for every feature.
- **FR-048**: A feature assigned to an agent MUST use that agent's provider, model, key, endpoint, and request parameters for its requests.
- **FR-049**: A feature assigned to an agent that is missing or disabled MUST fall back to the active agent and MUST inform the user once, not on every request.
- **FR-050**: The system MUST support an ordered fallback list of agents used when the selected agent fails for a transient reason.
- **FR-051**: The fallback MUST preserve the behaviour of the existing offline-provider fallback for configurations that use it.
- **FR-052**: When a **chat** answer is produced by a fallback agent rather than the selected one, the panel MUST say which agent answered and that the selected one was unavailable. The other features display no agent attribution at all (FR-042 covers chat only), so they MUST NOT name an agent on a fallback either — a fallback there is recorded for diagnosis, not surfaced. What they must never do is present a fallback answer as though the selected agent produced it.

### Functional Requirements — Health, testing and errors

- **FR-053**: Each agent MUST carry a health status of exactly one of: not tested, ready, needs API key, or failed — with the time of the last check.
- **FR-054**: **Test connection** MUST test the values currently in the editor, not the last saved values, and MUST NOT require saving first.
- **FR-055**: A cloud agent with no key MUST be reported as needing a key without any network request being attempted.
- **FR-056**: Test results MUST distinguish authentication failure, unreachable endpoint, unknown or rejected model, provider/model family mismatch, missing endpoint for providers that require one, and the engine not running.
- **FR-057**: A failed live request caused by agent configuration MUST name the agent in its error and MUST offer a route to that agent's settings.
- **FR-058**: No error message, status line, log entry, or copied text may contain an API key.

---

### Key Entities *(full data description)*

#### Entity: `AiAgent`

One saved AI configuration. Persisted as an element of `ai.agents` in `config.json`.

| JSON name | Type | Default | Required | Rules |
|---|---|---|---|---|
| `id` | string | 32 lowercase hex characters, no dashes | yes | Unique across the list. Assigned once at creation and never changed, including on rename. Referred to by `ai.activeAgentId`, `ai.featureAgents.*`, and `ai.fallbackOrder`. |
| `name` | string | `"Agent 1"`, `"Agent 2"`, … (first unused) | yes | 1–40 characters after trimming. Unique across the list, compared case-insensitively after trimming. Shown in the agent list, the chat picker, answer attribution, and error text. |
| `provider` | string | `""` | yes | One of the canonical provider ids: `anthropic`, `openai`, `azure`, `gemini`, `kimi`, `ollama`, `lmstudio`, `custom`. Legacy and display spellings (`AzureOpenAI`, `LMStudio`, `Kimi (Moonshot)`, `moonshot`) are accepted on read and normalised; only canonical ids are written. |
| `model` | string | provider's default model, or `""` | yes | Free text — user fine-tunes and local model names must pass through unchanged. For `anthropic`, `openai`, `gemini` and `kimi`, a model name that clearly belongs to a different family is a validation failure naming both. |
| `apiKey` | string | `""` | conditional | Required for `anthropic`, `openai`, `azure`, `gemini`, `kimi`. Optional for `ollama`, `lmstudio`, `custom`. Stored wrapped with the `dpapi:` prefix. An unprefixed value is legacy plaintext and is accepted on read, wrapped on the next save. Never logged. |
| `endpoint` | string | `""` | conditional | Required for `azure` and `custom`. Optional elsewhere; blank means the provider default (`kimi` → the international Moonshot endpoint, `ollama` → the local default). Must be an absolute URL when present. |
| `maxTokens` | integer | `4096` | no | 256 … 32768. |
| `temperature` | number | `0.2` | no | 0.0 … 2.0, one decimal place. |
| `timeout` | integer | `30` | no | 5 … 300 seconds. Bounds the request, its retries, and the caller's wait. |
| `retries` | integer | `2` | no | 0 … 5 automatic retries on transient failure. |
| `enabled` | boolean | `true` | no | A disabled agent is hidden from the chat picker and treated as missing by feature assignments and the fallback order, but is not deleted. |
| `createdUtc` | string | current UTC time, ISO 8601 | no | Informational; used only to order suggested names and break ties. |
| `health` | object \| null | `null` | no | The last recorded `AgentHealth` (below). `null` means never tested. |

#### Entity: `AgentHealth`

The last known result of checking an agent. Persisted inside its agent as `health`.

| JSON name | Type | Default | Rules |
|---|---|---|---|
| `status` | string | `"unknown"` | Exactly one of `unknown` (never tested), `ready` (a test succeeded), `needsKey` (a cloud agent with no key — determined without a request), `failed` (a test or live request failed). |
| `checkedUtc` | string \| null | `null` | ISO 8601 UTC time of the last check. `null` when `status` is `unknown`. |
| `latencyMs` | integer | `0` | Round-trip milliseconds of the last successful check. `0` when the last check did not succeed. |
| `message` | string | `""` | User-facing summary of the last result. Never contains an API key. At most 500 characters. |

#### Entity: `FeatureAgentAssignments`

Which agent serves each AI feature. Persisted as `ai.featureAgents`.

| JSON name | Type | Default | Rules |
|---|---|---|---|
| `chat` | string | `""` | An agent `id`, or `""` meaning "use the active agent". |
| `textToSql` | string | `""` | As above. |
| `explain` | string | `""` | As above. |
| `fix` | string | `""` | As above. |
| `optimize` | string | `""` | As above. |
| `indexSuggestions` | string | `""` | As above. |
| `ghostText` | string | `""` | As above. |

An id that matches no enabled agent is treated as `""` and is cleared on the next save.

#### Entity: `AiSettings` (extended)

The existing AI settings section gains the agent list and loses nothing.

| JSON name | Type | Default | Rules |
|---|---|---|---|
| `agents` | array of `AiAgent` | `[]` | 0 … 20 entries. Order is the display order in the Options list and the chat picker. |
| `activeAgentId` | string | `""` | The `id` of the active agent, or `""` when there is none. An id matching no agent resolves to the first enabled agent, or `""`. |
| `featureAgents` | `FeatureAgentAssignments` | all `""` | See above. |
| `fallbackOrder` | array of string | `[]` | Agent ids tried in order when the selected agent fails transiently. Ids matching no enabled agent are skipped and cleared on the next save. |
| `provider`, `model`, `apiKey`, `endpoint`, `maxTokens`, `temperature`, `timeout`, `retries` | as today | as today | **Kept, and kept in sync with the active agent** (FR-013), so every existing consumer keeps working unchanged. Written on every save from the active agent; when there is no active agent, `provider` is `""`. |
| `offlineProvider`, `offlineModel`, `offlineEndpoint` | as today | as today | Kept. Expressed as the tail of the fallback order (FR-051). |
| `enabled` | boolean | as today | True when at least one enabled agent exists. |
| `privacyMode`, `privacyConsentRequired`, `schemaContextMaxObjects`, `textToSql`, `explain`, `fix`, `optimize`, `indexSuggestions`, `chatPanel`, `inlineCompletion`, `autoFixOnError`, shortcut and ghost-text fields | as today | as today | **Global, not per agent** (FR-033). Privacy and consent must not become per-agent settings — a user who withheld cloud consent must not be able to grant it accidentally by adding an agent. |

#### Example: the `ai` section after this feature

```json
"ai": {
  "enabled": true,
  "provider": "anthropic",
  "model": "claude-sonnet-4-6",
  "apiKey": "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…",
  "endpoint": "",
  "maxTokens": 4096,
  "temperature": 0.2,
  "timeout": 30,
  "retries": 2,
  "privacyMode": "schemaOnly",
  "privacyConsentRequired": false,
  "schemaContextMaxObjects": 500,
  "activeAgentId": "7f3c1a9e2b6d4f508c1e5a7b9d0c2e34",
  "agents": [
    {
      "id": "7f3c1a9e2b6d4f508c1e5a7b9d0c2e34",
      "name": "Claude (work)",
      "provider": "anthropic",
      "model": "claude-sonnet-4-6",
      "apiKey": "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…",
      "endpoint": "",
      "maxTokens": 4096,
      "temperature": 0.2,
      "timeout": 30,
      "retries": 2,
      "enabled": true,
      "createdUtc": "2026-09-07T10:14:02Z",
      "health": { "status": "ready", "checkedUtc": "2026-09-07T10:15:41Z", "latencyMs": 812, "message": "Connection succeeded." }
    },
    {
      "id": "b21d4e6f8a0c3157d9e2f4a6b8c0d1e2",
      "name": "Kimi",
      "provider": "kimi",
      "model": "kimi-latest",
      "apiKey": "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…",
      "endpoint": "",
      "maxTokens": 4096,
      "temperature": 0.2,
      "timeout": 30,
      "retries": 2,
      "enabled": true,
      "createdUtc": "2026-09-07T10:20:11Z",
      "health": { "status": "unknown", "checkedUtc": null, "latencyMs": 0, "message": "" }
    },
    {
      "id": "c93a5b7d1f2e4068a1b3c5d7e9f0a2b4",
      "name": "Local Llama",
      "provider": "ollama",
      "model": "llama3.1:8b",
      "apiKey": "",
      "endpoint": "http://localhost:11434",
      "maxTokens": 2048,
      "temperature": 0.1,
      "timeout": 60,
      "retries": 1,
      "enabled": true,
      "createdUtc": "2026-09-07T10:31:55Z",
      "health": { "status": "ready", "checkedUtc": "2026-09-07T10:32:07Z", "latencyMs": 240, "message": "Connection succeeded." }
    }
  ],
  "featureAgents": {
    "chat": "",
    "textToSql": "",
    "explain": "",
    "fix": "",
    "optimize": "",
    "indexSuggestions": "",
    "ghostText": "c93a5b7d1f2e4068a1b3c5d7e9f0a2b4"
  },
  "fallbackOrder": ["b21d4e6f8a0c3157d9e2f4a6b8c0d1e2", "c93a5b7d1f2e4068a1b3c5d7e9f0a2b4"]
}
```

In this example: chat and most features use **Claude (work)** (the active agent); ghost text uses **Local Llama**; if Claude fails transiently, **Kimi** is tried, then **Local Llama**. The flat `provider`/`model`/`apiKey` fields mirror the active agent so nothing that reads them today has to change.

#### State: agent usability

An agent is **usable** when: `enabled` is true, `provider` is a canonical id, `model` is non-empty, the key is present when the provider requires one, and the endpoint is present when the provider requires one. The empty state of FR-015 applies exactly when no agent is usable.

#### State: agent health transitions

```
unknown ──test succeeds──▶ ready
unknown ──cloud agent, no key──▶ needsKey        (no request sent)
unknown ──test fails──▶ failed
ready   ──live request fails on configuration──▶ failed
ready   ──key removed──▶ needsKey
failed  ──test succeeds──▶ ready
any     ──provider / model / key / endpoint edited──▶ unknown
```

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with nothing configured goes from opening the AI chat panel to receiving a working answer in under 2 minutes, without consulting documentation.
- **SC-002**: 100% of attempts to use an AI feature with no agent configured produce the guided "add an agent" path — none produce a raw provider or connection error.
- **SC-003**: Reaching the agent-setup screen from an unconfigured chat panel takes exactly one click.
- **SC-004**: A user can create, name and save three agents with different providers, and all three survive a restart with their own settings and keys intact.
- **SC-005**: Switching the answering agent from the chat panel takes at most two interactions and applies to the very next message, with no restart, in 100% of attempts.
- **SC-006**: 100% of existing single-provider configurations continue to work after upgrade with zero user action and no re-entry of the API key.
- **SC-007**: Every assistant answer in a mixed-agent conversation identifies its agent, in the panel and in the copied text.
- **SC-008**: Every feature with an explicit agent assignment uses that agent for 100% of its requests, verifiable from the engine log.
- **SC-009**: No API key appears in any log file, error message, status line, or copied conversation, verified by scanning a full session's logs after configuring, testing, and using every agent.
- **SC-010**: Every configuration failure mode (no key, no endpoint, unknown model, wrong model family, unreachable endpoint, engine not running) produces a distinct message that names the agent.
- **SC-011**: Adding an agent does not slow the Options dialog: it opens within the same time budget as today with 20 agents configured.
- **SC-012**: Resolving which agent serves a request adds no perceptible delay — under 5 milliseconds per request.

---

## Assumptions

1. **"AI Agent" means a saved configuration, not an autonomous tool-using agent.** The user's comparison is to GitHub Copilot's model picker — several configured models, one chosen per conversation. Tool use, multi-step planning, and agentic execution are out of scope.
2. **The Options dialog remains the place where agents are created and edited.** The chat panel routes into it rather than growing its own editor, matching the user's stated flow ("open option page to add it").
3. **Twenty agents is the ceiling.** Far beyond any realistic use, and it bounds the dialog's list, the config file size, and the picker.
4. **Privacy mode and cloud consent stay global.** Making them per agent would let a user grant cloud consent by accident when adding an agent; the privacy-first default is preserved deliberately.
5. **The flat provider fields stay and mirror the active agent.** This keeps the web edition, the engine's existing paths, and any hand-written `config.json` working with no migration step and no coordinated change across editions.
6. **The existing connection-test path is reused for per-agent testing** rather than a second mechanism.
7. **Agent selection is per user, not per editor window or per connection.** A user asking about two databases uses the same agent for both unless they change it.
8. **The conversation is not per agent.** Switching agents continues the same conversation, with history, matching Copilot's behaviour; each answer is attributed so the transcript stays readable.
9. **Migration is one-way and silent.** A configuration written by this build and then opened by an older build shows the mirrored flat fields and simply ignores the agent list — the older build keeps working with the active agent.
10. **Health is advisory.** A stale `ready` status never blocks a request; it only informs the list.
11. **Ghost text never speaks up.** It is the one AI feature the user does not invoke — it fires on typing. A user who has not configured an agent is not asking for a completion, so telling them about it would interrupt ordinary editing. Every deliberately invoked feature reports the state instead (FR-022). The alternative — a ghost-text notice — was rejected as an interruption a user cannot dismiss by not asking.
12. **Fallback attribution follows agent attribution.** Only chat names the agent that answered (FR-042), so only chat says when a fallback answered (FR-052). Explain, Fix, Optimize and Index Analysis never name an agent, so naming one only in the failure case would be noise, not information. The fallback is recorded for diagnosis in all cases. Extending named attribution to those four panels is a reasonable future change; it is not in this feature.

---

## Out of Scope

- Autonomous or tool-using agents, multi-step task execution, or agent-to-agent delegation.
- Streaming multiple agents in parallel or comparing answers side by side.
- Per-agent system prompts, personas, or custom instructions.
- Per-agent or aggregate cost and token accounting.
- Sharing, exporting, importing, or syncing agents between machines or users.
- Changes to the web edition's AI settings screen. The shared settings contract must stay loadable by it (FR-013), but its UI is unchanged.
- New providers beyond the eight already supported.
- Changes to the schema-context assembly, privacy transformation, or prompt construction.
- Changes to the installer or the update channel.

---

## Dependencies

- The existing AI settings section, its atomic save path, and its defaults.
- The existing canonical provider identifiers and their legacy-spelling normalisation.
- The existing model-family detection used to auto-correct a model on a provider switch and to refuse cross-provider mismatches.
- The existing key-protection mechanism and its entropy string, which must not change.
- The existing connection-test request/response pair between shell and engine.
- The existing Options dialog page framework, its theme tokens, and its search registration.
- The existing chat panel's editor binding, schema-status polling, privacy notice, and copy actions.
- The existing primary-then-fallback execution path in the engine.

---

## Appendix: Implementation Notes *(non-normative)*

Orientation for the implementing model. These are current-state facts verified in the repository on 2026-09-07, not requirements. Where this appendix and the requirements disagree, the requirements win.

### Where the single configuration lives today

| Concern | Location |
|---|---|
| Settings shape | `src/AkmlSql.Core/Config/AppSettings.cs:1126` — `class AiSettings`, flat `Provider`/`Model`/`ApiKey`/`Endpoint` plus global feature switches |
| Canonical provider ids + legacy normalisation | `src/AkmlSql.Core/Config/AiProviderIds.cs` — `Normalize`, `CanonicalIds` |
| Model-family heuristics | `src/AkmlSql.Core/Config/AiModelFamily.cs` — `Detect`, `DefaultModelFor` |
| Key wrapping | `src/AkmlSql.Core/Config/ApiKeyProtector.cs` — `dpapi:` prefix, entropy `"AkmlSql-ApiKey-v1"` (**must not change — every stored key becomes unreadable**) |
| Options page | `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs` — `Providers` table at `:28`, `Load` at `:319`, `Save` at `:364`, decrypt-failure notice at `:79` |
| Options host | `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` — page registry at `:53`, nav order at `:1083`, `SelectTreeLeafByPageKey` at `:1023`, `ShowDialog` at `:175`, per-page reset at `:1819` |
| Dialog launcher | `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs:46` — constructs `SettingsWindow`, saves on OK, notifies the engine via `AnalysisSettingsChanged` |
| Chat panel | `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` — greeting at `:238`, header/copy-conversation at `:76`, `RefreshBinding` at `:255`, `SendMessageAsync` at `:423` |
| Connection test caller | `src/AkmlSql.Shell.Shared/Ai/AiProviderTestRunner.cs` — builds and sends `AiProviderTest` |
| Provider construction | `src/AkmlSql.AI/Providers/AiProviderFactory.cs` — `Create(AiSettings)` at `:59`, `CreateFromFallback` at `:90` |
| Primary→fallback execution | `src/AkmlSql.Engine/Ai/AiPipelineServices.cs:113` — `ExecuteWithFallbackAsync` |
| Engine AI handlers | `src/AkmlSql.Engine/Handlers/Ai/` — chat, explain, fix, optimize, index analysis, text-to-SQL, ghost text |
| IPC ids | `src/AkmlSql.Core/Ipc/RpcMessage.cs:216-224` — AI requests 70–78, responses 170–179. **Free**: request `79`, and the `95`–`99` / `195`–`199` band |

### Facts worth knowing before designing the change

1. **`AiProviderFactory.Create` takes an `AiSettings`, not a provider name.** An agent is therefore trivially convertible into what the factory already accepts — build an `AiSettings` from the agent and pass it. `CreateFromFallback` already does exactly this trick with the offline fields (`AiProviderFactory.cs:104`). This is why FR-013's mirroring is cheap and why no provider code needs to change.

2. **The greeting at `AiChatPanel.cs:238` is unconditional.** It is added in the constructor before any configuration is read, which is why an unconfigured user is greeted warmly and then fails on their first question. The empty state replaces this call, not the panel.

3. **`SettingsWindow` has no way to open on a specific page.** `BuildWindowInner` (`:206`) always selects the first tree item. `SelectTreeLeafByPageKey` (`:1023`) already exists for search navigation and is the seam FR-017 needs — it wants an entry point that selects a page before showing.

4. **`OptionsCommand.Execute` reloads settings and reopens the dialog in a loop** to support theme changes (`:44`). Any "open Options from chat" route should go through the same save-and-notify path (`ConfigManager.Save` then the `AnalysisSettingsChanged` notification at `:69`) rather than a parallel one, or the engine will keep serving the old configuration.

5. **The engine caches settings per request context** and drops the cache on `AnalysisSettingsChanged`. That notification is what makes FR-019's "no restart" true; nothing else is needed.

6. **The decrypt-failure handling added in the PR #251 review is load-bearing** (`AiAssistancePage.cs:79`, `:329`, `:369`). Per-agent keys need the same guarantee per agent: an agent whose key will not unwrap must show its notice, must not be blanked by a save, and must clear the guard the moment the user types.

7. **`Save` currently writes `settings.Ai.Enabled = _provider.SelectedIndex > 0`** (`:394`). With agents, "enabled" means "at least one usable agent exists" — the derivation moves but the flag stays, because `AiCommandVisibility` and the engine both read it.

8. **The provider list is positional today and that has bitten before.** `Providers` at `:28` is keyed by canonical id precisely because index-keyed switches produced the Azure/LM Studio mismatch. Per-agent editing must keep the id keying.

9. **The chat panel polls for its editor binding every 2 seconds** (`BindingRefreshIntervalMs`, `:40`). The same poll is the natural place to notice that configuration changed and to leave or enter the empty state — no second mechanism, per Constitution V.

10. **Tests to extend rather than duplicate**: `tests/AkmlSql.Shell.Shared.Tests/AiProviderModelAutofillTests.cs` already pins provider round-tripping, legacy spellings, model auto-correction, and the decrypt-failure contract. `tests/AkmlSql.Core.Tests/` covers settings load/save. `tests/AkmlSql.AI.Tests/` covers context assembly.

### Constitution touch points

- **I (Process Isolation)**: agent resolution belongs in the engine or the shared library; the shell picks a name and sends an id. No provider SDK work moves in-process.
- **II (Build Integrity)**: shell projects build with full MSBuild only.
- **III (Tests non-regressible)**: new behaviour lands with tests; the format-parity and completion corpora are untouched by this feature and must stay green.
- **V (Simplicity)**: extend `AiSettings`, `AiAssistancePage`, `AiChatPanel` and the existing test-connection path. Do not introduce a second settings store, a second progress mechanism, or a second dialog.
