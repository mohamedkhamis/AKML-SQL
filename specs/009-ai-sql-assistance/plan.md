# Implementation Plan: Custom WPF Completion Popup

**Branch**: `009-ai-sql-assistance` | **Date**: 2026-03-29 | **Spec**: [design](../../docs/superpowers/specs/2026-03-29-custom-completion-popup-design.md)
**Input**: Custom WPF Completion Popup design spec

## Summary

Replace the broken VS `ICompletionSourceProvider` with a custom WPF adorner popup replicating Redgate SQL Prompt's autocomplete UX. Code-only WPF (no XAML), non-blocking Engine RPC, client-side filtering, SQL Prompt color scheme, keyboard navigation, schema loading spinner.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shared project compiled for 6 host targets)
**Primary Dependencies**: VS SDK (MEF IWpfTextViewCreationListener, IAdornmentLayer, IOleCommandTarget), WPF (System.Windows)
**Storage**: N/A (Engine handles schema caching)
**Testing**: Manual testing in SSMS 22 (MEF adornments can't be unit tested without VS host)
**Target Platform**: SSMS 20 (x86), SSMS 21/22 (x64), VS 2019/2022/2026
**Project Type**: VS/SSMS extension (shared project)
**Performance Goals**: <200ms popup appearance, <16ms client-side filter (60fps)
**Constraints**: No [Import] on MEF types, code-only WPF (no XAML), IPC types behind [NoInlining], ContentType "SQL Server Tools"+"SQL"+"T-SQL", TextViewRole Document
**Scale/Scope**: 7 new files, 3 deleted files, 2 modified files

## Project Structure

```text
src/AkmlSql.Shell.Shared/
├── Editor/
│   ├── Completion/                    # NEW directory
│   │   ├── AkmlCompletionPopup.cs     # Popup UI (code-only WPF)
│   │   ├── CompletionPopupAdornment.cs # Adornment lifecycle (show/hide/position)
│   │   ├── CompletionPopupProvider.cs  # MEF provider (IWpfTextViewCreationListener)
│   │   ├── CompletionController.cs     # Keystrokes → Engine RPC → popup updates
│   │   ├── CompletionItemModel.cs      # Item data model
│   │   └── SchemaStatusIndicator.cs    # Bottom-right loading spinner
│   ├── CompletionRpcHelper.cs          # KEEP — Engine RPC utility
│   ├── ConnectionWiringHelper.cs       # KEEP — connection detection
│   ├── SsmsConnectionDetector.cs       # KEEP — caption parsing
│   ├── SqlPromptIcons.cs               # KEEP — color scheme (used by popup)
│   ├── TextViewCreationListener.cs     # MODIFY — remove old handler, add new
│   ├── CompletionCommandHandler.cs     # DELETE — replaced by CompletionController
│   └── CompletionSource.cs             # DELETE — replaced by custom popup
```

## Implementation Tasks

### Task 1: CompletionItemModel — data model
**File**: `Editor/Completion/CompletionItemModel.cs`
**Dependencies**: None
**Effort**: Small

Simple POCO with properties matching Engine's CompletionItem:
- `DisplayText`, `InsertText`, `SecondaryText`, `ObjectType`, `SortPriority`
- `IconLetter` and `IconColor` computed from ObjectType (SQL Prompt scheme)
- `MatchesFilter(string filter)` method for client-side fuzzy filtering

### Task 2: AkmlCompletionPopup — code-only WPF popup
**File**: `Editor/Completion/AkmlCompletionPopup.cs`
**Dependencies**: Task 1
**Effort**: Large

WPF Border containing:
- `ListBox` with custom ItemTemplate (badge + display text + secondary text)
- Footer `TextBlock` ("5 of 56 objects • Toledo")
- Loading indicator ("Loading..." when waiting for Engine)
- SQL Prompt dark theme styling (background #252526, selected #094771, border #3c3c3c)

Public API:
- `SetItems(CompletionItemModel[] items)` — populate list
- `SetFilter(string text)` — filter displayed items client-side
- `MoveSelection(int delta)` — up/down navigation
- `GetSelectedItem()` → `CompletionItemModel`
- `IsVisible` property
- `Show()` / `Hide()`

### Task 3: CompletionPopupAdornment — adornment lifecycle
**File**: `Editor/Completion/CompletionPopupAdornment.cs`
**Dependencies**: Task 2
**Effort**: Medium

Manages popup position on a text view:
- Creates `AkmlCompletionPopup` as a WPF element in the adornment layer
- Positions popup below caret (flips above if near editor bottom)
- Repositions on scroll/caret move
- Hides on editor deactivation
- Uses adornment layer "AkmlSqlCompletion" (defined via MEF export)

### Task 4: CompletionController — keystroke orchestrator
**File**: `Editor/Completion/CompletionController.cs`
**Dependencies**: Task 2, Task 3, CompletionRpcHelper
**Effort**: Large

Implements `IOleCommandTarget`:
- Intercepts all keystrokes per the keyboard handling spec
- Manages debounced Engine RPC (150ms idle timer)
- Sends DocumentChanged on each keystroke
- Sends CompletionRequest on trigger (letter/dot/Ctrl+Space)
- Updates popup with Engine response
- Client-side filter on subsequent keystrokes (no round-trip)
- Suppresses native IntelliSense via `ICompletionBroker.DismissAllSessions`
- Handles commit (Tab/Enter) — inserts text into editor buffer
- Handles dismiss (Esc/Space/parens)

### Task 5: CompletionPopupProvider — MEF wiring
**File**: `Editor/Completion/CompletionPopupProvider.cs`
**Dependencies**: Task 3, Task 4
**Effort**: Small

MEF `IWpfTextViewCreationListener` that:
- No `[Import]` properties (use ServiceProvider.GlobalProvider)
- Exports adornment layer definition "AkmlSqlCompletion"
- Content types: "SQL Server Tools" + "SQL" + "T-SQL"
- TextViewRole: Document
- Creates `CompletionPopupAdornment` + `CompletionController` per text view
- Registers controller as `IOleCommandTarget` filter

### Task 6: SchemaStatusIndicator — loading spinner
**File**: `Editor/Completion/SchemaStatusIndicator.cs`
**Dependencies**: None (independent adornment)
**Effort**: Small

WPF `TextBlock` in bottom-right adornment layer:
- Listens for ConnectionChanged event (from ConnectionWiringHelper)
- Shows "⟳ Loading schema for {database}..." during Phase A
- Shows "✓ {database} ready ({n} objects)" for 3 seconds
- Then hides
- Semi-transparent dark background, white text

### Task 7: Wire everything + cleanup
**File**: `TextViewCreationListener.cs`, `projitems`, delete old files
**Dependencies**: Tasks 1-6
**Effort**: Medium

- Remove `CompletionCommandHandler` creation from TextViewCreationListener
- Delete `CompletionCommandHandler.cs` and `CompletionSource.cs`
- Add all new files to `AkmlSql.Shell.Shared.projitems`
- Remove old files from projitems
- Update `CompletionRpcHelper` if needed for new calling pattern
- Build and test in SSMS 22

### Task 8: Build, deploy, test end-to-end
**Dependencies**: Task 7
**Effort**: Medium

- Clean rebuild all 6 shell projects
- Rebuild installer
- Deploy to SSMS 22
- Test: type `SELECT * FROM ` → see tables with SQL Prompt icons
- Test: type `se` → see SELECT keyword
- Test: Ctrl+Space → manual trigger
- Test: Tab/Enter commits
- Test: schema loading spinner appears on connection
- Test: JOIN context shows related tables
- Test: dot navigation (schema.table.column)

## Execution Order

```
Task 1 (model) ──┐
                  ├── Task 2 (popup UI) ── Task 3 (adornment) ──┐
Task 6 (spinner) ─────────────────────────────────────────────────┤
                                                                   ├── Task 5 (MEF) ── Task 7 (wiring) ── Task 8 (test)
                  Task 4 (controller) ─────────────────────────────┘
```

Tasks 1 and 6 can be done in parallel. Tasks 2→3→5 are sequential. Task 4 is independent until wiring.
