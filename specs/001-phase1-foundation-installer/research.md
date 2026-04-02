# Research: AKML SQL Phase 1 — Foundation & Windows EXE Installer

**Date**: 2026-03-16
**Branch**: `001-phase1-foundation-installer`

## 1. Installer Technology — Inno Setup 6

### Decision: Inno Setup 6 with Pascal scripting for custom environment scanner

**Rationale:** Inno Setup is the only free, open-source installer framework that supports custom wizard pages with tree-view checkboxes, silent install switches, code signing, and UAC elevation — all required by the spec. WiX would be heavier and more complex for this use case.

**Alternatives considered:**
- WiX Toolset 4: More powerful but steeper learning curve, XML-heavy, overkill for the wizard-based UX needed.
- NSIS: Less capable custom page support, weaker silent install handling.
- Raw MSI: No wizard customization without significant effort.

### Key Implementation Decisions

| Area | Decision | Rationale |
|------|----------|-----------|
| Environment scanner UI | `TNewCheckListBox` on `CreateCustomPage` | Native hierarchical checkbox control, no external DLLs needed |
| Silent switches | Built-in `/SILENT`, `/VERYSILENT` + custom `/TARGETS` via `{param:}` | Standard Inno Setup pattern, extensible |
| Per-target file deployment | `Check:` function + `{code:}` dest dirs in `[Files]` section | Dynamic paths determined at runtime per detected IDE |
| VS extension install | Direct file copy for all targets (SSMS and VS) | SSMS does not support VSIXInstaller; uniform approach across all 6 targets |
| MEF cache clearing | Delete `ComponentModelCache` in `CurStepChanged(ssPostInstall)` | Standard approach used by SQL Prompt and other extensions |
| UAC elevation | `PrivilegesRequired=admin` | Required for writing to Program Files directories |
| Code signing | `SignTool` directive with SHA256 + RFC 3161 timestamp | Built-in Inno Setup support; SignPath.io free tier for open-source |
| Upgrade detection | Stable GUID `AppId`, `UsePreviousAppDir=yes` | Standard Inno Setup pattern for seamless upgrades |
| License screen | `InfoBeforeFile` with MIT license text (informational, no acceptance required) | MIT does not require click-through acceptance; shows license for transparency |

---

## 2. VSPackage Extension Architecture

### Decision: Separate project per target IDE, shared code via .shproj (Shared Project)

**Rationale:** Different SDK versions (16.x for x86 targets, 17.x for x64 targets), different architectures, and different menu placement rules make a single binary impossible. A shared project avoids duplicating C# source while allowing compile-time differences.

**Alternatives considered:**
- Single binary with runtime detection: Not feasible — x86 and x64 binaries are fundamentally different, and VS SDK 16.x vs 17.x have breaking interop changes.
- Linked files (`<Compile Include="..\Shared\**">`) instead of .shproj: Works but .shproj is the standard VS approach with better tooling support.

### NuGet Dependencies

| Target | SDK Package | Build Tools |
|--------|-------------|-------------|
| SSMS 20, VS 2019 (x86) | `Microsoft.VisualStudio.SDK` 16.0.208 | `Microsoft.VSSDK.BuildTools` 16.x |
| SSMS 21/22, VS 2022/2026 (x64) | `Microsoft.VisualStudio.SDK` 17.14.x | `Microsoft.VSSDK.BuildTools` 17.x |

### Extension Loading Pattern

- `AsyncPackage` with `AllowsBackgroundLoading = true` and `ProvideAutoLoad(ShellInitialized, BackgroundLoad)`
- Entire `InitializeAsync` wrapped in try-catch for fault isolation
- `UseCodebase=true` in .csproj for xcopy deployment (generates correct .pkgdef paths)
- `.pkgdef` auto-generated from C# attributes by VSSDK.BuildTools

### Menu Placement

- **SSMS 20 / VS 2019**: Top-level menu via `IDG_VS_MM_TOOLSADDINS` parent in .vsct
- **SSMS 21/22 / VS 2022/2026**: Same .vsct parent, but VS 2022+ shell automatically relocates extension menus under the Extensions menu

### Status Bar

- `IVsStatusbar.SetText()` for the version badge
- Must be periodically refreshed as other operations overwrite status bar text
- Red indicator on failure set via the same API with an error prefix

### Out-of-Process Architecture (Future Phases)

- Decision: Custom named-pipe RPC (not VS Brokered Services)
- Rationale: Works uniformly across all 6 targets including SSMS 20/VS 2019 which lack ServiceHub
- Phase 1: No OOP needed. Interfaces defined in Core for future use.

---

## 3. Environment Detection

### Decision: Three-strategy detection for SSMS (registry, vswhere, file system); vswhere-only for VS

### SSMS Detection

| Strategy | Primary For | Method |
|----------|-------------|--------|
| Registry | SSMS 20 | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server Management Studio 20` |
| vswhere | SSMS 21/22 | `vswhere -products Microsoft.VisualStudio.Product.SSMS -all -prerelease -format json` |
| File system | Fallback | Check default paths for `Ssms.exe` |

**Known issue:** vswhere has a documented bug where SSMS 21 may not be found in certain configurations. The `-prerelease` flag and registry fallback mitigate this.

### SSMS Registry Keys

| Version | Registry Path | Notes |
|---------|---------------|-------|
| SSMS 20 (x86) | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server Management Studio 20` | WOW6432Node because 32-bit app |
| SSMS 21 (x64) | `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 21` | Native 64-bit hive |
| SSMS 22 (x64) | `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 22` | Native 64-bit hive |

