# Feature Specification: Web edition hosting without IIS — a standalone Windows service by default

**Feature Branch**: `045-web-standalone-hosting`
**Created**: 2026-10-26
**Status**: Draft
**Input**: User description: "please i want to do big change in installer it current install akml web into IIS and some machines did not enabled it i want to check this first ( to disable install into IIS if it did not enabled )  also show install as windows ervice stand alone and default running insteated of IIS   , and if i have IIS allow me to choise   , and if you have questions let me know"

## Overview

Setup puts the web edition's pages on IIS. Many machines do not have IIS enabled. On those
machines the web edition is installed but cannot be opened: setup either asks to turn IIS on
(a change to Windows features that takes up to a minute and can fail) or leaves the files on
disk for the user to serve.

This feature makes the web edition work as soon as setup finishes, on any Windows machine:

- **Standalone by default.** Setup installs the web edition as a Windows service that serves
  its own pages at the engine's own address. One address is both the web edition and its
  engine: by default `http://localhost:47291/`. The service starts with Windows and needs no
  IIS. This is the default choice on every machine.
- **IIS only when it can work.** Before the hosting choice is shown, setup checks whether IIS
  is enabled and has everything needed to create the AKML SQL site. If it does, the user can
  choose IIS instead. If it does not, the IIS choice is shown but disabled, with the reason and
  how to turn IIS on. Setup never turns IIS on itself.
- **The same web edition on either host.** Standalone or IIS, the web edition behaves the same
  way: deep links, security protections, LAN access over HTTPS with a firewall rule, install
  summary, repair and uninstall.
- **Upgrades keep what works.** An existing install keeps its host and its address. Switching
  host is a deliberate choice, made by running setup again.

The SSMS 22 part of setup does not change.

## Today

Facts about the current installer and web edition that this spec builds on (read from the
`043-web-query-tabs-polish` branch on 2026-10-26):

- The web edition component has two sub-components, "Host on local IIS (recommended)" and
  "Install Windows service for the engine". Both are ticked by default.
- A "Web edition hosting" page offers two choices: "Host on local IIS (recommended)", which is
  preselected, and "Don't host — I'll serve the files myself".
- If IIS is chosen and is not installed, clicking Next opens a dialog with three answers: enable
  IIS now (turns the Windows feature on, up to a minute), switch to "Don't host", or cancel
  setup.
- Setup decides that IIS is installed from IIS's presence alone. A machine can have IIS but not
  IIS's management scripting tools. It passes that check, but the site is not created at the
  end of setup. The user finds out from a warning in the install summary and from a dialog that
  points to the "Repair AKML SQL Web hosting" Start-menu shortcut.
- The choice on the hosting page does not decide on its own whether IIS is set up. The IIS step
  follows the "Host on local IIS" component tick. If that component is still ticked, choosing
  "Don't host" on the page (or passing `/WEB_HOST=NONE` to a silent install) can still create
  the IIS site. This comes from reading setup's script and must be confirmed during planning.
- The "AKML SQL Web Engine" Windows service runs the engine that the browser talks to, on the
  engine port (default 47291). It does **not** serve the web edition's pages. Opening the
  engine's address in a browser shows only a short page that says it is the connection point.
  **So serving the pages from a Windows service is a new product capability, not only a change
  to setup's pages.**
- In LAN mode, the engine's address uses HTTPS for every computer, including this one. Its
  self-signed certificate covers this computer's host names, its network addresses and
  `localhost`. Setup does not make any computer trust the certificate. A browser accepts it
  once, by opening the engine's address, and the engine's short page explains that step.
- IIS hosting does more than put the files on a site. It also sets up behaviour that the web
  edition depends on:
  - the correct file types for the files the browser runtime loads;
  - no caching of the app's files, so the next refresh after a reinstall loads the new version;
  - the content security policy from the AI-key threat model, which lists the addresses the
    page may connect to (the engine on this computer or another one, the supported AI
    providers, and local AI runtimes);
  - deep links: reloading any page of the app (editor, settings, history and so on) or
    opening a bookmark to one loads the app instead of "not found";
  - in LAN mode, an HTTPS address for other computers that uses the engine's certificate. Review
    WEB-03 requires this: the page that handles pairing tokens and AI keys must arrive over TLS,
    and the browser stores keys only on a secure page.
