# Feature Specification: Readable Options Navigation, Kimi-Capable Schema-Aware AI Chat, and a Working Update Channel

**Feature Branch**: `036-kimi-chat-updater-fixes`
**Created**: 2026-09-02
**Status**: Draft
**Input**: User description: "1- at AKML sql option, light them when the mouse hovers over the right menu items; the text light is white, and I cannot see it. Please fix it. 2- AI chat, I want to make it easy for me first, add Kimi to allow me to select my API, second, allow me to copy SQL (like now) and copy the full message, and make sure my API have full access to my schema because now he does not have full access to my current DB schema to allow me to ask related to the current DB. 3- installer AKML need to visit, and auto update needs to be processed on it and tested well, because we have a server and CDN at GitHub."

**Delivery note**: This specification is written to be implemented end-to-end by a single implementer working alone. Every user story below is independently shippable and independently verifiable, so partial delivery still produces working value. Work outside this specification is handled separately and must not be started here.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The AI chat actually knows the database I am connected to (Priority: P1)

A database developer has an AKML SQL editor open against a live database. They open the AI chat panel and ask questions about that database in plain language — "what tables do I have?", "which columns are in Orders?", "how is Orders related to Customers?", "write me a query that lists unpaid invoices per customer". The assistant answers using the real objects, columns, keys and relationships of the database the editor is connected to, and the generated SQL references object and column names that actually exist.

**Why this priority**: This is the difference between a chat panel that is useful and one that is decorative. Today the assistant answers as though the database were empty, so every schema question produces invented or generic output. Nothing else in this feature matters as much.

**Independent Test**: Connect an editor to a known database, open the chat, ask "list the tables in this database" and "describe the columns of <a known table>". Both answers must contain the real names from that database and no invented ones. Fully testable without touching provider selection, copy actions, theming, or the updater.

**Acceptance Scenarios**:

1. **Given** an editor connected to a database containing tables the user can name, **When** the user asks the chat "what tables are in this database?", **Then** the answer lists the actual user tables of that database (not a placeholder, not an apology, not an invented example schema).
2. **Given** an editor connected to a database, **When** the user asks about a specific existing table by name, **Then** the answer includes that table's real columns with their data types and states which column(s) form its primary key.
3. **Given** two tables joined by a foreign key, **When** the user asks how they relate, **Then** the answer names the real foreign-key relationship and produces a join on the correct columns.
4. **Given** an editor connected to a database, **When** the user asks a question phrased with no object names at all ("summarise my schema"), **Then** the answer still reflects that database's inventory rather than an empty or arbitrarily filtered view of it.
5. **Given** the user switches the editor to a different database and asks the same question again, **Then** the answer reflects the newly selected database, and the chat panel shows which database it is answering about.
6. **Given** no AKML SQL editor is connected to any database, **When** the user asks a schema question, **Then** the assistant states plainly that it has no database connection and tells the user how to get one, instead of answering from nothing.
7. **Given** the schema for the connected database is still being loaded, **When** the user asks a schema question, **Then** the assistant says the schema is still loading and the answer that follows (or the retry) uses the loaded schema.

---

### User Story 2 - I can choose Kimi as my AI provider and use my own key (Priority: P1)

The user holds a Kimi (Moonshot) API key and wants AKML SQL's AI features to run on it. They open Options, pick Kimi from the provider list, paste their key, confirm the connection works from inside the dialog, save, and every AI feature in the product then runs on Kimi.

**Why this priority**: The user cannot use the product's AI at all with the key they own. Combined with Story 1, this is what "make it easy for me first" means: pick my provider, paste my key, ask about my database.

**Independent Test**: In Options → AI Assistance, select Kimi, paste a valid key, use the in-dialog connection test, save, reopen Options and confirm the selection persisted, then send one chat message and receive a real answer. Testable independently of schema access — a non-schema question ("what does a clustered index do?") is enough to prove the provider path.

**Acceptance Scenarios**:

1. **Given** the AI provider list in Options, **When** the user opens it, **Then** Kimi is offered as a named choice alongside the existing providers.
2. **Given** the user selects Kimi, **When** the model and endpoint fields are shown, **Then** they are pre-filled with working defaults for Kimi that the user can override.
3. **Given** the user pastes a Kimi API key, **When** they look at the field, **Then** the key is masked, and it is never written to logs or to any diagnostics bundle.
4. **Given** a saved Kimi configuration, **When** the user triggers the in-dialog connection test, **Then** they get an unambiguous success or failure result within a few seconds, and a failure names the likely cause (bad key, wrong endpoint, no network, wrong model).
5. **Given** Kimi is saved as the provider, **When** the user closes and reopens Options, **Then** Kimi is still selected with the same model, endpoint, and key.
6. **Given** Kimi is the active provider, **When** the user invokes any AI feature (chat, explain, fix, optimize, index analysis, natural-language-to-SQL, inline completion), **Then** each runs against Kimi and returns a result.
7. **Given** the user leaves a model name from a different vendor in the model box while Kimi is selected, **When** they run an AI action, **Then** they get a message naming both the model's actual vendor and the selected provider and telling them how to fix it — never a raw provider error page or JSON.
8. **Given** any provider in the list is selected and saved, **When** an AI action runs, **Then** it is accepted — no entry in the list may save a provider name the AI backend then rejects as unknown.

