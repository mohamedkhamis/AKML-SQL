# Contract: web edition UI changes for PostgreSQL

Existing SQL Server labels, test ids and behaviours stay as they are (FR-091). New test ids are
listed so browser tests can be written before the components.

## Connection dialog (`ConnectionManagerModal.razor`)

| Element | Test id | Behaviour |
| --- | --- | --- |
| Kind switch (segmented control: SQL Server / PostgreSQL) | `conn-kind-sqlserver`, `conn-kind-postgresql` | PostgreSQL option disabled with tooltip "Requires an engine with PostgreSQL support (dialect.postgresql.v1)" when the capability is absent (FR-006) or `postgreSql.enabled` is false |
| Host | `conn-pg-host` | required |
| Port | `conn-pg-port` | default 5432, 1..65535 |
| Database | `conn-pg-database` | default `postgres`; the list is loaded by Test / List databases |
| User name | `conn-pg-user` | required (FR-102: no integrated auth) |
| Password | `conn-pg-password` | empty allowed when a remembered password exists (`CredentialRequest(Has)`) |
| TLS mode | `conn-pg-sslmode` | disable / prefer (default) / require / verify-ca / verify-full |
| CA certificate path | `conn-pg-rootcert` | shown for verify-ca / verify-full; "a path on the engine machine" |
| Remember password | `conn-pg-remember` | off by default; help text: "Stored on the engine machine for the account the engine runs as. On the web engine service this means any administrator of the engine machine. Any client paired with this engine can use it." (FR-104, FR-106) |
| Forget password | `conn-pg-forget` | visible when `Has` is true; sends `CredentialRequest(Forget)` |
| Test | existing | PostgreSQL: shows "PostgreSQL 18.1 on host:port" or the server's message + hint (FR-002, FR-007) |
| Save / Connect | existing | saves `Kind`, `Port`, `SslMode`, `RememberPassword`; never the password |

Wrong-kind detection (FR-008): a connect error whose message matches the other protocol's
signature (SqlClient's pre-login failure on a PostgreSQL port; Npgsql's "invalid startup packet"
style error on a SQL Server port) is rephrased: "The server at host:port did not answer as a
<kind> server. Check the database kind."

## Connection picker, status bar, palette, Settings

| Surface | Change | Test id |
| --- | --- | --- |
| Connection manager saved list (`ConnectionManagerModal`; `ConnectionPickerComponent` lists engines, not database connections) | kind badge per entry ("PG" for PostgreSQL, "Win" / "SQL" for SQL Server, with the full name as its title) | `conn-row-kind` |
| `StatusBar` | kind segment before the server segment: icon + "PostgreSQL" / "SQL Server"; tooltip with server version | `status-kind` |
| `StatusBar` | transaction segment when state ≠ None: "Transaction open" / "Transaction aborted" + Rollback button | `status-txn`, `status-txn-rollback` |
| `StatusBar` | `status-login` title becomes "Login" (was "SQL Server login") only on PostgreSQL sessions | existing id |
| `CommandPalette` | existing "Connect to SQL Server…" entry unchanged; new "Connect to PostgreSQL…" opens the dialog on the PostgreSQL kind | row selector `[data-action-id='sql:connect-postgresql']` (superseded by spec 043: one "Connect to database…" command, `sql:connect`; see `specs/043-web-query-tabs-polish/contracts/web-ui.md` §5) |
| Settings → Connections | kind badge per saved connection; Forget password action | `settings-conn-kind`, `settings-conn-forget` |
| Settings → Diagnostics | "PostgreSQL support: available / not advertised / disabled in config"; active kind + server version | `diag-postgresql` |

## Editor and workspace

