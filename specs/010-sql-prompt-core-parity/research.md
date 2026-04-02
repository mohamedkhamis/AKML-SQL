# Research: SQL Prompt Core Feature Parity

**Date**: 2026-04-01
**Branch**: `010-sql-prompt-core-parity`

## Revised Gap Assessment (Post-Research)

Research revealed that several features have significantly more infrastructure than the spec estimated. Updated assessment:

| Gap Area | Spec Estimate | Actual Status | Remaining Work |
|----------|--------------|---------------|----------------|
| Execution Guard | 0% | **85%** — Engine, IPC, dialog, interceptor all exist | Hook into SSMS pre-execution event + audit logging |
| Snippet Manager UI | 0% UI | **0% UI, 100% backend** — Full engine + IPC | WPF dialog + menu command |
| Settings UI | 40% | **60%** — WinForms + WPF dialogs exist, 15 sections defined | Complete WPF pages for all categories |
| Safe Rename | 0% stub | **70%** — Engine operation fully implemented | Shell command + preview dialog + script output |
| Actions List | 50% | **75%** — LightbulbProvider + ISuggestedActionsSourceProvider exist | Add refactoring actions to lightbulb |
| Grid Sort/Filter | 0% | **30%** — GridAccessHelper, ColumnStatisticsPopup exist | Sort on header click, filter UI |
| Object Definition Box | 30% | **50%** — QuickInfoProvider + IPC exist | Secondary popup panel in completion |
| Navigation Polish | 70% | **70%** — DocumentOutline is stub, no bookmarks | Implement outline + bookmarks |

---

## Decision Log

### D1: Execution Guard — Pre-Execution Hook Strategy

- **Decision**: Use IOleCommandTarget command filter to intercept ECMD_EXECUTE before SSMS processes it
- **Rationale**: `ExecutionInterceptor.cs` already has TODO comments describing this approach. The interceptor, engine handler, IPC messages, and dialog are all complete. Only the SSMS hookup is missing.
- **Alternatives considered**: (1) DTE Events — rejected because DTE CommandEvents fires too late (post-execution). (2) IVsQueryExecution COM interop — investigated but not exposed in SSMS SDK.
- **Key files**: `ExecutionInterceptor.cs` (Shell.Shared/Safety/), `SafetyCheckHandler.cs` (Engine/Safety/), `SafetyWarningDialog.cs` (Shell.Shared/Safety/)

### D2: Snippet Manager — WPF Dialog Pattern

- **Decision**: Use `DialogWindow` base class (PlatformUI) with programmatic layout matching `ProfileEditorDialog` pattern
- **Rationale**: ProfileEditorDialog already demonstrates the exact pattern needed: split-pane, ThemeManager colors, no XAML (SharedProject compatibility), INotifyPropertyChanged ViewModel. The snippet IPC backend (list/save/delete/expand) is 100% complete.
- **Alternatives considered**: (1) WinForms dialog — rejected, WPF is the modern pattern and matches existing ProfileEditorDialog. (2) XAML-based — rejected, SharedProject doesn't support XAML well across different VS SDK versions.
- **Key files**: `ProfileEditorDialog.cs` (template), `SnippetRequestHandler.cs` (backend), IPC messages in Core/Ipc/Messages/Snippet*.cs

### D3: Settings UI — Extend SettingsWindow (WPF)

- **Decision**: Extend the existing `SettingsWindow.cs` (WPF) with additional category pages, repurpose `OptionCategoryTreeBuilder` for hierarchical navigation
- **Rationale**: SettingsWindow already has theme support and basic structure. AppSettings has 15+ sections all defined. OptionCategoryTreeBuilder already builds tree navigation (used in formatter editor). Combining these fills the gap.
- **Alternatives considered**: (1) Rebuild from scratch — rejected, existing WPF window is solid foundation. (2) Keep WinForms SettingsDialog — rejected, WPF is the forward path.
- **Key files**: `SettingsWindow.cs`, `OptionCategoryTreeBuilder.cs`, `AppSettings.cs` (15+ sections)

### D4: Safe Rename — Script Generation Model