---

### User Story 3 - I can get anything out of the chat and into my query window (Priority: P2)

The user gets an answer that mixes prose and one or more SQL blocks. They copy just one SQL block when they want to run it, copy the whole message when they want to keep the explanation with it, select a few lines with the mouse when they want only part, and copy the whole conversation when they want to file it somewhere.

**Why this priority**: The chat is only useful if its output can leave the panel. Per-SQL copy already exists; whole-message and partial copy are the gaps that force the user to retype.

**Independent Test**: Ask a question that returns prose plus two SQL blocks. Copy each SQL block, copy the full message, select part of the text with the mouse and copy it, and copy the conversation. Paste each into a query window and confirm the content. Independent of provider choice and of schema access.

**Acceptance Scenarios**:

1. **Given** an assistant answer containing SQL, **When** the user uses the copy action attached to a SQL block, **Then** only that SQL reaches the clipboard, without surrounding prose or code-fence markers.
2. **Given** an assistant answer containing multiple SQL blocks, **When** the user looks at the message, **Then** each block has its own copy action and it is clear which block each one belongs to.
3. **Given** any message in the conversation, **When** the user uses its copy-message action, **Then** the complete message text reaches the clipboard including prose and all SQL.
4. **Given** any message, **When** the user drags across part of the text, **Then** the text is selectable and the standard copy gesture copies exactly the selection.
5. **Given** a message the user sent, **When** they want it back, **Then** their own messages offer the same copy affordance as the assistant's.
6. **Given** a conversation with several turns, **When** the user copies the conversation, **Then** the clipboard holds the full exchange with each turn clearly attributed.
7. **Given** any copy action, **When** it succeeds, **Then** the user sees a brief confirmation; **When** it fails, **Then** the user is told it failed and the message is still on screen and still copyable.

---

### User Story 4 - The Options navigation stays readable while I move the mouse over it (Priority: P2)

The user opens AKML SQL Options and moves the mouse down the navigation list to find a page. Every item stays readable under the pointer, including the page they currently have selected.

**Why this priority**: A small defect the user hits on every single visit to Options, and it makes the currently selected page disappear exactly when the user is looking for it. Cheap to fix, constant irritation.

**Independent Test**: Open Options in each shipped theme, move the pointer over every navigation item including the selected one, and confirm every label is readable at all times. Purely visual, independent of everything else in this feature.

**Acceptance Scenarios**:

1. **Given** the Options dialog in any shipped theme, **When** the pointer rests on a navigation item that is not selected, **Then** the item's label remains clearly readable against its hover background.
2. **Given** the currently selected navigation item, **When** the pointer rests on it, **Then** its label remains clearly readable — the hover state must never leave light text on a light background or dark text on a dark background.
3. **Given** the pointer moves off an item, **When** the hover state clears, **Then** the item returns to exactly the appearance it had before, with no residual colour.
4. **Given** the Options search results list, **When** the pointer rests on a result, including a selected one, **Then** the same readability guarantee holds.
5. **Given** any hover or selected state anywhere in the Options dialog, **When** its label and background colours are compared, **Then** the contrast between them meets the normal-text accessibility threshold in every shipped theme, including High Contrast.

---

### User Story 5 - Updates reach me, install cleanly, and are proven to work (Priority: P3)

The user is running an older installed build. Within a day of a new release being published, the product tells them a new version exists, shows what changed, and gets them to an installed new version without hunting for a download. Their settings, history, snippets, styles and saved credentials survive the upgrade.

**Why this priority**: Nothing is broken for a user who never updates, but the update path currently points at a destination that does not exist, so no user will ever be told about a release. It ranks below the AI and readability work because it affects future releases rather than today's session.

**Independent Test**: On a clean machine, install the previous published build, publish (or point at) a newer release, let the product check for updates, and follow the notification through to a completed upgrade. Confirm afterwards that settings, query history, snippets, format styles and stored credentials are intact. Independent of all AI and theming work.

**Acceptance Scenarios**:

