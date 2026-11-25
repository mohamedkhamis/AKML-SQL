# Contract: workspace features and AI prompts (SN-001, WEB-011–014, UX-002, SSMS-003, AI-001, AI-002)

## 1. Custom snippets in completion (SN-001)

- **Wiring.** `CompletionService(IEngineBridge, ISchemaCacheStore?, IDialectGate?, ISqlConnectionService?, ISnippetStore?)`
  takes the snippet store as a new optional last argument, and DI passes it.
- **When user snippets are added** (mapping in data-model §10):
  - **Online:** to the engine's response when that response contains at least one item with
    `ObjectType == Snippet`.
  - **Offline:** to `BuildFromCacheAsync`'s list when the text before the caret (from `liveDocumentText`) is a
    statement start, that is, at the document start or after `;`, `GO` or a blank line, ignoring comments.
- **Which snippets.** Dialect = `IDialectGate.IsPostgres ? PostgreSql : SqlServer`. Only non-built-in snippets of
  that kind are added. A user snippet whose shortcode equals a built-in's replaces that item.
- **Cache.** The store list is cached in the service. `ISnippetStore` raises `Changed` (new event) on Save, Delete
  and Import, which drops the cache.
- **As built (2026-11-21, spec amendment A6).** The engine filters its list by the word typed, so for `qaz` its reply
  holds no snippet item. The statement-start rule therefore applies online too: user snippets are added when the reply
  has a snippet item **or** the caret is at a statement start. `CompletionService.AtStatementStart` also takes `GO n`. A
  comment line between the statement and the caret is not a blank line.
- **Browser test** (`SnippetsInEditorTests`):
  1. Create `qaz` with body `SELECT $CURSOR$ FROM dbo.Region;` on /snippets.
  2. Open the editor, type `qaz`, press Tab. The editor holds `SELECT  FROM dbo.Region;` with the caret after
     `SELECT `.
  3. Delete `qaz`. Typing `qaz` and Tab inserts nothing.

## 2. Explorer (WEB-013, WEB-014)

- **WEB-013, incomplete metadata.** When `payload.MetadataLimited` is true, the Stored Procedures and Functions
  headers read "Stored Procedures (may be incomplete)" / "Functions (may be incomplete)", with no count. The first
  child is a note (`schema-metadata-limited`): "The engine signs in as {RunsAs}, which cannot see every object in
  {database}. Grant it VIEW DEFINITION on the database, or connect with a login that can." `RunsAs` comes from the
  handshake; if it is unknown, the note says "the engine's account". When the flag is false, counts are as today.
- **WEB-014, empty filter.** When a filter is set and every group is empty, the tree shows one row
  (`schema-filter-empty`), "No objects match '{filter}'.", with a **Clear filter** button
  (`schema-filter-clear`). Clearing restores the expansion state from before.

## 3. Refactor menu (WEB-011, WEB-012)

- **Items as buttons.** Each item is a `<button>` inside an `<li>`. The disabled ones are `disabled` with
  `aria-describedby` pointing at their reason text, so the reason is readable without hovering.
- **The three Expand items:**

  | Schema state for the tab | Item | Reason shown |
  | --- | --- | --- |
  | Phase B snapshot (columns) present | enabled | — |
  | Phase A only | disabled | "Still loading the column list." |
  | No snapshot / not connected | disabled | "Needs the database schema. Connect to a database first." |
  | PostgreSQL tab | as today (unavailable) | today's text |

  When enabled, they run with `SchemaCache = SchemaSnapshotCache.ToDatabaseCache(phaseB)`. The operation's own
  warnings are listed in the preview, for example "Could not resolve columns for dbo.Regio".
- **Heavy items.** With the engine connected but without `refactoring.heavy`, the reason is "Not available in the
  browser yet."; with no engine, "Needs a paired engine.".
