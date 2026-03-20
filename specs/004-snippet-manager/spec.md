# Feature Specification: Snippet Manager

**Feature Branch**: `004-snippet-manager`
**Created**: 2026-03-20
**Status**: Draft
**Input**: Phase 4 PRD — Schema-aware code snippet system with 75+ built-in snippets, custom snippet creation, tab-stop navigation, surround-with, multi-source library, IntelliSense integration, format-on-expand, and SQL Prompt import.
**Depends on**: Phase 2 (IntelliSense engine, schema cache), Phase 3 (formatter integration for format-on-expand)

## Out of Scope

- AI-generated snippets (deferred to Phase 9)
- Snippet marketplace or paid community snippets
- Cross-language snippets (C#, PowerShell) — T-SQL only
- AKML Hub community repository (future enhancement; multi-folder architecture supports it when ready)
- Real-time collaborative snippet editing

## User Scenarios & Testing

### User Story 1 — Snippet Expansion via Shortcode (Priority: P1)

A developer types a short trigger code (e.g., `ssf`) in the SQL editor, presses Tab, and the shortcode is instantly replaced with a full SQL template (e.g., `SELECT * FROM $TABLE$`). The cursor moves to the first placeholder, which they can fill in. Pressing Tab advances to the next placeholder. The expanded snippet is formatted according to the active formatting profile.

**Why this priority**: This is the core value proposition — type a shortcode, get a code template. Without expansion, there is no snippet system.

**Independent Test**: Type a shortcode in the editor, press Tab, verify the template expands with placeholders navigable via Tab/Shift+Tab.

**Acceptance Scenarios**:

1. **Given** the user types `ssf` in the editor, **When** they press Tab, **Then** the shortcode is replaced with `SELECT * FROM $TABLE$` and the cursor is positioned at the `$TABLE$` placeholder.
2. **Given** a snippet with multiple placeholders is expanded, **When** the user presses Tab, **Then** the cursor moves to the next placeholder. Pressing Shift+Tab moves to the previous placeholder.
3. **Given** a snippet has linked placeholders (same variable name used multiple times), **When** the user edits one instance, **Then** all linked instances update simultaneously.
4. **Given** a snippet contains `$CURSOR$`, **When** all placeholders are filled and the user presses Tab past the last one, **Then** the cursor lands at the `$CURSOR$` position.
5. **Given** format-on-expand is enabled, **When** a snippet expands, **Then** the expanded text is formatted according to the active formatting profile.
6. **Given** the user types a shortcode that does not match any snippet, **When** they press Tab, **Then** normal Tab behavior occurs (indentation).

---

### User Story 2 — Built-in Snippet Library (Priority: P1)

A developer installs AKML SQL and immediately has access to 75+ built-in snippets covering common SQL patterns — DML (SELECT, INSERT, UPDATE, DELETE), DDL (CREATE TABLE, CREATE PROCEDURE), DBA queries (index usage, wait stats, blocking), error handling (TRY/CATCH), and surround-with templates. No configuration needed.

**Why this priority**: Built-in snippets provide immediate value out of the box. Without them, the snippet system requires users to create all their own snippets before getting any benefit.

**Independent Test**: Open IntelliSense, verify 75+ snippets are available across 5 categories, each expanding correctly.

**Acceptance Scenarios**:

1. **Given** AKML SQL is freshly installed, **When** the user opens the snippet manager or types in the editor, **Then** at least 75 built-in snippets are available across 5 categories: DML, DDL, DBA/Metadata, Error Handling/Control Flow, and Surround-With.
2. **Given** a built-in snippet, **When** the user expands it, **Then** the expansion produces valid, well-formed SQL with sensible placeholder defaults.
3. **Given** built-in snippets, **When** the user attempts to edit one directly, **Then** the system prevents modification and offers to create a personal copy.

---

### User Story 3 — Snippets in IntelliSense Popup (Priority: P1)

When a developer is typing, snippets appear alongside keywords, tables, and columns in the IntelliSense completion popup. Snippets are visually distinguished with a snippet icon and ranked by relevance to the current cursor context and usage frequency. Selecting a snippet from the popup expands it immediately.

**Why this priority**: Integration with IntelliSense makes snippets discoverable without memorizing shortcodes. The boundary between typing code and using snippets becomes invisible.

**Independent Test**: Type a partial shortcode in the editor, verify matching snippets appear in the completion popup with a distinct icon.

**Acceptance Scenarios**:

1. **Given** the user starts typing in the editor, **When** text matches a snippet shortcode, **Then** the matching snippet(s) appear in the IntelliSense popup with a distinct snippet icon.
2. **Given** snippets and other completions (keywords, objects) both match, **When** the popup displays, **Then** snippets are ranked by usage frequency within their relevance group.
3. **Given** a context-sensitive cursor position (e.g., after FROM), **When** the popup shows, **Then** only snippets relevant to that context appear (e.g., JOIN snippets after FROM, not DDL snippets).
4. **Given** the user selects a snippet from the popup, **When** they press Tab or Enter, **Then** the snippet expands with placeholder navigation.

---

### User Story 4 — Custom Snippet Creation (Priority: P2)

A developer wants to create their own snippet for a pattern they use frequently. They open the Snippet Manager, define a shortcode, name, description, category, and body with placeholders. They can set placeholders as schema-aware (e.g., `$TABLE$` shows table list from schema cache). A live preview shows the expanded snippet as they edit.

**Why this priority**: Custom snippets let users codify their team's patterns. This builds on the built-in library foundation.

**Independent Test**: Create a custom snippet, save it, verify it appears in IntelliSense and expands correctly.

**Acceptance Scenarios**:

1. **Given** the user opens the Snippet Manager, **When** they click "New," **Then** an editor opens with fields for shortcode, name, description, category, tags, variables, and code body.
2. **Given** the user defines a variable with schema-aware type (e.g., `tables`), **When** the snippet expands and the user reaches that placeholder, **Then** IntelliSense suggestions from the schema cache are shown.
3. **Given** the user saves a custom snippet, **When** they type the shortcode in the editor, **Then** the snippet appears in IntelliSense and expands correctly.
4. **Given** the user creates a snippet with the same shortcode as a built-in snippet, **When** they type the shortcode, **Then** their personal snippet takes priority over the built-in one.
5. **Given** the snippet editor, **When** the user edits the body, **Then** a live preview shows the expanded result with default variable values and the active formatting profile applied.

---

### User Story 5 — Surround-With Snippets (Priority: P2)

A developer selects a block of SQL code, invokes the surround-with command (Ctrl+K, Ctrl+S), and chooses from a list of wrapping templates — TRY/CATCH, transaction, BEGIN/END, timing measurement, noformat tags, etc. The selected code is wrapped with the chosen template, and the cursor is positioned at the first placeholder.

**Why this priority**: Surround-with is a productivity multiplier for common patterns like error handling and transactions. It requires the expansion engine from US1.

**Independent Test**: Select SQL, trigger surround-with, choose a template, verify the selection is wrapped correctly.

**Acceptance Scenarios**:

1. **Given** the user selects a block of SQL, **When** they press Ctrl+K, Ctrl+S, **Then** a list of surround-with snippets is displayed.
2. **Given** the user chooses "Surround TRY/CATCH," **When** applied, **Then** the selected code is wrapped in a TRY/CATCH block with error handling, and `$SELECTEDTEXT$` is replaced with the original selection.
3. **Given** no text is selected, **When** the user invokes surround-with, **Then** the command is disabled or shows surround-with snippets that work without a selection.
4. **Given** at least 10 built-in surround-with snippets are available, **When** the user browses them, **Then** they include: TRY/CATCH, transaction, IF EXISTS, BEGIN/END, timing, SET NOCOUNT, comment block, region, noformat, and temp table.

---

### User Story 6 — Schema-Aware Placeholders (Priority: P2)

When a snippet expands and the user navigates to a placeholder marked as schema-aware, the IntelliSense popup appears with relevant suggestions from the database schema cache — tables, views, columns, schemas, data types, procedures, or functions depending on the placeholder type. This makes snippet expansion as intelligent as regular IntelliSense.

**Why this priority**: Schema-aware placeholders are the key differentiator from competing products. No other snippet tool offers IntelliSense inside snippet placeholders.

**Independent Test**: Expand a snippet with a schema-aware placeholder, verify IntelliSense shows relevant schema objects.

**Acceptance Scenarios**:

1. **Given** a snippet variable is marked as schema-aware type `tables`, **When** the user navigates to that placeholder during expansion, **Then** IntelliSense shows a list of tables from the schema cache.
2. **Given** a schema-aware placeholder of type `columns`, **When** a table context is known (e.g., from a preceding `tables` placeholder), **Then** IntelliSense shows columns from that specific table.
3. **Given** no database connection is active, **When** the user reaches a schema-aware placeholder, **Then** the placeholder behaves as a regular text placeholder with no error.
4. **Given** schema-aware types include: schemas, tables, views, columns, procedures, functions, datatypes, databases, and indexes, **When** each type is used, **Then** the appropriate schema objects are suggested.

---

### User Story 7 — Snippet Manager UI (Priority: P2)

A developer opens the Snippet Manager dialog to browse, search, create, edit, duplicate, and delete snippets. The manager shows a tree view organized by source (Personal, Team, Built-in) and category, with a search bar, snippet editor panel, and live preview.

**Why this priority**: The manager UI makes the snippet library browsable and manageable. Without it, users can only interact with snippets through shortcodes.

**Independent Test**: Open Snippet Manager, browse by category, search for a snippet, edit it, verify changes persist.

**Acceptance Scenarios**:

1. **Given** the user opens the Snippet Manager, **When** it loads, **Then** a split-pane view shows a tree of snippets on the left (organized by source and category) and an editor/preview on the right.
2. **Given** the user types in the search bar, **When** results filter, **Then** matching snippets are found by name, shortcode, description, tags, or body content.
3. **Given** the user selects a snippet, **When** the editor loads, **Then** all fields are editable (for personal/team snippets) with a live preview below.
4. **Given** the user right-clicks a built-in snippet, **When** they choose "Duplicate," **Then** a personal copy is created that they can customize.
5. **Given** the user creates a snippet with a shortcode that already exists, **When** they save, **Then** a warning shows the conflict and which source has priority.

---

### User Story 8 — Built-in Variables (Priority: P2)

A developer uses built-in variables in their snippets — `$DATE$`, `$USER$`, `$DATABASE$`, `$SERVER$`, `$SCHEMA$`, `$GUID$`, `$FILENAME$`, `$CLIPBOARD$` — that are automatically resolved to their current values when the snippet expands. These variables require no configuration and work alongside custom placeholders.

**Why this priority**: Built-in variables make snippets dynamic and context-aware (file headers with date/user, database-specific templates). They complement custom placeholders.

**Independent Test**: Create a snippet using `$DATE$` and `$USER$`, expand it, verify the current date and username appear.

**Acceptance Scenarios**:

1. **Given** a snippet body contains `$DATE$`, **When** the snippet expands, **Then** `$DATE$` is replaced with the current date in ISO format (YYYY-MM-DD).
2. **Given** a snippet body contains `$DATABASE$`, **When** a connection is active, **Then** `$DATABASE$` is replaced with the current database name.
3. **Given** a snippet body contains `$DATABASE$` but no connection is active, **When** the snippet expands, **Then** `$DATABASE$` is replaced with an empty string or a sensible default.
4. **Given** at least 14 built-in variables are supported: `$CURSOR$`, `$SELECTEDTEXT$`, `$CLIPBOARD$`, `$DATE$`, `$DATETIME$`, `$TIME$`, `$USER$`, `$MACHINE$`, `$DATABASE$`, `$SERVER$`, `$SCHEMA$`, `$GUID$`, `$YEAR$`, `$FILENAME$`.

---

### User Story 9 — Multi-Source Snippet Library (Priority: P3)

A team lead configures a shared network folder (or Git repository path) as the team snippet source. Team snippets are available to all team members alongside personal and built-in snippets. When multiple sources have the same shortcode, personal takes priority over team, which takes priority over built-in.

**Why this priority**: Team sharing enables organizational standards. It requires the core snippet system to be working first.

**Independent Test**: Configure a team folder, add snippets to it, verify they appear in the snippet library with correct priority.

**Acceptance Scenarios**:

1. **Given** the user configures a team snippet folder path, **When** the path contains `.akmlsnippet` files, **Then** those snippets appear in the snippet library under the "Team" source.
2. **Given** personal and built-in snippets share a shortcode, **When** the user types the shortcode, **Then** the personal snippet takes priority.
3. **Given** a team snippet folder is a network share, **When** the share is unavailable, **Then** the system gracefully degrades — personal and built-in snippets remain available, team snippets show as unavailable.
4. **Given** a snippet file is added/modified/deleted in any source folder, **When** the file watcher detects the change, **Then** the snippet index is updated within 100ms without requiring a restart.

---

### User Story 10 — Import from SQL Prompt and SSMS (Priority: P3)

A team migrating from SQL Prompt or using SSMS native snippets wants to bring their existing snippets to AKML SQL. They use the import feature to convert `.sqlpromptsnippet` (XML) or `.snippet` (VS CodeSnippet XML) files into native `.akmlsnippet` format with variable mapping.

**Why this priority**: Import reduces migration friction. It depends on the snippet file format and manager UI being complete.

**Independent Test**: Import a SQL Prompt snippet file, verify the resulting snippet expands correctly with mapped variables.

**Acceptance Scenarios**:

1. **Given** a SQL Prompt `.sqlpromptsnippet` file, **When** the user imports it, **Then** a native `.akmlsnippet` file is created with variables mapped (e.g., `$DBNAME$` to `$DATABASE$`).
2. **Given** an SSMS native `.snippet` file, **When** the user imports it, **Then** VS CodeSnippet-style placeholders are mapped to AKML format.
3. **Given** the SQL Prompt snippet folder is detected automatically, **When** the user opens the import dialog, **Then** one-click migration of all found snippets is offered.
4. **Given** a bulk import of a directory, **When** the import completes, **Then** a summary shows total imported, successfully converted, and any that failed with reasons.

---

### User Story 11 — Snippet Usage Statistics (Priority: P3)

A developer wants to see which snippets they use most frequently, and the system uses this data to rank snippets higher in IntelliSense suggestions. Usage counts are tracked per snippet and displayed as badges in the Snippet Manager.

**Why this priority**: Usage statistics improve suggestion relevance over time. This is a refinement feature that enhances the core experience.

**Independent Test**: Use several snippets, open Snippet Manager, verify usage counts are displayed and IntelliSense ranking reflects frequency.

**Acceptance Scenarios**:

1. **Given** the user expands a snippet, **When** they check the Snippet Manager, **Then** the usage count for that snippet is incremented.
2. **Given** two snippets match the user's input, **When** IntelliSense displays them, **Then** the more frequently used snippet is ranked higher.
3. **Given** usage tracking is disabled in settings, **When** the user expands snippets, **Then** no usage data is recorded and ranking falls back to alphabetical.

---

### User Story 12 — Create Snippet from Selection (Priority: P3)

A developer has written a useful SQL pattern and wants to save it as a snippet. They select the code, right-click, and choose "Create Snippet from Selection." The Snippet Manager opens with the selected code pre-filled in the body, and the user can add placeholders and metadata.

**Why this priority**: Creating snippets from existing code is a natural workflow enhancement. It requires the manager UI to be complete.

**Independent Test**: Select SQL code, right-click, create snippet, verify the code is pre-filled and the resulting snippet works.

**Acceptance Scenarios**:

1. **Given** the user selects SQL code in the editor, **When** they right-click and choose "Create Snippet from Selection," **Then** the Snippet Manager opens with the body pre-filled with the selected code.
2. **Given** the pre-filled snippet, **When** the user highlights portions of the body and marks them as variables, **Then** the variable definitions are created automatically.
3. **Given** the snippet is saved, **When** the user types its shortcode later, **Then** it expands correctly.

---

### Edge Cases

- What happens when a snippet shortcode conflicts with a SQL keyword? Snippets are ranked lower than keywords in IntelliSense; the user must explicitly select the snippet.
- What happens when a snippet file is corrupted or has invalid JSON? The file is skipped during loading, a warning is logged, and all other snippets remain available.
- What happens when a team snippet folder path changes while the IDE is running? The file watcher detects the configuration change and re-indexes the new path.
- What happens when a snippet body contains syntax that the T-SQL parser cannot parse? The snippet expands as plain text; format-on-expand is skipped with a silent fallback.
- What happens when a schema-aware placeholder type does not match any database objects? The placeholder shows an empty IntelliSense list and behaves as a regular text input.
- What happens when the user cancels snippet expansion mid-way (presses Escape)? The expansion is reverted — the original shortcode text is restored.
- What happens when a snippet references a built-in variable that cannot be resolved (e.g., `$DATABASE$` with no connection)? The variable is replaced with an empty string.
- What happens when hundreds of snippets from multiple sources are loaded? The system indexes all snippets and searches across 500+ snippets within 50ms.

## Requirements

### Functional Requirements

**Core Expansion**
- **FR-001**: System MUST expand snippet shortcodes when the user presses the configured trigger key (default: Tab).
- **FR-002**: System MUST support tab-stop navigation through placeholders using Tab (forward) and Shift+Tab (backward).
- **FR-003**: System MUST support linked placeholders — editing one instance of a variable updates all other instances in real-time.
- **FR-004**: System MUST position the cursor at `$CURSOR$` after all placeholders are filled or the user exits placeholder mode.
- **FR-005**: System MUST support reverting snippet expansion when the user presses Escape during placeholder navigation.
- **FR-006**: System MUST apply the active formatting profile to expanded snippets when format-on-expand is enabled.
- **FR-007**: Normal Tab behavior (indentation) MUST be preserved when the typed text does not match any snippet shortcode.

**Built-in Library**
- **FR-008**: System MUST ship with at least 75 built-in snippets across 5 categories: DML (20), DDL (15), DBA/Metadata (20), Error Handling/Control Flow (10), and Surround-With (10).
- **FR-009**: Built-in snippets MUST be read-only; users MUST be able to create a personal copy (duplicate) to customize.
- **FR-010**: Each built-in snippet MUST produce valid, well-formed SQL with sensible placeholder defaults.

**IntelliSense Integration**
- **FR-011**: Snippets MUST appear in the Phase 2 IntelliSense completion popup with a distinct visual icon.
- **FR-012**: Snippets MUST be ranked by usage frequency within their relevance group in the completion popup.
- **FR-013**: Snippets MUST be filtered by cursor context — only snippets relevant to the current clause/position are shown.
- **FR-014**: Selecting a snippet from the completion popup MUST trigger expansion with placeholder navigation.

**Custom Snippets**
- **FR-015**: Users MUST be able to create custom snippets with shortcode, name, description, category, tags, variables, and body.
- **FR-016**: Custom snippet variables MUST support a default value, tooltip, and optional schema-aware type.
- **FR-017**: Custom snippets MUST be stored as portable files in the user's personal snippet folder.
- **FR-018**: System MUST validate snippet shortcodes for uniqueness within a source and warn on cross-source conflicts.

**Surround-With**
- **FR-019**: System MUST support surround-with snippets that wrap selected code using the `$SELECTEDTEXT$` variable.
- **FR-020**: Surround-with MUST be accessible via a dedicated keyboard shortcut (default: Ctrl+K, Ctrl+S).
- **FR-021**: At least 10 built-in surround-with snippets MUST be provided: TRY/CATCH, transaction, IF EXISTS, BEGIN/END, timing, SET NOCOUNT, comment block, region, noformat, and temp table.

**Schema-Aware Placeholders**
- **FR-022**: System MUST support schema-aware placeholder types: schemas, tables, views, columns, procedures, functions, datatypes, databases, and indexes.
- **FR-023**: When a user navigates to a schema-aware placeholder, IntelliSense MUST show relevant objects from the Phase 2 schema cache.
- **FR-024**: When no database connection is active, schema-aware placeholders MUST fall back to regular text input without error.

**Built-in Variables**
- **FR-025**: System MUST support at least 14 built-in variables: `$CURSOR$`, `$SELECTEDTEXT$`, `$CLIPBOARD$`, `$DATE$`, `$DATETIME$`, `$TIME$`, `$USER$`, `$MACHINE$`, `$DATABASE$`, `$SERVER$`, `$SCHEMA$`, `$GUID$`, `$YEAR$`, `$FILENAME$`.
- **FR-026**: Built-in variables MUST be resolved to their current values at the moment of snippet expansion.
- **FR-027**: Connection-dependent variables (`$DATABASE$`, `$SERVER$`, `$SCHEMA$`) MUST resolve to an empty string when no connection is active.

**Multi-Source Library**
- **FR-028**: System MUST support three snippet sources: Personal (highest priority), Team (configurable shared path), and Built-in (lowest priority).
- **FR-029**: When multiple sources have snippets with the same shortcode, the highest-priority source MUST win.
- **FR-030**: System MUST watch snippet source folders for changes and hot-reload the snippet index within 100ms.
- **FR-031**: Team source unavailability MUST NOT affect personal or built-in snippet availability.

**Snippet Manager UI**
- **FR-032**: System MUST provide a Snippet Manager dialog with a tree view (organized by source and category), snippet editor, and live preview.
- **FR-033**: The Snippet Manager MUST support full-text search across snippet name, shortcode, description, tags, and body.
- **FR-034**: The Snippet Manager MUST support creating, editing, duplicating, and deleting snippets.
- **FR-035**: The Snippet Manager MUST show usage count badges next to each snippet.
- **FR-036**: System MUST support "Create Snippet from Selection" via the editor right-click context menu.

**Import/Export**
- **FR-037**: System MUST support importing SQL Prompt `.sqlpromptsnippet` (XML) files with variable mapping.
- **FR-038**: System MUST support importing SSMS native `.snippet` (VS CodeSnippet XML) files.
- **FR-039**: System MUST support exporting snippets as `.akmlsnippet` JSON files.
- **FR-040**: System MUST support bulk import from a directory of mixed-format snippet files.
- **FR-041**: System MUST auto-detect the SQL Prompt snippet folder and offer one-click migration.

**Usage Statistics**
- **FR-042**: System MUST track snippet usage frequency per snippet when usage tracking is enabled.
- **FR-043**: Usage data MUST influence snippet ranking in IntelliSense suggestions.
- **FR-044**: Usage tracking MUST be disabled by default and individually toggleable.

**Integration**
- **FR-045**: System MUST work in SSMS 20, SSMS 21, SSMS 22, VS 2019, VS 2022, and VS 2026.
- **FR-046**: The Snippet Manager and editor MUST follow the host IDE's visual theme (Light/Dark).
- **FR-047**: All keyboard shortcuts MUST be configurable.
- **FR-048**: The snippet engine MUST operate within the existing Phase 2 out-of-process engine.

### Key Entities

- **Snippet**: A reusable code template with a unique shortcode, name, description, category, tags, body (multi-line template text), and a list of variables. Can be built-in (read-only), personal, or team-shared. Stored as a portable file.
- **Snippet Variable**: A named placeholder within a snippet body. Has a name, default value, tooltip, and optional schema-aware type. Can be a built-in variable (auto-resolved) or a custom placeholder (user-fills).
- **Snippet Source**: A folder-based collection of snippets with a priority level. Three sources: Personal (priority 1), Team (priority 2), Built-in (priority 3). Each has a directory path and read/write permissions.
- **Snippet Index**: An in-memory searchable index of all loaded snippets across all sources. Supports full-text search and context-based filtering. Hot-reloaded on file changes.
- **Usage Record**: Per-snippet usage count tracking expansion frequency. Used for ranking in IntelliSense. Stored locally in the user's app data.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Users can expand a snippet from shortcode in under 20ms — indistinguishable from instant.
- **SC-002**: At least 75 built-in snippets are available across 5 categories, each producing valid SQL on expansion.
- **SC-003**: Full-text search across 500+ snippets returns results within 50ms.
- **SC-004**: Schema-aware placeholders show relevant suggestions within 100ms of tab-stop navigation.
- **SC-005**: Tab-stop navigation between placeholders completes in under 10ms per jump.
- **SC-006**: Over 95% of SQL Prompt snippets import successfully without manual intervention.
- **SC-007**: Over 70% of users use at least one snippet per session within the first month.
- **SC-008**: File watcher hot-reloads snippet changes within 100ms without requiring IDE restart.
- **SC-009**: Snippet expansion works identically across all 6 supported IDE targets.
- **SC-010**: Format-on-expand applies the active profile to expanded snippets without visible delay.
- **SC-011**: Linked placeholder updates propagate in real-time (under 10ms per keystroke).
- **SC-012**: Usage-based ranking visibly improves suggestion relevance — most-used snippets appear first.

## Assumptions

- Phase 2 (IntelliSense engine) is complete, providing the completion popup, schema cache, cursor context analysis, and named pipe communication.
- Phase 3 (SQL Formatter) is complete, providing the formatting engine for format-on-expand.
- The snippet engine operates within the existing Phase 2 out-of-process engine — no additional process is spawned.
- Built-in snippets are bundled with the installer and deployed to a read-only directory alongside the extension.
- Personal snippets are stored in `%AppData%/AKML SQL/snippets/` as individual `.akmlsnippet` JSON files.
- The team snippet source is a local or network file path configured by the user; no cloud sync in this phase.
- Snippet usage statistics are stored locally in the user's app data directory, not synchronized across machines.
- Tab is the default expansion trigger key; Enter selects from the IntelliSense popup but does not expand shortcodes typed directly.
- Snippet shortcodes are case-insensitive.
