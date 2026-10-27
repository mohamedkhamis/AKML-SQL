# Spec 045 implementation and verification

Started 2026-10-26 (Africa/Cairo). Changes are uncommitted. No installation,
deployment, IIS change, service change, firewall change or certificate change has
been performed. The owner confirmed that no disposable VMs are available and
asked to continue independent local work.

## Before the feature (T002)

Raw logs and test results: [baseline/before](baseline/before/).

| Check | Result |
| --- | --- |
| Requirements checklist | 16 checked, 0 unchecked; read-only |
| T001 safety gate | Four negative opt-in cases passed; all eight InstallerSmoke tests skipped with the opt-in unset. The added positive opt-in/host-eligibility regression passed in the full Installer run. |
| Release solution restore/build | Passed with existing warnings; full Visual Studio MSBuild |
| Engine suite | 2,635 passed, 1 failed, 133 skipped (Release). `PerformanceBaselineTests.Capture_or_compare_M0_baseline`: completion p50 84.369 ms against July's 55.058 ms (53.2%, limit 5%). Baseline unchanged. |
| Core suite | 1,470 passed, 1 failed, 1 skipped (Release). Existing `ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only`: expects 7 keys, actual 9. |
| Web suite | 1,984 passed, 0 failed, 0 skipped (Release) |
| Installer suite excluding InstallerSmoke | 64 passed, 0 failed, 0 skipped (Release) |
| Debug Engine/Web/browser-harness builds + serial SpikePageTests | Builds passed; spike retry 1 passed, 0 failed, 0 skipped against an isolated dev server. First attempt failed because Playwright Chromium v1248 was missing; installed that dependency and reran. Both TRX files retained. |
| ISCC compile to a separate output folder | Passed; `.review-artifacts/045-baseline-iscc/AKMLSQLSetup.exe`, never executed |
| Web + Site generated-theme drift | Both passed |

The first test launch hit sandbox registry restrictions in .NET telemetry; disabling
telemetry and using the approved external SDK execution path allowed testing.
The initial four gate tests passed before the fix because this process was not an
eligible elevated IIS host. Strengthening the tests to require the opt-in skip
reason failed to compile before implementation (`SkipReason` was missing), then
passed with the gate implemented. No installer ran in either case.

T002 is complete as a before-feature recording task; this is **not an all-green
baseline**. The Engine run took 25m20s. Stack captures confirmed its long-running
performance benchmark progressed through completion, format and bulk format.
Debug builds, the browser spike and the isolated ISCC compile overlapped portions
of that run, so it is not a dedicated performance measurement. Most Engine skips
require an unconfigured PostgreSQL test server; the elevated LAN case also skipped.
No performance threshold or golden/corpus baseline was changed. The known Core
failure and pre-feature Engine comparison failure remain visible for final comparison.

## Local implementation checks

- T003–T007 tests failed first: 21 failed against missing pure helpers and the old
  side-effecting scripts. The tests refuse to source those old scripts; child
  PowerShell processes use isolated temporary ProgramData folders.
- T010–T014 implement the pure decisions, bridge config, firewall plan and answer
  check. The final non-smoke Installer run is **87 passed, 0 failed, 0 skipped**
  (`baseline/after/installer-helpers.trx`). It includes complete reason wording and
  a non-English HTTP.SYS port-holder fixture with a strong-wildcard URL.
- T015 includes the pure decisions before the installer hooks. The full installer
  compiled to `.review-artifacts/045-helpers-iscc/`; it was not executed.
- T016 adds registry/file readers and the conditional 64-bit IIS management probe.
  The full installer compiles with them (`baseline/after/iscc-readers.log`). The
  new readiness reader is not wired into the pending hosting wizard yet.
- Plan correction: the pure exposure function is `InferPreviousExposure`, because
  the planned `PreviousExposure` function would collide with the planned global
  of the same name. The tests and task references use the corrected name.
- Inno needs an empty `[UninstallDelete]` section between the two includes: the
  pure include ends in `[Code]`, whereas `web-installer.iss` starts with INI comments.
- The firewall entry point temporarily retains `-Port -Action -RuleName` for the
  current installer/uninstaller callers, including `-Port 0 -Action Remove`.
  The new `-Apply` interface is ready for T019 and repair integration.

