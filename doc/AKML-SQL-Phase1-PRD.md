# AKML SQL — Phase 1: Foundation & Windows EXE Installer

> **Version:** 1.1 | **Date:** March 2026 | **Author:** Abdulrahman Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential

---

## 1. Executive Summary

Phase 1 establishes the foundational infrastructure for AKML SQL: a professional-grade Windows EXE installer with a classic Next-Next-Finish wizard flow (matching the Redgate SQL Prompt installation experience), and the extension host architecture that loads inside both SQL Server Management Studio (SSMS 20, 21, 22) and Visual Studio (2019, 2022, 2026).

This phase delivers **zero SQL functionality** — its sole purpose is to ensure that every future phase has a rock-solid, production-grade deployment pipeline and extension shell to build upon.

### Core Philosophy

Users download a single `AKMLSQLSetup.exe` file, double-click it, click **Next → Next → Next → Install → Finish**, and within 60 seconds AKML SQL is integrated into their SSMS and Visual Studio instances — no manual DLL copying, no VSIX double-click, no registry editing. Just like SQL Prompt.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 1 — Foundation & Windows EXE Installer |
| **Target SSMS** | SSMS 20 (x86), SSMS 21 (x64), SSMS 22 (x64) |
| **Target Visual Studio** | VS 2019, VS 2022, VS 2026 (with SSDT installed) |
| **Target OS** | Windows 10 (21H2+), Windows 11, Windows Server 2019+ |
| **Installer Type** | Single EXE (Inno Setup) — NOT raw MSI or VSIX |
| **.NET Version** | .NET 11 Preview (dev) / .NET 10 LTS (stable) + .NET Fx 4.7.2 (shell compat) |

---

## 3. .NET Version Strategy

AKML SQL adopts a dual-targeting approach to leverage the latest .NET innovations while maintaining stability.

### 3.1 Development Platform

| Component | Framework | Rationale |
|---|---|---|
| **AkmlSql.Core** | .NET 10 LTS (multi-target .NET 11 Preview) | Business logic targets LTS for stability; .NET 11 preview for Runtime Async, C# 15 features |
| **AkmlSql.Ssms20** | .NET Framework 4.7.2 | SSMS 20 (x86) runs on VS 2019 Shell which requires .NET Framework |
| **AkmlSql.Ssms21/22** | .NET Framework 4.7.2 (shell) + .NET 10 (out-of-proc) | SSMS 21/22 shell is .NET Fx but heavy processing offloaded to .NET 10/11 process |
| **AkmlSql.VS2019** | .NET Framework 4.7.2 | VS 2019 extensions require .NET Framework |
| **AkmlSql.VS2022/2026** | .NET Framework 4.7.2 (shell) + .NET 10 (out-of-proc) | VS 2022/2026 are x64 but extension host still .NET Fx |
| **AkmlSql.Updater** | .NET 11 Preview / .NET 10 LTS | Standalone process, free to use latest runtime |
| **AkmlSql.SettingsManager** | .NET 11 Preview (WPF or MAUI) | Standalone WPF/MAUI app, no shell constraints |
| **AI Engine (Phase 10+)** | .NET 11+ (future) | Will leverage Runtime Async, Zstandard compression, C# 15 features |

### 3.2 Why .NET 11 Preview Matters

- **Runtime Async:** Moves async/await handling from compiler state machines into the runtime itself — better debugging, improved performance, cleaner async call stacks for the AI engine in later phases.
- **Zstandard Compression:** Native support for compressing schema caches, snippet libraries, and telemetry payloads.
- **C# 15 Features:** Collection expression arguments, improved pattern matching — cleaner code for the core library.
- **Hardware Requirements:** .NET 11 requires x86-64-v3 on x64 and armv8.2-a on ARM. Aligns with our Windows 10 21H2+ minimum.

### 3.3 Multi-Target Build Strategy

The solution uses `<TargetFrameworks>net10.0;net11.0-preview;net472</TargetFrameworks>` where applicable. CI/CD builds produce artifacts for all targets. The installer bundles the appropriate runtime-specific DLLs based on the target host (SSMS/VS version).

---

## 4. Goals & Non-Goals

### 4.1 Goals

