# Set up the AKML browser workspace

AKML provides a browser workspace for SQL Server and PostgreSQL. The browser is the
editor; a connected AKML engine handles database access. The SSMS 22 extension is
the separate desktop experience for SQL Server.

## Choose and install your workspace

1. Download the current AKML installer from [Download](/download).
2. Use the installer’s web-edition setup options to install the browser application
   and its engine service. Use the address provided by setup, or the address supplied
   by the person who manages your AKML installation.
3. Open the browser application. Under **Settings → Connections**, add your engine
   using its host and port. If pairing requests a PIN, use the current PIN supplied
   by that engine's administrator or installation. Do not share pairing credentials.
4. Wait for the engine connection to report that it is live. Then open **Connect to
   database** from the editor's connection status or schema panel.

The public AKML product website is documentation and downloads; it is not itself a
connection to your databases. Database addresses are resolved from the engine machine.

## SQL Server

Select **SQL Server**, supply the server and appropriate authentication, and select
your database. Windows authentication uses the identity under which the engine runs,
which may differ from your browser's Windows user. Use **Test** to check settings,
then **Connect**. Start with a sample database and a read-only query.

For the desktop workflow, install the extension for **SQL Server Management Studio 22**
and follow [Getting started](getting-started.md).

## PostgreSQL

Select **PostgreSQL** and enter the host, port, user, password and database. Choose
the TLS mode appropriate to your server. Test the connection, then connect.

PostgreSQL must be enabled on a compatible AKML engine. Its completion, formatting
and analysis run through that engine. If the choice is disabled, read the reason
shown by AKML and check the engine version/configuration with its administrator.
PostgreSQL is available in the browser edition, not inside the AKML SSMS extension.

## What you can do next

- Browse schemas and write a query with language-specific completion.
- Format the query and review analysis findings before execution.
- Execute it and inspect the result grid. Editing is available only for eligible
  results; joined, aggregate or unkeyed results may be read-only.
- Reuse snippets and return to previous queries through History.

SQL Server formatting and analysis can run locally in the browser. Database access
and execution require the engine; PostgreSQL language services require it too.
The two database languages do not support every AKML feature identically.

## Optional AI

The core SQL workflow does not require an AI provider. If you enable AI, configure
your chosen provider and review the context shared with it. Provider service charges
may apply. See [AI assistance](ai-assistance.md) for guidance.