## Spikes

S5 passed locally: all contract §8 raw-request variants refused private-file bytes
and directory listings. Engine/static/bridge/host checks: **122 passed, 0 failed,
0 skipped** (`baseline/after/static-host.trx`). These include 32 slow readers,
recovery after 33 aborted downloads, invalid roots with one Warning and no exit,
reparse-point exclusion, CSP drift, bundle extensions, validators, plain-page and
WebSocket compatibility, and a child web-mode engine's occupied-port log/exit.
Core after the setting change: **1,473 passed, 1 failed, 1 skipped**; the only
failure is the same positional-key-count assertion as before. All three new
webRoot regressions passed. Initial missing-API/setting compile failures and the
invalid-root fallback failure were observed before correction.

Platform finding: the app's Host gate accepts `[::1]` on any port, verified
directly. HTTP.SYS rejects an IPv6 Host header sent to the existing IPv4-only
loopback registration with its own 400 Invalid Hostname before calling the app.
Network tests therefore exercise localhost and 127.0.0.1; no extra registration
was added. LAN HTTPS/IPv6 still requires S3 on the VM pair.

Manifest defense: the known state names `pairing-pin.txt`, `tokens.json`,
`config.json` and `INSTALL-SUMMARY.txt` are excluded even if copied into the
public tree. The root/state overlap check is also enforced as designed.

S1, S2, S3, S4, S6 and S7 require the disposable machines described in
[quickstart.md](quickstart.md). They have not been run. Their dependent installer
decisions remain pending; no VM outcome or fallback has been assumed.

S5 and the browser-hosted raw-path replay pass; T034's overall network gate remains open.

## Local browser implementation and regression checks

The page engine, saved-record precedence, strict HTTPS scheme and PIN-prefill
service/UI tests were written before their implementation. The first API compile
and three UI cases failed as expected; the implemented service/UI suites then
passed. A later saved-bearer regression failed before the local-engine action was
fixed to reuse the matching record's vault token.

T033 composes the source `wwwroot` and Debug `_framework` bytes in a temporary
folder, separate from the sandbox engine's private state. This installed preview
SDK also leaves Debug boot JavaScript under `obj/dotnet-watch`; simply copying the
two planned folders produced a real startup 404. The harness now materializes
those extra framework JavaScript routes from the SDK runtime asset manifest,
without editing their bytes. It supplies `hotReload:false` for the optional
dotnet-watch availability check because this static sandbox has no watch server.
This affects the temporary test bundle only. The freshness guard remains in place.

**T034 remains open: 7 passed, 1 failed, 0 skipped.** The actual engine-served app
reaches Live without typing or pairing at the chosen sandbox port. All ten direct
route loads return 200 with the required headers. `/spike` loads byte-exact SQL as
text/plain, formats SQL, and completes all seven corpus rows. Raw hostile paths
disclose no private bytes or directory listings. Five summary parser cases pass.

The strict no-failed-request assertion still fails: Chromium reports the WASM
`HEAD /` detection requests as `net::ERR_ABORTED`, despite successful detection
and connection. A native JavaScript HEAD probe (including cache revalidation and
an AbortSignal) finishes successfully against the same engine. The precise cause
of this discrepancy is unresolved. Header/content completion modes, explicit body
reads, disabling streaming and retaining the response for the page session did
not eliminate it. Those unsuccessful application workarounds and temporary fetch
instrumentation were removed. The strict same-tab route/network test is retained,
not weakened or skipped. Results and per-route/raw-path output are in
`baseline/after/engine-served.trx`; earlier boot/HEAD diagnostics are retained too.
SC-005's route behavior is exercised; full SC-006 compliance is **not** claimed.

T052 parses the first whitespace-delimited URL token, preserves HTTPS for the
standalone address, and retains the existing IIS local-HTTP alternative. All five
summary parser cases pass without reading the owner's installed summary.

The complete Release solution builds with existing warnings. Engine regression
run after the change: **2,703 passed, 0 failed, 133 skipped**, explicitly excluding
only `PerformanceBaselineTests` (`baseline/after/engine-without-perf-baseline.trx`).
The full original performance baseline was already run above and failed before
this feature; it has not been changed or claimed as passing. Both theme drift
checks pass. This is local verification, not completion of T054 or T107's VM gates.