- **Decision**: Reuse existing `SafeRenameOperation.PreviewAsync()` for dependency analysis, then generate a SQL script file instead of calling `ApplyAsync()`
- **Rationale**: Per spec clarification, Safe Rename generates a script file (no direct DB execution). The engine's `SafeRenameOperation` already collects all references and generates change info. We adapt the preview output into a CREATE/ALTER script opened in a new editor tab.
- **Alternatives considered**: (1) Use ApplyAsync for direct execution — rejected per user clarification. (2) Build separate reference collector — rejected, existing one works.
- **Key files**: `SafeRenameOperation.cs` (Engine), `SafeRenameCommand.cs` (Shell, stub), `RefactoringPreviewDialog.cs` (Shell, stub), `ReferenceCollector.cs` (Engine)

### D5: Actions List — Extend Existing LightbulbProvider

- **Decision**: Add refactoring actions (Qualify Names, Expand Wildcards, Surround with, etc.) to the existing `LightbulbProvider` / `ISuggestedActionsSourceProvider`
- **Rationale**: The lightbulb infrastructure already works for code analysis fixes. Adding more action types is additive, not architectural.
- **Alternatives considered**: (1) Separate actions popup — rejected, VS lightbulb API is the standard. (2) Custom margin button — rejected, non-standard UI.
- **Key files**: `LightbulbProvider.cs`, `FixAction.cs` (Shell.Shared/Analysis/)

### D6: Grid Sort/Filter — Extend GridFeatureInitializer

- **Decision**: Add sort handler on DataGridView.ColumnHeaderMouseClick and filter popup on column header right-click, integrated through existing GridFeatureInitializer
- **Rationale**: GridFeatureInitializer already discovers grids via timer polling and attaches features. GridAccessHelper provides DataGridView access. ColumnStatisticsPopup already handles column header right-click. DataGridView natively supports SortMode per column.
- **Alternatives considered**: (1) Custom grid control — rejected, too invasive. (2) Aggregate-only — rejected per user decision to keep all in scope.
- **Key files**: `GridFeatureInitializer.cs`, `GridAccessHelper.cs`, `ColumnStatisticsPopup.cs`

### D7: Object Definition Box — Secondary Popup Panel

- **Decision**: Add a secondary WPF popup to `AkmlCompletionPopup` that shows on item highlight, with Summary/Script tabs populated via QuickInfo IPC
- **Rationale**: QuickInfoProvider already returns rich object info (columns, types, keys, row count). AkmlCompletionPopup is WPF-based and extensible. The secondary popup follows the same programmatic WPF pattern.
- **Key files**: `AkmlCompletionPopup.cs`, `QuickInfoProvider.cs` (Engine), `QuickInfoRequest/Response.cs` (Core/Ipc)

### D8: Navigation — Bookmarks and Document Outline

- **Decision**: Implement bookmarks as editor margin glyphs with session-scoped storage; implement Document Outline by completing the existing stub using DocumentOutline IPC (MessageType 64/164)
- **Rationale**: VS SDK provides `IGlyphFactory` for margin glyphs (standard bookmark pattern). DocumentOutline IPC messages already exist; engine handler needs implementation, shell stub needs completion.
- **Alternatives considered**: (1) File-persisted bookmarks — rejected, session-scoped per spec assumption. (2) Skip outline — rejected, stub infrastructure already in place.
- **Key files**: `DocumentOutlineCommand.cs` (stub), IPC types 64/164

---

## Technology Choices Confirmed

| Area | Technology | Rationale |
|------|-----------|-----------|
| Dialogs | WPF `DialogWindow` (PlatformUI), programmatic layout | SharedProject compatibility, ThemeManager |
| Execution hook | `IOleCommandTarget` command filter chain | Standard VS extensibility for command interception |
| Grid | Native `DataGridView` with attached behaviors | SSMS uses WinForms grids, no custom control needed |
| Bookmarks | `IGlyphFactory` + `IClassifierAggregatorService` | Standard VS SDK margin glyph pattern |
| Audit logging | Serilog (existing) | Already used throughout codebase |
| IPC | MessagePack over named pipes (existing) | All new features use existing IPC infrastructure |