1. **Given** an installed build and a newer published release, **When** the product performs its scheduled update check, **Then** the check reaches a live endpoint and completes successfully.
2. **Given** a newer release is found, **When** the user next opens the product, **Then** they are told which version is available and can reach its release notes.
3. **Given** the update notification, **When** the user chooses to update, **Then** the installer is downloaded for them with visible progress, verified, and — after one confirmation that names the applications which must close — launched with its normal interface, so the user never has to find and download a file themselves.
4. **Given** an installer obtained through the update flow, **When** it is about to run, **Then** its integrity is verified against the published checksum, and a mismatch stops the flow with a clear message rather than running the file.
4a. **Given** the confirmation prompt before the installer launches, **When** the user declines, **Then** nothing is installed, no application is closed, and the user is left exactly where they were with the update still available to accept later.
4b. **Given** a download in progress, **When** the user cancels it, **Then** the download stops, no partial file is left on disk, and the next check still offers the update.
5. **Given** the newest release is already installed, **When** an update check runs, **Then** the user is told nothing at all during the automatic check, and is told "you are up to date" when they asked for the check themselves.
6. **Given** no network, a blocked host, or a malformed response, **When** an update check runs automatically, **Then** the product carries on normally, the user sees no error, and the failure is recorded in the log.
7. **Given** the user picks "Check for updates" from the menu, **When** the last automatic check was recent, **Then** the check still runs immediately and reports its outcome.
8. **Given** a completed upgrade over an existing install, **When** the user opens the product, **Then** their configuration, query history, snippets, format styles and saved database credentials are exactly as before, and no manual uninstall was needed.
9. **Given** the shells were open during the upgrade, **When** the installer runs, **Then** the user is told which applications must close and the upgrade completes without leaving a half-installed state.
10. **Given** a release is published, **When** the download page and the update channel are compared, **Then** they name the same latest version, the same file, and the same checksum.

---

### Edge Cases

- **Chat asked about a database whose schema is enormous**: the assistant must be given the largest useful, budget-bounded view, must be told that the inventory was truncated, and must say so rather than implying the list is complete.
- **Chat asked about a database the user cannot fully read**: objects the connected login cannot see are simply absent; the assistant must not claim the database is empty.
- **Privacy mode strips or hashes identifiers**: the user must be told why the assistant is not naming their objects and how to change it, rather than receiving a confidently wrong answer.
- **Connection changes mid-conversation**: earlier turns referred to the old database; the panel must make the current binding obvious so the user is not misled.
- **Chat opened before any editor exists**: the panel must say it has no database context yet, and pick one up when an editor connects.
- **Kimi key valid but out of quota, or endpoint region unreachable**: the failure must name the cause; a quota or region problem must not be reported as "AI is disabled".
- **Kimi key saved while an older provider's model name is still present**: the mismatch must be caught before the request leaves the machine.
- **Copy attempted while another application holds the clipboard**: the failure is surfaced and the content is not lost.
- **Copying a message with no SQL in it**: the whole-message copy must still work; no SQL-specific action should appear.
- **High Contrast theme active**: hover and selection colours come from the operating system; the readability guarantee must still hold.
- **Host application theme changed while Options is open**: hover states must adopt the new theme without leaving an unreadable combination behind.
- **Update check on a machine with no credentials for the release host**: must succeed anonymously; the update channel must never require the user to sign in anywhere.
- **Update published while the previous notification is still pending**: the user must end up pointed at the newest version, not a stale one.
- **A published release whose file is missing or whose checksum does not match**: the update flow must refuse it and say why.
- **Downgrade or equal version offered**: must be treated as "no update", never offered as an upgrade.
- **Download interrupted, or the disk fills part-way through**: the user is told, no partial file survives, and the update remains available to retry.
- **User declines the confirmation, or closes it**: nothing installs, no application closes, and the offer is still there next time.
- **Update already downloaded and verified on a previous attempt**: the user is not made to download it a second time.
- **A second host is running while the upgrade proceeds**: the confirmation must name every application that will be closed, not just the one the user is looking at.

## Requirements *(mandatory)*

### Functional Requirements — Options navigation readability

- **FR-001**: Every item in the AKML SQL Options navigation MUST remain readable while the pointer is over it, in every shipped theme.
- **FR-002**: The currently selected navigation item MUST remain readable while the pointer is over it; hovering a selected item MUST NOT change its background without also keeping its label colour matched to that background.
- **FR-003**: The same readability guarantee MUST apply to every other hover-highlighted list in the Options dialog, including the search results list.
- **FR-004**: For every combination of state (normal, hovered, selected, selected-and-hovered) and every shipped theme (Light, Dark, host-derived, High Contrast), the contrast between an item's label colour and its background colour MUST meet the normal-text accessibility threshold, and this MUST be enforced by an automated test that fails the build on regression.
- **FR-005**: Leaving an item MUST restore its previous appearance exactly.

### Functional Requirements — Kimi provider