One later parallel-load Web run reproduced an unrelated short-lived toast timing
failure (`NotificationHostTests.An_info_toast_is_a_polite_status_and_leaves_on_its_own`;
element already removed). It is retained in `baseline/after/web-toast-flake.trx`.
No toast implementation/test timing was changed. Final serial Web results are
**2,015 passed, 0 failed, 0 skipped**, recorded in `baseline/after/web-final.trx`.

## Initial implementation checkpoint (superseded by the owner continuation below)

45 of 108 tasks are checked. T034 is implemented but unchecked because its
strict network assertion fails. The implementation skill requires halting on a
non-parallel task failure, so no dependent story/acceptance checkpoint is marked
complete. The separate installer migration/UI/readiness/ownership spikes remain
unrun because the owner has no disposable VMs. Existing source-side helper and
reader work is retained for review; no installer defaults or installed application
were changed. Extension hooks were checked before/after: extensions.yml is absent.

## Disposable machines (quickstart §3)

Not available. No rows have been executed or marked complete.

## Owner installs (quickstart §4)

Not run. T108 requires separate approval after the preceding gates.

## Owner continuation — remaining source implemented

The owner explicitly asked to defer T034 to Claude Code and continue all remaining
tasks. That instruction supersedes the skill's earlier halt at T034. Its task and
strict test remain unchanged/unchecked; it was not rerun during this continuation.

**92 of 108 tasks are checked.** Remaining tasks are T009/T020/T047/T053/T055/T057/
T060/T069/T078/T085/T095 (disposable-machine experiments/acceptance), T034 and its
dependent T054, T104 (public docs updated but the pre-existing Site gate is red),
T107 (release-wide gate incomplete), and T108 (owner upgrades, not authorized/run).

### Implemented source

- Code-owned Service/IIS/None wizard, standalone default, disabled IIS readiness
  reason, persisted previous host/exposure/ports, shared-port validation and Ready
  memo/origin migration notices. No Windows features are enabled automatically.
- Permanent Support config/firewall/answer helpers, IIS removal plans, safe host
  switching and stale framework cleanup, LAN summaries and the silent matrix.
- Pre-install state/custom-folder ownership and exact protected DACLs, with
  reparse refusal before recursive changes. Repair validates its installed location
  and effective write ACEs before and after elevation and repairs either host.
- Silent-safe uninstall and fresh legacy uninstall-log migration/rollback. The
  UTF-16 temp-action detector builds markers at runtime to avoid self-matching.
- Standalone/LAN/switch and both silent-uninstall smoke cases are implemented,
  but all 13 real-install smoke cases skip here without opt-in. No owner-machine
  installer, service, IIS, firewall or certificate changes were performed.
- Deployment/configuration/security/transport/public browser docs and Claude Code
  handoff updated. No Git mutation was performed; existing staging is preserved.

VM-dependent choices remain provisional: hidden legacy names use the always-false
`WebLegacyComponent` (literal `Check: False` was rejected by ISCC), the readiness
notice wraps below the list, ToolsUnavailable guidance is generic pending S4,
and custom-folder ownership refusal remains strict pending S7. No VM outcome was
assumed or marked complete.

### Validation after integration

The added tests first failed **37/37** before implementation. A subsequent probe
found ANSI loss in the test harness's output; it now writes UTF-8 explicitly.
The real generated Pascal ACL commands are compiled into an unrelated,
lowest-privilege abort-before-install probe and parsed under PowerShell 5.1.
Script AST parsing passes. Repair ACL access uses Framework APIs directly after
the isolated environment exposed a PowerShell security-module autoload failure.

| Check | Final outcome |
| --- | --- |
| Requirements checklist | 16 checked, 0 unchecked; never edited |
| Full MSBuild Restore/Release Build | Passed; existing warnings |
| Debug Engine, Web, browser harness builds | Passed |
| Fresh Engine/Web Release publishes | Passed; previous publish outputs preserved under .review-artifacts |
| ISCC with current payloads | Passed; installer compiled, never executed |
| Web | **2,015 passed, 0 failed, 0 skipped** |
| Installer | **127 passed, 0 failed, 13 real-install smoke skips** |
| Engine, excluding historical PerformanceBaselineTests | **2,703 passed, 0 failed, 133 environment skips** |
| Core | **1,473 passed, 1 same baseline failure, 1 skipped** (positional keys 7 vs 9) |
| Site before/after public docs | **916 passed, 1 same failure** on both sides (F1 anchors 30 vs 27) |
| Isolated browser subset, T034 excluded | **8 passed, 0 failed, 0 skipped** |
| Web and Site theme drift | Both passed |

