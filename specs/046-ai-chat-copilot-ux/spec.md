# Feature Specification: AI Chat — Copilot-style experience in SSMS and the web edition

**Feature Branch**: `046-ai-chat-copilot-ux`  
**Created**: 2026-11-04  
**Status**: Draft  
**Input**: User description: "Enhance AI Chat inside SSMS and the web edition (both, SSMS first). UI like Copilot. Ctrl+Enter to send, with the hint shown on mouse hover over the button and similar enhancements. Swap between models in a clear way. Clear the conversation easily to start a new one. The message 'No database connection — this chat answers questions about the database the active SQL editor is connected to. Open a query window and connect it to a database, then ask again.' is not correct: the user did not change the connection, the tab or anything. Investigate well first."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The chat keeps answering about the connected database (Priority: P1)

A SQL developer has a query window connected to a database and the AI Chat open beside it. They run a query, read the error in the Messages pane, copy it, paste it into the chat and ask for a fix. The chat answers about that database, exactly as it did a minute earlier. Whatever part of the query window they last clicked — the editor text, the results grid, the Messages pane — and whether or not the chat panel itself has the keyboard, the chat follows the query window that is active in SSMS.

**Why this priority**: today the chat refuses a question with a wrong explanation ("No database connection") in a routine flow (copy an error from the results, ask for a fix). A wrong refusal destroys trust in the feature faster than any missing polish, and the panel contradicts itself (header says connected, answer says not connected).

**Independent Test**: open one connected query window, open the chat, send a question from each of four starting points — after clicking the editor text, after clicking the results grid, after clicking the Messages pane, and after clicking only inside the chat — and confirm that all four are answered about the same database. Then disconnect the window and confirm the chat refuses with a message that names that window and says it is disconnected.

**Acceptance Scenarios**:

1. **Given** a connected query window and the chat open, **When** the user clicks inside the Messages pane of that window, then clicks into the chat and sends a question, **Then** the question is answered about that window's database and the header still names it.
2. **Given** a connected query window, **When** the user executes a query (focus moves to the results area) and immediately sends a chat message, **Then** the message is answered about that window's database.
3. **Given** the chat header names `server.database`, **When** the user sends any message, **Then** the chat never replies "No database connection" — the header and the send decision always agree.
4. **Given** two query windows connected to different databases, **When** the user activates the second window and sends a message, **Then** the header names the second database within 2 seconds and the answer is about it; the conversation shows one inline note that the chat now answers about the new database.
5. **Given** a connected query window was the last one used, **When** the user clicks into Object Explorer (or any non-query window) and then sends a chat message, **Then** the chat still answers about that last query window and the header still names it.
6. **Given** the bound query window is disconnected (or closed with no other query window open), **When** the user sends a message, **Then** the chat refuses, says which window it looked at and why it cannot answer (for example "SQLQuery1.sql is not connected to a database"), and tells the user what to do; the same reason is written to the product log.
7. **Given** any refusal, **When** the owner opens the product log, **Then** a line written at that moment names the reason and what was resolved (the document name, whether it was a query editor, whether it was connected).

---

### User Story 2 — A Copilot-style conversation in SSMS (Priority: P1)

The SSMS chat reads like GitHub Copilot Chat: each turn is clearly attributed (You / the agent, with its provider and model), the assistant's answer is rendered rather than dumped as raw text — paragraphs, lists, inline code, and SQL in distinct code panels in the editor font — and every code panel carries its own Copy and Insert-into-query actions with a visible confirmation. The composer sits at the bottom with the model selector and the Send control; the empty state welcomes the user, names the bound database and offers starter questions to click.

**Why this priority**: the owner's first request is "UI like Copilot". The present panel shows fenced-code markers as text, stacks detached buttons under each answer, and gives no visible cue what the panel can do. Presentation is what the user sees in every single interaction.

