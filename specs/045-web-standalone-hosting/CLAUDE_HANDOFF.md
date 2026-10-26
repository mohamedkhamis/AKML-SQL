# Spec 045 continuation for Claude Code

The owner explicitly asked Codex to defer T034 and implement the remaining source
work. No disposable VMs are available. No owner installation, service, IIS site,
firewall or certificate was changed, and no Git mutation was performed by Codex.
Some files were already staged externally; preserve the index and unrelated edits.

## Deferred browser issue: T034

Keep the test enabled and T034 unchecked. Reproduce after current Debug builds:

```powershell
dotnet build src/AkmlSql.Engine -c Debug
dotnet build src/AkmlSql.Web -c Debug
dotnet test tests/AkmlSql.Web.E2E.Tests -c Debug --filter 'FullyQualifiedName~EngineServedWebTests'
```

`Engine_serves_the_real_app_reloads_routes_and_runs_SQL_without_pairing` reaches
Live without typing or pairing on all ten routes. Headers, text/plain SQL bytes,
formatting and all seven corpus rows pass. Its final strict request check sees
`HEAD /` detection fetches with Chromium `net::ERR_ABORTED`. Native JavaScript
HEAD (cache revalidation and an AbortSignal included) passes against the same
engine. The cause is unresolved. Response buffering, explicit body reads, streaming
off, response lifetime changes and per-visit checks did not solve it; those trial
changes were removed. Do not repeat them without new evidence or weaken the check.
The current `PageEngine` keeps the original HEAD/disposal design. Evidence is in
baseline/after/engine-served.trx, the diagnostic TRXs, and baseline.md.

The Debug harness composes wwwroot and _framework. This preview SDK additionally
leaves boot modules in obj/dotnet-watch, so it materializes framework JavaScript
from the runtime asset manifest and supplies hotReload:false in the temporary
bundle. No boot/module bytes are patched. It rejects stale Debug builds.

## Installer source completed locally

The installer now has Service/IIS/None choices, persisted exposure/ports, strict
IIS readiness, shared-port standalone, origin-change notices, switching cleanup,
LAN summaries, the full silent matrix, custom/state-folder hardening, guarded
repair and silent-safe uninstall. Support scripts are permanent. A fresh uninstall
log retires older elevated temp-script actions; rollback restores the old log.
The UTF-16 detector builds its markers at runtime to avoid matching its own code.

Provisional choices awaiting disposable-machine experiments:

- S1: retain both legacy component names behind `WebLegacyComponent`, returning
  false. Literal `Check: False` in the plan is rejected by this Inno compiler.
- S2: shortened checklist with a wrapped notice; disabled IIS choice and DPI/
  keyboard behavior still need the VM walkthrough.
- S4: the management-tools reason is still generic. Determine the exact feature
  and cold-probe time on the VM matrix before changing wording.
- S7: use the strict custom-folder owner rule. Earlier custom elevated installs
  may need the documented compatibility fallback; do not assume their owner.
- S3/S6: LAN IPv6/SSL behavior and DelayedAutostart still need real VM evidence.

Build the installer with fresh Engine/Web publishes before VM smoke runs. The
local compiled artifact lives under .review-artifacts/045-final-iscc; the default
smoke harness expects src/AkmlSql.Installer/Output/AKMLSQLSetup.exe. Never run that
product installer on the owner's machine for acceptance. Use snapshotted VMs and
`AKML_INSTALLER_SMOKE=1` only there. Routine Installer tests exclude InstallerSmoke.

## Remaining verification

Consult tasks.md and baseline.md for exact final counts and uncompleted gates.
T034, VM experiments/acceptance, release-wide validation and the owner's explicitly
approved upgrades must not be marked complete from source tests or an ISCC compile.
Existing failures are retained: Core positional MessagePack key count (7 versus 9),
Engine historical performance baseline, and Site F1 anchors (30 versus 27), the
last reproduced before editing this feature's public docs. A short-lived toast
test was intermittent under concurrent builds; a serial Web run passed.

Final verification also exposed narrow random-port collisions in older EngineHost
tests and CPU contention in the existing 100ms refactoring timing gate. The test
ports now come from the OS, and refactoring timing tests run in a non-parallel
collection. No refactoring implementation, timing threshold or corpus was changed.
The original contended/collision TRXs are retained for transparency.

Final local status: **92/108 tasks checked**. Installer: **127 passed, 13 real-install
skips**; Web: **2,015 passed**; Engine: **2,703 passed, 133 environment skips**, plus
**2 fast historical-baseline contract cases passed separately**; isolated browser
subset: **8 passed**, excluding T034 and owner-installed acceptance. The historical
timing benchmark was not rerun. Full Release build and fresh-payload ISCC compile
passed. The before/after passing-case comparison has **0 regressed, 0 missing,
0 newly skipped**, without an allow-list. Core/Site's pre-existing failures and
all remaining acceptance gates still apply. Exact evidence is in baseline.md.