- Ports: the site port is 80 (`http://localhost/`), the engine port is 47291, and the secure
  site port is 443 (LAN mode only). Setup remembers the ports and the localhost or LAN choice,
  and offers them again the next time it runs.
- In LAN mode, setup adds two Windows Firewall rules: one for the engine port and one for the
  secure site port. In localhost mode it adds none.
- Silent installs take `/WEB_HOST=IIS|NONE` (IIS when the flag is left out),
  `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT`, `/BRIDGE_PORT` and `/WEB_HTTPS_PORT`. A silent
  install that would host on IIS when IIS is not installed is stopped. The installer smoke
  tests pass `/WEB_HOST=IIS` explicitly.
- The Finish page's "Open AKML SQL Web" link appears only when IIS hosts the web edition.
- The browser keeps the web edition's saved connections, settings, query history and open
  query tabs separately for each address (scheme, host and port). If the web edition is opened
  at a different address, none of them are there. Settings can be exported and imported. Saved
  connections cannot.
- The owner's machine runs the web edition on IIS on a non-default port
  (`http://localhost:59416/`).
- Uninstall removes the IIS site, the HTTPS binding that setup created, the firewall rules and
  the service. It asks before it deletes the web edition's data.

## Clarifications

### Session 2026-10-26

- Q: Where does the standalone service serve the pages: on a separate site port as today, or at
  one address shared with the engine? → A: At one address shared with the engine, on the engine
  port (default 47291, so `http://localhost:47291/`). Setup also adds a Windows Firewall allow
  rule for that port, so that other computers can reach it in LAN mode.
- Q: Which host does an upgrade of an existing IIS install preselect? → A: The host the machine
  already uses. IIS installs stay on IIS until the user picks standalone.
- Q: When IIS is not enabled, does setup still offer to enable it? → A: No. The IIS choice is
  shown disabled, with the reason and how to turn IIS on in Windows Features. Setup never
  changes Windows features.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Install on a machine without IIS and use the web edition right away (Priority: P1)

A user runs setup on a Windows machine where IIS has never been enabled and keeps the
defaults. Setup installs the web edition as a Windows service. At the end, the Finish page
opens the web edition in the browser at `http://localhost:47291/`. Setup does not ask about
IIS, does not try to use it, and does not report it as a problem.

**Why this priority**: This is the failure the user hit. The web edition must work on machines
where IIS is not enabled, and that is most desktop machines.

**Independent Test**: On a clean machine or VM without IIS, run setup with the defaults. Open
the address from the Finish page: the web edition loads and connects to its engine without an
engine address being typed. Restart the machine: without anyone starting anything by hand, the
address answers again.

**Acceptance Scenarios**:

1. **Given** a machine without IIS, **When** the user reaches the hosting page, **Then**
   "Standalone Windows service" is selected and the IIS choice is disabled, with the reason
   "IIS is not enabled on this computer" and how to turn it on.
2. **Given** standalone hosting, **When** the user reaches the ports page, **Then** setup asks
   only for the web edition's port (default 47291). The site port and the secure site port
   are not shown, because only IIS uses them.
3. **Given** the default choices, **When** setup finishes, **Then** the Finish page offers
   "Open AKML SQL Web" with the standalone address, and the install summary shows the same
   address with no IIS warnings.
4. **Given** a standalone install, **When** the user opens the address, **Then** the web
   edition loads and connects to the engine at that same address. The user does not enter an
   engine address.
5. **Given** a standalone install, **When** the user reloads any page of the app or opens a
   bookmark to one, **Then** that page loads.
6. **Given** the computer restarts, **When** Windows has started, **Then** the web edition
   answers at its address before anyone signs in, and nobody has to start it.
7. **Given** the web edition's service stops unexpectedly, **Then** it is started again
   automatically.
8. **Given** setup has finished, **Then** setup has made no IIS site, no IIS configuration and
   no change to Windows features. The install log records the result of the IIS check and
   that IIS was not used.
9. **Given** a standalone install, **When** anyone requests something other than the web
   edition's own files (a folder listing, the web edition's state such as the PIN, tokens,
   certificates or configuration, or any file outside the web edition's folder), **Then**
   nothing is returned.