**Independent Test**: ask a question whose answer contains two SQL blocks and a bulleted explanation; confirm both blocks are rendered as code panels with their own Copy / Insert actions, the explanation is rendered as a list, the answer is attributed to the agent with provider and model, and inserting block 2 puts exactly block 2 into the bound query window at the caret.

**Acceptance Scenarios**:

1. **Given** an assistant answer containing a fenced SQL block, **When** it is shown, **Then** the SQL appears in a code panel (editor font, distinct background, no fence markers) with Copy and Insert into query attached to that panel, and activating Copy shows a short "Copied" confirmation on that control.
2. **Given** an answer with a heading, a bulleted list, inline code and a paragraph, **When** it is shown, **Then** each is rendered as such (no raw `#`, `-`, backtick markers visible).
3. **Given** the chat has no turns yet, **When** the panel is shown, **Then** an empty state names the bound database (or that none is bound), says what the chat can do, and offers at least three starter questions; clicking one sends it.
4. **Given** an answer is in flight, **When** the user looks at the panel, **Then** a visible working state names the agent being asked, the composer stays visible, and Send is disabled with a tooltip saying an answer is in progress.
5. **Given** the user scrolled up to read an earlier turn, **When** a new answer arrives, **Then** the view does not jump; **Given** the view was at the bottom, **Then** it follows the new answer.
6. **Given** any of the three themes (light, dark, high contrast), **When** the panel is shown, **Then** every element follows the theme and every control has an accessible name and is reachable by keyboard.

---

### User Story 3 — Send with Ctrl+Enter and discover it (Priority: P2)

The user types a question and presses Ctrl+Enter to send it. Hovering the Send control shows "Send (Ctrl+Enter)". The composer's placeholder says how to send and how to add a line. Every control in the chat (New chat, model selector, copy conversation, per-block actions) explains itself on hover.

**Why this priority**: explicitly requested; cheap; makes the panel self-explaining. Lower than P1 only because the panel is usable without it.

**Independent Test**: in both editions, type a message, press Ctrl+Enter — it sends; hover Send — the tooltip names the shortcut; hover each header and block control — each has a tooltip stating its action.

**Acceptance Scenarios**:

1. **Given** text in the composer, **When** the user presses Ctrl+Enter, **Then** the message is sent (SSMS and web).
2. **Given** text in the composer, **When** the user presses Enter, **Then** the message is sent; **When** Shift+Enter is pressed, **Then** a new line is inserted (unchanged from today).
3. **Given** the Send control is hovered, **When** the composer has text and sending is possible, **Then** the tooltip reads "Send (Ctrl+Enter)"; when it is empty, in flight, or no agent can answer, the tooltip says why Send is disabled.
4. **Given** the composer is empty, **When** the user looks at it, **Then** the placeholder names Enter / Ctrl+Enter to send and Shift+Enter for a new line.
5. **Given** any header or per-block control, **When** hovered, **Then** a tooltip states its action (and its shortcut where one exists).

---

### User Story 4 — Switch the answering model clearly (Priority: P2)

The user sees, next to the composer, which agent will answer and with which provider and model (for example "Gemini · gemini-2.5-flash" or "Anthropic · claude-sonnet-4-5"). Opening the selector lists every usable agent the same way, so two agents on the same provider are told apart by their model. Choosing one takes effect for the next message and the next answer is attributed to it. A trailing entry opens the agent settings.

**Why this priority**: requested ("swap between models in a clear way"). Today the picker lists agent names only; with names such as "gemini-3.5-flash-lite" the user cannot tell provider from model, and in the web edition the chat has no selector at all.

**Independent Test**: configure two agents on different providers; from the chat, switch from the first to the second with at most two clicks; send a message; confirm the answer's attribution names the second agent with its provider and model, and that the selection is still shown after re-opening the panel.

**Acceptance Scenarios**:

