# Contract: the Options → AI Assistance agent list

Satisfies **FR-024 – FR-036** and **FR-053 – FR-058**. Shell-side, WPF, programmatic (no XAML).

**Anchors**: `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs:28,79,319,364` · `src/AkmlSql.Shell.Shared/Dialogs/Pages/TabsPage.cs:49-71` · `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:206,1023,1936` · `src/AkmlSql.Shell.Shared/Ai/AiProviderTestRunner.cs`

---

## Layout

The page keeps its current structure and gains a list above the provider rows. The provider rows
become the **editor for the selected agent**.

```
┌─ AI Assistance ─────────────────────────────────────────────────────────┐
│ Agents                                                    (group header) │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ ● Claude (work)      anthropic · claude-sonnet-4-6        ✔ Ready   │ │
│ │   Kimi               kimi · kimi-latest                   – Not tested│
│ │   Local Llama        ollama · llama3.1:8b                 ✔ Ready   │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
│ [ Add ] [ Duplicate ] [ Remove ] [ Set as active ]                       │
│                                                                          │
│ Selected agent                                            (group header) │
│   Name              [ Claude (work)                    ]                 │
│   AI Provider       [ Anthropic                      ▾ ]                 │
│   Model             [ claude-sonnet-4-6                ]                 │
│   API Key           [ ••••••••••••••••                 ]                 │
│   (key notice, when the stored key could not be decrypted)               │
│   Endpoint          [                                  ]                 │
│   [ Test connection ]   Connection succeeded — 812 ms                    │
│   Max tokens / Temperature / Timeout / Retries   (the existing sliders)  │
│                                                                          │
│ Feature assignments                                       (group header) │
│   Chat / Text-to-SQL / Explain / Fix / Optimize / Index / Ghost text     │
│   each a dropdown: "Use active agent" + every agent by name              │
│                                                                          │
│ Privacy and features                                      (group header) │
│   (unchanged: privacy mode, cloud consent, the per-feature switches)     │
└──────────────────────────────────────────────────────────────────────────┘
```

`●` marks the active agent. The list is a `ListBox` following the `TabsPage` coloring-rules
precedent (`TabsPage.cs:49`), but the **editor is inline, not a modal** — an agent has ten fields,
and the rows already exist on this page. Row rendering and the health badge live in
`AiAgentListView` so `AiAssistancePage` stays reviewable.

---

## The working copy (FR-031)

**The rule**: the page edits a deep copy and writes it back wholesale.

```
Load(settings):
    _agents      := deep copy of settings.Ai.Agents
    _activeId    := settings.Ai.ActiveAgentId
    _assignments := copy of settings.Ai.FeatureAgents
    _fallback    := copy of settings.Ai.FallbackOrder
    select the active agent (or the first) and bind the editor to it

Save(settings):
    CommitEditorToSelectedAgent()
    settings.Ai.Agents        := _agents
    settings.Ai.ActiveAgentId := _activeId
    settings.Ai.FeatureAgents := _assignments
    settings.Ai.FallbackOrder := _fallback
    // the flat mirror is NOT this page's job — AiAgentResolver.MirrorActiveAgent runs inside
    // ConfigManager.Save (and again on the next Load), so the invariant holds on disk
```

`CommitEditorToSelectedAgent` runs at exactly four moments: on selection change, before any CRUD
action, in `Save`, and before a Test connection (FR-054 — the working copy must hold the values
being tested). One private method, four call sites.

Cancel is correct for free: `SettingsWindow.GetSettings()` is only called on OK
(`SettingsWindow.cs:221`), so an abandoned working copy is an abandoned edit.

---

## CRUD obligations

| Action | Obligation |
|---|---|
| **Add** | New agent, `Id = Guid.NewGuid().ToString("N")`, `Name = SuggestName(_agents)`, defaults elsewhere. Appended and selected. Refused at 20 with a message naming the limit (V12). |
| **Duplicate** | Every field copied **including the key**, `Id` regenerated, `Name = SuggestCopyName(...)`, `Health` reset to `null`. Appended after the source and selected. Refused at 20. |
| **Remove** | Confirms by naming the agent. On confirm: removed; `_activeId` repaired if it pointed here; assignments naming it reset to `""`; fallback entries naming it dropped. Selection moves to the nearest remaining agent. |
| **Set as active** | `_activeId := selected.Id`. Refused, with a reason, when the selected agent is not usable (S1). |

---

## Validation (FR-032)

OK is refused while any agent fails V1–V12. The message names **the agent and the field**:

> "Kimi: a model name is required." · "Claude (work): that name is already used by another agent."

Validation runs against the **whole working copy**, not just the selected agent — a user can leave
an invalid agent, select another, and press OK. The offending agent is selected before the message
is shown, so the user lands where the problem is.

Name validation fires on focus loss from the name box, so the user learns immediately rather than at
OK (US2 scenario 5).

---

## Per-agent key protection (FR-009, V23)

This generalises the PR #251 contract to every agent. Getting it wrong destroys keys.

