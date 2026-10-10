# Web workspace update

The SQL editor now has resizable result columns, spreadsheet selection, sorting, Copy as,
full cell values, and CSV or tab-delimited downloads. Results, Messages and Problems share
one pane. Scripts support `GO` batches and server-side Parse; F5 executes the selection or
document. The database selector keeps the session's temporary tables and follows `USE`.

Schema, results and AI panels can be hidden and resized. Settings brings the existing
preferences into one page, with search, section resets and import/export that excludes
credentials. New, Open, Save and Save as give queries persistent names. Dialogs and
notifications use the app's theme, including High contrast and reduced motion.

After updating, users with two app tabs open may see an **upgrade is blocked** message
once. Close the other tab, then retry so the browser can finish upgrading its local storage.
Developer builds may show a **web/engine version mismatch** marker because the two builds
were produced at different times. The status bar shows both versions; update both parts
together when deploying this workspace.

Running statements still cannot be interrupted: **Cancel if not started** only cancels queued
work. Multiple query-document tabs and execution plans remain outside this update.