- **Classic Next-Next EXE installer:** Wizard-based setup (Welcome → EULA → Environment Scan → Component Selection → Directory → Options → Install → Finish) with Back/Next/Cancel on every screen.
- **Environment scanner:** Automatically detect ALL installed SSMS versions (20, 21, 22) and Visual Studio versions (2019, 2022, 2026) with SSDT. Display with checkboxes.
- **Visual Studio support:** Install as VSIX-style extension into Visual Studio. Requires SSDT workload.
- **Multi-architecture:** Handle x86 (SSMS 20, VS 2019) and x64 (SSMS 21/22, VS 2022/2026) seamlessly.
- **Extension host shell:** VSPackage-based extension that loads, registers a top-level menu, and displays About dialog.
- **Clean lifecycle:** Full uninstall via Windows Programs & Features. Upgrade-in-place.
- **Silent install:** Enterprise deployment switches matching SQL Prompt.
- **.NET 11 Preview foundation:** Solution structured for .NET 11 with multi-target builds.

### 4.2 Non-Goals (Deferred)

- No SQL editing features (IntelliSense, formatting, snippets)
- No AI/ML capabilities
- No licensing or activation system (Phase 17)
- No Linux or macOS support (Phases 20–21)

---

## 5. Installer Wizard Flow (Next-Next Setup)

The installer follows a classic Windows setup wizard pattern with Back/Next/Cancel buttons on every screen. Every screen has the AKML SQL branding banner at the top.

### Screen 1: Welcome

First impression screen. Displays AKML SQL logo, version, brief description. If a previous version is detected, shows "What's New" link.

```
┌──────────────────────────────────────────────┐
│  [AKML SQL Logo]                             │
│                                              │
│  Welcome to AKML SQL Setup                   │
│  Version 1.0.0                               │
│                                              │
│  This wizard will install AKML SQL on your   │
│  computer. AKML SQL integrates with SSMS     │
│  and Visual Studio to provide AI-powered     │
│  SQL development assistance.                 │
│                                              │
│  Click Next to continue.                     │
│                                              │
│              [Cancel]  [Next >]              │
└──────────────────────────────────────────────┘
```

**Buttons:** Cancel, Next

### Screen 2: License Agreement (EULA)

Full scrollable EULA text embedded in the installer. Radio buttons: "I accept the agreement" / "I do not accept the agreement". Next button grayed out until user selects "I accept".

**Buttons:** Back, Next (disabled until accepted), Cancel

### Screen 3: Environment Scan & Selection

**THE KEY SCREEN** — This is where AKML SQL differentiates from raw VSIX installs. The installer automatically scans the system and presents ALL detected environments in a tree-view with checkboxes:

```
Detected Environments:

  ▼ SQL Server Management Studio
    ☑ SSMS 22.1  (x64)  C:\Program Files\...SSMS 22\
    ☑ SSMS 21.1  (x64)  C:\Program Files\...SSMS 21\
    ☑ SSMS 20.2  (x86)  C:\Program Files (x86)\...SSMS 20\

  ▼ Visual Studio (requires SSDT workload)
    ☑ VS 2026 Insiders (x64)  — SSDT: Installed
    ☑ VS 2022 Enterprise (x64) — SSDT: Installed
    ☐ VS 2022 Community (x64)  — ⚠ SSDT: Not found
    ☑ VS 2019 Professional (x86) — SSDT: Installed

  [Select All]  [Deselect All]  [Refresh Scan]

          [< Back]  [Next >]  [Cancel]
```

**Key behaviors:**

- **Green checkmark (☑):** Environment detected and compatible. Pre-checked by default.
- **Orange warning:** Visual Studio found but SSDT workload not installed. Checkbox unchecked, greyed out, with tooltip: "Install SQL Server Data Tools (SSDT) via Visual Studio Installer to enable AKML SQL for this instance."
- **SSMS running warning:** If any selected SSMS/VS instance is currently running, a yellow warning banner appears: "The following applications are running and must be closed: SSMS 22, VS 2022. [Close them for me] [I'll close them manually]"
- **No environments found:** Error panel with download links to SSMS 22 and Visual Studio 2026.
- **Architecture badge:** Each entry shows (x86) or (x64).

