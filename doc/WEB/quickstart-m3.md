# Quickstart â€” connect the browser to its engine

Spec 045 supersedes the original IIS-only M3/M4 installation instructions.

## One computer

1. Select **Web edition â†’ Standalone Windows service (recommended)** in setup.
   IIS is optional. Choose **Localhost** and keep the engine/web port, default 47291,
   unless another program uses it. No separate site port is needed.
2. Open the URL from the Finish page or
   `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt`, normally
   `http://localhost:47291/`.
3. A fresh browser connects to the engine serving its page automatically and reports
   **Live**. If this origin already has an active engine connection, that choice wins.
4. Open **Connect to database** in the editor to connect to SQL Server or PostgreSQL.
   The engine's Windows identity is used for Windows authentication. Start with a
   sample database and a read-only query.

For a separately hosted bundle (IIS, a dev server or files-only hosting), use
**Settings â€º Connections** to pair with the host and **Bridge port** in the summary.

## Another computer on the LAN

1. On the engine machine choose **Standalone** and **LAN exposed**. Setup installs
   the certificate and one inbound **AKML SQL Web Engine** rule on the chosen port.
2. Read the summary's `https://COMPUTERNAME:<port>/` address. Use that address on
   every computer, including the engine host. Plain LAN HTTP does not serve the app.
3. Install `%ProgramData%\AKML SQL Web\certs\bridge.cer` into the client's
   **Local Machine â†’ Trusted Root Certification Authorities**, then reload.
4. If the engine requests its current PIN, Settings â€º Connections opens with the
   page host and port prefilled. Enter the PIN supplied privately by the administrator.
   An HTTPS page's engine uses `wss` exclusively, including reconnects.

## IIS and files only

Choose **IIS** only when its enabled item reports readiness. Required parts are
W3SVC, Static Content, Default Document and HTTP Errors, enabled W3SVC/WAS services,
and working native IIS management tooling. Setup does not enable Windows features.
IIS uses a separate HTTP site port and, for LAN, an HTTPS site port; its service runs
the engine bridge only. **Don't host (files only)** still installs that engine service.

## Unattended installation

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=SERVICE /WEB_EXPOSURE=LAN /BRIDGE_PORT=47291
```

`/WEB_HOST=SERVICE|IIS|NONE` selects the host. `/WEB_PORT` and `/WEB_HTTPS_PORT`
are IIS-only flags. No host flag means standalone on a new install, the previous
host on an upgrade, or standalone fallback if the previous IIS host is unavailable.
Explicit `NONE` plus `LAN` is invalid. See [deployment](../deployment.md#web-edition).

## Switching and troubleshooting

Setup announces an address change. Browser connections and tabs at the previous
origin do not migrate; use **Settings â€º Exportâ€¦** before switching. Query history
stays with the engine. The Start-menu **Repair AKML SQL Web hosting** shortcut
repairs the recorded host. The summary and `install.log` distinguish a stopped
service, an unanswered address and an address answering with the wrong page.

LAN rules use every firewall profile, including Public; use a Private or Domain
network. On shared Windows sessions, a local user can occupy a localhost page
origin while the service is stopped. Use a trusted machine/session.
