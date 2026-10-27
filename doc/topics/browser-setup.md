# Set up the AKML browser workspace

AKML provides a browser workspace for SQL Server and PostgreSQL. The browser is the
editor; a connected AKML engine handles database access. The SSMS 22 extension is
the separate desktop experience for SQL Server.

## Choose and install your workspace

1. Download the current AKML installer from [Download](/download).
2. Select **Web edition → Standalone Windows service (recommended)**. It needs no
   IIS installation. Localhost uses one address, normally `http://localhost:47291/`;
   use the actual URL provided by setup or your installation's administrator.
3. Open that address. A fresh browser connects automatically to the engine serving
   its page. An already active saved engine remains your choice. If pairing requests
   a PIN, Settings → Connections prefills this engine's address. Use the current PIN
   supplied by the administrator; do not share pairing credentials.
4. Wait for the engine connection to report that it is live. Then open **Connect to
   database** from the editor's connection status or schema panel.

The public AKML product website is documentation and downloads; it is not itself a
connection to your databases. Database addresses are resolved from the engine machine.

## Hosting and network choices

**IIS** is optional. Setup enables its choice only when IIS and its required HTTP
features, services and management tools are ready. It explains a disabled choice
and does not enable Windows features for you. **Don't host (files only)** installs
the files and engine service for a static host managed separately. With IIS or a
separately hosted app, add the engine under Settings → Connections when needed.

For **LAN standalone**, use the summary's `https://COMPUTERNAME:<port>/` address on
every computer, including the engine host. Follow its recommended `bridge.cer`
trust steps before pairing. The page uses secure `wss` connections. Standalone LAN
adds one engine firewall rule; IIS LAN also has a secure-site rule. Rules apply to
every profile, including Public; use a Private or Domain network.

Changing host, port or standalone network exposure can change the browser address.
Setup shows both addresses. Connections and query tabs at the old address do not
appear at the new one. Export settings before switching and import at the new
address; query history is kept by the engine. **Repair AKML SQL Web hosting** in the
Start menu repairs the selected host if its address does not answer.

For managed installation, `/WEB_HOST=SERVICE|IIS|NONE` selects the host;
`/BRIDGE_PORT` sets the engine port. `/WEB_PORT` and `/WEB_HTTPS_PORT` are IIS-only.
An unattended install also requires `/VERYSILENT /ACCEPTEULA`. Without a host flag,
a new install uses standalone and an upgrade preserves its previous host, with
standalone fallback if the previous IIS host is unavailable.

On a machine shared by multiple Windows sessions, another local user can occupy
the localhost page address while the engine is stopped. Use a trusted machine/session.

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