---

### User Story 2 — Choose IIS on a machine where IIS is ready (Priority: P2)

A user's machine has IIS enabled, for example a server that already hosts other sites. The
user wants the web edition on IIS next to them. Setup offers both choices with standalone
preselected. The user picks IIS and gets the IIS-hosted web edition as it works today.

**Why this priority**: IIS hosting is how setup works today, and some users prefer to manage
their sites in IIS. It must stay available where it can work, but it is no longer the default.

**Independent Test**: On a machine with IIS ready, run setup and pick IIS: the site exists in
IIS and the web edition loads at its address. On a machine that has IIS but is missing
something setup needs, the IIS choice is disabled and names the missing part.

**Acceptance Scenarios**:

1. **Given** IIS is enabled with everything setup needs, **When** the hosting page appears,
   **Then** both choices are enabled and "Standalone Windows service" is preselected.
2. **Given** the user selects IIS, **When** setup finishes, **Then** IIS serves the web edition
   at the chosen site address and behaves as it does today. The Windows service runs the engine
   only, and the engine's address still shows its short page.
3. **Given** IIS is enabled but lacks something setup needs to create the site (for example,
   IIS's management scripting tools), **When** the hosting page appears, **Then** the IIS
   choice is disabled, and the reason names the missing part and where to turn it on.
4. **Given** IIS is not enabled, **When** the hosting page appears, **Then** the IIS choice is
   disabled, and the reason says how to turn IIS on in Windows Features and that the user can
   choose IIS afterwards. Setup offers no way to turn IIS on itself.
5. **Given** the IIS choice is disabled, **Then** setup never tries to use IIS: no dialog
   about it, no change to Windows features, and no site step.

---

### User Story 3 — Upgrade or re-run setup without losing a working web edition (Priority: P3)

A user already has the web edition, hosted on IIS, standalone or not hosted. They run a newer
setup, either directly or from the update notification. Setup keeps their host, so the web
edition keeps working at the same address and the data their browser saved is still there. To
change host, they run setup again and pick the other one. Setup tells them the address will
change, moves the web edition over, and removes what the old host left behind.

**Why this priority**: Existing installs must not break on update, including the owner's IIS
install on a non-default port. If the address changes, the browser's saved connections,
settings, history and tabs disappear without any message.

**Independent Test**: Install on IIS on port X, use it, and save a connection. Upgrade: IIS is
preselected, and the address and the saved connection still work. Re-run setup and choose
standalone: before installing, setup shows the old and new addresses and warns about browser
data. Afterwards, only the standalone service serves the web edition and the IIS site is gone.

**Acceptance Scenarios**:

1. **Given** an existing install, **When** the user upgrades, **Then** the hosting page
   preselects the host the machine already uses, and keeps its ports and its localhost or LAN
   choice.
2. **Given** an upgrade that keeps the same host, **When** setup finishes, **Then** the address
   is unchanged and the browser's saved data at that address still opens.
3. **Given** an IIS install, **When** the user chooses standalone, **Then** before installing
   setup shows the current and new addresses. It says that the connections, history and tabs
   saved in the browser at the current address will not appear at the new one, and that
   settings can be exported first.
4. **Given** the user goes ahead with switching from IIS to standalone, **When** setup finishes,
   **Then** the AKML SQL site and its firewall rule are gone from IIS and Windows Firewall. No
   other IIS site, no IIS setting and not the IIS feature itself has been changed. The
   standalone address answers.
5. **Given** the user switches from standalone to IIS, **When** setup finishes, **Then** the
   same address warning was shown first, IIS serves the web edition, and the service runs the
   engine only.
6. **Given** the previous install used IIS and IIS is no longer ready, **When** setup runs,
   **Then** the IIS choice is disabled with its reason, standalone is preselected, and the page
   says that the web edition will move to the standalone service at a new address.
7. **Given** the update notification starts setup, **Then** the same rules apply.

---

### User Story 4 — Reach the web edition from other computers in LAN mode, on either host (Priority: P4)

A user chooses LAN exposure with standalone hosting. Other computers on the network open the
web edition at `https://<this computer>:47291/`, trust the certificate once, pair with the PIN
and work, just as they can with IIS today. Setup adds the firewall rule that lets them in.

**Why this priority**: LAN use is a documented mode. The standalone host must keep its
protections, including serving the pages over TLS.

**Independent Test**: Install standalone in LAN mode. Check that Windows Firewall has one
AKML SQL Web allow rule for the web edition's port. On a second machine, open the HTTPS address
from the install summary, trust the certificate, pair and run a query.

**Acceptance Scenarios**:

1. **Given** LAN exposure and standalone hosting, **When** setup finishes, **Then** the summary
   shows one HTTPS address that every computer uses, this one included, with how to trust the
   certificate. Windows Firewall has one inbound allow rule for that port, and no other rule
   for the web edition.
2. **Given** LAN mode, **When** another computer asks for the web edition over plain HTTP,
   **Then** it does not get the app.
3. **Given** another computer opens the standalone address, **When** the web edition loads,
   **Then** its default engine is the engine at that address. The user pairs with the PIN as
   today, without typing an engine address.
4. **Given** localhost exposure, **Then** no firewall rule is added, and other computers can
   reach neither the pages nor the engine.
5. **Given** LAN exposure on IIS, **Then** the firewall rules and the secure site port behave
   as they do today.

---

### User Story 5 — Silent installs choose the host explicitly (Priority: P5)

An administrator installs with a silent command line. They can choose standalone, IIS or no
hosting. Without a choice, a new install gets standalone and an upgrade keeps the host it has.
If they ask for something that cannot work, such as IIS when IIS is not ready, or IIS-only
ports with another host, setup stops before it changes anything and writes the reason to its
log.

**Why this priority**: Scripted deployments and the automated installer tests depend on these
flags. Changing the default must be explicit and documented.

**Independent Test**: Run the silent installer with each `/WEB_HOST` value on a machine with IIS
and on one without. Check the result and the log.

**Acceptance Scenarios**:

1. **Given** `/WEB_HOST=SERVICE`, **Then** the web edition is hosted standalone on the
   `/BRIDGE_PORT` port (default 47291).
2. **Given** no `/WEB_HOST` on a new machine, **Then** the web edition is hosted standalone.
   **Given** no `/WEB_HOST` on an upgrade, **Then** it keeps the host it had.
3. **Given** `/WEB_HOST=IIS` and IIS is ready, **Then** IIS hosts the web edition. **Given**
   `/WEB_HOST=IIS` and IIS is not ready, **Then** setup stops before any change, and the log
   names what is missing.
4. **Given** `/WEB_HOST=NONE`, **Then** only the files are installed. No IIS site is created
   and no service serves the pages.
5. **Given** `/WEB_PORT` or `/WEB_HTTPS_PORT` while the host is not IIS, **Then** setup stops
   before any change, and the log says these ports apply to IIS hosting only.
6. **Given** the existing checks, **Then** they still apply: the ports must differ, and `NONE`
   with `LAN` is still rejected.

---

### User Story 6 — Repair and uninstall work for either host (Priority: P6)

A user whose web edition stopped answering runs "Repair AKML SQL Web hosting" from the Start
menu, and the host they installed is restored. A user who uninstalls gets back a machine with
nothing left of the host they used.

**Why this priority**: Both actions exist today for IIS only. Standalone hosting needs them
too, but they matter less often than installing and upgrading.

**Independent Test**: For each host, break the hosting (stop and disable the service, delete
the firewall rule, or delete the IIS site), run the repair shortcut and check that the address
answers again. Then uninstall and check that nothing remains.

**Acceptance Scenarios**:

1. **Given** a standalone install, **When** the user runs the repair shortcut, **Then** the
   standalone host is restored with its address and its startup with Windows. In LAN mode its
   HTTPS address and its firewall rule are restored too.
2. **Given** an IIS install, **When** the user runs the repair shortcut, **Then** the site is
   recreated as it is today.
3. **Given** either host, **When** the user uninstalls, **Then** everything setup set up for
   that host is removed, including the firewall rules. Other programs' settings and other IIS
   sites are left alone. Setup still asks before it deletes the web edition's data.
4. **Given** IIS was removed from the machine after an IIS install, **When** the user
   uninstalls, **Then** uninstall finishes without an error.

---

### Edge Cases

- **IIS is turned on while setup is open.** Setup checks IIS readiness again each time the
  hosting page is shown, so going Back and then Next picks up the change.
- **IIS is enabled, but its web service is disabled.** IIS is not ready. The reason says the
  IIS web service is disabled.
- **IIS is enabled and its web service is stopped but not disabled.** IIS is ready. If the site
  does not answer after install, the summary says so and points to the repair shortcut.
- **Another program already uses the web edition's port.** Setup says which port is taken and
  asks for another; a silent setup stops. A port held by AKML SQL's own previous install is not
  a conflict. Setup never takes over or shares another program's address.
- **The port was free during the wizard but is taken by the time the service starts.** The
  summary says that the address does not answer and why, and points to the repair shortcut.
- **The service does not start.** In standalone mode neither the pages nor the engine are
  available. The summary says the address will not answer until the service runs, and how to
  start it.
- **This computer in LAN mode.** This computer also opens the HTTPS address. Its browser sees
  the certificate warning once, as other computers do, unless the certificate is trusted. The
  summary explains how to trust it.
- **Switching host always changes the address**, because the two hosts use different ports.
  Bookmarks to the old address stop working. The hosting page warns before installing, and the
  summary gives the new address.
- **The web edition's port is changed on a re-run.** This is an address change too, with the
  same warning. The firewall rule moves to the new port.
- **The previous install used "Don't host".** If the new setup recorded that choice, it stays
  that way unless the user picks a host.
- **An install from before this feature, on a machine without a working IIS site.** No hosting
  choice was recorded, so setup preselects standalone. If the install was set up for IIS but
  IIS is not ready, FR-031 applies. Either way, the machines where the web edition could never
  be opened get a working host when they upgrade.
- **The user unticks the web edition.** No hosting pages appear and nothing is hosted, as
  today. An upgrade that leaves the web edition out keeps a running service running, as today.
- **A silent upgrade where the previous host was IIS and IIS has since been removed.** Setup
  uses standalone and writes the reason and the new address to its log; it does not stop. Only
  an explicit `/WEB_HOST=IIS` stops when IIS is not ready.
- **Uninstalling after IIS was removed from the machine.** Uninstall skips the IIS steps and
  finishes normally.

## Requirements *(mandatory)*

### Functional Requirements

**Hosting choice**

- **FR-001**: Setup MUST offer three hosting choices for the web edition: "Standalone Windows
  service", "IIS" and "Don't host (files only)".
- **FR-002**: "Standalone Windows service" MUST be preselected on a new install on every
  machine, whether or not IIS is ready.
- **FR-003**: The hosting choice alone MUST decide what setup sets up. No other control in setup
  (for example, a component tick) may set up IIS while another choice is selected, or allow a
  combination that cannot work, such as standalone hosting without the service that serves it.
- **FR-004**: Before the hosting choice is shown, setup MUST check whether IIS is ready. IIS is
  ready only when the IIS web server is enabled, its web service is not disabled, and everything
  setup needs to create and configure the AKML SQL site is present.
- **FR-005**: When IIS is not ready, the IIS choice MUST be visible but disabled. A one-line
  reason MUST name what is missing (IIS is not enabled, a named part of IIS is missing, or the
  IIS web service is disabled) and where to turn it on.
- **FR-006**: Setup MUST NOT offer to turn IIS on, and MUST NOT change Windows features. The
  reason from FR-005 MUST tell the user how to turn IIS on in Windows Features, and that they
  can choose IIS afterwards by going Back and then Next, or by running setup again.
- **FR-007**: Setup MUST check IIS readiness again each time the hosting page is shown.
- **FR-008**: Setup MUST write to its log the result of the IIS check with its reason, and the
  hosting choice that was used.

**Standalone host**

- **FR-010**: The standalone choice MUST serve the web edition's pages from a Windows service
  that needs neither IIS nor any other web server.
- **FR-011**: The service MUST start when Windows starts, without anyone signing in. It MUST be
  started again automatically after a failure, as the engine service is today.
- **FR-012**: The standalone host MUST serve the web edition with the same behaviour as IIS
  hosting:
  - every file the browser runtime loads arrives with its correct file type;
  - the app's files are not cached, so the next refresh after an upgrade loads the new version;
  - the same content security policy applies, and it still allows engines on other computers,
    the supported AI providers and local AI runtimes;
  - every route of the app loads when it is opened directly or reloaded.
- **FR-013**: The standalone host MUST serve the pages at the engine's own address. One port
  (the engine port, default 47291) carries both the pages and the engine, so a localhost
  install opens at `http://localhost:47291/`. For standalone hosting, setup MUST ask only for
  that port. The site port and the secure site port belong to IIS hosting.
- **FR-014**: When the web edition is opened from the standalone address, its default engine
  MUST be the engine at that same address, so that nobody, on this computer or another, has to
  type an engine address. The user MUST still be able to connect to a different engine.
  Pairing rules for other computers do not change.
- **FR-015**: Before installing, setup MUST check that the web edition's port is free, or is
  held by AKML SQL's own previous install. If another program holds it, an interactive setup
  MUST say which port is taken and require a different one, and a silent setup MUST stop. Setup
  MUST NOT take over or share another program's address.
- **FR-016**: In localhost mode, the standalone address MUST answer only on this computer.
- **FR-017**: In LAN mode, the standalone address MUST use HTTPS for every computer, including
  this one, with the engine's certificate. A plain HTTP request from another computer MUST NOT
  get the app.
- **FR-018**: In LAN mode with standalone hosting, setup MUST add one inbound Windows Firewall
  allow rule, named for AKML SQL Web, for the web edition's port, and MUST NOT open any other
  port for the web edition. Setup MUST update or remove that rule when the port changes, the
  user switches to localhost or to another host, or the user uninstalls. In localhost mode no
  rule is added, because the address answers only on this computer.
- **FR-019**: The standalone host MUST serve only the installed web edition's files, read-only.
  It MUST NOT list folders, MUST NOT serve files outside the web edition's folder, and MUST NOT
  expose the web edition's state (pairing PIN, tokens, certificates, configuration).
- **FR-020**: The standalone host MUST NOT run with more privilege than the engine service has
  today. The files it serves MUST NOT be writable by standard users.

**IIS host**

- **FR-021**: When IIS is chosen and ready, setup MUST host the web edition on IIS as it does
  today: the site and its ports, file types, security policy, deep links, HTTPS and firewall
  rules for LAN mode, and the repair shortcut.
- **FR-022**: Only one host MUST serve the web edition's pages at a time. When IIS is chosen,
  the Windows service MUST NOT also serve them; the engine's address keeps its short page.

**Upgrade and switching hosts**

- **FR-030**: On an upgrade or a re-run, setup MUST preselect the host the machine already uses.
  An IIS install stays on IIS, a standalone install stays standalone, and a "Don't host" install
  stays that way unless the user picks a host. Ports and the localhost or LAN choice MUST be
  kept, as today.
- **FR-030a**: Installs made before this feature did not record a hosting choice. On their
  first upgrade, setup MUST work out the previous host as follows. An install that was set up
  for IIS hosting counts as IIS, and FR-031 applies if IIS is not ready. Any other install
  counts as having no recorded choice, and standalone is preselected. Only a "Don't host" choice
  made with the new setup counts as deliberate and is kept by FR-034. This means the machines
  where the web edition could never be opened get a working host when they upgrade.
- **FR-031**: If the previous host was IIS and IIS is no longer ready, setup MUST preselect
  standalone. The hosting page MUST say that the web edition will move to the standalone
  service at a new address, and why.
- **FR-032**: After a switch, exactly one host MUST serve the web edition. Setup MUST remove
  only what the previous host set up for AKML SQL: for IIS, its site, the HTTPS binding setup
  created and its firewall rules; for standalone, its registrations with Windows and its
  firewall rule. Setup MUST NOT change other IIS sites, IIS's own settings or the IIS feature.
- **FR-033**: The two hosts use different addresses, so switching host changes the address.
  Before installing, the hosting page MUST show the current and new addresses. It MUST say that
  the connections, history and tabs saved in the browser at the current address will not appear
  at the new one, and that settings can be exported first and imported afterwards. The install
  summary MUST repeat the new address.
- **FR-034**: An install whose recorded choice is "Don't host" MUST stay that way on upgrade
  unless the user picks a host (see FR-030a for installs from before this feature).
- **FR-035**: Changing the web edition's port on a re-run MUST give the same address-change
  warning as FR-033.

**Silent installs**

- **FR-040**: `/WEB_HOST` MUST accept `SERVICE`, `IIS` and `NONE`.
- **FR-041**: Without `/WEB_HOST`, a new install MUST use standalone and an upgrade MUST keep
  the host it has. If the kept host is IIS and IIS is no longer ready, setup MUST use standalone
  and write the reason and the new address to its log.
- **FR-042**: `/WEB_HOST=IIS` when IIS is not ready MUST stop setup before any change, and the
  log MUST name what is missing, as today.
- **FR-043**: `/WEB_HOST=NONE` MUST set up no host at all.
- **FR-044**: `/WEB_EXPOSURE` and `/BRIDGE_PORT` MUST keep their meaning and checks for both
  hosts. With standalone hosting, `/BRIDGE_PORT` sets the port of the one shared address.
  `/WEB_PORT` and `/WEB_HTTPS_PORT` apply to IIS hosting only. If either is passed and the host
  is not IIS, setup MUST stop before any change, and the log MUST say that they apply to IIS
  hosting only. There is one exception. If the FR-041 fallback moved a kept IIS host to
  standalone, setup MUST ignore these ports and say so in the log, so that unattended updates
  are not blocked.

**What setup tells the user**

- **FR-050**: The install summary (the wizard page and the saved file) and the Finish page's
  "Open AKML SQL Web" link MUST show the address of whichever host serves the web edition,
  standalone included. In LAN mode the summary MUST also say how to trust the certificate.
- **FR-051**: Setup MUST check that the address answers before it shows the address as working.
  If the address does not answer within 30 seconds of the host starting, the summary MUST say
  so, give the reason when it is known, and say how to repair it.
- **FR-052**: The summary MUST name the hosting choice that was used: "Standalone Windows
  service", "IIS" or "Not hosted".

**Repair and uninstall**

- **FR-060**: The "Repair AKML SQL Web hosting" Start-menu shortcut MUST be installed for both
  standalone and IIS hosting. It MUST restore whichever host is installed, including its
  firewall rule in LAN mode, without asking for any settings.
- **FR-061**: Uninstall MUST remove everything setup set up for the host it installed,
  including the firewall rules. It MUST finish without an error when IIS was removed after
  install.
- **FR-062**: Uninstall MUST still ask before it deletes the web edition's data, and MUST NOT
  touch the SSMS extension's per-user data, as today.

**Documentation and scope**

- **FR-070**: The deployment guide, the web edition installer quickstart and the public docs
  MUST describe the three hosting choices, when IIS counts as ready, the standalone address and
  its firewall rule, the silent flag values, and how switching host changes the address.
- **FR-071**: The SSMS 22 part of setup, how the engine works with the browser, and the web
  edition's features MUST NOT change, except where the standalone host needs it (FR-013,
  FR-014).

