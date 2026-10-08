# Contract: `data-testid` vocabulary

**Feature**: 041-web-ssms-ux-parity

Convention (unchanged): kebab-case, component-prefixed, dynamic ids by interpolation,
companion `data-*` attributes, hidden `*-complete` markers with `data-version` for Playwright
edge-trigger waits. bUnit and Playwright select through these ids only; CSS classes change with
the restyle and are never selectors.

## Never change (existing tests select them)

`results-cell-{r}-{c}`, `results-cell-input`, `results-apply`, `results-apply-message`,
`results-row-{r}`, `results-delete-{r}`, `results-elapsed`, `results-affected` (both move to
the pane but keep their ids), `engine-add-*`, `status-pill` (texts "Live · no SQL", "Live",
"Cached", "Offline"), `status-connection` (text format `Server/Database`), `styles-status`
(Styles page inline status line), `conn-*`, `ai-button`, `ai-tab-actions`, `ai-tab-chat`,
`ai-dock`, `sql-editor`, `style-*`, `page-{id}`, `option-{path}`, `reset-{path}`,
`preview-sample`, `preview-mysql`, `format-complete`, `analyse-complete`, `schema-tree`,
`schema-tree-object`, `schema-tree-column`, `schema-tree-virtualized`, `schema-tree-stale`,
`execute-button`, `error-banner` (MainLayout contract: asserted **empty** in every scenario —
dialogs and toasts must not reuse it). CSS class `akml-tool-button` stays on
`RefactorPreviewPanel`'s footer buttons (a bUnit test selects it).

Stacked result sets reuse `results-cell-{r}-{c}` **inside** `results-set-{n}`; tests select
with a descendant selector so the first set keeps today's ids.

## Reserved for this feature

| Area | Ids |
|---|---|
| Results pane | `results-pane`, `results-tab-results`, `results-tab-messages`, `results-tab-problems`, `results-problems-badge`, `results-empty`, `results-running`, `results-set-{n}`, `results-set-header-{n}`, `results-jump-{n}`, `results-apply-{n}`, `results-discard-{n}`, `execute-complete` (hidden, `data-version`), `execute-running` |
| Grid | `results-corner`, `results-rownum-{r}`, `results-col-{c}`, `results-col-resizer-{c}`, `results-sort-{c}`, `results-col-type-{c}`, `results-context-menu`, `results-header-menu`, `results-view-value`, `results-save-csv`, `results-cell-tooltip`, `results-cell-invalid-{r}-{c}` |
| Messages | `messages-list`, `messages-line-{i}`, `messages-error-{i}`, `messages-completion` |
| Toolbar | `parse-button`, `db-selector`, `db-selector-list`, `doc-new`, `doc-open`, `doc-open-input`, `doc-save`, `doc-saveas`, `view-menu`, `view-toggle-{panel}`, `caps-rows`, `caps-timeout` (existing `caps-*` kept) |
| Document bar | `doc-name`, `doc-modified` |
| Workspace | `workspace`, `workspace-schema`, `workspace-ai`, `workspace-results`, `workspace-splitter-left`, `workspace-splitter-right`, `workspace-splitter-bottom`, `workspace-edge-left`, `workspace-edge-right`, `workspace-edge-bottom`, `workspace-edge-nav`, `workspace-reset-layout` |
| Schema panel | `schema-header`, `schema-filter`, `schema-refresh`, `schema-connect`, `schema-loading` |
| Settings | `settings-page`, `settings-section-{id}`, `settings-nav-{id}`, `settings-filter`, `settings-restore-{id}`, `settings-restore-all`, `settings-export`, `settings-import`, `settings-import-input`, `setting-{id}` (each `SettingRow`), `suppressed-rule-{id}`, `suppressed-rule-undo-{id}`, `provider-needs-key-{id}`, `settings-manage-connections` (existing) |
| Shell | `dialog`, `dialog-title`, `dialog-message`, `dialog-input`, `dialog-confirm`, `dialog-cancel`, `toast`, `toast-close`, `nav-{route}` (editor, snippets, styles, history, settings), `tabstrip-{name}`, `tab-{id}`, `page-header`, `page-title`, `page-actions` |
| Status bar | `status-outcome`, `status-rows`, `status-caret`, `status-version-web`, `status-version-engine`, `status-version-mismatch` |

Hidden completion markers follow the `format-complete` idiom: `execute-complete` carries
`data-version` (incremented per execution) and `data-status`.