### Screen 4: Installation Directory

Default: `C:\Program Files\AKML SQL\`. Browse button to change. Disk space required/available shown. Extension DLLs go to each IDE's Extensions folder separately — this screen only controls the shared base directory.

**Buttons:** Back, Next, Cancel

### Screen 5: Additional Options

```
Additional Options:

  ☑ Check for updates automatically (once per 24h)
  ☐ Send anonymous usage telemetry (no PII)
  ☑ Create desktop shortcut for Settings Manager
  ☑ Add AKML SQL to Start Menu

          [< Back]  [Next >]  [Cancel]
```

Telemetry is **OFF** by default (opt-in only). Auto-update is **ON** by default.

**Buttons:** Back, Next, Cancel

### Screen 6: Ready to Install (Summary)

Final confirmation before any files are written. Shows complete summary:

- Target environments: SSMS 22 (x64), SSMS 21 (x64), VS 2022 Enterprise (x64)
- Installation directory: C:\Program Files\AKML SQL\
- Options: Auto-update enabled, Telemetry disabled
- Disk space required: ~45 MB

**Buttons:** Back, **Install** (prominent/colored), Cancel. Clicking Install triggers UAC elevation if not already elevated.

### Screen 7: Installing... (Progress)

Progress bar with percentage and current operation label:

1. Checking prerequisites...
2. Copying core binaries to C:\Program Files\AKML SQL\...
3. Installing extension for SSMS 22 (x64)...
4. Installing extension for SSMS 21 (x64)...
5. Installing extension for VS 2022 Enterprise...
6. Writing VSIX manifests and .pkgdef registration files...
7. Registering in Windows Programs & Features...
8. Creating shortcuts...
9. Writing default configuration to %AppData%\AKML SQL\...
10. Clearing MEF component cache for each target IDE...

A "Show Details" expander reveals the full installation log in real-time. No Cancel button (in-progress).

### Screen 8: Installation Complete (Finish)

```
✅ AKML SQL has been installed successfully!

Installed to:
  ✔ SSMS 22.1 (x64)
  ✔ SSMS 21.1 (x64)
  ✔ VS 2022 Enterprise (x64)

  ☑ Launch SSMS 22 now
  ☐ View Getting Started Guide
  ☐ View Installation Log

                    [Finish]