- **FR-006**: Kimi MUST be offered as a named, selectable AI provider wherever AI providers are chosen.
- **FR-007**: Selecting Kimi MUST pre-fill a working default model identifier and a working default service endpoint, both of which the user MAY override.
- **FR-008**: A Kimi API key MUST be accepted, masked in the interface, protected at rest with the same mechanism used for other providers' keys, and excluded from logs and diagnostics bundles.
- **FR-009**: The AI configuration page MUST provide a way to verify the configured provider, model, endpoint and key from inside the dialog, returning a clear success or a failure that names the likely cause. This verification MUST be available for every provider, not only Kimi.
- **FR-010**: A saved Kimi configuration MUST round-trip: reopening the configuration MUST show the same provider, model, endpoint and key.
- **FR-011**: All AI capabilities in the product — chat, explain, fix, optimize, index analysis, natural-language-to-SQL, and inline completion — MUST work with Kimi selected.
- **FR-012**: A model name that clearly belongs to a different vendor MUST be refused before the request leaves the machine, with a message naming the model's vendor, the selected provider, and the corrective action. This guard MUST cover Kimi in both directions (a Kimi model under another provider, and another vendor's model under Kimi).
- **FR-013**: Every provider offered in the configuration list MUST be accepted by the AI backend when saved. Any provider name that the interface can save but the backend rejects as unknown MUST be corrected as part of this work.
- **FR-014**: When an AI request fails, the user MUST see a message that distinguishes at minimum: missing or invalid key, unknown or unavailable model, unreachable endpoint, quota or rate limit, and request timeout.

### Functional Requirements — Chat copy actions

- **FR-015**: Each SQL block in an assistant message MUST have its own copy action that places only that SQL on the clipboard, without prose or code-fence markers.
- **FR-016**: Every message in the conversation, from the user and from the assistant, MUST offer an action that copies its complete text.
- **FR-017**: Message text MUST be selectable with the pointer and the keyboard, and the standard copy gesture MUST copy exactly the selection.
- **FR-018**: The panel MUST offer an action that copies the entire conversation with each turn attributed to its speaker.
- **FR-019**: A successful copy MUST give brief visible confirmation; a failed copy MUST tell the user it failed and MUST leave the message intact and re-copyable.
- **FR-020**: Copy actions MUST be reachable by keyboard and MUST carry accessible names.

### Functional Requirements — Schema access for AI

- **FR-021**: Every AI request that claims schema awareness MUST be associated with the database connection of the editor the user is actually working in; requests MUST NOT be sent under an identity that has no connection behind it.
- **FR-022**: When an editor is connected, the chat MUST be given the connected database's real object inventory, and MUST answer schema questions from it.
- **FR-023**: The schema information supplied to the assistant MUST include, for the objects in scope: schema and object name, object type, columns with data type and nullability, primary key columns, and foreign-key relationships between included objects.
- **FR-024**: A question that names no recognisable object ("what tables do I have", "describe my schema", "where should I add indexes") MUST result in the assistant receiving the database's full object inventory up to the size budget, NOT a subset produced by incidental word matching.
- **FR-025**: Relevance narrowing MAY be used to add detail for objects a question names, but MUST NOT be able to remove the rest of the inventory from the assistant's view when the question is general.
- **FR-026**: The size budget for schema information MUST be explicit, documented, and configurable; when it is exceeded, the assistant MUST be told the inventory was truncated and the user MUST see a note saying so.
- **FR-027**: The chat panel MUST display the server and database the conversation is currently bound to, and MUST update that display when the user's active connection changes.
- **FR-028**: When no connection is available, the assistant MUST say so explicitly and tell the user how to establish one, instead of answering as though the database were empty.
- **FR-029**: When the schema is still loading, the user MUST be told, and the answer MUST use the schema once it is available.
- **FR-030**: The active privacy mode MUST continue to be honoured. When the active mode prevents the assistant from seeing real object names, the user MUST be told this is why, and MUST be told which setting controls it.
- **FR-031**: The schema-context correctness required by FR-021 to FR-026 MUST apply to explain, fix, optimize, index analysis, natural-language-to-SQL and inline completion as well as chat — not to chat alone.
- **FR-032**: Schema information supplied to a provider MUST NOT include table data rows; only metadata and, where the user has opted in, the query text they are working on.

### Functional Requirements — Installer and automatic updates

