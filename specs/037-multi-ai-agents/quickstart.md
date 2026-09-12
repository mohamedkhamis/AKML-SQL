# Quickstart: validating Multiple AI Agents with Guided Setup

**Feature**: `037-multi-ai-agents` | **Date**: 2026-09-07

These numbered scenarios are the acceptance gate for "done" (Constitution, Development Workflow).
Run them in order — later scenarios assume the agents earlier ones create.

**Environment**: SSMS 22 or Visual Studio 2026 with the AKML SQL extension deployed, a SQL Server
instance to connect a query window to, and at least one real AI provider key. Config lives at
`%AppData%\AKML SQL\config.json`.

**Before you start**: back up `config.json`. Several scenarios ask you to edit or delete it.

```
copy "%AppData%\AKML SQL\config.json" "%AppData%\AKML SQL\config.backup.json"
```

---

## A. Empty state and guided setup (US1 — FR-015 … FR-023)

1. Delete `config.json`, or edit it so `ai.agents` is `[]` and `ai.provider` is `""`. Restart the host.
2. Open the AI chat panel (Tools → AKML SQL → AI Chat, or `Alt+Z`). **Expect**: an onboarding card
   saying no AI agent is set up. **Expect**: no "Hello! I'm your AI SQL assistant" greeting.
3. Try to type in the message box. **Expect**: it is disabled. **Expect**: Send is disabled.
4. Count the actions on the card. **Expect**: exactly one — **Add AI agent**.
5. Click **Add AI agent**. **Expect**: the Options dialog opens with **AI Assistance** already
   selected in the left tree — you did not have to navigate.
6. **Expect**: an agent already exists in the list, selected, with empty fields ready to edit.
7. Click Cancel. **Expect**: the chat panel still shows the card; the input is still disabled.
8. Click **Add AI agent** again. Fill in a name ("Claude (work)"), pick a provider, paste a key,
   accept the suggested model. Click **Test connection**. **Expect**: success within the timeout.
9. Click OK. **Expect**: within a second, the card is replaced by a greeting naming the agent, and
   the input and Send become enabled — **without restarting the host**.
10. Ask a question. **Expect**: an answer.
11. Now open Options → AI Assistance and clear the agent's API key. Click OK.
12. **Expect**: the chat panel returns to the card within ~5 seconds, and the text names the agent
    and says it needs an API key — not a generic "not set up" message.
13. Click the card's button. **Expect**: Options opens **on that agent**, not on a blank one.
14. Restore the key and click OK.
15. With no agent configured (temporarily clear the key again), invoke **AI → Explain** on a
    selection. **Expect**: the same "no AI agent" message and the same route into Options — not a
    provider error. Restore the key afterwards.

---

## B. Several agents side by side (US2 — FR-024 … FR-036)

16. Options → AI Assistance. Click **Add**. **Expect**: a new agent named "Agent 2" (the lowest free
    number), selected, with the editor bound to it.
17. Name it "Kimi", provider **Kimi (Moonshot)**. **Expect**: the model box auto-fills
    `kimi-latest`.
18. Click **Add** again, name it "Local Llama", provider **Ollama**, model `llama3.1:8b`, endpoint
    `http://localhost:11434`, leave the key empty. **Expect**: no complaint about a missing key —
    local providers do not need one.
19. Select "Claude (work)" in the list, change its temperature slider, then select "Kimi", then
    select "Claude (work)" again. **Expect**: the temperature change is still there. (Unsaved edits
    survive selection changes.)
20. **Expect**: selecting and editing "Kimi" left "Claude (work)" and "Local Llama" untouched.
21. Select "Kimi" and click **Duplicate**. **Expect**: a copy named "Kimi (copy)" with the same
    provider, model and key, selected.
22. Rename the copy to "claude (work)" (different case, same name as agent 1) and move focus away.
    **Expect**: a validation message; OK is refused.