| Behaviour | Contract |
| --- | --- |
| Dialect | `akml-editor.js` exports `setDialect(kind)`; the language compartment is `PostgreSQL` for PostgreSQL sessions, `StandardSQL` otherwise (unchanged today). Called by `Editor.razor` on connection change and on disconnect (FR-060, FR-065) |
| Execute (F5) | unchanged key; `IQueryExecutionService` sends the script untouched for PostgreSQL (no in-browser `SplitBatches`) |
| Parse (Ctrl+F5) | `ExecuteParse` as today; the engine never contacts the server for PostgreSQL |
| Format / Format actions | routed to the engine (`Format`, `FormatAction`, `FormatPreview`) for PostgreSQL; in-browser formatter never receives PostgreSQL text |
| Code analysis | routed to the engine (`CodeAnalysis`) for PostgreSQL; "Enable code analysis" off → no request |
| Completion / quick info / signature help / wildcard expansion | engine, as the online path already does; offline fallbacks (`OfflineSqlScanner`, basic keywords) disabled for PostgreSQL |
| Snippets | built-in list swapped by kind; user snippets stored with `Kind` and filtered by it (FR-066) |
| Safety check | `SafetyCheckRequest.Kind = PostgreSql` |
| Unsupported surfaces (FR-092) | Refactor menu entries, Find invalid objects: disabled with tooltip "Not available for PostgreSQL" (`data-testid="unavailable-postgresql"`); the palette entries carry the same note; shortcuts show the message in the status outcome segment |
| Database selector | before switching on PostgreSQL: dialog "Switch to {db}?" - "PostgreSQL connects to one database at a time, so switching to {db} reconnects. Temporary tables and session settings are lost." (`data-dialog-id="db-switch-confirm"`); when `TransactionState ≠ None`: "Open transaction" / "Aborted transaction" - "... Switching to {db} reconnects and rolls it back. Nothing is committed." with a destructive "Roll back and switch" (FR-009, FR-031). The connection manager asks the same way before Connect |
| Connection switch | same transaction guard (FR-009); editor text untouched; `setDialect` after the switch |

## Results pane

| Behaviour | Contract |
| --- | --- |
| Result sets | one per statement, stacked, `BatchIndex` = statement ordinal |
| Messages | command tags, notices (`NOTICE:` prefix, level styling of Info), errors with `SQLSTATE 42P01: ...` header line, detail and hint lines; click → caret at `Line`/`Column` (FR-014) |
| Problems | errors and Parse results; grouping unchanged |
| Transaction state | from `ExecuteQueryResult.TransactionState`; status segment above; Rollback sends `ROLLBACK;` through Execute and refreshes the state |
| Editing | `IsEditable` from the engine; read-only reason text: "Rows from more than one table", "No primary key in the result", "Result of a view or expression"; Set to NULL and invalid-cell handling unchanged; Apply builds `CrudCellDto` with `TypeName` from `ColumnSqlTypes`; failure shows the failing row's message and "not applied: edit N failed" on the others (FR-131) |
| Values | displayed exactly as received (server text output); NULL as today |

## History page

| Element | Test id | Behaviour |
| --- | --- | --- |
| Kind badge per entry | `history-kind` | "PostgreSQL" / "SQL Server" |
| Kind filter control | `history-kind-filter` | All / SQL Server / PostgreSQL → `HistorySearchRequest.Kind` |
| Search syntax | — | `kind:postgresql` / `kind:sqlserver` (and `kind:pg`, `kind:mssql` aliases) in the page's search builder |
| Restore | existing | suggests the saved connection whose kind, server and database match (FR-070) |

## AI panel

- Prompts carry the active kind (`SqlDialect.PostgreSql`); the panel's system text says
  "PostgreSQL expert" for PostgreSQL sessions.
- Before a cloud call on PostgreSQL text: `RedactRequest`; on `Ok = false` or no engine → the
  existing consent dialog states "Literals could not be removed from this script" with Send
  anyway / Cancel (FR-081).

## Not changed

Keyboard map, View menu, layout store, Settings sections other than Connections and Diagnostics,
theme tokens (the kind is shown by label and icon, not by a new colour), Export / Import envelope.