- **FR-033**: The automatic update check MUST query a live, publicly reachable, project-owned location. No shipped build may point the check at a host that does not resolve.
- **FR-034**: The update check MUST succeed anonymously — no account, credential, or token on the user's machine.
- **FR-035**: The published update information MUST carry, for the latest release: version, release date, download location, checksum, and a link to release notes.
- **FR-036**: The download page on the product site and the update channel MUST always name the same latest version, the same file, and the same checksum. This consistency MUST be produced by the release process, not by hand, and MUST be checked automatically.
- **FR-037**: A version is an update only if it is strictly newer than the installed one; equal or older versions MUST be reported as "no update available".
- **FR-038**: When an update is available, the user MUST be notified in-product with the version and a route to the release notes.
- **FR-039**: From the notification the user MUST be able to reach an installed new version through one guided flow: the product downloads the installer for them, verifies it, asks for a single confirmation that names which applications must close, and then launches the installer with its normal interface visible. The upgrade MUST NOT proceed silently or close the user's applications without that confirmation, and the user MUST be able to decline and be left exactly as they were.
- **FR-039a**: While the download is in progress the user MUST be able to see that it is happening and MUST be able to cancel it; cancelling MUST leave no partial file behind and MUST NOT count as a completed check.
- **FR-040**: Any installer obtained automatically MUST have its checksum verified against the published value before it is executed; a mismatch MUST abort the flow with an explicit message and MUST NOT run the file.
- **FR-041**: An automatic update check that fails for any reason MUST be invisible to the user, MUST NOT block or delay the host application, and MUST be recorded in the log with the reason.
- **FR-042**: The manual "Check for updates" command MUST run the check immediately regardless of when the last automatic check happened, and MUST report its outcome — up to date, update available, or check failed.
- **FR-043**: Installing a newer build over an existing installation MUST upgrade in place without requiring a manual uninstall, and MUST preserve configuration, query history, snippets, format styles and saved database credentials.
- **FR-044**: The upgrade MUST tell the user which applications need to close, and MUST leave the installation in a working state whether or not those applications were open when it started.
- **FR-045**: After an upgrade, the version reported inside the product, the version recorded for the installation, and the version that was published MUST all agree.
- **FR-046**: The complete path — publish, detect, notify, download, verify, install, restart, confirm version — MUST be executed on a clean machine against a real published release, and the evidence MUST be recorded in the repository as part of this work. A passing unit test is not sufficient evidence for this requirement.

### Key Entities

- **AI Provider Profile**: the user's choice of AI service and the settings that make it work — provider name, model identifier, service endpoint, secret key, and request limits. One profile is active at a time; a separate profile may be nominated as the offline fallback.
- **Schema Context**: the description of the connected database handed to the AI for a single request — the database identity, the objects in scope with their columns, keys and relationships, the detail level, and whether the inventory was truncated.
- **Editor Session**: the link between an open SQL editor, the server and database it is connected to, and the cached schema for that database. Every schema-aware AI request belongs to exactly one of these.
- **Chat Message**: one turn in a conversation — its speaker, its full text, and the SQL blocks extracted from it. Each is independently copyable in whole, in part, and per SQL block.
- **Release Record**: one published version — version number, release date, download location, mirror location, checksum, supported hosts, and release notes. The set of these is what both the download page and the update check read.
- **Update Outcome**: the result of one update check — whether a newer version exists, which one, where to get it, its checksum, and when the check ran.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Sweeping the pointer across every item in the Options navigation, in every shipped theme, produces zero unreadable states — including over the currently selected item. Every state/theme combination meets the normal-text contrast threshold, verified automatically.
- **SC-002**: A user who has a Kimi key can go from opening Options to receiving their first successful AI answer in under 2 minutes, without editing any file by hand and without consulting documentation.
- **SC-003**: With an editor connected to a database, 10 out of 10 attempts at "list the tables in this database" return the database's real tables; 9 out of 10 varied schema questions are answered using real object and column names, with no invented objects.
- **SC-004**: Generated SQL for a question about the connected database references only objects and columns that exist in that database, in at least 9 of 10 attempts.
- **SC-005**: Any part of any chat answer can be placed on the clipboard in at most two actions — one SQL block alone, one whole message, an arbitrary selection, or the whole conversation.
- **SC-006**: Every AI failure a user can provoke by misconfiguration (bad key, wrong model, wrong endpoint, no network, exhausted quota) produces a message that names the cause; zero raw provider errors or unexplained failures reach the user.
- **SC-007**: Automatic update checks reach a live endpoint and complete without error on at least 99% of attempts from a machine with ordinary internet access; zero checks are made against a host that does not resolve.
- **SC-008**: A user running the previously published build learns about a new release within one day of publication and reaches the installed new version with exactly one confirmation and no manual file hunting; zero upgrades close a user's applications without that confirmation.
- **SC-009**: 100% of user configuration, query history, snippets, format styles and saved credentials survive an in-place upgrade, measured by comparing before and after on a populated installation.
- **SC-010**: The latest version named by the download page, the update channel and a freshly upgraded installation agree in 100% of release checks.
- **SC-011**: A recorded end-to-end upgrade run on a clean machine — from the previous published build to the newest — exists in the repository, dated, listing each step and its result.

## Assumptions

