# Tasks: AKML SQL Phase 1 — Foundation & Windows EXE Installer

**Input**: Design documents from `/specs/001-phase1-foundation-installer/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Not explicitly requested. Test tasks omitted. Manual integration testing per quickstart.md.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution structure, project files, NuGet dependencies, build configuration

- [x] T001 Create solution file and folder structure per plan.md in `AKML-SQL.sln`
- [x] T002 [P] Create AkmlSql.Core project targeting netstandard2.0;net10.0 in `src/AkmlSql.Core/AkmlSql.Core.csproj`
- [x] T003 [P] Create AkmlSql.Shell.Shared shared project in `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.shproj` and `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`
- [x] T004 [P] Create AkmlSql.Ssms20 project (net472, x86, VS SDK 16.0.208) in `src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj`
- [x] T005 [P] Create AkmlSql.Ssms21 project (net472, x64, VS SDK 17.x) in `src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj`
- [x] T006 [P] Create AkmlSql.Ssms22 project (net472, x64, VS SDK 17.x) in `src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj`
- [x] T007 [P] Create AkmlSql.VS2019 project (net472, x86, VS SDK 16.0.208) in `src/AkmlSql.VS2019/AkmlSql.VS2019.csproj`
- [x] T008 [P] Create AkmlSql.VS2022 project (net472, x64, VS SDK 17.x) in `src/AkmlSql.VS2022/AkmlSql.VS2022.csproj`
- [x] T009 [P] Create AkmlSql.VS2026 project (net472, x64, VS SDK 17.x) in `src/AkmlSql.VS2026/AkmlSql.VS2026.csproj`
- [x] T010 [P] Create AkmlSql.Updater project (net10.0, self-contained) in `src/AkmlSql.Updater/AkmlSql.Updater.csproj`
- [x] T011 [P] Create AkmlSql.Core.Tests project (xUnit, net10.0) in `tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [x] T012 Add MIT LICENSE.txt to repository root and `src/AkmlSql.Installer/LICENSE.txt`
- [x] T013 Verify solution builds without errors across all projects via `dotnet build AKML-SQL.slnx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core library infrastructure that ALL user stories depend on — logging, configuration, constants, shared command handlers

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T014 Implement version constants and product GUIDs in `src/AkmlSql.Core/Constants.cs`
- [x] T015 [P] Implement Serilog rolling file logger factory (10 files, 5MB each, 50MB cap) in `src/AkmlSql.Core/Logging/LoggerFactory.cs`
- [x] T016 [P] Implement AppSettings model matching config.json contract in `src/AkmlSql.Core/Config/AppSettings.cs`
- [x] T017 [P] Implement UpdateManifest model matching update-manifest.json contract in `src/AkmlSql.Core/Update/UpdateManifest.cs`
- [x] T018 [P] Implement UpdateResult model matching update-result.json contract in `src/AkmlSql.Core/Update/UpdateResult.cs`
- [x] T019 Implement ConfigManager for reading/writing %AppData%\AKML SQL\config.json in `src/AkmlSql.Core/Config/ConfigManager.cs`
- [x] T020 Implement PackageGuids with all GUIDs for package and command sets in `src/AkmlSql.Shell.Shared/PackageGuids.cs`
- [x] T021 [P] Implement AboutCommand handler (shows About dialog) in `src/AkmlSql.Shell.Shared/Commands/AboutCommand.cs`
- [x] T022 [P] Implement ViewLogsCommand handler (opens log folder in Explorer) in `src/AkmlSql.Shell.Shared/Commands/ViewLogsCommand.cs`
- [x] T023 [P] Implement SendFeedbackCommand handler (opens browser to feedback URL) in `src/AkmlSql.Shell.Shared/Commands/SendFeedbackCommand.cs`
- [x] T024 [P] Implement OptionsCommand handler (placeholder "coming soon" dialog) in `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`
- [x] T025 [P] Implement CheckUpdateCommand handler (triggers manual update check) in `src/AkmlSql.Shell.Shared/Commands/CheckUpdateCommand.cs`
- [x] T026 Implement AboutDialog (WinForms) showing version, build date, runtime, IDE version, Copy Diagnostics button in `src/AkmlSql.Shell.Shared/Dialogs/AboutDialog.cs`
- [x] T027 Implement StatusBarManager for setting green/red status text via IVsStatusbar in `src/AkmlSql.Shell.Shared/StatusBar/StatusBarManager.cs`

**Checkpoint**: Foundation ready — all shared code is in place for user story implementation

---

## Phase 3: User Story 2 — Extension Loads and Shows Menu in IDE (Priority: P1) MVP

**Goal**: After installation, the AKML SQL extension loads in all target IDEs, registers menus, shows status bar indicator, and provides About dialog.

**Independent Test**: Launch each target IDE with the extension manually deployed to its Extensions folder. Verify menu appears, About dialog works, status bar shows green indicator.

**Note**: US2 (extension) is built before US1 (installer) because the installer needs the built extension binaries to deploy.

### Implementation for User Story 2

- [x] T028 [P] [US2] Create .vsct command table with top-level AKML SQL menu (5 items) for x86 targets in `src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct`
- [x] T029 [P] [US2] Create .vsct command table with Extensions submenu placement for x64 targets in `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`
- [x] T030 [P] [US2] Copy and adapt .vsct for VS 2019 (top-level menu, x86) in `src/AkmlSql.VS2019/AkmlSqlVS2019.vsct`
- [x] T031 [P] [US2] Copy and adapt .vsct for SSMS 21 (Extensions submenu, x64) in `src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct`
- [x] T032 [P] [US2] Copy and adapt .vsct for VS 2022 (Extensions submenu, x64) in `src/AkmlSql.VS2022/AkmlSqlVS2022.vsct`
- [x] T033 [P] [US2] Copy and adapt .vsct for VS 2026 (Extensions submenu, x64) in `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`
- [x] T034 [US2] Implement AsyncPackage entry point with fault-isolated InitializeAsync, menu command registration, status bar setup, and logging in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` (reference implementation)
- [x] T035 [P] [US2] Create extension.vsixmanifest for SSMS 20 (VS 2019 shell, x86, version range [16.0,17.0)) in `src/AkmlSql.Ssms20/source.extension.vsixmanifest`
- [x] T036 [P] [US2] Create extension.vsixmanifest for SSMS 21 (VS 2022 shell, x64, ProductArchitecture=amd64) in `src/AkmlSql.Ssms21/source.extension.vsixmanifest`
- [x] T037 [P] [US2] Create extension.vsixmanifest for SSMS 22 (VS 2022 shell, x64) in `src/AkmlSql.Ssms22/source.extension.vsixmanifest`
- [x] T038 [P] [US2] Create extension.vsixmanifest for VS 2019 (x86, version range [16.0,17.0)) in `src/AkmlSql.VS2019/source.extension.vsixmanifest`
- [x] T039 [P] [US2] Create extension.vsixmanifest for VS 2022 (x64, version range [17.0,18.0)) in `src/AkmlSql.VS2022/source.extension.vsixmanifest`
- [x] T040 [P] [US2] Create extension.vsixmanifest for VS 2026 (x64, version range [18.0,19.0)) in `src/AkmlSql.VS2026/source.extension.vsixmanifest`
- [x] T041 [P] [US2] Adapt AkmlSqlPackage.cs for SSMS 20 (x86 specifics) in `src/AkmlSql.Ssms20/AkmlSqlPackage.cs`
- [x] T042 [P] [US2] Adapt AkmlSqlPackage.cs for SSMS 21 in `src/AkmlSql.Ssms21/AkmlSqlPackage.cs`
- [x] T043 [P] [US2] Adapt AkmlSqlPackage.cs for VS 2019 in `src/AkmlSql.VS2019/AkmlSqlPackage.cs`
- [x] T044 [P] [US2] Adapt AkmlSqlPackage.cs for VS 2022 in `src/AkmlSql.VS2022/AkmlSqlPackage.cs`
- [x] T045 [P] [US2] Adapt AkmlSqlPackage.cs for VS 2026 in `src/AkmlSql.VS2026/AkmlSqlPackage.cs`
- [x] T046 [US2] Implement first-load validation (check DLLs present, config dir writable, IDE version match) in `src/AkmlSql.Shell.Shared/Validation/LoadValidator.cs`
- [x] T047 [US2] Build all shell projects and verify via F5 experimental instance on VS 2022 that menu, About dialog, and status bar work

