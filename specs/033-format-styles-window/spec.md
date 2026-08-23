# Feature Specification: Format Styles Window Promotion — the dedicated SQL Prompt-grade style editor

**Feature Branch**: `033-format-styles-window`
**Created**: 2026-07-22
**Status**: Draft
**Input**: User description: "Split SQL style (all related to style) into a new window, remove it from SQL Options, and make it readable and clear like SQL Prompt."

## Context

A dedicated style window **already exists and is already reachable**: Tools → AKML SQL → **Format Styles...** opens `FormatStylesEditorWindow` (spec 020) in both SSMS 22 and VS 2026 (`cmdFormatStyles` 0x0916, wired in commit `17e294c` — the "T059 deferred" notes in CLAUDE.md, `doc/architecture.md:290`, and `T059-runbook.md` are stale). All layout/whitespace/casing rules already live in engine-side `.akmlstyle` profiles, not in Options; of the 25 Options pages only "Format › Styles" (`FormattingPage.cs`) is style-adjacent, and it holds just the active-style dropdown plus 9 formatter behavior/safety toggles.

The gap is that the window is a **browser, not an editor**:

- Selecting a style never loads its stored values — `_workingValues` are seeded once from schema defaults (`SeedWorkingValuesFromSchema`), so the tree always shows defaults + session edits regardless of selection.
- There is **no Save**: every edit is preview-only and discarded on close. No Rename or Delete either. The engine's `ProfileSave` (msg 15) and `ProfileDelete` (msg 16) handlers exist and are registered but have zero callers from this window; there is **no profile-read IPC at all** (only List 14 / Import 17 / SchemaRequest 28 / Export 29 / Duplicate 32).
- The settings tree is a **flat list of 18 reflection-generated groups** — `FormatSettingGroup.ParentId` exists but `FormatSettingSchema.BuildDefault()` never sets it — instead of SQL Prompt's readable 2-level hierarchy. Enum settings render as **free-text TextBoxes** (`AllowedEnumValues` never populated); `Min`/`Max`/`Description` are never populated either.
- Adjacent debris: a dead legacy editor (`Ui/ProfileEditorDialog.cs` + `ProfileEditorViewModel.cs`, `EditProfileCommand` 0x0220 — never initialized, no VSCT button) whose "Edit Format Profile" Command Palette entry (`CommandRegistry.cs:110`) dispatches an unregistered command; the SSMS DTE-injected fallback menu (`EnsureTopLevelMenu`, hardcoded 14-command array) omits Format Styles; Format Styles has no Command Palette entry; the style-list lock-glyph DataTemplate references an unregistered converter and silently falls back to a template without the lock glyph; the Options page has no button launching the window (spec 020 research R4 intended one).

The reference design is fully documented in-repo: `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` §8/§18 + `14_format_styles_editor.svg`. SQL Prompt's split: Options → Format → Styles holds **only** an active-style dropdown and an "Edit Formatting Styles" button; the separate larger modal holds everything else in three panels — style list ("Your Styles" / read-only "Redgate Styles", ✔ active marker, + Create with based-on picker, per-style Copy/Rename/Delete/Set-active menu), a 2-level settings tree (Global / Statements / Clauses / Expressions / Other), and type-appropriate controls above a real-time preview. AKML's `FormattingProfile` (17 sub-category POCOs) already models every SQL Prompt category.

## Clarifications

### Session 2026-07-22

- Q: What happens to the Options "Format › Styles" page? → A: **SQL Prompt-exact** — keep a slim page: active-style dropdown + "Edit formatting styles…" button + the 9 behavior/safety toggles under a "Behavior" header. All layout/casing editing happens only in the window.
- Q: Do borderline settings (IntelliSense `keywordCase`, Qualification, bracket mode, special characters, aliases) move? → A: **No — Redgate split.** They are insertion policies and stay in Options, exactly as SQL Prompt keeps them. No settings migration.
- Q: How deep does the window upgrade go? → A: **Full SQL Prompt parity** — real editing (load/save/rename/delete/create-based-on), 2-level tree, enum dropdowns, per-setting descriptions, active marker, plus cleanup of the legacy dialog and menu/palette gaps.
- Q: Implementation approach? → A: **Approach A — engine-authoritative**: enrich the reflection-generated schema (hierarchy, enum values, descriptions, ranges) and add the two missing IPC reads; upgrade the existing window in place. Shell-side hardcoding (drifts from the profile model) and resurrecting the legacy dialog were rejected.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit a style and have it stick (Priority: P1)