- "The right menu items" in the user's report refers to the navigation list in the AKML SQL Options dialog; the reported symptom (white text that becomes invisible on hover) is treated as covering every hover-highlighted list in that dialog.
- "Kimi" means the Moonshot AI service. Its chat interface is compatible with the widely used OpenAI-style request format, so it is treated as a first-class named provider rather than requiring the user to configure a generic custom endpoint by hand. The default endpoint targets the international service; users on the mainland-China service override the endpoint field.
- The default Kimi model offered on selection is a rolling alias rather than a pinned version, following the existing project convention that pinned vendor model names rot.
- "Full access to my schema" means the assistant must see the complete object inventory of the connected database, plus column, key and relationship detail — not that it may read table data. Row data is out of scope and remains excluded.
- The size budget for schema information exists to control cost and latency. A default budget is chosen so that a typical departmental database (a few hundred objects) fits entirely.
- "We have a server and CDN at GitHub" refers to the existing product site and the existing practice of publishing installers as GitHub release assets. The update channel is expected to reuse both rather than introduce new infrastructure.
- Update checks remain opt-out via the existing automatic-update setting, and remain throttled to at most one automatic check per day.
- The update flow is assisted, not silent (decided 2026-09-02): the product does the downloading and verifying, the user keeps control of when their applications close. A fully unattended background upgrade was considered and rejected because it would close the user's SSMS or Visual Studio without warning and would hide failures.
- Kimi is delivered for the desktop hosts only in this feature (decided 2026-09-02); the web edition keeps its current provider list.
- Existing behaviour that already works — per-SQL-block copy in the chat, the notification file the shell reads on startup, the installer's in-place upgrade identity — is to be preserved, not rebuilt.
- Accessibility threshold means the normal-text contrast ratio conventionally required for accessible interfaces (4.5:1).

## Out of Scope

- Any change to which AI features exist, or to their prompts' content beyond what schema access requires.
- Redesign of the Options dialog beyond the readability defects described.
- Rich rendering of chat messages (syntax highlighting, tables, streaming markdown) — only selection and copying are in scope.
- Reading or sending table data to any AI provider.
- New update infrastructure — no new servers, domains, or hosting arrangements.
- **The browser-based web edition's AI configuration surface.** Kimi is delivered for the SSMS 22 and Visual Studio 2026 desktop hosts only in this feature. Shared components that both editions consume may change as a consequence of the desktop work, and must not be broken by it, but no web-edition provider picker, key-vault path, or web test is in scope. Web-edition parity is a separate future decision.
- Localisation of any new user-facing text.

## Dependencies

- A valid Kimi (Moonshot) API key must be available to verify Story 2 end to end.
- A SQL Server database with a known, non-trivial schema — several tables, columns of varied types, primary keys and at least one foreign key — must be available to verify Story 1.
- A clean Windows machine with the previously published build installed, plus the ability to publish a newer release, is required to verify Story 5 (FR-046). Verification cannot be completed on the development machine alone.
- Publishing a release requires the existing release process and its credentials.

---

## Appendix: Implementation Notes (non-normative)

*The requirements above are the contract. This appendix records what the current code does and where, so the implementer does not have to rediscover it. It is background, not requirement text — if it conflicts with a requirement, the requirement wins.*

### A. Options navigation hover (Story 4)