**Checkpoint**: Extension loads in all target IDEs with menu, About dialog, and status bar indicator

---

## Phase 4: User Story 1 — Wizard-Based Installation (Priority: P1)

**Goal**: A single AKMLSQLSetup.exe with an 8-screen wizard that detects all SSMS/VS installations, lets the user select targets, and deploys extension binaries.

**Independent Test**: Run the installer on a machine with at least one supported IDE, complete the wizard, and verify the extension loads in the target IDE.

### Implementation for User Story 1

- [x] T048 [US1] Create main Inno Setup script with AppId, version, SignTool, PrivilegesRequired=admin, InfoBeforeFile=LICENSE.txt in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T049 [US1] Implement Screen 1 (Welcome) and Screen 2 (License Information — MIT, no acceptance required) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T050 [US1] Implement SSMS detection Pascal functions (registry Strategy 1, vswhere Strategy 2, file system Strategy 3) in `src/AkmlSql.Installer/environment-scanner.iss`
- [x] T051 [US1] Implement VS detection Pascal functions (vswhere with SSDT component check, two-pass query) in `src/AkmlSql.Installer/environment-scanner.iss`
- [x] T052 [US1] Implement running IDE process detection (tasklist-based) in `src/AkmlSql.Installer/environment-scanner.iss`
- [x] T053 [US1] Implement Screen 3 (Environment Scan) with TNewCheckListBox showing detected SSMS/VS instances, architecture badges, SSDT warnings, and running IDE warnings in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T054 [US1] Implement Screen 4 (Installation Directory) with disk space check and browse button in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T055 [US1] Implement Screen 5 (Additional Options) with auto-update ON by default, telemetry OFF by default, shortcuts in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T056 [US1] Implement Screen 6 (Ready to Install) showing summary of selections in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T057 [US1] Implement [Files] section with Check: functions and {code:} dest dirs for per-target deployment (core binaries + per-IDE extension files) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T058 [US1] Implement Screen 7 (Installing Progress) with operation labels and Screen 8 (Finish) with per-target success/failure status in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T059 [US1] Implement post-install MEF cache clearing via CurStepChanged(ssPostInstall) for all target IDEs in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T060 [US1] Implement default config.json creation in %AppData%\AKML SQL\ during post-install in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T061 [US1] Implement upgrade-in-place detection using stable AppId and UsePreviousAppDir in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T062 [US1] Implement Windows Programs & Features registration and shortcut creation (Start Menu, desktop) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T063 [P] [US1] Create installer branding assets (banner.bmp, icon.ico, sidebar.bmp) in `src/AkmlSql.Installer/assets/`
- [ ] T064 [US1] Compile installer with iscc.exe and verify end-to-end wizard flow on a test machine

