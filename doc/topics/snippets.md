# Snippets

Snippets are reusable SQL templates. Type a short code, press Tab, and the full template expands in the editor with placeholders you can tab through and fill in.

## Snippet sources

AKML SQL loads snippets from three places:

- **Built-in** — a library that ships with the plugin (create table, transaction wrappers, and more).
- **Personal** — your own snippets, stored on your machine.
- **Team** — an optional shared folder your whole team points at.

Snippets also appear in the [IntelliSense](intellisense.md) completion list with a `{}` icon.

## Expand a snippet

1. Type the snippet's shortcode, for example `ct` for Create Table.
2. Press **Tab**.
3. The template expands. The first placeholder is highlighted — type to replace it.
4. Press **Tab** to jump to the next placeholder, **Shift+Tab** to go back.

If several snippets match, pick one from the completion popup first. Snippets can be formatted automatically on expand, matching your active [format style](formatting.md).

## Surround selected code

1. Select one or more statements.
2. Press **Ctrl+K, Ctrl+S** (default) to open the surround-with picker.
3. Choose a surround snippet, such as a transaction with `TRY/CATCH`.

The selected code is inserted where the template marks `$SELECTEDTEXT$`.

## Create your own snippet

1. Select the SQL you want to reuse.
2. Right-click and choose **Create Snippet from Selection**.
3. Give it a name, a shortcode, and optional tags.
4. Add `$CURSOR$` where the caret should end up after expansion.
5. Save. The snippet works immediately.

Snippets are stored as `.akmlsnippet` files (plain JSON), so you can also create or edit them with any text editor.

## Where snippets are stored

```
%AppData%\AKML SQL\snippets\personal\
```

- Personal snippets live in the folder above (you can override the path in Options).
- Team snippets live in whatever folder you set as the team folder — point it at a shared drive or a synced folder.
- Built-in snippets are embedded in the plugin and cannot be edited.

## Import from SQL Prompt

The Snippet Manager can import a Redgate SQL Prompt snippet library (`.sqlpromptsnippet` files). It detects the SQL Prompt folder automatically, converts each snippet to the `.akmlsnippet` format, and reports any shortcode conflicts.

## Settings

Open **Tools** -> **Options** -> **AKML SQL** -> **Snippets** to change the expand key, the surround-with shortcut, folder paths, and context filtering. See the [Configuration reference](../configuration.md) for all keys.
