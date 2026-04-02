# Feature Specification: AKML SQL Phase 1 — Foundation & Windows EXE Installer

**Feature Branch**: `001-phase1-foundation-installer`
**Created**: 2026-03-16
**Status**: Draft
**Input**: User description: "AKML SQL Phase 1: Foundation and Windows EXE Installer with wizard-based setup, environment detection, and extension host architecture for SSMS and Visual Studio"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wizard-Based Installation (Priority: P1)

A database administrator downloads AKMLSQLSetup.exe and double-clicks it. A classic Windows setup wizard appears with branded screens. The administrator clicks through Welcome, accepts the EULA, sees all detected SSMS and Visual Studio instances with checkboxes, confirms the installation directory, reviews additional options, and clicks Install. Within 60 seconds, AKML SQL is integrated into their selected environments. The administrator clicks Finish and opens SSMS to confirm the extension is loaded.

**Why this priority**: The installer is the sole delivery mechanism for the product. Without a working wizard-based installer, no other functionality can reach users. This is the foundation everything else depends on.

**Independent Test**: Can be fully tested by running the installer on a machine with at least one supported IDE installed and verifying the wizard completes end-to-end, files are deployed correctly, and the extension loads in the target IDE.

**Acceptance Scenarios**:

1. **Given** a Windows machine with SSMS 22 installed, **When** the user runs AKMLSQLSetup.exe and clicks through all wizard screens accepting defaults, **Then** the installation completes successfully in under 60 seconds and all extension files are placed in the correct SSMS 22 extensions directory.
2. **Given** a Windows machine with multiple SSMS/VS versions installed, **When** the user reaches the Environment Scan screen, **Then** all installed SSMS (20, 21, 22) and Visual Studio (2019, 2022, 2026) instances are listed with correct architecture badges and pre-checked checkboxes.
3. **Given** the user is on the Environment Scan screen, **When** a Visual Studio instance is detected without SSDT installed, **Then** that instance appears grayed out with a warning explaining SSDT is required.
4. **Given** the user clicks Back on any wizard screen, **When** they return to the previous screen, **Then** all previously entered choices are preserved.
5. **Given** the user has not accepted the EULA, **When** they are on the License Agreement screen, **Then** the Next button is disabled until "I accept the agreement" is selected.

---

### User Story 2 - Extension Loads and Shows Menu in IDE (Priority: P1)

After installation, a user opens SSMS or Visual Studio. The AKML SQL extension loads automatically. An "AKML SQL" top-level menu appears with items: About AKML SQL, Check for Updates, Options, Send Feedback, and View Logs. A status bar indicator shows "AKML SQL v1.0.0" with a green icon. The user clicks About and sees version, build date, runtime, and IDE information.

**Why this priority**: The extension host is the platform on which all future phases build. Without a working extension that loads and registers menus, there is nothing to extend in subsequent phases.

**Independent Test**: Can be fully tested by launching each target IDE after installation and verifying the menu appears, each menu item functions correctly, and the status bar indicator is visible.

**Acceptance Scenarios**:

1. **Given** AKML SQL is installed for SSMS 22, **When** the user launches SSMS 22, **Then** an "AKML SQL" submenu appears under the Extensions menu and a status bar indicator shows "AKML SQL v1.0.0" with a green icon.
2. **Given** the user clicks "About AKML SQL" from the menu, **When** the About dialog appears, **Then** it displays the correct version number, build date, runtime version, and IDE version.
3. **Given** the extension encounters an internal error during load, **When** SSMS or VS starts, **Then** the IDE launches normally without crashing, and the status bar shows a red indicator.
4. **Given** the user clicks "View Logs", **When** the action completes, **Then** the log folder opens in Windows Explorer.
5. **Given** the user clicks "Send Feedback", **When** the action completes, **Then** the default browser opens to the AKML SQL feedback portal.

---

### User Story 3 - Silent Enterprise Installation (Priority: P2)

An IT administrator deploys AKML SQL across 200 workstations using a deployment tool. They run the installer with command-line switches specifying silent mode, EULA acceptance, target IDEs, and auto-close of running applications. The installer completes without any UI and writes a log file for verification.

**Why this priority**: Enterprise deployment is critical for adoption in corporate environments but depends on the wizard installer (P1) working first. It extends the same installer with command-line switches.