### Key Entities

- **Hosting choice**: Standalone Windows service, IIS or Not hosted. Setup keeps it on the
  machine and reads it on the next run, for repair and for uninstall.
- **IIS readiness**: Ready, or Not ready with a reason (IIS not enabled, a named part missing,
  or the IIS web service disabled). Worked out each time the hosting page is shown, and for
  silent installs.
- **Web edition address**: The scheme, host and port the browser opens. For standalone hosting
  it is the engine's own address; for IIS it is the site's. The browser keeps the user's saved
  data under this address, so changing it costs the user that data.
- **Web hosting settings**: The hosting choice, the engine port, and for IIS the site port and
  the secure site port, plus localhost or LAN exposure. Kept on the machine for the next setup
  run, repair and uninstall.
- **Firewall rule**: The inbound allow rule named for AKML SQL Web that lets other computers
  reach the web edition in LAN mode. It is owned by setup, follows the port, and is removed on
  uninstall.
- **Install summary**: What setup tells the user at the end, on the wizard page and in a saved
  file: the host, the address, the PIN and certificate steps for LAN mode, and every warning.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On machines without IIS, every default interactive install ends with the web
  edition loading from the Finish page link, with no prompt, warning or error about IIS.
  Checked on at least one clean Windows 11 machine and one Windows Server machine without IIS.