The final full Release build was confirmed again after the test-harness edits:
MSBuild on `AKML-SQL.slnx` exited **0**; output is in
`baseline/after/solution-build-confirmed.log` (existing warnings retained).

Final results are the `*-integrated-final.trx`, `site-after-docs.trx` and
`isolated-e2e-final.trx` files under baseline/after. The isolated browser script
starts a freshly published engine on a free loopback port, with temporary private
state and telemetry off, serving the fresh Release bundle. SpikePageTests runs
the corpus there; parser/native-HEAD/raw-path checks also pass. The process and
temporary state are removed afterwards. Installed-service/owner-dependent E2E
tests and the deferred T034 method are excluded, not claimed as passing.

Two verification-harness issues were resolved without product/performance changes:
old EngineHost narrow random ports collided with existing listeners/reservations,
so they now use OS-assigned ports; the 100ms refactoring gate missed by 2ms under
parallel tests, so its unchanged timing tests now run in a non-parallel collection.
Contended/collision TRXs are retained. No time threshold, corpus, refactoring
implementation or historical performance baseline was changed. The earlier full
historical benchmark failure remains recorded and that long benchmark was not
rerun during this installer-only continuation.

The comparison script uses curated suite-named before/after TRXs under
`.review-artifacts/045-comparison` (including Site's pre-doc baseline and the
successful original Spike retry), with no allow-list. The result is recorded in
`baseline/after/comparison-final.log`. It compares actual case outcomes; it does
not turn pre-existing red gates or the deferred browser gate green.

The first comparison identified two missing fast contract cases because the
Engine run excluded their entire historical performance test class. Both were
then run separately: **2 passed** in `engine-baseline-contracts.trx`, with no
timing benchmark or baseline update. The final comparison includes those results
and exits **0**: **0 regressed, 0 missing, 0 newly skipped** across all six suites,
with **2,705 Engine passes** in the combined comparison. The known pre-existing
performance failure remains unrerun and outside the passing-case comparison.

Review artifact: `.review-artifacts/045-final-iscc/AKMLSQLSetup.exe`, 58,405,291 bytes,
SHA-256 `900092A51D7B2C03F07C7A64505405FAC3BAC65AE379A9A2152DBF2E1F211062`.
Do not deploy it until the remaining acceptance gates are resolved. The handoff is
`CLAUDE_HANDOFF.md`. Extension hooks were checked again; extensions.yml is absent.

## Server continuation — 2026-10-26 to 2026-10-27

The owner's new request resumed missing tasks and asked for full-cycle checks of SSMS,
web and the Windows service. This supersedes the earlier T034-only deferral. Work started
from clean `11827eb4` on `045-web-standalone-hosting`, in `C:\Repos\AKML\AKML-SQL`.
No Git mutation or product installer execution was performed. The requirements checklist
remains read-only and passes 16/16. `.specify/extensions.yml` is absent.

### Source and test corrections

- **T034:** the original strict test reproduced the aborted HEAD requests. Explicit
  consumption plus non-streaming HttpClient still failed. PageEngine now uses native
  `js/akml-page-engine.js` through JS interop: same-origin HEAD, no-cache, five-second
  deadline, header detection and unchanged three-outcome cache/precedence. The helper
  clears its timer on success instead of aborting that successful fetch. The strict
  route/network assertion was not changed. See `engine-served-before.trx`,
  `engine-served-head-completion.trx`, `engine-served-after.trx` and `browser-confirmed.trx`.
- **Core/Site gates:** ProfileGet's positional test now checks the existing nine keys,
  including SQL Prompt JSON/source, and loads an old seven-key payload. Options help
  tests pin the exact intentional General/History combined-page aliases; accidental
  aliases still fail. No production IPC or Options implementation changed.