- The navigation tree is built in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (~lines 390–435). Two triggers are added to the same style: a selected trigger that sets **both** background and foreground, then a mouse-over trigger that sets **only** background. In WPF the later trigger wins for the property it sets, so a *selected and hovered* item ends up with the hover background and the selected foreground — light background, white text. That is the reported symptom.
- The search-results list in the same file (~lines 728–742) adds its triggers in the opposite order, so it does not show the bug — but it also never pairs a foreground with the hover background, so it is one palette change away from the same defect.
- Colours come from `PageTheme` (`src/AkmlSql.Shell.Shared/Ui/Theme/PageTheme.cs`), which maps `TreeHover` to the `SurfaceHover` token and `SelectedText` to `TextOnAccent`. Token values live in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemePalette.cs`; note the High Contrast palette maps `SurfaceHover` to the system highlight brush, so any fix must not assume a fixed colour.
- Existing coverage to extend: `WindowChromeTests` already reflects over the nav style to assert nav text visibility per theme. FR-004's automated contrast sweep belongs alongside it.
- Project convention: brushes are frozen, chrome colours always come from tokens, never hex. See the WPF conventions section of `CLAUDE.md`.

### B. Kimi provider (Story 2)

- Provider construction: `src/AkmlSql.AI/Providers/AiProviderFactory.cs`. The switch accepts `anthropic`, `openai`, `azure`, `gemini`, `ollama`, `lmstudio`, `custom`. OpenAI-compatible endpoints are already handled by `CreateOpenAiClient(apiKey, model, endpoint)`, which is the natural shape for Kimi.
- Provider list and persistence: `src/AkmlSql.Shell.Shared/Dialogs/Pages/AiAssistancePage.cs`. The list is a hard-coded string array; `Load` maps stored names to indices and `Save` maps indices back to names.
- **Existing defect covered by FR-013**: `Save` writes `"AzureOpenAI"` and `"LMStudio"`, but the factory switches on `"azure"` and `"lmstudio"`. Selecting Azure OpenAI or LM Studio therefore saves a value the factory rejects with "Unknown AI provider". Adding a provider means touching exactly this mapping, so fix it here.
- Model-family guard: `src/AkmlSql.Core/Config/AiModelFamily.cs` — `Detect` and `DefaultModelFor`. Extend both for Kimi (FR-012, FR-007) and keep `Detect` returning null for genuinely unrecognised names so local and fine-tuned models are never second-guessed.
- Settings shape: `AiSettings` in `src/AkmlSql.Core/Config/AppSettings.cs` (~line 1126). `Provider`, `Model`, `Endpoint`, `ApiKey` already exist; no new fields should be needed.
- Key protection: the factory decrypts through the `KeyDecryptor` hook, which the engine points at its DPAPI credential manager. Kimi keys must flow through the same hook — do not add a second key path.
- The connection test required by FR-009 is **already built engine-side and simply unreachable**: `AiProviderTest` (message 77) / `AiProviderTestResult` (177) exist in `src/AkmlSql.Core/Ipc/RpcMessage.cs`, the DTOs exist in `src/AkmlSql.Core/Ipc/Messages/AiProviderTest{Request,Response}.cs`, and `src/AkmlSql.Engine/Ai/AiProviderTestHandler.cs` is registered in `EngineHandlerRegistry.cs:78`. It builds temporary settings, sends a one-line prompt, and returns success/error plus latency. **Nothing in the shell ever sends message 77.** FR-009 is therefore a UI wiring task — add the button and call the existing contract — not a new IPC pair.
- One caveat on that handler: it passes the request's key through `AiProviderFactory.Create`, which runs it through the `KeyDecryptor` hook (DPAPI in the engine). `CredentialManager.Decrypt` passes plaintext through unchanged when the value lacks the `dpapi:` prefix, so an unencrypted key still works — but see the note on FR-008 below.
- **FR-008 finding**: AI API keys are stored in **plaintext** today. `AiAssistancePage.Save` writes `settings.Ai.ApiKey = _apiKey.Text` straight into `config.json`, and nothing encrypts it. SQL credentials, by contrast, are DPAPI-wrapped (`SqlCredentialStore`). The engine already has the matching wrap/unwrap in `src/AkmlSql.Engine/Ai/Security/CredentialManager.cs` (entropy `"AkmlSql-ApiKey-v1"`), but it lives in the engine (net10) and the Options page is net472. `System.Security.Cryptography.ProtectedData` is already referenced by `AkmlSql.Core` on **both** target frameworks, so the shared home for this is `AkmlSql.Core` — promote the protector there and have the engine's `CredentialManager` delegate to it rather than introducing a second mechanism. Note the entropy differs from `SqlCredentialStore`'s; the AI entropy string must be preserved exactly or every already-stored key becomes unreadable.

### C. Chat copy actions (Story 3)

- `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`. `CreateMessageBubble` already renders a per-message copy button (`OnCopyMessageClick`), and per-SQL-block buttons are created in `AddAssistantMessage` from the response's code actions. Both must be preserved.
- The gap is selection: bubbles render a `TextBlock`, which is not selectable, so partial copy is impossible (FR-017). The conversation-level copy (FR-018) also does not exist.
- SQL blocks are extracted server-side by `AiPipelineServices.ExtractCodeActions`; multiple blocks already produce multiple actions, so FR-015's "one action per block" mostly needs labelling that identifies which block (Story 3 scenario 2).

### D. Schema access (Story 1) — the root cause

- **`src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`, in `SendMessageAsync`: `SessionId = Guid.NewGuid().ToString("N")`.** A brand-new random identifier is generated for every message. The engine looks that identifier up in its session table, finds nothing, and the schema builder returns an empty context. The system prompt the model receives literally contains "(No schema objects available)". This single line is why the assistant has no access to the database.
- The same pattern appears in `src/AkmlSql.Shell.Shared/Ai/GhostTextAdornment.cs` (~line 134) and in `TextToSqlCommand.ExtractPromptFromEditor` (~line 236), so those features are equally blind. FR-031 covers them.
- The correct identifier already exists: the editor buffer carries an `AkmlSqlSessionId` property, set in `src/AkmlSql.Shell.Shared/Editor/TextViewCreationListener.cs`. Two resolvers already read it — `src/AkmlSql.Shell.Shared/Refactoring/RefactorCommandHelper.cs` (~line 41) and `RefreshCacheCommand.TryGetActiveSessionId` (~line 225). Reuse one of them rather than writing a third; a tool window has no buffer of its own, so it must resolve the active editor's identifier at send time and re-resolve when the active window changes (FR-027).
- Engine side: `src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs` builds the context via `Services.SchemaContext.BuildAsync(request.SessionId, sessionLookup, request.Message, compressionLevel: 2)`. The lookup returns `(null, null)` unless `ctx.Sessions.GetSession(sid)` finds a session with `IsConnected` true.
- `src/AkmlSql.AI/Context/SchemaContextBuilder.cs`, `FilterByRelevance`: the prompt is tokenised and objects are kept if a token is a substring of their name or a column name. Objects are only *all* included when the match count is exactly zero. Short noise tokens ("my", "do", "in") match objects incidentally, so a general question like "what tables do I have" can produce a tiny arbitrary subset instead of the full inventory — FR-024 and FR-025 exist to close this. The `maxObjects` cap defaults to 500 and `compressionLevel: 2` omits the primary keys, indexes and relationships that FR-023 requires.
- Detail levels are documented in the class summary of `SchemaContextBuilder` and rendered by `src/AkmlSql.AI/Context/SchemaContextFormatter.cs` (levels 1–4). Consider level 1 for the full inventory plus level 3 detail for objects the question names — that satisfies FR-023 to FR-026 within a budget.
- Schema availability depends on the cache: Phase A loads names, Phase B loads columns and foreign keys in the background (`SchemaCacheManager`). FR-029's "still loading" state should read the same progress signal the editor margin uses (`src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`).
- Privacy: `src/AkmlSql.AI/Privacy/PrivacyTransformer.cs` and the `privacyMode` setting (default `schemaOnly`). The anonymous mode hashes identifiers, which makes real object names impossible by design — FR-030 is about explaining that, not removing it.
- The chat header today shows only the provider name (`DetectDatabaseContext`); `SetDatabaseContext` exists but nothing calls it. FR-027 needs it wired to the active connection.

### E. Installer and updates (Story 5)

- **`src/AkmlSql.Core/Constants.cs`: `UpdateManifestUrl = "https://updates.akmlsql.com/manifest.json"`.** Nothing in the repository publishes to that host and no release process produces that file. Every update check in every shipped build fails. `tests/AkmlSql.Core.Tests/ConstantsTests.cs` asserts this exact string, so it will need updating with the value.
- What does exist: the product site at `https://akml.khamis.work` (`Site:BaseUrl` in `src/AkmlSql.Site/appsettings.json`) serves `src/AkmlSql.Site/wwwroot/releases.json`, which already carries version, release date, local download path, SHA-256, release-notes URL, supported hosts, and — since the most recent commit — a `cdnUrl` pointing at the GitHub release asset. `scripts/deploy-site-iis.ps1` writes that file and uploads the installer to GitHub Releases via the `gh` CLI. This is the "server and CDN at GitHub" the user is referring to; FR-033 to FR-036 should be met by pointing the update check at this existing source rather than inventing a second one.
- Note the shape difference: the updater deserialises `src/AkmlSql.Core/Update/UpdateManifest.cs` (a single latest release), while `releases.json` is a list. Either the check reads the list and picks the newest, or the release process emits both from one source — FR-036 requires that they cannot drift, so generating both from the deploy script is the safer option.
- `src/AkmlSql.Updater/Program.cs` only *checks*: it fetches the manifest, compares versions, and writes `%AppData%\AKML SQL\cache\update-available.json`. It never downloads and never installs. `src/AkmlSql.Shell.Shared/Commands/CheckUpdateCommand.cs` reads that file and opens the download URL in the user's browser. FR-039/FR-039a/FR-040 (download with progress and cancel, verify checksum, confirm, launch) are new work — `UpdateResult.Sha256Hash` is already carried through and is currently unused.
- FR-039 is deliberately *not* a silent install: launch the installer with its normal interface (the `/VERYSILENT` path stays reserved for the documented unattended-deployment scenario in `doc/deployment.md`). The confirmation dialog must name the applications that will close, and must follow the project's FR-005 safety-dialog conventions — Cancel is `IsCancel` and holds initial focus, and the proceed button is never the default button. See `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` for the canonical pattern.
- The download belongs in the updater process, not in the shell: the shell runs inside SSMS/Visual Studio and must not be blocked by a ~72 MB transfer. Cache the file under `%AppData%\AKML SQL\cache\` and delete any partial file on cancel or checksum failure (FR-039a, FR-040).
- `UpdateLauncher.LaunchIfDue` enforces the 24-hour throttle from `Constants.UpdateCheckIntervalHours`. FR-042 requires the manual command to bypass it.
- Installer: `src/AkmlSql.Installer/AkmlSqlSetup.iss` keeps a fixed `AppId`, so in-place upgrades are already supported, and `CloseApplications=yes` with `CloseApplicationsFilter=Ssms.exe,devenv.exe` already handles running hosts. FR-043's preservation guarantee needs verifying, not building — note the post-install step writes `config.json` only when absent.
- FR-046 is a manual verification requirement. Record the evidence in `doc/progress.md` following the format used by earlier specs, and reference it from this feature's task list.