1. **Given** at least one usable agent, **When** the chat is shown, **Then** the current agent is visible at the composer with its name, provider and model.
2. **Given** several usable agents, **When** the selector is opened, **Then** each entry shows name, provider and model; the current one is marked.
3. **Given** the user picks another agent, **When** the next message is sent, **Then** it is answered by that agent and the answer's attribution shows that agent's name, provider and model; an answer already in flight keeps the agent it was sent to.
4. **Given** the user picked an agent, **When** the panel is closed and reopened (SSMS) or the page is reloaded (web), **Then** the same agent is selected.
5. **Given** the web edition, **When** the chat is shown with a configured provider, **Then** the same selector is available in the chat and switching there changes the agent the chat uses.
6. **Given** the user chooses a different agent, **When** the selection is made, **Then** one inline note appears in the conversation naming the new agent with its provider and model; choosing the same agent again adds no note.

---

### User Story 5 — Start a new conversation easily (Priority: P2)

The user finishes one topic and wants a clean slate. A "New chat" control in the chat's header clears the conversation, keeps the bound database and the selected agent, and puts the cursor back in the composer. The next question is answered without the earlier turns as context.

**Why this priority**: requested; the SSMS chat has no way to clear at all today (only copy), so the only way to start over is to restart SSMS.

**Independent Test**: after at least two turns, activate New chat; the conversation is empty, the empty state shows, the composer is focused and empty, the header still names the database and agent; send a question that depends on the earlier turns ("and the second one?") and confirm the assistant does not use them.

**Acceptance Scenarios**:

1. **Given** a conversation with turns, **When** the user activates New chat, **Then** the turns are removed, the empty state shows, the composer is focused and empty, and the binding and agent are unchanged.
2. **Given** a new chat was started, **When** the user sends the next message, **Then** no earlier turn is sent as context.
3. **Given** an answer is in flight, **When** the user hovers New chat, **Then** it is disabled with a tooltip saying to wait for the answer (SSMS) — in the web edition it stays usable and stops the running answer, as Clear does today.
4. **Given** the web edition, **When** the user activates New chat, **Then** the stored conversation is replaced by the empty one, as the existing Clear does, and the control is labelled consistently with SSMS.
5. **Given** keyboard-only use, **When** the user tabs to New chat and presses Enter, **Then** it works as by mouse.

---

### User Story 6 — The web edition gets the shared improvements (Priority: P3)

In the browser edition, the chat shows the same Copilot-style presentation (rendered answers, code panels with Copy and Insert at caret, attribution with provider and model, empty state with starter questions), the same composer hints, the model selector and New chat.

**Why this priority**: the owner asked for both editions with SSMS first. The web chat already streams answers, persists the conversation and has Clear/Export, so the gap is smaller.

**Independent Test**: run the web edition with one agent; confirm rendered answers with code panels and per-block Copy / Insert at caret, Ctrl+Enter sends, the Send tooltip names it, the selector shows provider and model, and New chat behaves as in story 5.

**Acceptance Scenarios**:

1. **Given** an answer containing a SQL block, **When** it is shown in the web chat, **Then** it is a code panel with its own Copy and Insert-at-caret actions.
2. **Given** the web chat, **When** the user hovers Send, **Then** the tooltip names Ctrl+Enter.
3. **Given** the web chat with a configured provider, **When** shown, **Then** the agent with provider and model is visible at the composer and selectable.
4. **Given** the existing Export, **When** New chat is added, **Then** Export remains available for a non-empty conversation.

---

### Edge Cases

- The bound query window is closed while the chat is open: the chat rebinds to the next active query window if one exists, otherwise shows "Not connected" and refuses with the reason.
- The query window is connected but its schema is still loading: the existing "schema still loading" note stays; the answer uses the schema once ready (unchanged).
- The engine process is not running: the existing engine error stays (unchanged).
- The user switches agent while an answer is in flight: the in-flight answer keeps the agent it was sent to (unchanged rule).
- New chat while an answer is in flight (SSMS): the control is disabled until the answer completes or fails.
- An answer contains no code: no code panel, no block actions, rendered prose only.
- An answer contains malformed or unclosed fences: rendered as best as possible, never lost; Copy still copies the raw text of the message.
- Very long answers (thousands of lines) stay scrollable and the panel stays responsive.
- Empty or whitespace-only composer: Ctrl+Enter and Enter do nothing; Send is disabled with the reason in its tooltip.
- No usable agent: the existing onboarding card stays; Send, Ctrl+Enter and starter questions do nothing; the selector shows only "Add agent…".
- Privacy mode "anonymous": the existing note stays; attribution and rendering are unaffected.
- A refusal must never be answered "as if the database were empty" (spec 036 FR-028 is kept).

