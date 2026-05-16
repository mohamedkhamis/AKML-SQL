# Contract — Installer component (M4)

This contract specifies the user-visible installer behaviour and the on-disk artefacts left after the web edition is installed.

Cross-references: spec.md FR-018–FR-023; clarification 1 (LAN TLS); M4 PRD.

---

## Component-selection page

The existing `AkmlSqlSetup.iss` component-selection page gains a new group below the plugin group:

```text
Plugins
  [X] SSMS 22
  [ ] SSMS 21
  [ ] SSMS 20
  [X] Visual Studio 2022
  [ ] Visual Studio 2026
  [ ] Visual Studio 2019

Web edition (local)
  [ ] Install web edition
       Hosting:
         (*) Host on local IIS    — recommended
         ( ) Don't host — I'll serve the files myself
       Network exposure:
         (*) Localhost only       — only my machine can browse
         ( ) LAN exposed          — other machines on my network can browse
       Port: [ 47291 ]            — engine bridge port
```

Component selection is independent: any combination of plugin and web-edition checkboxes is valid (FR-002). The page MUST default to the user's previous selection on re-run installs (FR-023).

---

## Pre-install validation

| Check | Action on failure |
|-------|-------------------|
| `Port` is in `[1024, 65535]` | Inline validation; Next button disabled until valid |
| `Port` not already bound on the host | Warn; offer to choose another port (not a hard block — could be the previous install's engine still running) |
| If "Host on local IIS" + IIS not installed | Warn and link to Windows feature install; user can proceed with "Don't host" |
| If "LAN exposed" + no public network interfaces detected | Informational note; not a block |

---

## Install steps (in order)

1. **Engine binary**: copy to `%ProgramFiles%/AKML SQL/Engine/AkmlSql.Engine.exe` (shared with IDE plugin install if already present).
2. **Web bundle**: copy WASM bundle to `%ProgramFiles%/AKML SQL/Web/`.
3. **AppData**: create `%AppData%/AKML SQL Web/` for the **invoking user** (per-user install) or for all users (machine-wide install). Write initial `config.json`.
4. **IIS site** (only if "Host on local IIS"):
   - Create or update site `AkmlSqlWeb` at the chosen URL.
   - Application physical path = `%ProgramFiles%/AKML SQL/Web/`.
   - MIME types added to the site config:
     - `.wasm` → `application/wasm`
     - `.dat` → `application/octet-stream`
     - `.blat` → `application/octet-stream`
     - `.br` → `application/octet-stream`
     - `.dll` → `application/octet-stream`
   - HTTP response headers added:
     - `Cache-Control: no-cache` on `*.json`, `*.dll`, `*.wasm` (so updates are picked up)
     - `Content-Security-Policy` per the AI key wrapping contract's allow-list
5. **TLS cert** (only if "LAN exposed"):
   - Generate self-signed RSA-2048 certificate via `New-SelfSignedCertificate -Subject "CN=AKML SQL Web Engine" -DnsName <hostname>,<hostFqdn>,<lanIp> -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2)`.
   - Export to `%ProgramData%/AKML SQL Web/certs/bridge.pfx` (engine reads), and `%ProgramData%/AKML SQL Web/certs/bridge.cer` (user-facing trust artefact).
   - Bind cert to engine's chosen port via `netsh http add sslcert ipport=0.0.0.0:<port> certhash=<thumbprint>`.
6. **Firewall** (only if "LAN exposed"):
   - `netsh advfirewall firewall add rule name="AKML SQL Web Engine" dir=in action=allow protocol=TCP localport=<port>`
7. **Engine service / launcher**:
   - If LAN exposed OR "Host on local IIS": create Windows service `AkmlSqlWebEngine` that launches the engine with `--config %AppData%/AKML SQL Web/config.json`.
   - Service runs under the **NetworkService** account by default; user may override.
8. **Pairing PIN**: only if LAN exposed: engine generates the one-time 6-digit PIN on first start; installer captures it from the engine's log and shows it on the success page.
9. **Install summary file**: write `%ProgramFiles%/AKML SQL/Web/INSTALL-SUMMARY.txt` per data-model.md E11.

---

## `config.json` written by installer

```json
{
    "transport": {
        "mode": "localhost",
        "bindAddress": "127.0.0.1",
        "port": 47291,
        "tls": {
            "enabled": false,
            "pfxPath": null,
            "thumbprint": null
        }
    },
    "pairing": {
        "tokenStorePath": "%AppData%/AKML SQL Web/tokens.json",
        "tokenTtlDays": 90,
        "rateLimitPerMinute": 5
    },
    "logs": {
        "minimumLevel": "Information",
        "directory": "%AppData%/AKML SQL Web/logs/"
    }
}
```

For LAN-exposed installs, `transport.mode = "lan"`, `bindAddress = "0.0.0.0"`, `tls.enabled = true`, with paths set to the generated artefacts.

---

## Success page

The installer's success page MUST display:

- The web edition URL (e.g. `https://hostname.local:47291/akmlsql/` or `http://localhost:47291/akmlsql/`)
- (LAN only) The pairing PIN — displayed in monospace, with a Copy button.
- (LAN only) The TLS cert thumbprint — short form (last 12 hex chars), with a "How to trust" link.
- A "Copy summary to clipboard" button that places the install-summary contents on the clipboard.
- A "Open in browser" button that launches the default browser at the URL.

---

## Re-run installer

FR-023: re-running the installer to add or remove the web edition MUST leave plugin state untouched.

Behaviour:

- Re-running with the SAME selection → no-op for plugin component; web component verifies all artefacts exist (idempotent).
- Re-running with a CHANGED selection (e.g. switch localhost → LAN) → migrate config: generate cert if missing, add firewall rule if missing, regenerate PIN, update `config.json`. **Existing bearer tokens are preserved** so previously paired browsers stay paired.
- Re-running with UNCHECKED web component → uninstall path (see below).

---

## Uninstall

When the web component is unchecked OR the whole product is uninstalled:

1. Stop and remove `AkmlSqlWebEngine` Windows service.
2. Remove `netsh http sslcert` binding (LAN only).
3. Remove firewall rule (LAN only).
4. Remove IIS site (`AkmlSqlWeb`) — preserve any user-added bindings if non-default.
5. Delete `%ProgramFiles%/AKML SQL/Web/`.
6. Delete `%AppData%/AKML SQL Web/` after a Yes/No prompt: *"Also delete settings, tokens, and engine logs for the web edition?"* — default: No.
7. Plugin install and `%AppData%/AKML SQL/` MUST be untouched.

SC-007 verification: a smoke test diffs `%AppData%/AKML SQL/` before and after the web install/uninstall cycle and asserts no change.

---

## Silent install

The existing silent flags extend with web-edition flags:

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=22,2022,WEB
                 /WEB_HOST=IIS         (or NONE)
                 /WEB_EXPOSURE=LOCALHOST (or LAN)
                 /WEB_PORT=47291
                 /NOUPDATE
```

Defaults match the interactive defaults. Invalid combinations (`/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN`) error out.

---

## Test obligations (Installer.Tests)

- Component-selection page: `{plugins only, web only, both, neither}` cross-product; each produces a valid uninstall path.
- Re-run with changed selection: localhost → LAN, then LAN → localhost; bearer tokens preserved; firewall rule and cert added then removed.
- Uninstall preserves `%AppData%/AKML SQL/` (SC-007).
- Silent install with all four combinations of `/WEB_HOST` and `/WEB_EXPOSURE`.
- IIS-not-installed path: warn shown, user can complete install with "Don't host".
