# Custom WPF Completion Popup — SQL Prompt Style

**Date:** 2026-03-29
**Status:** Approved
**Approach:** WPF Adorner Popup (Option A)
**Scope:** Full SQL Prompt experience (Option B)

## Overview

Replace the broken VS `ICompletionSourceProvider` integration with a custom WPF adorner popup that replicates Redgate SQL Prompt's autocomplete UX. The popup renders as a WPF overlay on the SSMS editor, positioned at the caret, with real-time filtering, SQL Prompt colors, keyboard navigation, and schema-aware context.

## Architecture

### Components

| Component | File | Responsibility |
|-----------|------|---------------|
| `AkmlCompletionPopup` | `Editor/Completion/AkmlCompletionPopup.xaml[.cs]` | WPF UserControl — the popup UI (list, filter, footer) |
| `CompletionPopupAdornment` | `Editor/Completion/CompletionPopupAdornment.cs` | Manages popup lifecycle on a text view (show/hide/position) |
| `CompletionPopupProvider` | `Editor/Completion/CompletionPopupProvider.cs` | MEF `IWpfTextViewCreationListener` — creates adornment per editor |
| `CompletionController` | `Editor/Completion/CompletionController.cs` | Orchestrates keystrokes → Engine RPC → popup updates |
| `CompletionItemModel` | `Editor/Completion/CompletionItemModel.cs` | Data model for a single completion item |
| `SchemaStatusIndicator` | `Editor/Completion/SchemaStatusIndicator.cs` | Bottom-right schema loading spinner adornment |

### Data Flow

```
User types letter/dot
  → CompletionController intercepts via IOleCommandTarget
  → Sends DocumentChanged + CompletionRequest to Engine (background)
  → Engine returns CompletionResponse (tables/columns/keywords based on context)
  → CompletionController updates AkmlCompletionPopup items
  → Popup filters client-side as user continues typing
  → Tab/Enter commits selected item into editor
  → Esc dismisses popup
```

### Key Design Decisions

1. **Adornment layer, not Window** — popup is a WPF element in the editor's adornment layer, moves with scroll, no focus stealing
2. **Non-blocking Engine RPC** — all Engine calls are `Task.Run` fire-and-forget; popup shows "Loading..." until results arrive
3. **Client-side filtering** — after initial fetch, typing filters cached results instantly (no round-trip)
4. **Debounced fetch** — new Engine request only after 150ms of idle typing (prevents flooding)
5. **Replace CompletionCommandHandler** — the new CompletionController replaces the existing one entirely

## Popup UI Design (SQL Prompt Style)

### Visual Structure

```
┌─────────────────────────────────────┐
│ [T] Customers     Table (~1.2M rows)│  ← selected (highlight)
│ [T] CustomerAddr   Table (~500K)    │
│ [V] vw_CustSumm   View             │
│ [P] usp_GetCust   Procedure        │
│ [F] fn_CalcTotal   Function         │
├─────────────────────────────────────┤
│ 5 of 56 objects • Toledo            │  ← footer
└─────────────────────────────────────┘
```

### Color Scheme (SQL Prompt)

| Type | Badge Color | Letter |
|------|------------|--------|
| Table | `#1565C0` (Blue) | T |
| View | `#2E7D32` (Green) | V |
| Column | `#F9A825` (Gold) | C |
| Keyword | `#546E7A` (Blue-Gray) | K |
| Snippet | `#E65100` (Orange) | S |
| Function | `#AD1457` (Magenta) | F |
| Procedure | `#6A1B9A` (Purple) | P |
| Schema | `#616161` (Gray) | S |
| Variable | `#00838F` (Cyan) | @ |
| Alias | `#283593` (Indigo) | A |

### Popup Behavior

- **Show:** On letter typed (after 1+ chars), on dot, on Ctrl+Space
- **Hide:** On Esc, on space (outside identifier), on clicking outside editor
- **Filter:** Real-time as user types — fuzzy match against DisplayText
- **Navigate:** Up/Down arrows move selection, wraps at top/bottom
- **Commit:** Tab or Enter inserts the selected item's InsertText
- **Max items:** 15 visible, scrollable
- **Position:** Below caret line, left-aligned to word start; flips above if near bottom of editor

## Schema Loading Spinner

- WPF adornment in **bottom-right** corner of the text view
- Shows: `"⟳ Loading schema for {database}..."` during Phase A
- Shows: `"✓ {database} ready ({n} objects)"` for 3 seconds after Phase A completes
- Then hides

## Context-Aware Completions

The Engine already handles context (CursorContextAnalyzer + provider routing). The popup just needs to send the right request:

| Context | What Shows |
|---------|-----------|
| Start of statement | Keywords (SELECT, INSERT, UPDATE, etc.) |
| After FROM/JOIN | Tables, Views |
| After SELECT | Columns (if table in FROM), Keywords |
| After WHERE | Columns, Variables, Keywords |
| After dot (`.`) | Schema.Table or Table.Column navigation |
| After `@` | Variables |
| After JOIN ... ON | FK-related columns |

## Keyboard Handling

The `CompletionController` implements `IOleCommandTarget` and intercepts:

| Key | Action |
|-----|--------|
| Letter/digit/underscore | Filter popup, trigger if not shown |
| `.` (dot) | Commit current + trigger new (schema.table.column) |
| `@` | Trigger variable completion |
| Up/Down | Navigate popup list |
| Tab/Enter | Commit selected item |
| Esc | Dismiss popup |
| Space/parens/semicolon | Dismiss popup |
| Backspace | Re-filter; dismiss if filter empty |
| Ctrl+Space | Force trigger (manual) |

## Files to Modify/Create

### New Files
- `Editor/Completion/AkmlCompletionPopup.xaml` — popup XAML
- `Editor/Completion/AkmlCompletionPopup.xaml.cs` — popup code-behind
- `Editor/Completion/CompletionPopupAdornment.cs` — adornment lifecycle
- `Editor/Completion/CompletionPopupProvider.cs` — MEF provider
- `Editor/Completion/CompletionController.cs` — keystroke → RPC → popup
- `Editor/Completion/CompletionItemModel.cs` — item data model
- `Editor/Completion/SchemaStatusIndicator.cs` — loading spinner

### Modified Files
- `Editor/TextViewCreationListener.cs` — remove old CompletionCommandHandler wiring, add new provider
- `Editor/CompletionCommandHandler.cs` — DELETE (replaced by CompletionController)
- `Editor/CompletionSource.cs` — DELETE (replaced by custom popup)
- `Editor/CompletionRpcHelper.cs` — keep as Engine RPC utility, used by CompletionController
- `AkmlSql.Shell.Shared.projitems` — add new files, remove old

### Constraints
- .NET Framework 4.7.2, C# latest (shared project compiled for all targets)
- No `[Import]` properties on MEF-exported types (breaks SSMS 22 instantiation)
- Content types: `"SQL Server Tools"` + `"SQL"` + `"T-SQL"`
- TextViewRole: `Document`
- IPC types behind `[NoInlining]` barrier
- XAML files need `<Resource>` in projitems, not `<Compile>`
