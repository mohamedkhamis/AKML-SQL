# Quickstart â€” install, switch and repair Web hosting

Spec 045 supersedes the original IIS-first M4 flow. Runtime installer acceptance
must use disposable machines; see the feature verification record before release.

## Choose a host

| Choice | Address | Service startup |
| --- | --- | --- |
| Standalone Windows service (recommended) | `http://localhost:<engine-port>/`, or `https://COMPUTERNAME:<engine-port>/` for LAN | Automatic, no delay |
| IIS | Separate site URL; HTTPS site URL for LAN | Delayed automatic |
| Don't host (files only) | Supply your own static host | Delayed automatic |

IIS is enabled only when IIS 7+, W3SVC, Static Content, Default Document, HTTP Errors,
non-disabled W3SVC/WAS services and the native WebAdministration probe are ready.
The disabled choice explains the missing prerequisite. Enable it through Windows
Features yourself, then go Back and Next. Setup never installs Windows features.

## Install

Select Web edition, the host and the exposure. Standalone uses one port, 1024â€“65535,
default 47291. A listener held by another program blocks that port choice. IIS has
separate site, engine and secure-site ports; only IIS permits site port 80 and
secure-site port 443. Its ports must differ from the bridge port.

The Finish URL and `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt` give the chosen
address. A fresh engine-served browser pairs locally automatically; an HTTPS page
uses only `wss` and prefills a requested PIN. Saved active engines keep precedence.
See [browser pairing](quickstart-m3.md).

## LAN

Standalone serves one HTTPS address to all computers and adds one **AKML SQL Web
Engine** firewall rule. IIS LAN also adds **AKML SQL Web Site (HTTPS)** on its secure
site port. Localhost removes both rules. The rules apply to every profile, including
Public; use a Private or Domain network. The summary provides the PIN, thumbprint
and recommended `bridge.cer` trust steps. Keep the PIN private.

## Silent commands

```text
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=SERVICE /BRIDGE_PORT=47291
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT=8090 /WEB_HTTPS_PORT=8443 /BRIDGE_PORT=47291
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /COMPONENTS=web /WEB_HOST=NONE
```

`SERVICE|IIS|NONE` are the allowed host values. `/WEB_PORT` and `/WEB_HTTPS_PORT`
are IIS-only. Explicit `NONE` plus `LAN` is invalid. Omitted flags keep previous
ports and exposure. No host flag preserves the previous host; a new install gets
Service. If previous IIS is no longer ready, omitted host falls back to Service,
logs the reason and ignores obsolete IIS-only port flags. Explicit IIS instead stops.

## Upgrade, switch and repair

Setup preserves the previous host and ports. It shows the old and new addresses
before a host, port or standalone-exposure change. Browser connections and tabs are
per origin; export settings before moving and import at the new origin. Engine
query history is preserved. Leaving IIS removes the AKML site, its unused pool,
owned HTTPS binding, obsolete `web.config` and markers; other sites remain.

Use **Repair AKML SQL Web hosting** from the Start menu. It checks the installed
Support location and denies non-admin write permissions before elevating. It repairs
either host and verifies the page, including the engine marker for standalone.
Read the summary and `install.log` when the service does not run, the address does
not answer, or another program answers instead of AKML.

## Data and uninstall

The public bundle is `{app}\Web`. Private machine state is
`%ProgramData%\AKML SQL Web` with Administrators/SYSTEM access, separate from desktop
`%AppData%\AKML SQL`. Custom install trees require trusted ownership and protected
permissions. Interactive uninstall asks about machine data; `/VERYSILENT` keeps
machine data and desktop settings without prompts. The service, named firewall
rules, web files, AKML IIS resources and unbound AKML certificate are removed.

On a multi-session machine, another local user may occupy the localhost page
origin while the engine is stopped. Use a trusted machine/session.