```

If any IDE failed, shows ✔ green (success) and ✘ red (failed) with "View Log" link.

**Buttons:** Finish

---

## 6. Environment Detection Engine

The scanner uses a multi-strategy approach to find all SSMS and Visual Studio installations.

### 6.1 SSMS Detection

**Strategy 1 — Registry Scan:**
- SSMS 20: `HKLM\SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server Management Studio 20`
- SSMS 21: `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 21`
- SSMS 22: `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 22`

**Strategy 2 — File System Fallback:**
- Scan default paths: `C:\Program Files\...SSMS XX\` and `C:\Program Files (x86)\...SSMS XX\`
- Validate by checking for `Ssms.exe` in the expected subdirectory

**Strategy 3 — VS Installer Query (SSMS 21/22):**
- Query `vswhere.exe` to enumerate VS Shell instances matching the SSMS product ID

### 6.2 Visual Studio Detection

**Strategy 1 — vswhere.exe (Primary):**
```
vswhere.exe -all -products * -format json -requires Microsoft.VisualStudio.Component.SSDT
```
Returns every VS instance that has SSDT installed.

**Strategy 2 — Registry Fallback (VS 2019):**
- `HKLM\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\16.0`

**Strategy 3 — SSDT Workload Check:**
For each detected VS instance, verify whether the "Data storage and processing" workload (SSDT) is installed. If SSDT is missing, the VS instance is shown grayed out with a warning.

### 6.3 Compatibility Matrix

| IDE | Arch | VS Shell | .NET Fx | AKML DLL | Status |
|---|---|---|---|---|---|
| SSMS 20 | x86 | VS 2019 | 4.7.2 | x86 build | ✅ Supported |
| SSMS 21 | x64 | VS 2022 | 4.7.2 | x64 build | ✅ Supported |
| SSMS 22 | x64 | VS 2022+ | 4.7.2 | x64 build | ✅ Supported |
| VS 2019 | x86 | VS 2019 | 4.7.2 | x86 build | ✅ Supported |
| VS 2022 | x64 | VS 2022 | 4.7.2 | x64 build | ✅ Supported |
| VS 2026 | x64 | VS 2026 | 4.7.2 | x64 build | ✅ Supported |

---

## 7. Extension Host Architecture

### 7.1 Project Structure

| Project | Target | Purpose |
|---|---|---|
| **AkmlSql.Core** | net10.0; net11.0-preview; netstandard2.0 | Shared business logic, interfaces, config |
| **AkmlSql.Ssms20** | net472 (x86) | VSPackage for SSMS 20 |
| **AkmlSql.Ssms21** | net472 (x64) | VSPackage for SSMS 21 |
| **AkmlSql.Ssms22** | net472 (x64) | VSPackage for SSMS 22 |
| **AkmlSql.VS2019** | net472 (x86) | VSIX for VS 2019 |
| **AkmlSql.VS2022** | net472 (x64) | VSIX for VS 2022 |
| **AkmlSql.VS2026** | net472 (x64) | VSIX for VS 2026 |
| **AkmlSql.Installer** | Inno Setup 6.x | EXE installer script (.iss) |
| **AkmlSql.Updater** | net11.0-preview | Background update checker |
| **AkmlSql.SettingsManager** | net11.0-preview (WPF/MAUI) | Standalone settings UI |

### 7.2 Integration Points (Phase 1)

In Phase 1, the extension registers these minimal integration points in both SSMS and Visual Studio:

**Top-Level Menu — "AKML SQL"** (or under Extensions menu for SSMS 21/22 and VS 2022/2026):

- **About AKML SQL:** Version, build date, .NET runtime version, IDE version, extension load status
- **Check for Updates:** Manual update check
- **Options:** Placeholder settings dialog (populated in later phases)
- **Send Feedback:** Opens browser to AKML SQL feedback portal
- **View Logs:** Opens the log folder in Explorer

**Status Bar Indicator:**
Small status bar badge showing "AKML SQL v1.0.0" with green (loaded) or red (failed) icon.

**Extension Load Validation (on first load after install):**
- Validates all required DLLs are present and correct version
- Checks write access to the configuration directory
- Verifies the IDE version matches the extension target
- Logs the result to `%AppData%\AKML SQL\logs\`

---

## 8. Silent & Enterprise Installation

| Switch | Description |
|---|---|
| `/SILENT` | Progress bar only, no wizard pages |
| `/VERYSILENT` | Fully silent. Requires `/ACCEPTEULA`. |
| `/ACCEPTEULA` | Accept EULA without displaying it |
| `/DIR="path"` | Override base installation directory |
| `/TARGETS="ssms22,vs2022"` | Comma-separated targets. Options: ssms20, ssms21, ssms22, vs2019, vs2022, vs2026. Default: all detected. |
| `/NOTELEMETRY` | Disable telemetry (disabled by default anyway) |
| `/NOUPDATE` | Disable auto-update checker |
| `/LOG="path"` | Write installation log to file |
| `/FORCECLOSEAPPS` | Auto-close running SSMS/VS instances |

**Enterprise Deployment Example:**
```
AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS="ssms21,ssms22,vs2022" /FORCECLOSEAPPS /LOG="C:\Logs\akmlsql.log"
```

---

## 9. File System Layout

### 9.1 Base Installation

| Path | Contents |
|---|---|
| `C:\Program Files\AKML SQL\` | Core binaries, updater, settings manager |
| `  \AkmlSql.Core.dll` | Shared business logic (.NET 10 / .NET Standard 2.0) |
| `  \AkmlSql.Updater.exe` | Update checker (.NET 11 Preview) |
| `  \AkmlSql.SettingsManager.exe` | Settings UI (.NET 11 Preview WPF/MAUI) |
| `  \runtimes\` | Bundled .NET 10/11 runtime (self-contained) |

### 9.2 Per-IDE Extension Deployment

| IDE | Extension Path |
|---|---|
| SSMS 20 | `C:\Program Files (x86)\...SSMS 20\Common7\IDE\Extensions\AkmlSql\` |
| SSMS 21 | `C:\Program Files\...SSMS 21\Release\Common7\IDE\Extensions\AkmlSql\` |
| SSMS 22 | `C:\Program Files\...SSMS 22\Common7\IDE\Extensions\AkmlSql\` |
| VS 2019 | `C:\Program Files (x86)\Microsoft Visual Studio\2019\...\Extensions\AkmlSql\` |
| VS 2022 | `C:\Program Files\Microsoft Visual Studio\2022\...\Extensions\AkmlSql\` |
| VS 2026 | `C:\Program Files\Microsoft Visual Studio\2026\...\Extensions\AkmlSql\` |

### 9.3 User Data

| Path | Contents |
|---|---|
| `%AppData%\AKML SQL\config.json` | User preferences and settings |
| `%AppData%\AKML SQL\logs\` | Rolling logs (10 files × 5MB = 50MB cap) |
| `%LocalAppData%\AKML SQL\cache\` | Cache data (safe to delete) |

---

## 10. Update Mechanism

### 10.1 Update Check Flow

1. On IDE startup, the extension spawns `AkmlSql.Updater.exe` (fire-and-forget, non-blocking)
2. The updater checks a version manifest at `https://updates.akmlsql.com/manifest.json`
3. If a newer version is available, writes a flag to `%AppData%\AKML SQL\update-available.json`
4. On next IDE startup (or manual check), the extension shows a non-modal notification bar
5. User clicks "Download Update" which opens the browser to the download page