- **SC-002**: On a machine without IIS, a user can start setup and be using the web edition in
  under 3 minutes, not counting the download, without typing an address or an engine address.
- **SC-003**: On machines without IIS, setup makes no IIS changes at all: no Windows feature
  change and no site. Checked against the install log and the machine's state.
- **SC-004**: After a restart, the web edition answers at its address within 3 minutes of
  Windows starting, without anyone signing in.
- **SC-005**: On both hosts, every page reachable from the app's navigation loads when it is
  reloaded or opened directly.
- **SC-006**: For the same web edition files, the standalone host and IIS apply the same
  security policy and caching behaviour, and every file the app loads succeeds (the browser
  console shows no failed loads). Checked by comparing the two hosts.
- **SC-007**: Upgrading an existing IIS install, including one on a non-default port, keeps IIS,
  the address and the browser's saved connections, with no extra step for the user.
- **SC-008**: In every switch of host, the address change is shown before anything is
  installed. Afterwards exactly one host serves the web edition, and nothing set up for the
  previous host remains, firewall rules included.
- **SC-009**: In LAN mode, another computer pairs and runs a query over HTTPS on both hosts, and
  no app page reaches another computer over plain HTTP. With standalone hosting there is exactly
  one AKML SQL Web firewall rule and one certificate to trust.