- **As built (2026-11-21, spec amendment A7).**
  - The menu is `Shared/RefactorMenu.razor`, built from `RefactorMenuItem` records. Arrow keys, Home and End move between
    the entries that can run. Choosing one gives focus back first, so the preview remembers the Refactor button.
  - The schema comes from `Services/RefactorSchema.cs` (`From`, `LoadAsync`, `ExpandReason`, `HeavyReason`,
    `NeedsSchema`), using the existing `SchemaPhaseRehydrator` instead of a new `SchemaSnapshotCache`.
  - `LoadAsync` takes only the connected database's snapshot, never the last-used one that offline completion falls
    back to.
  - The preview now lists the operation's warnings when it changes nothing, too ("Could not resolve columns for:
    dbo.Regio"). Its Cancel button is `refactor-cancel`; focus goes to Apply, or to Cancel when there is nothing to apply.
- **Keyboard and focus.** The menu and the preview panel follow the shared dialog pattern (`akml-ui.js`):
  - `stashFocus` when opened, focus on the first enabled item / the preview's Apply, and `trapTab` while open;
  - **Escape** closes the menu or preview, and `restoreFocus` returns focus to the Refactor toolbar button;
  - the scrim click keeps closing them.

## 4. Imports (UX-002)

| Page | Input | Message |
| --- | --- | --- |
| /styles | not JSON, or JSON that is not a SQL Prompt style | "That file is not a SQL Prompt style (.json)." The exception detail goes to Diagnostics at Info, never to the page. |
| /styles | file over 1 MB | today's text |
| /snippets | valid snippet | "Imported '{shortcode}'." |
| /snippets | JSON without a snippet shape, or a non-JSON file | "No snippet was found in that file. Existing snippets are unchanged." |

Every import outcome calls `StateHasChanged`, and the message is in an `aria-live="polite"` status element.

**As built (2026-11-21).**

- "Not a SQL Prompt style" also covers JSON that parses but has neither SQL Prompt's `metadata` object nor any known
  option (`SqlPromptStyleDocument.LooksLikeStyle`), such as a snippet export or a JSON array. Before, such a file was
  imported as a style.
- A file that cannot be read says "That file could not be read.".
- The Snippets status element is always in the page (empty, it takes no room), so the first message is announced. The
  Styles status keeps its conditional element and gains an explicit `aria-live`.

## 5. Find Invalid Objects (SSMS-003)

- **Engine query.** It returns the candidates and the count in one batch:

  ```sql
  SELECT COUNT(*) FROM sys.sql_expression_dependencies d
    JOIN sys.objects o ON o.object_id = d.referencing_id
   WHERE o.is_ms_shipped = 0;
  SELECT … (as today, WHERE d.referenced_id IS NULL AND o.is_ms_shipped = 0);
  ```

  `TotalScanned` = the first result.
- **Dialog.** It uses the texts in data-model §9 and hides the grid when there are no records. `WindowTitles.For` is
  kept.
- **Engine test.** With fake rows, the summary uses the total and not the candidate count.

## 6. AI prompts (AI-001, AI-002)

- **`PromptTemplates.BuildSystemPrompt(schemaText, taskInstruction, dialect, serverVersion)`.** When
  `string.IsNullOrWhiteSpace(schemaText)`:
  - it omits "The user's database has the following schema:", the schema block and "Use only tables and columns
    from the provided schema";
  - it adds "No database schema is shared (privacy setting). Use clear placeholder table and column names and say
    that you did.";
  - the PostgreSQL variant does the same.
- **Task instructions.** `IndexAnalysisPrompt` and `OptimizePrompt` choose a no-schema variant of their
  instruction: they drop "(provided in the schema context)" and "Do not suggest indexes that already exist in the
  schema context", and add "Existing indexes are unknown; say which ones to check first."
- **Test** (`PromptPrivacyModeTests`). For each of Explain, Fix, Optimize, Text to SQL, Index Analysis, Ghost text and
  Chat × each privacy mode, the built system prompt never contains "provided schema" or "following schema:" when the
  schema text is empty. With a schema it contains both, as today.
- **AI-002.** In the web `AiPanel`, the Fix button is disabled while `LastErrorProvider` returns null for the tab
  (`ai-fix` `disabled`, with `title` "Run the query first: Fix needs the error the server returned."). It enables
  when the tab's last run returned an error. `FixPrompt` is never built with the "(no error)" placeholder from the
  web. A bUnit test asserts the button state and that no Fix request is sent without an error.