A developer opens Format Styles, clicks a style, sees **that style's actual values** in the tree, changes settings with live preview, and clicks **Save**. Format SQL immediately renders with the new values. Built-in styles are visibly read-only with a "Copy this style to edit" hint.

**Why this priority**: Without load-on-select and Save the window cannot fulfil "split style editing into its own window" at all — today's window silently discards every edit, which is worse than not having one.

**Independent Test**: Open the window, select the built-in "Khamis Style" — its values (not defaults) populate the tree. Copy it, change a casing or comma setting on the copy, Save, close, Format SQL a script — output reflects the edit without an engine or shell restart.

**Acceptance Scenarios**:

1. **Given** the window is open, **When** the developer selects any style, **Then** the settings tree, controls, and preview reflect that style's stored values (fetched via a new profile-read IPC), not schema defaults.
2. **Given** a custom style is selected and a value was changed, **When** the developer clicks Save, **Then** the profile is persisted via the existing `ProfileSave` IPC and a subsequent Format SQL uses the new values with no restart.
3. **Given** unsaved edits exist, **When** the developer selects a different style or closes the window, **Then** they are prompted to Save / Discard / Cancel.
4. **Given** a built-in (read-only) style is selected, **Then** all value controls are disabled, Save is disabled, and a hint offers "Copy this style to edit" (double-click on a built-in creates a copy, matching Redgate).
5. **Given** a style imported from Redgate (spec 031) is edited and saved, **Then** its `Metadata` and `ExtensionData` (unknown/round-trip keys and the preserved verbatim source) survive unchanged — Save merges the edited values into the loaded profile rather than reconstituting a fresh profile from working values.
6. **Given** the engine is disconnected, **When** any operation runs, **Then** the existing status-bar "Engine not connected" path fires and nothing is written or lost (pending edits stay in the window).

---

### User Story 2 - Readable, SQL Prompt-grade organization (Priority: P1)

The settings tree reads like SQL Prompt's: five top-level categories (Global / Statements / Clauses / Expressions / Other) with the 18 AKML groups nested beneath. Every enum setting is a dropdown listing its allowed values, every numeric setting validates its range, and every setting shows a one-line description.

**Why this priority**: This is the "make it readable and clear like SQL Prompt" half of the request; it is what turns 200+ raw reflection-named settings into something a user can navigate.

**Independent Test**: Expand the tree — exactly 5 top-level categories, every group nested under one; select an enum setting (e.g. comma placement) — a ComboBox lists all values; every visited setting shows a nonempty description; entering an out-of-range int is rejected inline.

**Acceptance Scenarios**:

1. **Given** the enriched schema (v2), **When** the tree renders, **Then** groups map: **Global** = Whitespace, Lists, Parentheses, Casing; **Statements** = DML, DDL, CTE, Control flow, Variables (Declare); **Clauses** = Joins, INSERT statements; **Expressions** = CASE, Operators, IN statements, Function calls, Expressions; **Other** = Comments, Format actions.
2. **Given** any enum-typed setting, **Then** it renders as a ComboBox populated from `AllowedEnumValues` (reflected from the CLR enum), pre-selected to the style's value — zero free-text enum boxes remain.
3. **Given** any int setting with a declared range, **Then** out-of-range input is rejected with inline feedback and is never sent to preview or Save.
4. **Given** any setting, **Then** a description sourced from the profile POCO's annotation is shown with the control; the existing SQL Prompt-key/AKML-only metadata line and spec-031 status badges keep rendering where they do today.
5. **Given** an old client cached schema v1, **When** the enriched engine responds, **Then** the version bump (SchemaVersion 2) invalidates the client cache automatically (existing `ClientSchemaVersion` mechanism).

---

### User Story 3 - Full style lifecycle in the list panel (Priority: P2)

The style list works like Redgate's: sectioned into "Your styles" and "Built-in styles", ✔ marks the active style, **New Style…** asks for a name and a based-on style, and each style offers Set Active / Copy / Rename / Delete / Export via context menu (plus the existing toolbar).

**Why this priority**: Editing (US1) is usable with the existing New/Copy alone; lifecycle completeness is what removes the last reasons to touch profile files on disk by hand.