> **Note:** Phase 1 does NOT do in-place auto-update. That is Phase 2+.

### 10.2 Update Manifest Schema

The manifest contains: current stable version, download URL, release notes URL, minimum required OS version, SHA-256 hash of the installer, and a mandatory-update flag for critical security fixes.

---

## 11. Uninstallation

Triggered from Windows Settings > Apps > AKML SQL, or Control Panel > Programs and Features:

1. Remove extension DLLs from ALL SSMS and VS Extensions folders
2. Remove core binaries from `C:\Program Files\AKML SQL\`
3. Remove Start Menu and desktop shortcuts
4. Remove Windows registry entries
5. Clear MEF cache for each target IDE
6. Optionally remove user data (`%AppData%\AKML SQL\`) — uninstaller asks

> **Running IDE Handling:** If SSMS or VS is running during uninstall, the uninstaller warns the user and offers to close them automatically, or defer removal until next reboot (matching SQL Prompt behavior).

---

## 12. Security & Code Signing

- **Authenticode EV signing:** Installer EXE and ALL DLLs signed. Eliminates SmartScreen warnings.
- **Offline capable:** No network required at install time. All files bundled in EXE. Critical for air-gapped government environments.
- **SHA-256 hashes:** Published on download page for manual verification.
- **IDE process safety:** Extension runs inside try-catch. If it fails, the IDE continues normally. Zero-crash guarantee.
- **Self-contained .NET runtime:** Updater and Settings Manager bundle their own .NET 11 runtime — no dependency on system-installed .NET.

---

## 13. Logging & Diagnostics

- All extension activity logged to `%AppData%\AKML SQL\logs\` using rolling file strategy (max 10 files, max 5MB each = 50MB cap)
- Log levels: DEBUG, INFO, WARN, ERROR, FATAL
- Installer writes separate log to `%TEMP%\AKMLSQLSetup.log`
- "View Logs" menu item opens the log folder in Explorer
- "About" dialog includes "Copy Diagnostics" button — copies version info, OS info, IDE info, and last 50 log lines to clipboard

---

## 14. Quality & Testing

### 14.1 Test Matrix

| OS | IDE | Arch | .NET | Scenario |
|---|---|---|---|---|
| Win 10 21H2 | SSMS 20 | x86 | Fx 4.7.2 | Fresh install |
| Win 11 23H2 | SSMS 22 | x64 | Fx 4.7.2 | Fresh install |
| Win 11 24H2 | SSMS 21+22 | x64 | Fx 4.7.2 | Multi-version |
| Win 11 24H2 | VS 2022 | x64 | Fx 4.7.2 | VS fresh install |
| Win 11 24H2 | VS 2026 Insiders | x64 | Fx 4.7.2 | VS 2026 + .NET 11 |
| Win 11 24H2 | SSMS 22 + VS 2022 | x64 | Fx 4.7.2 | Combined SSMS+VS |
| Win Server 2022 | SSMS 22 | x64 | Fx 4.7.2 | Server environment |
| Win 11 ARM64 | SSMS 22 | x64 emu | Fx 4.7.2 | ARM compatibility |
| All above | All | All | All | Upgrade + Uninstall |

### 14.2 Acceptance Criteria

1. Setup wizard completes on all test matrix combinations (Next-Next-Install-Finish)
2. Back button works correctly on every wizard screen
3. Environment scan detects all installed SSMS and VS instances
4. VS instances without SSDT shown as grayed out with warning
5. Installation completes in under 60 seconds
6. SSMS and VS launch without errors after installation
7. "AKML SQL" menu appears in all target IDEs
8. About dialog shows correct version, .NET runtime, IDE version
9. Silent installation works with /TARGETS switch
10. Uninstall removes ALL files from ALL IDE Extensions folders
11. Extension adds < 200ms to IDE startup time
12. Extension failure does not crash the IDE

---

## 15. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Solution Setup & Build Pipeline | .NET 11 Preview solution, multi-target build, CI/CD, EV cert procurement |
| 3–4 | VSPackage Shell (SSMS 22) | Extension loads in SSMS 22, menu registered, About dialog works |
| 5–6 | Multi-SSMS + VS Support | Extension loads in SSMS 20/21 and VS 2019/2022/2026 |
| 7–9 | Inno Setup Installer | Full Next-Next wizard, env scanner, silent switches. Signed EXE. |
| 10–11 | Updater & Settings Manager | Background update checker (.NET 11), standalone settings app |
| 12–14 | QA & Release | Full test matrix, bug fixes, documentation, v1.0.0 release |

**Total estimated duration: 14 weeks** (3.5 months) — expanded from 12 weeks to account for Visual Studio extension support and .NET 11 multi-target builds.

---

## 16. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| SSMS/VS extensibility API changes | Extension fails to load | Separate VSPackage per IDE version. Monitor preview releases. |
| .NET 11 Preview breaking changes | Build failures, runtime issues | Multi-target with .NET 10 LTS fallback. Only Updater/Settings use .NET 11. |
| VS 2026 Insiders unstable for extensions | Extension compat issues | VS 2026 support marked as "preview". Primary targets: VS 2022 + SSMS 22. |
| EV code signing cert delays | SmartScreen warnings | Start procurement in Week 1. Standard cert as fallback. |
| SSDT detection false negatives | VS instances incorrectly blocked | Multiple detection strategies + manual override option. |
| MEF cache corruption | Extension not discovered | Installer clears cache. Document manual procedure. |

---

## 17. Success Metrics

- **Install success rate:** > 98% across the full test matrix
- **Wizard completion rate:** > 95% of users who start the wizard finish the install
- **Install time:** < 60 seconds on SSD-equipped machines
- **IDE startup overhead:** < 200ms added by the extension
- **Uninstall cleanliness:** Zero orphaned files or registry entries
- **Environment detection accuracy:** 100% of SSMS/VS instances correctly detected
- **Phase 2 readiness:** Phase 2 (Core IntelliSense) can begin immediately on the extension shell

---

## 18. Competitive Installer Comparison

| Feature | SQL Prompt | dbForge | AKML SQL |
|---|---|---|---|
| Installer type | EXE (wraps MSI) | EXE | EXE (Inno Setup) |
| Next-Next wizard | Yes | Yes | Yes |
| SSMS auto-detect | Yes | Yes | Yes |
| VS auto-detect | Yes (2019, 2022, 2026) | Yes | Yes (2019, 2022, 2026) |
| SSDT check | Yes (required) | Yes | Yes (required) |
| Silent install | Yes | Yes | Yes |
| Code signed | EV cert | EV cert | EV cert (planned) |
| Offline capable | Yes | Yes | Yes |
| .NET version | .NET Fx 4.x | .NET Fx 4.x | .NET 11 Preview / 10 LTS |

---

*End of Phase 1 PRD — AKML SQL v1.1*