- **SC-010**: Every documented `/WEB_HOST` value gives its documented result, and every invalid
  command line stops before any change with the reason in the log. The installer's automated
  smoke tests cover this.
- **SC-011**: Across the acceptance runs, the install summary never shows an address that does
  not answer without also showing a warning.
- **SC-012**: Upgrading an install from before this feature on a machine where the web edition
  could not be opened (no IIS, or IIS without what setup needs) ends with the web edition
  answering at its standalone address.
- **SC-013**: With standalone hosting, no request returns a folder listing, any of the web
  edition's state, or a file outside the web edition's folder. Checked with a fixed list of
  such requests.

## Assumptions

- "Don't host" stays as a third choice for users who serve the files themselves, and for
  existing silent scripts that pass `/WEB_HOST=NONE`.
- Planning decides whether the engine's existing Windows service serves the pages or a second
  AKML SQL service does. The address is shared either way (FR-013). The service starts with
  Windows, restarts after failures, appears in Windows Services under an AKML SQL name, and
  cannot be left out when standalone is chosen (FR-003).
- The firewall rule is added in LAN mode only. This reads the answer "also add firewall allow
  rule" against FR-016: in localhost mode the address answers only on this computer, so a rule
  would open nothing.
- Setup does not add the self-signed certificate to any computer's trusted list. A browser
  accepts it once, as it does for the engine's address today, and the summary explains how to
  trust it instead.