**Checkpoint**: AKMLSQLSetup.exe wizard installs extension to selected IDEs, extension loads correctly

---

## Phase 5: User Story 3 — Silent Enterprise Installation (Priority: P2)

**Goal**: The installer supports command-line switches for fully unattended deployment across enterprise workstations.

**Independent Test**: Run the installer with `/VERYSILENT /ACCEPTEULA /TARGETS="ssms22" /LOG="install.log"` and verify silent completion, correct target installation, and log file output.

### Implementation for User Story 3

- [x] T065 [US3] Implement /TARGETS custom parameter parsing (comma-separated list, validate against detected IDEs) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T066 [US3] Implement /ACCEPTEULA validation (required for /VERYSILENT, exit with error code if missing) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T067 [US3] Implement /NOTELEMETRY and /NOUPDATE switches to override Additional Options defaults in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T068 [US3] Implement /FORCECLOSEAPPS switch to auto-close running SSMS/VS instances in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [ ] T069 [US3] Test silent install with /VERYSILENT /ACCEPTEULA /TARGETS="ssms22" /LOG and verify output

**Checkpoint**: Silent installation works with all documented switches, produces valid log file

---

## Phase 6: User Story 4 — Clean Uninstallation (Priority: P2)

**Goal**: Uninstall via Windows Settings removes ALL files from ALL IDE Extensions folders, core binaries, shortcuts, registry entries, and MEF caches. Optionally removes user data.