## Requirements *(mandatory)*

### Functional Requirements

#### Binding to the connected database (SSMS)

- **FR-001**: The chat MUST determine the database it answers about from the query window that is active in SSMS (the document tab), and MUST NOT depend on which pane inside that window (editor text, results grid, messages) last had the keyboard, nor on whether the chat panel itself has the keyboard.
- **FR-002**: The header's database display and the decision whether a message can be sent MUST come from one and the same resolution, so that whenever the header names a database, a sent message is answered about that database.
- **FR-003**: When the active window is not a query window (for example Object Explorer or another tool window), the chat MUST keep its last bound query window until that window closes or loses its connection.
- **FR-004**: The chat MUST refuse to send only when no bound, connected query window exists. The refusal MUST say what was found (the window it looked at, and whether it is not a query window, not yet ready, or not connected) and what the user can do.
- **FR-005**: Every refusal MUST be written to the product log at the moment it happens, with the reason and what was resolved (document name, whether it was a query editor with a session, whether it was connected), so that a user report can be verified from the log.
- **FR-006**: The chat MUST never send a request under a fabricated or unknown session identity (spec 036 rule R1 is kept).
- **FR-007**: When the bound window changes to a different server or database, the header MUST update within 2 seconds and the conversation MUST show one inline note naming the new binding; the existing turns are kept.
- **FR-008**: "Insert into query" MUST target the same bound query window the chat answers about (one definition of the active query window for the whole panel).

#### Composer, keys and hints (both editions)

- **FR-010**: Ctrl+Enter MUST send the message. Enter MUST continue to send; Shift+Enter MUST insert a new line.
- **FR-011**: The Send control MUST show a hover tooltip that names the shortcut when sending is possible, and the reason when it is not (empty composer, answer in progress, no agent can answer).
- **FR-012**: The composer placeholder MUST name how to send (Enter or Ctrl+Enter) and how to add a line (Shift+Enter).
- **FR-013**: Every control in the chat — New chat, the agent selector, copy conversation, export (web), per-message copy, per-block Copy and Insert — MUST have a hover tooltip stating its action (and shortcut where one exists) and an accessible name.
- **FR-014**: While an answer is in flight the panel MUST show a visible working state that names the agent being asked.

#### Copilot-style presentation (SSMS first, then web)

- **FR-020**: Assistant answers MUST be rendered: paragraphs, headings, bulleted and numbered lists, inline code, and fenced code blocks; markup characters MUST NOT be shown as literal text.
- **FR-021**: Each fenced SQL block MUST be shown as a distinct code panel in the editor font, with its own Copy and Insert into query (SSMS) / Insert at caret (web) actions attached to the panel, and a short visible confirmation after each action. When an answer has several blocks, each action names its block.
- **FR-022**: Each turn MUST be attributed: user turns as "You"; assistant turns with the answering agent's name, provider and model; the existing fallback wording ("X answered — Y was unavailable") is kept.
- **FR-023**: When the conversation is empty, the panel MUST show a welcome that names the bound database (or that none is bound), says what the chat can do, and offers at least three starter questions that send on activation.
- **FR-024**: The conversation MUST follow the newest turn only when the user is already at the bottom; a user who scrolled up MUST NOT be moved.
- **FR-025**: The panel MUST follow the product theme (light, dark, high contrast) through the existing token system, and all new controls MUST be keyboard reachable with accessible names.
- **FR-026**: Copy conversation (SSMS) and Export (web) MUST keep working and MUST include the answers' original text (not the rendered layout).
- **FR-027**: SSMS answers arrive whole, as today; while waiting, the working state of FR-014 is shown. Streaming answers in SSMS (typewriter, as in the web edition) are a follow-up spec and out of scope here; the web edition keeps the streaming it already has.

