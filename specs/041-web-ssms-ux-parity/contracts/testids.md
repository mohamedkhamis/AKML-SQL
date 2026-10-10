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

In every grid id, `{r}` is the **fetched** row (a sort never renames a row) and `{c}` the column.
The cell's display position is its `data-r` (row) / `data-c` (column; `-1` on the row number);
header cells carry `data-header-c`, resize handles `data-col`. The grid root's
`aria-activedescendant` is `akml-grid-{set}-{displayRow}-{c}` (`-h-{c}` for a header,
`-{displayRow}-rn` for a row number).

## Reserved for this feature

| Area | Ids |
|---|---|
| Results pane | `results-pane`, `results-tab-results`, `results-tab-messages`, `results-tab-problems`, `results-problems-badge`, `results-empty`, `results-running`, `results-set-{n}`, `results-set-header-{n}`, `results-jump-{n}`, `results-apply-{n}`, `results-discard-{n}` (only when two or more sets are stacked — a single set keeps `results-apply` / `results-discard`), `results-content`, `results-panel-results`, `results-panel-messages`, `results-panel-problems` (always mounted, `hidden` when not shown), `results-no-sets`, `execute-complete` (hidden, `data-version`, `data-status` = the run's ExecuteStatus), `execute-running`; inside Problems: `problems-header`, `problems-title` (absent in the pane), `problems-analysis-off` and its `problems-enable-analysis` ("Turn on") while code analysis is off |
| Grid | `results-corner`, `results-rownum-{r}`, `results-col-{c}`, `results-col-resizer-{c}`, `results-sort-{c}`, `results-col-type-{c}`, `results-context-menu`, `results-header-menu`, `results-view-value`, `results-save-csv`, `results-save-tsv`, `results-cell-tooltip`, `results-cell-invalid-{r}-{c}`, `results-insert-invalid-{i}-{c}`, `results-apply-blocked` (why Apply is disabled) |
| Grid menus | Cell menu: `results-menu-copy`, `results-menu-copy-headers`, `results-menu-copy-as` (submenu `results-menu-copy-csv`, `-json`, `-markdown`, `-insert`), `results-menu-select-all`, `results-menu-view-value`, `results-menu-set-null`, `results-save-csv`, `results-save-tsv`, `results-menu-delete-row` (Delete / Restore). Header menu: `results-menu-autofit`, `results-menu-autofit-all`, `results-menu-reset-widths`, `results-menu-copy-name`, `results-menu-sort-asc`, `results-menu-sort-desc`, `results-menu-sort-clear` |
| View value | `results-view-value` (the dialog), `results-view-value-title`, `results-view-value-type`, `results-view-value-size`, `results-view-value-text`, `results-view-value-cut` (engine-cut note), `results-view-value-copy`, `results-view-value-copied`, `results-view-value-close` |
| Messages | `messages-list`, `messages-line-{i}`, `messages-error-{i}`, `messages-completion` |
| Toolbar | `execute-cancel` (existing, relabelled "Cancel if not started"), `parse-button`, `db-selector`, `db-selector-list`, `doc-new`, `doc-open`, `doc-open-input`, `doc-save`, `doc-saveas`, `view-menu` (opens `view-menu-list`), `view-toggle-{panel}` (`nav`, `schema`, `results`, `ai`, `maximise`), `view-cycle-panes`, `caps-maxrows`, `caps-timeout` (the existing ids, kept — earlier drafts said `caps-rows`) |
| Document bar | `doc-bar`, `doc-name`, `doc-modified` (present only while modified). `doc-open` is a `<label>` around the hidden `doc-open-input` (`id=akml-doc-open-input`; the Open command clicks it); tab strips render `tabstrip-{name}` (`results`, `ai`, `preview`) |
| Workspace | `workspace`, `workspace-schema`, `workspace-ai`, `workspace-results`, `workspace-splitter-left`, `workspace-splitter-right`, `workspace-splitter-bottom`, `workspace-edge-left`, `workspace-edge-right`, `workspace-edge-bottom`, `workspace-edge-nav`, `workspace-reset-layout`. The side panels and the results host stay in the DOM while hidden — assert visibility, not presence. Region ids for F6: `#akml-ws-centre`, `#akml-ws-results`, `#akml-ws-schema`, `#akml-ws-ai` |
| Schema panel | `schema-header`, `schema-filter`, `schema-refresh`, `schema-connect`, `schema-loading` |
| Settings | `settings-page`, `settings-section-{id}`, `settings-nav-{id}`, `settings-filter`, `settings-restore-{id}`, `settings-restore-all`, `settings-export`, `settings-import`, `settings-import-input`, `setting-{id}` (each `SettingRow`), `suppressed-rule-{id}`, `suppressed-rule-undo-{id}`, `provider-needs-key-{id}`, `settings-manage-connections` (existing); also `settings-format-styles-link`, `settings-no-match` (filter with no result), `remapped-rule-{id}` (a rule with a changed severity, read-only), `schema-cache-clear-all`, `engine-remove-{id}`, `engine-repair-{id}` (LAN engines), `saved-connection-{id}`, `saved-connection-problem-{id}` (why the web app cannot use it), `saved-connection-delete-{id}`, `settings-sql-connected`. A row's control has the id `setting-{id}-control` (its label's `for`); block rows (tables, forms, groups) have a heading instead of a label |
| Shell | `dialog`, `dialog-title`, `dialog-message`, `dialog-input`, `dialog-confirm`, `dialog-cancel`, `toast`, `toast-close`, `nav-{route}` (editor, snippets, styles, history, settings), `tabstrip-{name}`, `tab-{id}`, `page-header`, `page-title`, `page-actions` |
| Status bar | `status-outcome`, `status-rows`, `status-caret`, `status-version-web`, `status-version-engine`, `status-version-mismatch` |

Hidden completion markers follow the `format-complete` idiom: `execute-complete` carries
`data-version` (incremented per execution) and `data-status`.