**Independent Test**: Install AKML SQL, then uninstall via Windows Settings. Verify no files, registry entries, or menu items remain in any target IDE.

### Implementation for User Story 4

- [x] T070 [US4] Implement [UninstallDelete] section removing extension files from all per-IDE Extensions folders in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T071 [US4] Implement uninstall MEF cache clearing for all target IDEs in CurUninstallStepChanged(usPostUninstall) in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T072 [US4] Implement user data removal prompt (ask whether to delete %AppData%\AKML SQL\) during uninstall in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [x] T073 [US4] Implement running IDE detection during uninstall with close/defer option in `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- [ ] T074 [US4] Test full uninstall: verify zero orphaned files, registry entries, shortcuts, and MEF cache remnants

**Checkpoint**: Clean uninstallation leaves no traces in any target IDE or the system

---

## Phase 7: User Story 5 — Update Notification (Priority: P3)

**Goal**: A background update checker detects new versions and shows a non-modal notification in the IDE on the next startup.

**Independent Test**: Configure the update manifest to report a newer version, launch the IDE, and verify the notification bar appears.

### Implementation for User Story 5

- [x] T075 [US5] Implement AkmlSql.Updater Program.cs — parse --check arg, GET manifest with 10s timeout, compare versions, write update-available.json, handle errors gracefully in `src/AkmlSql.Updater/Program.cs`
- [x] T076 [US5] Implement updater process launch logic in extension (fire-and-forget, hidden window, 24h throttle check) in `src/AkmlSql.Shell.Shared/Update/UpdateLauncher.cs`
- [x] T077 [US5] Implement update notification bar (non-modal info bar) reading update-available.json on IDE startup in `src/AkmlSql.Shell.Shared/Update/UpdateNotifier.cs`
- [x] T078 [US5] Wire CheckUpdateCommand to manually trigger updater and show result in `src/AkmlSql.Shell.Shared/Commands/CheckUpdateCommand.cs`
- [x] T079 [US5] Publish AkmlSql.Updater as self-contained single-file (win-x64, trimmed) and integrate output into installer [Files] section in `src/AkmlSql.Updater/AkmlSql.Updater.csproj` and `src/AkmlSql.Installer/AkmlSqlSetup.iss`

**Checkpoint**: Update checker runs in background, notification appears on next IDE startup when update is available

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, hardening, and cleanup across all user stories

- [x] T080 Verify extension fault isolation — force an exception in InitializeAsync and confirm IDE does not crash, status bar shows red
- [x] T081 [P] Measure IDE startup overhead — verify extension adds < 200ms to startup time
- [x] T082 [P] Verify logging works end-to-end — check rolling file creation, log levels, 50MB cap enforcement
- [x] T083 Verify upgrade-in-place — install v1.0.0, then install v1.0.1 over it, confirm config preserved and extension updated
- [ ] T084 Run full test matrix validation per spec.md Section 14.1 (Win 10/11, SSMS 20/21/22, VS 2019/2022/2026, combined scenarios)
- [x] T085 [P] Run quickstart.md validation — follow all build/test/install steps and confirm they work
- [x] T086 Code cleanup — remove TODO markers, verify all GUIDs are unique, confirm no hardcoded paths

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US2 Extension Host (Phase 3)**: Depends on Foundational — must complete before US1
- **US1 Wizard Install (Phase 4)**: Depends on US2 (needs built extension binaries to deploy)
- **US3 Silent Install (Phase 5)**: Depends on US1 (extends installer with switches)
- **US4 Uninstallation (Phase 6)**: Depends on US1 (extends installer with uninstall logic)
- **US5 Update Notification (Phase 7)**: Depends on Foundational only (extension + updater are independent of installer)
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US2 (P1)**: Extension Host — can start after Foundational. No dependencies on other stories. **Must complete first** to provide binaries for the installer.
- **US1 (P1)**: Wizard Install — depends on US2 output binaries. Can start the Inno Setup script structure in parallel but needs built DLLs for the [Files] section.
- **US3 (P2)**: Silent Install — extends US1 installer script. Start after US1.
- **US4 (P2)**: Uninstallation — extends US1 installer script. Can run in parallel with US3.
- **US5 (P3)**: Update Notification — depends only on Foundational. Can run in parallel with US1/US3/US4.

### Within Each User Story

- .vsct and .vsixmanifest files before AsyncPackage implementation
- Core models before services that use them
- Extension code before installer [Files] that deploy it
- Installer screens in sequential wizard order (1→8)

### Parallel Opportunities

- T002–T011: All project creation tasks can run in parallel
- T014–T018: Core models and logger can run in parallel
- T021–T025: All command handlers can run in parallel
- T028–T033: All .vsct files can run in parallel
- T035–T040: All .vsixmanifest files can run in parallel
- T041–T045: All per-target AkmlSqlPackage.cs adaptations can run in parallel
- T065–T068: All silent install switches can run in parallel
- US4 and US5 can run entirely in parallel after their prerequisites are met

---

## Parallel Example: User Story 2

```text
# Launch all .vsct files together:
T028: Create .vsct for SSMS 20 (top-level menu, x86) in src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct
T029: Create .vsct for SSMS 22 (Extensions submenu, x64) in src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct
T030: Create .vsct for VS 2019 (top-level, x86) in src/AkmlSql.VS2019/AkmlSqlVS2019.vsct
T031: Create .vsct for SSMS 21 (Extensions, x64) in src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct
T032: Create .vsct for VS 2022 (Extensions, x64) in src/AkmlSql.VS2022/AkmlSqlVS2022.vsct
T033: Create .vsct for VS 2026 (Extensions, x64) in src/AkmlSql.VS2026/AkmlSqlVS2026.vsct