**Independent Test**: Create "Team Standard" based on "Khamis Style"; rename it; set it active (✔ moves, status bar + Options dropdown update); attempt to delete it while active — blocked with a message; deactivate, delete — gone from disk and list.

**Acceptance Scenarios**:

1. **Given** New Style… is invoked, **Then** a name + based-on picker creates a copy of the chosen base server-side and selects it for editing.
2. **Given** a custom style is renamed, **Then** the profile file is renamed atomically engine-side (new `ProfileRename` IPC); if it was the active style, `Formatter.ActiveProfile` in config.json is updated in the same operation; name collisions and built-in names are rejected with the existing `ProfileManager` validation rules.
3. **Given** a custom style is deleted, **Then** the existing `ProfileDelete` IPC removes it; deleting the **active** style is blocked with an explanatory message; built-ins cannot be deleted.
4. **Given** the list renders, **Then** custom and built-in styles appear in separate sections, the active style carries a ✔ marker, and read-only styles show the lock glyph (the broken DataTemplate converter registration is fixed rather than silently falling back).

---

### User Story 4 - Options page becomes the SQL Prompt-exact launcher (Priority: P2)

Options → Format → Styles is the slim Redgate page: Active style dropdown, an **"Edit formatting styles…"** button that opens the window, and the 9 behavior/safety toggles grouped under a "Behavior" header. Style changes made in the window are reflected when it closes.

**Why this priority**: The entry-point from Options is the discoverability path users expect from SQL Prompt; the visual regrouping finishes the "remove style editing from Options" story (nothing else style-related remains there).

**Independent Test**: Open Options → Format → Styles; click Edit formatting styles… — window opens over Options; create + activate a new style in it; close it — the Options dropdown now lists and selects the new style.

**Acceptance Scenarios**:

1. **Given** the Format › Styles page, **Then** it shows exactly: Active style dropdown, Edit formatting styles… button, and the existing toggles under a "Behavior" group header — no layout/casing settings.
2. **Given** the window is closed after changing the style list or the active style, **Then** the page's dropdown re-queries `ProfileList` and re-selects the current active profile.
3. **Given** the window set a different active style, **When** Options is OK'd/cancelled afterwards, **Then** the Options save path does not clobber the newer `ActiveProfile` value (last-writer semantics verified).

---

### User Story 5 - Discoverability and debris removal (Priority: P3)

Format Styles is reachable from every command surface, and the dead legacy editor is gone.

**Independent Test**: In SSMS the DTE-injected AKML SQL menu shows "Format Styles..."; Command Palette finds "Format Styles"; searching the repo finds no `ProfileEditorDialog` and the palette has no "Edit Format Profile" entry.

**Acceptance Scenarios**:

1. **Given** SSMS shows the DTE-injected fallback menu, **Then** Format Styles... appears (added to the hardcoded command array in `EnsureTopLevelMenu`).
2. **Given** the Command Palette, **Then** a "Format Styles" entry dispatches 0x0916, and the dead "Edit Format Profile" entry is removed.
3. **Given** the cleanup, **Then** `Ui/ProfileEditorDialog.cs`, `ProfileEditorViewModel.cs`, `EditProfileCommand` and their projitems entries are deleted, and the stale "T044–T048/T059 deferred" notes in CLAUDE.md / `doc/architecture.md` / `T059-runbook.md` are corrected.

---

### Edge Cases

- Two hosts (SSMS + VS) editing the same custom style concurrently → last save wins file-wise (atomic profile writes); the window re-reads on selection so a stale working copy is only possible within one open session — acceptable, documented.
- Rename/delete while the style is being previewed → preview uses in-memory working values + `ProfileJson`, unaffected mid-flight; the list refresh after the operation reconciles selection.
- `ProfileGet` for a name that no longer exists (deleted externally) → error surfaced in status bar, list refreshed, selection cleared — no crash, no default-seeding masquerading as the style.
- Saving with the preview showing a stage-6 `SemanticValidator` rejection (amber bar) → Save is still allowed (the rejection is sample-specific), but the amber bar persists until a clean preview.
- Preview-sample editing (closes spec-020 T069): sample edits persist via the existing atomic write to `%AppData%/AKML SQL/editor/preview-sample.sql`; a broken/huge sample must not break preview (existing 2 s timeout + size cap apply).
- Schema v2 with an old engine (shell newer than engine mid-upgrade) → schema request returns v1 without `ParentId`/enums; the window must degrade to the flat tree + text boxes, not crash (guard: treat missing enrichment as absent, never assume).
- Enum values must serialize back exactly as the profile JSON expects (camelCase JSON enum handling identical to the existing `BuildProfileJson` path).
- Rename target differing only by case (`Path` collision on NTFS) → rejected as a collision.
- Active style deleted directly on disk while window open → Set Active/Save flows re-validate against the refreshed list.