#### Model switching (both editions)

- **FR-030**: The agent selector MUST show, for the current selection and for every entry, the agent's name, provider and model, and the current selection MUST be visible at the composer without opening the selector.
- **FR-031**: Choosing an agent MUST take effect for the next message, MUST be persisted as the chat's agent (as today's chat assignment), and MUST be reflected in the next answer's attribution; an answer already in flight keeps the agent it was sent to.
- **FR-032**: The selector MUST list only agents that can answer (spec 037 rule S1 is kept) and MUST end with an entry that opens the agent settings.
- **FR-033**: The web edition's chat MUST offer the same selector; choosing there changes the web edition's active agent, which every web AI action shares (see Assumptions).
- **FR-034**: When the user chooses another agent, the conversation MUST show one inline note naming the agent now answering, with its provider and model (for example "Now answering with Gemini Flash · Gemini · gemini-2.5-flash"), so the switch is visible where the answers appear and not only in the selector. Choosing the agent already selected shows no note.

#### New conversation (both editions)

- **FR-040**: A "New chat" control MUST be in the chat header in both editions. Activating it MUST remove all turns, keep the bound database and the selected agent, show the empty state, and focus the empty composer.
- **FR-041**: After New chat, no earlier turn MUST be sent as context with the next message.
- **FR-042**: In SSMS, New chat MUST be disabled while an answer is in flight, with a tooltip saying why. In the web edition it MUST behave as the existing Clear (usable while streaming, stops the stream).
- **FR-043**: The chat keeps one conversation at a time. New chat replaces it; earlier conversations are not kept and there is no history list. Copy conversation (SSMS) and Export (web) remain the way to keep a conversation before starting a new one.

### Key Entities

- **Conversation**: the ordered turns shown in the panel; cleared by New chat; copied/exported as text.
- **Turn**: one user message or one assistant answer; an assistant turn carries its attribution (agent name, provider, model) and zero or more code blocks.
- **Code block**: a fenced block inside an answer; has its own Copy and Insert actions and a position (n of N) within the answer.
- **Agent**: a configured AI endpoint with a name, a provider and a model; one is the chat's current agent.
- **Binding** (SSMS): the query window the chat answers about — its document name, server, database, connection state and editor session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With one connected query window, a message sent after clicking each of four places (editor text, results grid, Messages pane, chat composer only) is answered about that database in 4 of 4 cases, in a scripted acceptance run repeated on two different windows.
- **SC-002**: Over the whole acceptance run, 0 refusals occur while the header names a database (header and send decision never disagree).
- **SC-003**: 100% of refusals have a product-log line, written within the same second, naming the reason and what was resolved.
- **SC-004**: A new user discovers how to send by keyboard without documentation: the hint is visible in the empty composer, and the Send tooltip appears within 1 second of hovering, in both editions.
- **SC-005**: Switching the answering model takes at most 2 clicks from the composer, and the next answer's attribution names the chosen agent with its provider and model in 100% of switches tested.
- **SC-006**: Starting a new conversation takes 1 activation; within 1 second the conversation is empty and the composer is focused.
- **SC-007**: In 100% of answers containing fenced SQL, the SQL is shown as a code panel with per-block Copy and Insert; a side-by-side comparison with GitHub Copilot Chat passes a 5-item checklist (turn attribution, code panels, block actions, composer with model and send, empty state with starters).
- **SC-008**: All existing chat tests keep passing; every new control is announced with a name by the screen reader in light, dark and high-contrast themes.

## Assumptions

