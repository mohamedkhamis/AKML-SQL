# Web workspace update: query tabs

The SQL editor now opens queries in tabs, as SSMS 22 does. **New query** opens a tab on the
same connection as the tab you were in. Each tab keeps its own text, name, results and its own
live connection, so a temporary table or an open transaction in one tab is not seen in another.
Runs in two tabs can overlap: start a long query in one tab and keep working in another.
Up to 20 tabs can be open at once. (Two app windows can together leave more stored; every tab
comes back, and New query then says how many to close.)

Switching tabs switches the status bar, the database selector and the Schema panel to that
tab's connection. Closing a tab with unsaved changes asks whether to save, discard or cancel.
Closing a tab with an open transaction rolls it back after a warning, as Disconnect does.
Closing a tab while its query runs asks first, then cancels the query: the statement that is
running stops, the batches after it do not run, and anything not committed is rolled back.
Closing a SQL Server tab asks the server whether it holds an open transaction, as SSMS does.
**Open** puts a file in a new tab, unless the current tab is empty. **Open** and **Re-execute**
in SQL History also open a new tab and never ask to discard your work.

Your tabs come back after a reload, with their names, text, unsaved marks and connections.
A tab reconnects by itself when it needs no password. A tab that signs in with a password asks
for it once, on its first run. Passwords are never stored in the browser. If the engine restarts,
a tab's next run reports that its connection is gone; run it again and the tab reconnects with
nothing to type. With the app open in two browser windows, each window keeps its own
connections and neither disconnects the other. A tab whose connection is not one of your saved
connections, for example after you delete that saved connection, says "not saved" in its
tooltip; it still reconnects after a reload.

| Key | Action |
| --- | --- |
| Ctrl+Alt+N | New query tab |
| Ctrl+Alt+PageDown / Ctrl+Alt+PageUp | Next / previous query tab |
| Ctrl+Alt+W | Close the query tab |
| Arrow keys, Home, End, Delete | Move between and close tabs when the tab strip has focus |

The browser keeps Ctrl+Tab, Ctrl+W and Ctrl+F4 for its own tabs, so these keys use Ctrl+Alt.

**One Connect to database dialog** serves SQL Server and PostgreSQL. Its fields are grouped
into server, sign-in, database and security. **Connect to database…** opens on the tab you are
in: on its own connection, or on SQL Server when it has none. The SQL Server choice **Trust the server
certificate** is saved with the connection and used again after a reload.

**Editor text** has a set line spacing, clearer syntax colours in the light and dark themes, a
font choice in Settings › Editor, and matching-bracket highlighting. Writing assistants such as
Grammarly are asked to leave the SQL editor alone. An assistant that ignores the request can be
turned off for this site in its own settings.

**Dark theme**: form controls and scrollbars follow the theme, and badges and banners that were
hard to read now meet the contrast target.

## Upgrade notes

- The browser's local storage moves to version 5. After updating, users with two app tabs open
  may see an **upgrade is blocked** message once. Close the other tab, then retry.
- The single document of earlier versions becomes the first query tab, with its name and text.
- Update the web edition and the engine together. The engine now answers a browser's requests
  side by side, which overlapping runs need, and releases a closed tab's connection.
- **Cancel** now stops a statement that is running: the engine answers a page's requests side
  by side, so the cancel reaches the server while the statement runs, and the batches after it
  do not run. (Before, it only cancelled a statement the engine had not started.) Closing a tab
  whose query runs asks first, then does the same.