- On the browser side, the only change is that the page's own address becomes its default
  engine when the standalone host serves it (FR-014).
- An existing silent script that passes `/WEB_PORT` without `/WEB_HOST` now stops with a clear
  log message instead of quietly getting a different address (FR-044). The documented examples
  already pass `/WEB_HOST=IIS`.
- IIS counts as ready when the IIS web server and its management scripting tools are enabled
  and the IIS web service is not disabled. Setup does not need IIS's URL Rewrite module (deep
  links already work without it).
- Setup keeps running elevated, and on the Windows versions it already supports.
- The 30-second answer check in FR-051 replaces today's 10-second check that the service is
  running. It covers a host that is still starting.

## Out of Scope

- Setup turning IIS on. Today's "Enable IIS now" path is removed (FR-006).
- The public product site and its own IIS deployment do not change.
- The SSMS 22 extension does not change.
- Hosting outside Windows, and containers.
- Moving the browser's saved data from one address to another automatically.
- Certificates from a real certificate authority, or custom domain names for the standalone
  host. Operators can still re-issue the certificate with extra names, as they can today.

## Dependencies

- Spec 026 (M4 installer closure) behaviour carries over: the engine port, LAN TLS, the pairing
  PIN, the locked-down shared-state folder, service recovery and the install summary. The
  separate site and secure site ports now apply to IIS hosting only.
- Review WEB-03 and re-review REV-03 rules carry over: pages reach other computers over HTTPS,
  and setup removes only HTTPS bindings it created and still owns.