## Requirements *(mandatory)*

### Functional Requirements

**Engine — schema enrichment (`src/AkmlSql.Formatting/Profiles/FormatSettingSchema.cs`)**

- **FR-001**: `BuildDefault()` MUST populate `FormatSettingGroup.ParentId` producing the five-category hierarchy of US2 scenario 1; the response MUST include the five category nodes so the client renders a 2-level tree without hardcoding.
- **FR-002**: Every enum-typed setting MUST carry `AllowedEnumValues` (reflected from the CLR enum type, serialized in the same convention the profile JSON uses).
- **FR-003**: Every setting MUST carry a nonempty `Description`, and int settings a `Min`/`Max` where meaningful, sourced from annotations on the `FormattingProfile` sub-category POCOs so reflection keeps schema and model in lockstep (new options without annotations fail a schema test, not silently ship undescribed).
- **FR-004**: `SchemaVersion` MUST bump to 2; the existing `ClientSchemaVersion` cache short-circuit MUST invalidate v1 client caches automatically.

**Engine — IPC additions (`RpcMessage.cs`, `FormattingHandlers`)**

- **FR-005**: New **`ProfileGet` = 34**: request by profile name → stored profile JSON (including `Metadata` and `ExtensionData`) + read-only flag; unknown name → failure response, nothing created.
- **FR-006**: New **`ProfileRename` = 35**: atomic file rename of a custom profile; built-in names and collisions rejected via existing `ProfileManager` rules; response carries the final name.
- **FR-007**: Existing `ProfileSave` (15) / `ProfileDelete` (16) contracts MUST NOT change; the window becomes their first shell caller.

**Window — editing (`FormatStylesEditorWindow.cs` / `FormatStylesEditorViewModel.cs`)**

- **FR-008**: Style selection MUST load the style's stored values via `ProfileGet` into working values (flattened `group.setting` keys), replacing default-seeding; the loaded raw profile JSON is retained as the merge base.
- **FR-009**: Save MUST merge edited working values into the retained profile JSON (preserving `Metadata`, `ExtensionData`, unknown keys) and persist via `ProfileSave`; Save is enabled only when dirty and never for read-only styles.
- **FR-010**: Dirty tracking MUST prompt Save / Discard / Cancel on style switch and window close.
- **FR-011**: Read-only (built-in) styles MUST render all value controls disabled with a "Copy this style to edit" affordance; double-click on a built-in copies it (existing `DuplicateProfile` IPC).

**Window — readability**

- **FR-012**: The tree MUST render the 2-level hierarchy from FR-001 (categories expanded by default, groups beneath).
- **FR-013**: Enum settings MUST render as ComboBoxes from `AllowedEnumValues`; int settings MUST validate `Min`/`Max` inline; descriptions MUST display with each setting. A v1 schema (older engine) MUST degrade gracefully to the current flat/free-text rendering.
- **FR-014**: The preview sample MUST be editable in-window and persist via the existing atomic sample write (closes spec-020 T069); spec-031 status badges and the SQL Prompt-key metadata line keep their current placement.

**Window — lifecycle**

- **FR-015**: New Style… MUST offer name + based-on picker; Rename via `ProfileRename` (updating `Formatter.ActiveProfile` when renaming the active style); Delete via `ProfileDelete`, blocked for the active style and for built-ins; all reachable from a per-style context menu plus the toolbar.
- **FR-016**: The list MUST section "Your styles" / "Built-in styles", show a ✔ on the active style, and show the lock glyph on read-only styles (fix the unregistered `BoolToVisibilityConverter` so the primary DataTemplate renders).

**Options page (`Dialogs/Pages/FormattingPage.cs`)**