# Then launch all .vsixmanifest files together:
T035–T040: All source.extension.vsixmanifest files in parallel

# Then launch all AkmlSqlPackage.cs adaptations together:
T041–T045: All per-target package adaptations in parallel
```

## Parallel Example: User Story 1

```text
# Environment scanner functions can be developed in parallel:
T050: SSMS detection in src/AkmlSql.Installer/environment-scanner.iss
T051: VS detection in src/AkmlSql.Installer/environment-scanner.iss
T052: Running IDE detection in src/AkmlSql.Installer/environment-scanner.iss

# Note: These write to the same file but different functions — coordinate accordingly
```

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Complete Phase 1: Setup — solution structure and all .csproj files
2. Complete Phase 2: Foundational — Core library, logging, config, shared commands
3. Complete Phase 3: US2 — Extension loads in IDEs with menu and status bar
4. **STOP and VALIDATE**: F5 deploy to VS 2022 experimental instance, verify menu and About dialog
5. Complete Phase 4: US1 — Installer wizard deploys extension to real IDEs
6. **STOP and VALIDATE**: Run installer on test machine, verify end-to-end

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US2 (Extension Host) → Test via F5 → First working extension (MVP!)
3. US1 (Installer) → Test full wizard → Deployable product
4. US3 (Silent Install) → Test enterprise switches → Enterprise ready
5. US4 (Uninstall) → Test clean removal → Production quality
6. US5 (Update Notification) → Test update flow → Complete Phase 1

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US2 (Extension Host) → then US1 (Installer)
   - Developer B: US5 (Update Notification — independent of installer)
3. After US1 completes:
   - Developer A: US3 (Silent Install)
   - Developer B: US4 (Uninstallation)
4. Both stories complete → Polish phase

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- US2 must be built before US1 because the installer deploys extension binaries
- All 6 shell projects share code via .shproj — changes in Shell.Shared affect all targets