```
BindEditorTo(agent):
    (display, decrypted) := UnwrapKeyForDisplay(agent.ApiKey)
    _apiKeyBox.Text := display                 // fires TextChanged, which clears the flag
    agent.KeyDecryptFailed := !decrypted       // set AFTER the assignment — the ordering matters
    _keyNotice.Visibility := agent.KeyDecryptFailed ? Visible : Collapsed

CommitEditorToSelectedAgent():
    if not (agent.KeyDecryptFailed and _apiKeyBox.Text is empty):
        agent.ApiKey := IsProtected(text) ? text : Protect(text)
    // else: the box is empty because decryption failed, NOT because the user cleared it.
    //       Leave the stored value alone.

on _apiKeyBox.TextChanged:
    agent.KeyDecryptFailed := false
    _keyNotice.Visibility := Collapsed          // both, together — the notice's claim stops being true
```

`KeyDecryptFailed` is **working-copy state only** — it is never serialised. It lives on the working
copy rather than in a single page-level field because the user can switch between a decryptable and
an undecryptable agent without saving.

An agent in this state reports health `needsKey` and is not usable (S1), so it cannot silently
become the active agent.

---

## Health and testing (FR-053 – FR-057)

| Requirement | Obligation |
|---|---|
| Status values | `unknown` → "Not tested", `ready` → "Ready", `needsKey` → "Needs API key", `failed` → "Failed" — with the last-checked time beside it |
| `needsKey` is local | Computed from `RequiresApiKey(provider) && ApiKey == ""` (or `KeyDecryptFailed`). **No request is sent** (FR-055) |
| Test uses editor values | `AiProviderTestRunner.BuildRequest(providerSelection, model, apiKey, endpoint)` with the **current boxes**, not the saved agent (FR-054). The runner needs no change |
| Test updates health | On return, `Health.Status`, `CheckedUtc`, `LatencyMs`, `Message` are written to the working-copy agent |
| Edits invalidate health | Any change to provider, model, key or endpoint resets `Health.Status` to `unknown` (R10) |
| Failure taxonomy | The engine's `AiProviderTestHandler` already maps provider failures; the page adds the two it can determine locally — missing endpoint (V9) and family mismatch (V7) — before sending, and reports engine-not-connected distinctly (the runner already does) |
| No key in output | The runner already logs provider/model only. The health `Message` is capped at 500 chars and is never built from the key |

---

## Deep linking (FR-017, FR-021)

Two additions, both on existing types:

```
SettingsWindow.ShowDialog(string? initialPageKey)      // selects via SelectTreeLeafByPageKey (:1023)
SettingsWindow.InitialAgentId { get; set; }            // page selects this agent, or adds a new one when ""

OptionsCommand.ShowOptions(string? pageKey, string? agentId) -> bool
```

`ShowOptions` holds the body `Execute` already runs (`OptionsCommand.cs:44-72`): the theme-reopen
loop, `ConfigManager.Save`, `TabColoringManager.RepaintAllTabs`, and — critically — the
`AnalysisSettingsChanged` notification to the engine. The chat panel calls `ShowOptions`; it must
never call `ConfigManager.Save` itself, or the engine keeps serving stale settings and FR-019's
"no restart" silently fails.

When `InitialAgentId` is `""` **and** the agent list is empty, the page performs an implicit **Add**
so the user lands in an editable agent rather than an empty list (FR-017: "a new, empty agent ready
to edit").

---

## Theme and accessibility (FR-035, FR-036)

- Every brush comes from `PageTheme` / `ThemeTokens`. No hardcoded chrome hex. Semantic colours
  (green ready, amber not-tested, red failed) are the documented exception and must read correctly
  in every theme.
- `SolidColorBrush` instances are frozen; `FontFamily` is hoisted to `static readonly`.
- The list must remain legible **hovered and selected** — the exact class of bug spec 036 fixed on
  this dialog. `OptionsHoverContrastTests` is extended to cover the agent list.
- Every control carries `AutomationProperties.Name`. Tab order runs list → buttons → editor →
  assignments.
- New rows register with `ctx.RegisterSearch` so the settings search box finds them.

---

## Test coverage

| Test | Location | Asserts |
|---|---|---|
| Add creates a uniquely named agent and selects it | `AiAgentListPageTests.cs` | FR-026 |
| Duplicate copies the key and renames | same | FR-027 |
| Remove repairs active id, assignments and fallback | same | FR-028 + V16/V17 |
| Switching selection retains unsaved edits | same | FR-031 — the working-copy contract |
| Editing one agent does not alter another | same | FR-030 |
| Duplicate name refused, naming the agent | same | FR-032, V3 |
| 21st agent refused | same | V12 |
| Rename preserves assignments, active, fallback | same | FR-005, US2 scenario 6 |
| Cancel discards every edit | same | Working copy never reaches `AppSettings` |
| Undecryptable key survives a save | `AiAgentKeyProtectionTests.cs` | V23, per agent |
| Typing clears the guard and the notice together | same | The PR #251 follow-up, per agent |
| Switching between a good and a bad agent keeps both correct | same | The reason the flag is per agent |
| Health resets on edit | same | R10 |
| `needsKey` sends no request | same | FR-055 — the fake RPC accessor records zero calls |
| Test uses current field values | same | FR-054 |
| Deep link selects the AI page | `OptionsNavStructureTests.cs` (extended) | FR-017 |
| Hover and selection contrast in the agent list | `OptionsHoverContrastTests.cs` (extended) | FR-036 |