- "Models" are the product's configured agents (name + provider + model); switching a model means choosing an agent. Selection stays persisted as the chat's agent, as today.
- Enter keeps sending (today's behaviour in both editions) and Ctrl+Enter is added; Shift+Enter inserts a new line.
- In SSMS the conversation lives for the SSMS session (as today); in the web edition it stays persisted in the browser (as today). New chat replaces it; earlier conversations are not kept (FR-043, owner's decision 2026-11-04).
- SSMS answers arrive whole; streaming in SSMS is a follow-up spec (FR-027, owner's decision 2026-11-04).
- The "message not found" in the request was the confirmation after switching the model; it is the inline note of FR-034 (owner's decision 2026-11-04).
- When a non-query window is active, the chat keeps the last bound query window ("sticky" binding) rather than refusing.
- Agents that cannot answer stay hidden from the selector (spec 037 S1); the selector ends with the entry that opens the agent settings.
- The web edition's chat has no database binding (it calls the provider from the browser), so the binding fix is SSMS-only; everything else applies to both editions.
- Starter questions in the empty state are generic database questions (largest tables, recent objects, explain a table) and are not generated by the AI.
- Spec 036 rules kept: never send under a fabricated session id (R1); say explicitly when no connection exists (FR-028); the schema-loading and privacy-mode notes stay.
- In the web edition there is one active agent shared by every AI action (Explain, Fix, Optimize, Text to SQL, Index Analysis and chat). Choosing an agent in the web chat changes that shared active agent, so the other web AI actions follow it (FR-033). A chat-only selection in the web edition is not in scope unless the owner asks for it.

## Notes for planning (investigation of the "No database connection" report)

Context for the planner and for the owner to confirm, not requirements. User Story 1 is the behaviour that follows from it.

- **What the chat does today**: when the user presses Send, the SSMS chat looks up "the active editor" one way, and the header at the top of the panel looks it up another way. The header asks SSMS which *document window* is active and reads its title (the `server.database` caption). The send path instead asks SSMS which *text view* last had the keyboard, and then requires that view to be a query editor the extension has tagged with an editor session. The results **Messages** pane (and, to be verified, the results grid) is a text view of its own inside the same query window, and it is never tagged, because it is not a SQL document.
- **What happened in the report**: the owner ran the SQL the assistant produced, got `Msg 156 … Incorrect syntax near the keyword 'RowCount'`, selected the error text in the Messages pane (the pasted text, including the "Completion time" line, is exactly what that pane shows), copied it, clicked into the chat and sent it. At that moment the "last text view with the keyboard" was the Messages pane, not the editor, so the send path found no tagged editor and refused — while the header still showed `dbdevtestvm….dbCentralQuota`, and the "Insert into query" button (which uses the document window, like the header) kept working. "fix it" was refused for the same reason: the editor had not been clicked since.
- **Evidence in the local log** (`%AppData%\AKML SQL\logs\akmlsql-20261104.log`): three chat requests succeeded (16:58:31, 16:59:36, 17:03:53); no chat request reached the engine after 17:03:53, although the caption-based detector kept reporting the connected window every 2 seconds and the query was executed at 17:04:01 (`ExecutionCommandFilter: intercepted Query.Execute`). The refusal path writes nothing to the log, which is why the log cannot show the reason directly — that gap is FR-005.
- **The owner was right**: no connection, tab or window was changed. The chat's notion of "active editor" was narrower than the user's (a window, not a pane inside it), and two parts of the same panel disagreed about it.
- **One-step check for the owner**: with a connected query window and the chat open, click once inside the Messages (or Results) pane of that window, click into the chat composer, send any question. Today this reproduces the refusal; after this feature it must answer.
- **Scope boundary**: the web edition's chat sends to the AI provider directly from the browser and is not bound to a database session, so this defect cannot occur there. The web edition shares the other improvements (composer keys and hints, model switching, new chat, Copilot-style presentation).
- **Plan-level note**: attribution with provider and model (FR-022, FR-030) means the chat answer must carry more than the agent's name; an additive change, not a requirement here.