- **Browser formatting follow-up:** the full behavior run exposed one test entering text
  before the first query tab was attached. The editor now publishes `data-tab-id` only
  after its JS state is applied; a held-interop unit test fails before and passes after.
  The test waits for this state, types through the visible editor, and retains its casing,
  comma-placement and active-style assertions. `browser-confirmed.trx` passes all ten
  formatting/engine-hosted/query-cycle cases after fresh Debug builds. The earlier
  `browser-ready-final.trx` intentionally caught a stale bundle after a source edit;
  it is superseded by that rebuilt final run, and its freshness guard remains enabled.
- **Runtime coverage:** `StandaloneQueryCycleTests` checks auto-connect, SQL results 42/43
  on one temp-table session, persisted query/connection reload, then result 44. Its
  IndexedDB wait polls the resolved value through EvaluateAsync (an async predicate is
  not a repeated boolean poll in Playwright WaitForFunction). The first harness attempts
  and their failures are retained; no session-store implementation was changed.

Raw logs, TRXs, replay scripts, the screenshot and build artifacts are in
`.review-artifacts/045-continuation-20261026/` (ignored local evidence).

### Verification results

| Check | Terminal result / evidence |
| --- | --- |
| Full Release solution restore/build | Passed again after the final changes using full VS 18 Insiders MSBuild (`solution-final-restore.log`, `solution-final-build.log`) |
| Debug Engine/Web builds | Passed; final browser bundle rebuilt after the tab-state marker |
| Web unit suite | **2,017 passed**, no failures/skips (`Web-confirmed.trx`) |
| Core | **1,473 passed, 3 skipped**, no failures (`Core-final.trx`) |
| Site | **917 passed**, no failures/skips (`Site-final.trx`) |
| Installer, no InstallerSmoke | **127 passed**, no failures/skips (`Installer-full.trx`) |
| Shell unit suite, serial | **987 passed**, no failures/skips (`Shell.Shared-serial.trx`) |
| JavaScript | **298 passed**, no failures/skips (`js-full.log`) |
| Engine functional suite | **2,706 passed, 132 skipped**, no failures; excludes only the separate historical timed case (`Engine-functional.trx`) |
| Historical timed case | **1 passed** against the existing baseline; no rebaseline or threshold change, **21 m 50 s** (`Engine-performance.trx`); combined Engine result: **2,707 passed, 132 skipped, 0 failed** |
| Full browser behavior run | **154 passed, 1 failed, 63 skipped**; the one formatting failure is resolved by the final 10/10 rerun (`browser-full.trx`, `browser-confirmed.trx`) |
| Final distinct browser outcomes | **158 passed, 62 skipped, 0 failed**, combining the full run, rebuilt follow-up, explicit service/corpus and post-recovery runs; **not** a second full-suite run (`browser-final-outcomes.csv`) |
| Real isolated SCM service | **3/3** browser/query/corpus checks; **1/1** query cycle after actual SCM crash recovery; repeated successfully against the final publish (`service-final-query-cycle.trx`, `service-final-after-recovery.trx`) |
| Installed SSMS 22 | **1/1** format/execute/pipe/screenshot cycle (`ssms-cycle-verified.trx`) |
| Web/Site theme drift | Both passed |
| ISCC with fresh payloads | Passed again after final Web publish; installer compiled without executing it (`iscc-final.log`) |

The shell suite's initial parallel run had one active-profile isolation failure; the
complete serial run passes. SSMS initially needed the owner to reconnect RDP. This host's
UIA TextPattern includes rendered line numbers, and the success status is located by a
Name condition rather than traversing unrelated accessibility elements. The installed
extension is **1.26.1022.701** with Khamis Style. Its formatting response and private
pipe were verified; the inspected screenshot shows `SELECT 42 AS AKML_CYCLE_RESULT_045`,
a grid value of **42**, and **Query executed successfully**. Only the test's own SSMS
instance/scratch file was closed; installed extension files were not replaced.

The real test service used a separate name, private configuration/state, fresh publish,
free loopback port and LocalSystem account. SCM recovered it after only its verified
process was killed (final run PID **12888 → 10472**); HTTP, browser auto-connect and SQL worked again.
The service was stopped/deleted in finally. The installed service remained Running with
PID **21776** during that isolated acceptance. The broader existing reconnect browser
tests intentionally stop/start the installed service; its original PID before those
tests was 5552. No installed binary, hosting registry value, firewall or certificate
configuration was changed.

