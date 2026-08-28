# Connecting to SQL Server

AKML SQL works with the connection your query window already uses. Connect in SSMS or Visual Studio the way you normally do, and AKML SQL picks up the active connection, loads the database schema, and starts offering completions, hover info, and schema-aware features.

## Connect a query window

1. Open a new query window in SSMS or VS.
2. Use the host's normal connection dialog to pick a server and database.
3. Once connected, AKML SQL loads the schema in the background. A small progress indicator shows while objects are loading.

If the schema does not load, see [Troubleshooting](troubleshooting.md).

## Windows Authentication vs SQL authentication

- **Windows Authentication**: nothing extra to do. AKML SQL uses your Windows identity through the existing connection.
- **SQL authentication**: when AKML SQL detects a SQL login connection, it may ask for the login and password the first time. Enter them in the credential dialog. AKML SQL validates the credential against the server before saving it.

Once a credential is saved, other query windows to the same server and login pick it up automatically — no re-typing.

## Where saved credentials live

Saved SQL-auth credentials are stored at:

```
%AppData%\AKML SQL\sql-credentials.json
```

- Passwords are encrypted with Windows DPAPI, scoped to your Windows user account. The file never contains plaintext passwords.
- Entries are stored per server and login.
- Passwords are never written to log files.
- The credential dialog offers a "Clear saved password" option if a password changes.
- You can turn credential saving off in Options under the IntelliSense section.

If a saved password stops working (for example, the server password changed), AKML SQL asks you to re-enter it and updates the stored credential.

## Test a connection

The credential dialog tests the login and password against the server before storing them. If the test fails, nothing is saved and you can correct the details immediately.

## Permissions the schema loader needs

For IntelliSense and schema-aware features to work fully, the connecting login needs `VIEW DATABASE STATE` and `VIEW ANY DEFINITION` on the database. Without them, schema loading fails and completion falls back to keywords only.

## Refresh the schema after changes

AKML SQL detects `CREATE`, `ALTER`, and `DROP` statements you run and refreshes its schema view automatically. To force a refresh, use **Tools** -> **AKML SQL** -> **Refresh Schema Cache**.

Next: [IntelliSense](intellisense.md) and [AI Assistance](ai-assistance.md).