- **FR-017**: The page MUST gain an "Edit formatting styles…" button launching the window, MUST regroup the 9 existing toggles under a "Behavior" header, and MUST refresh the dropdown (list + active selection) when the window closes; the Options save path MUST NOT overwrite an `ActiveProfile` changed by the window (page re-reads before save). No other Options page changes; no settings move or migrate.

**Cleanup**

- **FR-018**: Delete `Ui/ProfileEditorDialog.cs`, `Ui/ProfileEditorViewModel.cs`, `Commands/EditProfileCommand.cs` (+ projitems entries) and the "Edit Format Profile" palette entry; add a "Format Styles" palette entry; add `CmdFormatStyles` to the SSMS `EnsureTopLevelMenu` command array; correct the stale deferred-work notes in CLAUDE.md, `doc/architecture.md`, `T059-runbook.md`.

### Key Entities

- **`FormatSettingSchema` v2** — reflection-built, now hierarchical (`ParentId`), enum-aware, described, ranged; the single source of truth the window renders.
- **Profile annotations** — description/range attributes on `FormattingProfile` sub-category POCO properties (`src/AkmlSql.Formatting/Profiles/`), picked up by the schema builder.
- **`ProfileGet` (34) / `ProfileRename` (35)** — new MessagePack request/response pairs beside the existing profile messages (14–17, 28–29, 32).
- **`FormatStylesEditorViewModel` state** — selected style + its loaded profile JSON (merge base), dirty flag, working values (existing `ConcurrentDictionary`), preview pipeline unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Selecting each of the 8 built-in styles shows that style's stored values — at least one setting visibly differs between "Default" and "Khamis Style" in the tree (today: identical defaults for all).
- **SC-002**: A value edited and saved on a custom style changes Format SQL output in the same session, no restarts (engine or shell).
- **SC-003**: 100% of enum-typed settings render dropdowns (zero free-text enum boxes); 100% of settings show nonempty descriptions; a schema unit test enforces both for every current and future profile property.
- **SC-004**: The tree has exactly 5 top-level categories with every group parented; verified by schema test + UI smoke.
- **SC-005**: Built-in styles cannot be modified from the window (controls disabled, Save disabled) — attempted edits leave `profiles/BuiltIn` untouched on disk.
- **SC-006**: An imported Redgate style (spec 031) edited and saved re-exports with its previously-preserved unknown keys intact (round-trip regression test).
- **SC-007**: Format Styles is reachable from: Tools menu (both hosts), the SSMS DTE fallback menu, the Command Palette, and the Options Format › Styles button.
- **SC-008**: `ProfileEditorDialog`/`ProfileEditorViewModel`/`EditProfileCommand` no longer exist in the repo; the palette lists no dead entries.
- **SC-009**: All existing test suites stay green; new tests cover schema enrichment, `ProfileGet`/`ProfileRename` handlers, and ViewModel load-on-select / dirty / merge-save / read-only / lifecycle flows against a fake IPC client.

## Assumptions

- Desktop only (SSMS 22 + VS 2026 via the shared project); the web edition does not host this window and is untouched.
- No formatter behavior changes: this spec is schema/IPC/UI only — layout gap closure remains spec 031 Phase 3.
- The existing engine-side `ProfileSave`/`ProfileDelete` handlers are correct as-is (they were built for the legacy dialog and spec-004; new tests will pin them).
- MessagePack contract changes are additive (new message types + new optional schema fields), preserving mixed-version tolerance per the FR-013 degradation guard.
- Engine redeploys are full-publish copies (per repo rule); shell + engine ship together in the installer.

## Dependencies

- Spec 031 import machinery (classification badges, `ExtensionData` round-trip) — consumed, not modified.
- Spec 020 window/preview/IPC plumbing — extended in place.
- Full engine publish + extension rebuild/redeploy for verification on the dev machine.

## Out of Scope

- Web edition surfaces.
- Moving/unifying `intelliSense.keywordCase`, Qualification, bracket mode, special characters, or alias generation (Redgate keeps them in Options; so do we).
- Spec-031 Phase 3 layout rendering (tab-stop alignment, comma gutter, 9-value paren styles, …) — the badges keep reporting honestly.
- Pixel-parity screenshot audit of the window (needs reference screenshots; tracked from report 10).
- Team/network shared style folders, pre-v8 style auto-import, cloud sharing (Prompt-gap items, unchanged).