### Comparison and remaining gates

`comparison-final.log` uses unmodified suite-named before/after TRXs and no allow-list.
It reports **0 missing tests** and **no newly failing previously passing cases**.
It exits **1** because two Core visual-reference checks that passed on the earlier
checkout now skip: the gitignored `doc/SQL-PROMPT/` reference pack is absent here.
Core's third existing skip is unchanged. No replacement reference material was invented,
no skip condition was changed and no skip was allowed away. One prior Engine LAN handshake skip
now passes. The comparison directories are under `comparison/`; Site uses the original
pre-docs baseline and the corpus uses the successful original Spike retry.

**95/108 tasks are checked**: T034, T054 and T104 are newly complete. The twelve VM/owner
tasks remain T009/T020/T047/T053/T055/T057/T060/T069/T078/T085/T095/T108. T107 remains open
for the strict before/after comparison's missing local reference pack. The timed
performance case passed. Installer smoke tests were not opted in or run; none of
the VM install/repair/uninstall/LAN/boot rows or owner interactive upgrades is claimed
from the isolated runtime checks. The task ledger's prohibition remains in force:
"Never run the installer smoke classes, or any quickstart §3 row, on the owner's machines."

Final review installer: `.review-artifacts/045-continuation-20261026/installer-final/AKMLSQLSetup.exe`,
**58,421,457 bytes**, SHA-256
`1957975AF0B61CF7BDE7738D5A8B7591DEE86450F63C7C2DB64D12AA33BA8038`.
The refreshed Web bundle is `publish/web-final/wwwroot` under the same evidence root.
The final SCM acceptance used that bundle and passed all four checks, then removed its
temporary service. The SSMS screenshot is `ssms-installed-read-only-cycle.png` there.
Final theme drift and `git diff --check` pass. Before/after extension hooks were checked;
none is registered. All source edits remain uncommitted; installer acceptance/owner
upgrade gates are not implied by a successful compile or isolated runtime test.

## Installed standalone configuration repair — 2026-10-27

The owner installed version **1.26.1027.1312** and reported that
`http://localhost:59417/` failed. SCM reported Running (PID 17068), and the registry
selected Service/Localhost, but config.json retained the old `0.0.0.0` TLS bridge
without webRoot. The October 27 installation log recorded:
`Exception calling "Replace" with "3" argument(s): "The path is not of a legal form."`
The proposed config remained in config.json.tmp. This was a real upgrade defect
missed by the earlier JSON-only helper coverage and source review.

Windows PowerShell 5.1 converts the `$null` backup-path argument to an empty string.
`web-config-bridge.ps1` now supplies
`[System.Management.Automation.Language.NullString]::Value`, preserving atomic
replacement. The new `Windows_PowerShell_entry_point_creates_and_replaces_config`
theory invokes the actual script with private temporary ProgramData/config paths:
both fresh-create-then-rerun and pre-existing-config cases failed before the fix
and passed afterward. Other settings survive and the temporary file is consumed.

Evidence root: `.review-artifacts/045-config-repair-20261027/` (ignored local files).

- `config-write-before.trx`: **2 failed**, reproducing the installed error.
- `config-write-after.trx`: **5 passed**, including both disk-write regressions.
- `installer-repaired.trx`: **129 passed**, no failures/skips, excluding InstallerSmoke.
- Full solution Release restore/build passed with VS 18 Insiders MSBuild
  (`solution-restore.log`, `solution-build.log`); `git diff --check` passed.
- `verify-installed.cjs` and `installed-service-sql.png`: installed localhost URL
  returns 200, goes Live, executes SELECT 42 in tempdb, restores text/connection
  after reload, and has no page/request failures. The screenshot was inspected.

The original installed config, pending config, summary/failure reason and Support
script were backed up under the private ProgramData folder
`repair-backup-20261027-142657`. The fixed Support script was copied into the installed
application, its selected Localhost settings and WebRoot written, and only
AkmlSqlWebEngine restarted. HTTP 200, the engine header and actual WASM bundle were
verified at the reported URL. No product installer was run and no engine/web binary
was replaced by this repair. The remaining VM and strict-comparison gates remain open.
