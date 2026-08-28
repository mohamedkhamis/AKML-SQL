# Formatting SQL

AKML SQL reformats T-SQL according to a formatting style. It fixes indentation, line breaks, commas, keyword casing, and more — without changing what the query does. If the formatter cannot prove the result is equivalent, it leaves your original SQL unchanged.

## Format the current query

1. Click inside the query window.
2. Press **Ctrl+K, Y** (default) or use the AKML SQL menu -> **Format SQL**.

The whole document is reformatted. The operation is undoable with Ctrl+Z.

## Format only a selection

1. Select the lines you want to reformat.
2. Press **Ctrl+K, F** or right-click and choose **Format Selection**.

Only the selected region changes.

## Protect code from the formatter

Wrap hand-tuned SQL in noformat comments:

```sql
-- noformat
SELECT   weird   spacing   FROM   dbo.LegacyTable
-- endnoformat
```

The region stays exactly as written.

## Pick a format style

The active style is shown in the status bar. AKML SQL ships with built-in read-only styles (the default is "Khamis Style"). Switch styles from the AKML SQL menu or Options.

## Edit styles with live preview

Open the Format Styles editor from **Tools** -> **Options** -> **AKML SQL** -> **Format** -> **Styles**.

- Left column: the list of styles. Built-in styles are read-only; create a custom style to make changes.
- Middle column: the settings tree, grouped by category (whitespace, casing, lists, joins, and so on).
- Right column: a live preview that updates about a tenth of a second after each change, using a sample query you can replace with your own.

## Import a Redgate SQL Prompt style

If your team uses SQL Prompt, you can import its `.sqlpromptstylev2` files:

1. Open the Format Styles editor.
2. Import the `.sqlpromptstylev2` file.
3. AKML SQL converts it to a new style and reports any settings it could not map.

Export back to `.sqlpromptstylev2` is also supported, so teams mixing both tools can share one style.

## Share styles with your team

Styles are stored as plain JSON files:

```
%AppData%\AKML SQL\profiles\{name}.akmlstyle
```

Copy an `.akmlstyle` file into a teammate's profiles folder, or use export/import in the editor, and the style appears in their list.

For the full option list and the style file format, see the [Formatter reference](../formatting.md) and the [Configuration reference](../configuration.md).
