# Implementation Plan: AKML SQL Phase 1 — Foundation & Windows EXE Installer

**Branch**: `001-phase1-foundation-installer` | **Date**: 2026-03-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-phase1-foundation-installer/spec.md`

## Summary

Phase 1 delivers a wizard-based Windows EXE installer (Inno Setup 6) that detects SSMS 20/21/22 and VS 2019/2022/2026, deploys architecture-appropriate VSPackage extensions into each target IDE, and provides a minimal extension shell with menu registration, About dialog, status bar indicator, rolling file logging, and background update notification. The application is open-source (MIT license) with no licensing or activation system.

## Technical Context

**Language/Version**: C# 12 / .NET Framework 4.7.2 (shell extensions), C# 13 / .NET 10 LTS (updater, core multi-target), Inno Setup 6 Pascal Script (installer)
**Primary Dependencies**: Microsoft.VisualStudio.SDK 16.0.208 (x86 targets), Microsoft.VisualStudio.SDK 17.14.x (x64 targets), Microsoft.VSSDK.BuildTools, Serilog + Serilog.Sinks.File, Inno Setup 6
**Storage**: File-based — JSON config in `%AppData%\AKML SQL\`, rolling logs via Serilog
**Testing**: xUnit (AkmlSql.Core.Tests on .NET 10), manual integration testing on IDE matrix
**Target Platform**: Windows 10 21H2+, Windows 11, Windows Server 2019+
**Project Type**: Desktop IDE extension + Windows installer
**Performance Goals**: < 200ms IDE startup overhead, < 60s installation time
**Constraints**: Offline-capable installer, self-contained .NET 10 runtime for updater (~30-40MB trimmed), x86/x64 dual architecture
**Scale/Scope**: 6 target IDEs, 10 solution projects, ~3K–5K LOC estimated

## Constitution Check

*No constitution file found. Proceeding without gate checks.*

## Project Structure

### Documentation (this feature)

```text
specs/001-phase1-foundation-installer/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── update-manifest.json
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/                        # Shared business logic
│   ├── AkmlSql.Core.csproj              # netstandard2.0;net10.0
│   ├── Constants.cs                     # Version, product name, GUIDs
│   ├── Config/
│   │   ├── AppSettings.cs               # User configuration model
│   │   └── ConfigManager.cs             # Read/write %AppData% config
│   ├── Logging/
│   │   └── LoggerFactory.cs             # Serilog rolling file setup
│   └── Update/
│       ├── UpdateManifest.cs            # Manifest model
│       └── UpdateResult.cs             # Check result model
│
├── AkmlSql.Shell.Shared/               # Shared project (.shproj)
│   ├── AkmlSql.Shell.Shared.shproj
│   ├── AkmlSql.Shell.Shared.projitems
│   ├── PackageGuids.cs                  # Package and command set GUIDs
│   ├── Commands/
│   │   ├── AboutCommand.cs
│   │   ├── CheckUpdateCommand.cs
│   │   ├── OptionsCommand.cs            # Placeholder "coming soon" dialog
│   │   ├── SendFeedbackCommand.cs
│   │   └── ViewLogsCommand.cs
│   ├── Dialogs/
│   │   └── AboutDialog.cs              # WinForms dialog (net472 compat)
│   └── StatusBar/
│       └── StatusBarManager.cs
│
├── AkmlSql.Ssms20/                     # net472, x86, VS SDK 16.x
│   ├── AkmlSql.Ssms20.csproj
│   ├── AkmlSqlPackage.cs               # AsyncPackage entry point
│   ├── AkmlSqlSsms20.vsct              # Top-level menu
│   └── source.extension.vsixmanifest
│
├── AkmlSql.Ssms21/                     # net472, x64, VS SDK 17.x
│   ├── AkmlSql.Ssms21.csproj
│   ├── AkmlSqlPackage.cs
│   ├── AkmlSqlSsms21.vsct              # Extensions submenu
│   └── source.extension.vsixmanifest
│
├── AkmlSql.Ssms22/                     # net472, x64, VS SDK 17.x
│   ├── AkmlSql.Ssms22.csproj
│   ├── AkmlSqlPackage.cs
│   ├── AkmlSqlSsms22.vsct
│   └── source.extension.vsixmanifest
│
├── AkmlSql.VS2019/                     # net472, x86, VS SDK 16.x
│   ├── AkmlSql.VS2019.csproj
│   ├── AkmlSqlPackage.cs
│   ├── AkmlSqlVS2019.vsct              # Top-level menu
│   └── source.extension.vsixmanifest
│
├── AkmlSql.VS2022/                     # net472, x64, VS SDK 17.x
│   ├── AkmlSql.VS2022.csproj
│   ├── AkmlSqlPackage.cs
│   ├── AkmlSqlVS2022.vsct              # Extensions submenu
│   └── source.extension.vsixmanifest
│
├── AkmlSql.VS2026/                     # net472, x64, VS SDK 17.x+
│   ├── AkmlSql.VS2026.csproj
│   ├── AkmlSqlPackage.cs
│   ├── AkmlSqlVS2026.vsct
│   └── source.extension.vsixmanifest
│
├── AkmlSql.Updater/                    # net10.0, self-contained single-file
│   ├── AkmlSql.Updater.csproj
│   └── Program.cs                      # Check manifest, write result, exit
│
└── AkmlSql.Installer/                  # Inno Setup 6
    ├── AkmlSqlSetup.iss                # Main installer script
    ├── environment-scanner.iss          # Pascal detection logic (include)
    ├── LICENSE.txt                      # MIT license text
    └── assets/
        ├── banner.bmp                   # Wizard header banner
        ├── icon.ico                     # Installer icon
        └── sidebar.bmp                  # Welcome/Finish sidebar image

tests/
├── AkmlSql.Core.Tests/                 # xUnit, net10.0
│   ├── AkmlSql.Core.Tests.csproj
│   ├── Config/
│   │   └── AppSettingsTests.cs
│   ├── Logging/
│   │   └── LoggerFactoryTests.cs
│   └── Update/
│       ├── UpdateManifestTests.cs
│       └── UpdateResultTests.cs
└── AkmlSql.Installer.Tests/            # PowerShell/batch scripts for installer validation
    ├── Test-SilentInstall.ps1
    ├── Test-Uninstall.ps1
    └── Test-EnvironmentDetection.ps1
```

**Structure Decision**: Multi-project solution with one shared project (.shproj) for shell command code, one core library (.NET Standard 2.0 + .NET 10 multi-target), six IDE-specific VSPackage projects (per the PRD architecture), one updater console app, and one Inno Setup installer project. This mirrors the PRD Section 7.1 project structure exactly.

## Complexity Tracking

| Aspect | Count | Justification |
|--------|-------|---------------|
| 10 projects in solution | Required by architecture | 6 IDE targets need separate binaries (different SDKs/architectures), Core is shared logic, Shell.Shared avoids code duplication, Updater is a separate process, Installer is Inno Setup |
| Dual VS SDK versions (16.x + 17.x) | Required | x86 targets (SSMS 20, VS 2019) need SDK 16.x; x64 targets need SDK 17.x |
| .shproj shared project | Avoids duplication | 5 identical command handlers + dialog shared across 6 shell projects |