**Independent Test**: Can be fully tested by running the installer from the command line with silent switches and verifying the installation completes, correct targets are installed, and the log file is written.

**Acceptance Scenarios**:

1. **Given** a machine with SSMS 21 and SSMS 22, **When** the admin runs `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS="ssms22" /LOG="C:\Logs\akml.log"`, **Then** the installer completes silently, installs only for SSMS 22, and writes a log file to the specified path.
2. **Given** SSMS 22 is currently running, **When** the admin runs the installer with `/FORCECLOSEAPPS`, **Then** SSMS 22 is closed automatically before installation proceeds.
3. **Given** the admin uses `/VERYSILENT` without `/ACCEPTEULA`, **When** the installer runs, **Then** it exits with an error code and logs that EULA acceptance is required.

---

### User Story 4 - Clean Uninstallation (Priority: P2)

A user decides to uninstall AKML SQL. They go to Windows Settings > Apps, find AKML SQL, and click Uninstall. The uninstaller removes all extension files from every IDE's Extensions folder, removes core binaries, removes shortcuts and registry entries, clears MEF caches, and optionally removes user data. After uninstallation, all target IDEs launch without any trace of AKML SQL.

**Why this priority**: Clean uninstallation is essential for user trust and professional software standards. Orphaned files or broken IDE states would damage credibility.

**Independent Test**: Can be fully tested by installing AKML SQL, then uninstalling, and verifying no files, registry entries, or menu items remain.

**Acceptance Scenarios**:

1. **Given** AKML SQL is installed for SSMS 22 and VS 2022, **When** the user uninstalls via Windows Settings, **Then** all extension files are removed from both SSMS 22 and VS 2022 extensions directories.
2. **Given** the user is uninstalling, **When** the uninstaller reaches the user data step, **Then** it asks whether to remove user data from `%AppData%\AKML SQL\` and respects the user's choice.
3. **Given** SSMS 22 is running during uninstall, **When** the uninstaller starts, **Then** it warns the user and offers to close SSMS automatically or defer removal until next reboot.
4. **Given** uninstallation is complete, **When** the user launches any previously targeted IDE, **Then** no AKML SQL menu, status bar indicator, or error messages appear.

---

### User Story 5 - Update Notification (Priority: P3)

A user opens SSMS after a new version of AKML SQL has been released. The extension spawns a background update checker that detects the new version. On the next IDE startup, a non-modal notification bar appears informing the user of the update. The user clicks "Download Update" and is taken to the download page in their browser.

**Why this priority**: Update awareness is important for long-term product health but is a secondary concern compared to installation and extension loading. Phase 1 only implements notification, not in-place updates.

**Independent Test**: Can be fully tested by configuring the update manifest to report a newer version and verifying the notification appears on the next IDE launch.

**Acceptance Scenarios**:

1. **Given** the user has auto-update enabled, **When** SSMS starts, **Then** the update checker runs in the background without blocking IDE startup.
2. **Given** a newer version is available, **When** the user starts the IDE after the check has completed, **Then** a non-modal notification bar appears with an option to download the update.
3. **Given** the user clicks "Check for Updates" from the AKML SQL menu, **When** no update is available, **Then** a message confirms the current version is up to date.
4. **Given** the user has disabled auto-update during installation, **When** the IDE starts, **Then** no background update check is performed.

---

### Edge Cases

- What happens when no supported IDE is installed on the machine? The installer shows an error panel with download links to SSMS 22 and Visual Studio 2026.
- What happens when the user selects an installation directory on a drive with insufficient disk space? The installer warns the user and prevents proceeding until a valid directory is selected.
- What happens when the selected installation directory requires elevated permissions? The installer triggers UAC elevation when the user clicks Install.
- What happens when a target IDE's Extensions folder has restrictive file permissions? The installer logs the error, marks that specific target as failed on the Finish screen, and continues installing for other targets.
- What happens when the MEF cache cannot be cleared for a target IDE? The installer logs a warning and documents the manual cache-clearing procedure in the installation log.
- What happens when the update manifest endpoint is unreachable? The update checker silently logs the failure and retries on the next scheduled check (24 hours later).
- What happens when the installer is run on an unsupported OS version (below Windows 10 21H2)? The installer shows a clear error message and exits without installing.
- What happens when an older version of AKML SQL is already installed? The installer detects it and performs an upgrade-in-place, preserving user settings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Installer MUST present a wizard-based setup flow with screens: Welcome, License Agreement, Environment Scan, Installation Directory, Additional Options, Ready to Install, Installing Progress, and Installation Complete.
- **FR-002**: Every wizard screen MUST have Back, Next, and Cancel buttons (except Welcome which has no Back, and the progress screen which has no Cancel).
- **FR-003**: Installer MUST automatically detect all installed SSMS versions (20, 21, 22) using registry scan, file system fallback, and VS installer query strategies.
- **FR-004**: Installer MUST automatically detect all installed Visual Studio versions (2019, 2022, 2026) and verify whether the SSDT workload is present for each.
- **FR-005**: Environment Scan screen MUST display detected environments in a tree-view with checkboxes, architecture badges (x86/x64), and installation paths.
- **FR-006**: Visual Studio instances without SSDT MUST appear grayed out with a warning tooltip explaining SSDT is required.
- **FR-007**: Installer MUST warn the user if any selected target IDE is currently running, offering to close them automatically or let the user close them manually.
- **FR-008**: Installer MUST support the following silent installation switches: `/SILENT`, `/VERYSILENT`, `/ACCEPTEULA`, `/DIR`, `/TARGETS`, `/NOTELEMETRY`, `/NOUPDATE`, `/LOG`, `/FORCECLOSEAPPS`.
- **FR-009**: `/VERYSILENT` mode MUST require `/ACCEPTEULA` and exit with an error if it is not provided.
- **FR-010**: Installer MUST deploy architecture-appropriate extension binaries (x86 for SSMS 20 and VS 2019; x64 for SSMS 21, SSMS 22, VS 2022, VS 2026).
- **FR-011**: Installer MUST deploy core binaries to the base installation directory and extension-specific files to each target IDE's Extensions folder.
- **FR-012**: Installer MUST clear the MEF component cache for each target IDE after installation.
- **FR-013**: Installer MUST register AKML SQL in Windows Programs & Features for standard uninstallation.
- **FR-014**: Extension MUST register an "AKML SQL" menu with items: About AKML SQL, Check for Updates, Options, Send Feedback, and View Logs. In SSMS 20 and VS 2019, the menu MUST appear as a top-level menu bar entry. In SSMS 21, SSMS 22, VS 2022, and VS 2026, the menu MUST appear as a submenu under the Extensions menu.
- **FR-015**: Extension MUST display a status bar indicator showing the version number and a green (loaded) or red (failed) icon.
- **FR-016**: About dialog MUST display version, build date, runtime version, IDE version, and extension load status, with a "Copy Diagnostics" button.
- **FR-017**: Extension MUST run inside a fault-isolation boundary so that any extension failure does not crash the host IDE.
- **FR-018**: Extension MUST log all activity to rolling log files (maximum 10 files, 5 MB each, 50 MB total cap) in the user's application data directory.
- **FR-019**: On first load after installation, extension MUST validate that all required files are present, configuration directory is writable, and IDE version matches the extension target.
- **FR-020**: Update checker MUST run as a separate background process, checking a version manifest and writing results to a local file without blocking IDE startup.
- **FR-021**: Update notification MUST be non-modal and only appear on the IDE startup following a successful update check.
- **FR-022**: Uninstaller MUST remove all extension files from all target IDE Extensions folders, core binaries, shortcuts, and registry entries.
- **FR-023**: Uninstaller MUST ask the user whether to remove user data and respect their choice.
- **FR-024**: Uninstaller MUST handle running IDE instances by warning the user and offering to close them automatically or defer until reboot.
- **FR-025**: Installer MUST be fully functional offline — all files MUST be bundled in the single EXE with no network requirement at install time.
- **FR-026**: Installer EXE and all deployed DLLs MUST be code-signed to prevent security warnings.
- **FR-027**: Installer MUST support upgrade-in-place, detecting a previous installation and upgrading without requiring manual uninstall first.
- **FR-028**: Telemetry MUST be opt-in only (disabled by default). Auto-update MUST be enabled by default.
- **FR-029**: Installation MUST write a default configuration file to the user's application data directory.
- **FR-030**: The "Options" menu item MUST display a placeholder dialog indicating settings will be available in a future update, with an OK button to dismiss.

### Key Entities

- **Installation Target**: Represents a detected IDE instance (SSMS or Visual Studio) with its version, architecture (x86/x64), installation path, compatibility status, and SSDT presence (for VS). Each target can be individually selected or deselected for installation.
- **Extension Package**: The set of binaries deployed to a specific IDE's Extensions folder, including the VSPackage, manifest, and registration files. Each package is architecture-specific and IDE-version-specific.
- **User Configuration**: Preferences stored in the user's application data directory, including update check settings, telemetry opt-in status, and future feature settings. Persists across upgrades.
- **Update Manifest**: A remote version descriptor containing the latest stable version, download URL, release notes URL, minimum OS version, and installer hash. (Mandatory-update flag deferred to a future phase.)
- **Installation Log**: A detailed record of all installer actions, written during both interactive and silent installations. Includes timestamps, success/failure status per target, and file operation details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users complete the full installation wizard (Welcome through Finish) in under 60 seconds on a standard machine.
- **SC-002**: The installer successfully installs on at least 98% of test matrix combinations (all supported OS, SSMS, and VS version permutations).
- **SC-003**: At least 95% of users who start the wizard complete the installation without abandoning.
- **SC-004**: The environment scanner detects 100% of installed SSMS and Visual Studio instances on the test matrix.
- **SC-005**: The extension adds less than 200 milliseconds to IDE startup time.
- **SC-006**: Extension failures never crash the host IDE — 100% fault isolation across all test scenarios.
- **SC-007**: After uninstallation, zero orphaned files or registry entries remain from any targeted IDE.
- **SC-008**: Silent installation with target switches correctly installs only to the specified targets and produces a valid log file.
- **SC-009**: The "AKML SQL" menu and status bar indicator appear correctly in all six target IDEs (SSMS 20, 21, 22 and VS 2019, 2022, 2026).
- **SC-010**: The update checker completes without blocking IDE startup or degrading IDE performance.

## Clarifications

### Session 2026-03-16

- Q: What should happen when the update checker detects a mandatory update flag? → A: Defer mandatory update behavior to a future phase (remove from Phase 1 scope).
- Q: Should the AKML SQL menu be top-level in all IDEs or placed under Extensions in newer IDEs? → A: Top-level in SSMS 20/VS 2019; under Extensions menu in SSMS 21/22 and VS 2022/2026.
- Q: What should happen when the user clicks "Options" in Phase 1? → A: Show a simple "coming soon" placeholder dialog with an OK button.

## Assumptions

- SSMS 20 uses the VS 2019 shell (x86, .NET Framework 4.7.2). SSMS 21 and 22 use the VS 2022+ shell (x64, .NET Framework 4.7.2).
- Visual Studio extension development still requires .NET Framework 4.7.2 for the shell layer in VS 2019, 2022, and 2026.
- The `vswhere.exe` utility is available on machines with Visual Studio installed and can reliably enumerate VS instances.
- An EV code-signing certificate will be procured during the first two weeks of development.
- The update manifest will be hosted at a stable HTTPS endpoint managed by the AKML SQL team.
- Windows 10 21H2 is the minimum supported OS version; older versions are explicitly unsupported.
- All target machines have sufficient disk space (approximately 45 MB) for the base installation plus per-IDE extension deployments.
- Users have administrator privileges or UAC access to install to Program Files directories.

## Scope Boundaries

**In Scope:**
- Wizard-based EXE installer with all eight screens
- Environment detection for SSMS 20/21/22 and VS 2019/2022/2026
- Extension host shell with menu registration, About dialog, and status bar
- Silent/enterprise installation switches
- Clean uninstall via Windows Programs & Features
- Background update notification (check and notify only)
- Logging and diagnostics infrastructure
- Code signing of installer and DLLs

**Out of Scope:**
- No SQL editing features (IntelliSense, formatting, snippets)
- No AI/ML capabilities
- No licensing or activation system
- No in-place auto-update (download notification only)
- No mandatory update enforcement (flag deferred to future phase)
- No Linux or macOS support
- No Settings Manager functionality beyond placeholder UI