### Visual Studio Detection

Primary command:
```
vswhere.exe -all -products * -format json -utf8
```

SSDT-filtered command (for eligibility):
```
vswhere.exe -requires Microsoft.VisualStudio.Component.SQL.SSDT -all -products * -format json -utf8
```

Two-pass approach: first query all VS instances (for display), then SSDT-equipped instances (for eligibility). Instances without SSDT shown grayed out.

vswhere location: `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe` (guaranteed path since VS 2017 15.2).

### Architecture Determination

| IDE | Architecture | How to determine |
|-----|-------------|------------------|
| SSMS 20 | x86 | Registry under WOW6432Node; path under Program Files (x86) |
| SSMS 21/22 | x64 | Registry under native hive; path under Program Files |
| VS 2019 | x86 | vswhere `installationVersion` major = 16 |
| VS 2022/2026 | x64 | vswhere `installationVersion` major = 17/18 |

---

## 4. MEF Cache Clearing

### Decision: Delete `ComponentModelCache` directory per IDE after file deployment

### Cache Paths

| IDE | Path Pattern |
|-----|-------------|
| SSMS 20 | `%LocalAppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache\` |
| SSMS 21 | `%LocalAppData%\Microsoft\SQL Server Management Studio\21.0\ComponentModelCache\` |
| SSMS 22 | `%LocalAppData%\Microsoft\SQL Server Management Studio\22.0\ComponentModelCache\` |
| VS 2019 | `%LocalAppData%\Microsoft\VisualStudio\16.0_{InstanceId}\ComponentModelCache\` |
| VS 2022 | `%LocalAppData%\Microsoft\VisualStudio\17.0_{InstanceId}\ComponentModelCache\` |
| VS 2026 | `%LocalAppData%\Microsoft\VisualStudio\18.0_{InstanceId}\ComponentModelCache\` |

Notes:
- SSMS 20 uses `_IsoShell` suffix (VS Isolated Shell mode)
- VS paths include per-instance GUID from vswhere `instanceId`
- Cache auto-rebuilds on next IDE launch (adds a few seconds to first startup)

---

## 5. Update Checker

### Decision: Self-contained, single-file .NET 10 LTS console application, fire-and-forget

**Rationale:** Separate process avoids .NET Framework 4.7.2 AppDomain conflicts. Self-contained eliminates runtime dependency. Single-file simplifies deployment.

### Publish Configuration

```xml
<TargetFramework>net10.0</TargetFramework>
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishTrimmed>true</PublishTrimmed>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

### Behavior

1. Extension checks `update-available.json` timestamp; if < 24h old, skip
2. Extension spawns `AkmlSql.Updater.exe --check` (fire-and-forget, hidden window)
3. Updater GETs manifest with 10-second timeout
4. On success: writes `%AppData%\AKML SQL\update-available.json`
5. On failure: logs error, exits cleanly (no result file written)
6. Next IDE startup reads result file and shows non-modal notification if update available

**Alternatives considered:**
- In-process HTTP check: Rejected — .NET Fx HttpClient limitations, AppDomain conflicts
- Windows Task Scheduler: Rejected — requires admin, harder lifecycle management
- Background thread: Rejected — ties up IDE process, less isolation

---

## 6. Logging

### Decision: Serilog + Serilog.Sinks.File targeting .NET Standard 2.0

**Rationale:** Fluent API, first-class structured logging, simple rolling file configuration, compatible with both .NET Framework 4.7.2 (shell) and .NET 10 (updater) via .NET Standard 2.0.

**Alternatives considered:**
- NLog: Fully viable, slightly more verbose. Would choose only if team has existing NLog expertise.
- Microsoft.Extensions.Logging: No built-in file sink; still needs Serilog/NLog as provider.
- log4net: Less maintained, XML-heavy, no structured logging.

### NuGet Packages

| Package | Version | Target |
|---------|---------|--------|
| `Serilog` | 4.x | netstandard2.0 |
| `Serilog.Sinks.File` | 6.x | netstandard2.0 |

### Configuration

- Rolling interval: Daily
- File size limit: 5 MB per file
- Retained files: 10 (50 MB total cap)
- Output template: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}`
- Path: `%AppData%\AKML SQL\logs\akmlsql-{date}.log`

---

## 7. Open-Source & License Considerations

### Decision: MIT license displayed as informational page (not click-through EULA)

**Rationale:** The user specified open-source without license enforcement. MIT is the simplest permissive license. Using `InfoBeforeFile` in Inno Setup shows the license text without requiring "I accept" — appropriate since MIT grants rights unconditionally.

### Impact on Spec

- FR-001 EULA screen becomes a License Information screen (Next always enabled)
- No license validation or activation logic needed
- Code signing still recommended for SmartScreen reputation (SignPath.io free tier for OSS)

---

## Sources

- Inno Setup Help: Pascal Scripting, Custom Pages, SignTool, AppId, PrivilegesRequired
- Microsoft Learn: VSPackage creation, AsyncPackage, Status Bar, Brokered Services, vswhere
- GitHub: vswhere, SSMSPlus, Clear MEF Component Cache, Serilog.Sinks.File
- VS Marketplace: Clear MEF Component Cache by Mads Kristensen
- Developer Community: SSMS 21 vswhere detection bug
- NuGet: Microsoft.VisualStudio.SDK versions 16.0.208 and 17.14.x