23. Rename it to "Kimi K2" and click **Remove**. **Expect**: a confirmation naming "Kimi K2"; after
    confirming, it is gone and a neighbouring agent is selected.
24. Click **Add** repeatedly until you have 20 agents, then click **Add** once more. **Expect**: a
    refusal naming the 20-agent limit; the existing 20 are untouched. Remove the extras.
25. Rename "Kimi" to "Kimi International". Click OK, reopen Options. **Expect**: the active agent,
    every feature assignment, and the fallback order still point at the right agents — renaming
    broke nothing.
26. Click OK, then open `config.json` in a text editor. **Expect**: every `agents[].apiKey` begins
    with `dpapi:`. **Expect**: no plaintext key anywhere in the file.
27. Reopen Options → AI Assistance. Navigate the whole page with `Tab` and arrow keys only.
    **Expect**: every control is reachable and the focus indicator is visible.
28. Switch the theme (Options → General) between Light and Dark and reopen the page. **Expect**: the
    agent list is legible in both, including the hovered row and the selected row.
29. Type "agent" into the Options search box. **Expect**: the new rows appear in the results and
    clicking one navigates to and flashes the right row.

---

## C. Switching agents from chat (US3 — FR-037 … FR-045)

30. Open the chat panel. **Expect**: a picker in the header showing the agent that will answer.
31. Ask a question. **Expect**: the answer is labelled with that agent's name.
32. Change the picker to "Kimi International". Ask another question. **Expect**: the second answer is
    labelled "Kimi International", the first answer keeps its original label, and the conversation
    was **not** cleared.
33. **Expect**: no restart was needed.
34. Close and reopen the panel. **Expect**: "Kimi International" is still selected.
35. Restart the host, reopen the panel. **Expect**: still "Kimi International".
36. Open the picker. **Expect**: an **Add agent…** entry at the end. Click it. **Expect**: Options
    opens on AI Assistance with a new empty agent. Add one and click OK. **Expect**: the new agent
    is selected in the picker.
37. Click **⧉ Conversation**. Paste into a text editor. **Expect**: each answer is attributed to the
    agent that produced it, in order.
38. Reduce your configuration to a single agent. **Expect**: the picker is still shown, naming that
    agent, and still offers **Add agent…**.
39. Verify the picker did **not** change the active agent: open Options and confirm the ● still
    marks the agent you set as active, not the one you picked in chat.
40. Confirm the untouched behaviours: the header still shows `server.database`; disconnecting the
    query window still produces the "No database connection" refusal; the schema-loading note still
    appears on a cold cache; copy-SQL still works on an answer containing a code block.

---

## D. Per-feature assignment (US4 — FR-046 … FR-052)

41. Options → AI Assistance → Feature assignments. **Expect**: seven dropdowns, each defaulting to
    **Use active agent**.
42. Set **Ghost text** to "Local Llama", leave the rest at the default. Click OK.
43. Type in a query window to trigger ghost text, then ask a chat question. Open **AKML SQL → View
    Logs**. **Expect**: the ghost-text request used "Local Llama" and the chat request used the
    active agent.
44. Change the active agent in Options. **Expect**: chat follows the change; ghost text still uses
    "Local Llama".
45. Delete "Local Llama". Trigger ghost text. **Expect**: it falls back to the active agent, and the
    notice appears **once**, not on every keystroke.
46. Reopen Options. **Expect**: the ghost-text assignment has reverted to **Use active agent**.
47. Assign **Explain** to an agent, then disable that agent (uncheck Enabled). **Expect**: Explain
    behaves exactly as if the agent had been deleted.
48. Configure a fallback order with a deliberately broken agent first. Ask a chat question.
    **Expect**: the answer arrives from the next agent in the order, and the panel says which agent
    answered and that the first was unavailable.
49. Set `ai.offlineProvider` by hand as an earlier build would have, with an empty `fallbackOrder`.
    **Expect**: the offline fallback still works exactly as before.

---

