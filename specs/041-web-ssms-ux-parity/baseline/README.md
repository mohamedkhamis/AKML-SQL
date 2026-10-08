# Baseline screenshots (2026-10-08)

Captured from the live web edition at `http://localhost:8083`, version 1.26.1007.0613,
Light theme, paired with the local engine and connected to `localhost/master`. Window
1440 × 900 unless noted. These show the state the spec's Overview describes; they are
evidence, not targets.

| File | What it shows |
|------|---------------|
| `editor.png` | Editor page before any execution: toolbar, editor, fixed Schema and Problems panels on the right, status bar with full build hash. |
| `editor-results.png` | After `SELECT name, database_id, create_date FROM sys.databases`: equal-width columns, blank row header, type chips under headers, ISO dates, no Messages tab, fixed-height results pane. |
| `editor-narrow.png` | Editor page at 1100 × 700: the editor is squeezed below half the window and gains a horizontal scrollbar while both side panels keep their width. |
| `settings.png` | `/settings`: one long column (Theme, Analyser, Editor reset, Engine connections, Connect to SQL Server, links to Schema cache and AI providers). |
| `settings-ai.png` | `/settings/ai`: provider table, add/edit form, privacy modes — a separate top-level page. |
| `schema-cache.png` | `/settings/schema-cache`: a third separate settings page. |
| `diagnostics.png` | `/diagnostics`: log viewer with level filters, a fourth settings-like page. |
| `history.png` | `/history`: every entry is named "query-01" because the editor has one unnamed document. |
| `styles.png` | `/styles`: the Format styles tool (spec 039) — the one page already laid out as a section list with a preview; the model for the consolidated Settings page. |
| `snippets.png` | `/snippets`: a compact toolbar-strip header, different from the large-heading settings pages. |
