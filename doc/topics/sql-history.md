# SQL History

AKML SQL records every query you execute — automatically, without you saving anything. The history is crash-safe: if SSMS or Visual Studio closes unexpectedly, your executed queries are still there on the next start.

## Open the history window

Press **Ctrl+Alt+H** (default) or open it from the AKML SQL menu.

## The three panels

The history window has three panels:

1. **Queries** — the list of executed queries, grouped by time (Today, Yesterday, ...), with server, database, duration, and row count for each entry.
2. **Versions** — snapshots of the selected query. A new version is captured when you execute or close the tab, so you can see how the query evolved.
3. **Preview** — the full SQL text of the selected version, with syntax highlighting.

## Find an old query

Type in the search box to run a full-text search across the whole history — table names, comments, anything in the SQL text. The list filters as you type.

## Star and filter entries

- Click the star on an entry to mark it as a favorite. Starred queries are never auto-deleted by retention cleanup.
- Use the filters to show only queries from **open** tabs, only **closed** tabs, or narrow the list to a specific server or database.

## Restore a query

1. Find the query in the list.
2. Open it in a new tab — the full SQL text comes back with the original connection context.
3. To go back to an earlier edit, pick the older version in the Versions panel and open that instead.

You can also re-execute a history entry directly, or compare two versions side by side.

## Recover tabs after a crash

If the previous session ended abnormally, AKML SQL offers to restore your unsaved query tabs on the next startup. Tabs are auto-saved about once a minute, so you lose at most the last minute of edits.

## Keep history under control

Retention rules trim old, unstarred entries automatically. Star anything you want to keep forever.