## E. Health and testing (US5 — FR-053 … FR-058)

50. Add an agent and do not test it. **Expect**: its status reads **Not tested**.
51. Add a cloud agent with no key. **Expect**: **Needs API key**, with no delay — nothing is sent.
    Confirm in the logs that no provider request was made.
52. Select an agent, change its model in the editor, and click **Test connection** without clicking
    OK first. **Expect**: the test uses the value you just typed, not the saved one.
53. **Expect**: the status and last-checked time update after the test.
54. Test with a deliberately wrong key. **Expect**: an authentication message, distinct from an
    unreachable-endpoint message.
55. Test an Azure agent with no endpoint. **Expect**: a missing-endpoint message, and no request
    sent.
56. Set a Gemini agent's model to `claude-sonnet-5`. **Expect**: a family-mismatch message naming
    both, before anything leaves the machine.
57. Stop the engine (close all query windows and wait), then test. **Expect**: a distinct
    engine-not-connected message.
58. Edit a `ready` agent's key. **Expect**: its status resets to **Not tested**.
59. Search the whole of `%AppData%\AKML SQL\logs\` for any API key you used.
    **Expect**: zero matches.

---

## F. Migration and compatibility (US6 — FR-010 … FR-014)

60. Restore a `config.json` from the current release — one with `ai.provider`, `ai.model` and a
    `dpapi:` key, and no `ai.agents`. Start the host.
61. Open Options → AI Assistance. **Expect**: exactly one agent, named for its provider, marked
    active. **Expect**: you were not prompted about anything.
62. Ask a chat question **without re-entering the key**. **Expect**: it works.
63. Click OK to save, then reopen. **Expect**: still exactly one agent — migration did not run twice.
64. Compare the stored `apiKey` before and after. **Expect**: byte-identical.
65. Hand-edit `config.json` to set `ai.activeAgentId` to a value matching no agent. Restart.
    **Expect**: the first enabled agent is active; nothing is lost.
66. Hand-edit one entry of `ai.agents` into malformed JSON-compatible garbage (for example, an empty
    `id`). Restart. **Expect**: the other agents load, the dialog opens, and the log names the
    dropped entry.
67. Hand-edit a feature assignment to an id that matches no agent. Restart. **Expect**: that feature
    reverts to **Use active agent**.
68. Set a config with `"provider": "AzureOpenAI"` (the legacy spelling). **Expect**: it migrates to
    an agent whose provider is `azure`, and it works.
69. Set the active agent to "Kimi International", click OK, and — **without restarting anything** —
    open `config.json` in a text editor. **Expect**: `ai.provider`, `ai.model` and `ai.apiKey`
    already match that agent. A stale mirror here means `MirrorActiveAgent` is not running on the
    save path, and every check below is invalid.
69a. With that same file, open it with the **previous** release. **Expect**: it reads the mirrored
    flat fields, uses the active agent, and works — it simply ignores `ai.agents`.
70. Open SSMS and Visual Studio at the same time, change agents in both, and click OK in each.
    **Expect**: the last OK wins; neither config is corrupted; a conversation already in flight
    finishes and picks up the change on its next message.

---

## G. Regression gates

71. Run the shell test suite. **Expect**: green, including the pre-existing
    `AiProviderModelAutofillTests`, `AiChatSessionBindingTests` and `AiChatPanelCopyButtonTests`.
72. Run the Core, AI and Engine suites. **Expect**: green.
73. Confirm the format-parity goldens (977) and the completion corpus (1,342, ~97.5%) are unchanged
    — this feature touches neither, and either moving is a defect.
74. Build the whole solution in one pass with full MSBuild. **Expect**: green, no new warnings.
75. Confirm no `ctoFiles.json`, `resources.json` or `mergeCto.cache` appeared at a drive root.

---

## Restore

```
copy "%AppData%\AKML SQL\config.backup.json" "%AppData%\AKML SQL\config.json"
```
