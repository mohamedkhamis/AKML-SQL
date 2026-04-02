# Data Model: AKML SQL Phase 1

**Branch**: `001-phase1-foundation-installer` | **Date**: 2026-03-16

## Entities

### 1. Installation Target

Represents a detected IDE instance on the user's machine.

| Field | Type | Description |
|-------|------|-------------|
| `ideType` | Enum: SSMS, VisualStudio | Category of IDE |
| `version` | String | IDE version (e.g., "20", "21", "22", "2019", "2022", "2026") |
| `displayName` | String | Human-readable name (e.g., "SSMS 22.1 (x64)", "VS 2022 Enterprise") |
| `architecture` | Enum: x86, x64 | Binary architecture |
| `installPath` | String | Root installation directory of the IDE |
| `extensionsPath` | String | Full path to the IDE's Extensions folder |
| `isCompatible` | Boolean | Whether the target can receive the extension |
| `incompatibilityReason` | String? | Why not compatible (e.g., "SSDT not installed") |
| `hasSsdt` | Boolean? | Whether SSDT workload is present (VS only, null for SSMS) |
| `isRunning` | Boolean | Whether the IDE process is currently running |
| `isSelected` | Boolean | Whether the user selected this target for installation |
| `detectionStrategy` | Enum: Registry, VsWhere, FileSystem | How this target was discovered |

**Identity**: Unique by `ideType` + `version` + `installPath`

**Validation Rules:**
- VS targets with `hasSsdt = false` must have `isCompatible = false` and `incompatibilityReason = "SSDT not installed"`
- SSMS 20 and VS 2019 must have `architecture = x86`
- SSMS 21/22 and VS 2022/2026 must have `architecture = x64`

### 2. User Configuration

Persisted to `%AppData%\AKML SQL\config.json`. Survives upgrades.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `autoUpdateEnabled` | Boolean | `true` | Check for updates on IDE startup |
| `telemetryEnabled` | Boolean | `false` | Send anonymous usage data (opt-in) |
| `lastUpdateCheck` | DateTime? | `null` | Timestamp of last update check |
| `installId` | String (GUID) | Auto-generated | Anonymous installation identifier |
| `installedTargets` | List of InstalledTarget | `[]` | Which IDEs have the extension installed |
| `configVersion` | Integer | `1` | Schema version for future migration |

#### InstalledTarget (nested)

| Field | Type | Description |
|-------|------|-------------|
| `ideType` | Enum: SSMS, VisualStudio | Category |
| `version` | String | IDE version |
| `architecture` | Enum: x86, x64 | Architecture |
| `extensionsPath` | String | Where extension files were deployed |
| `installedAt` | DateTime | When the extension was installed for this target |

### 3. Update Manifest

Remote JSON file at the update endpoint. Read-only from the client perspective.

| Field | Type | Description |
|-------|------|-------------|
| `version` | String (SemVer) | Latest stable version |
| `downloadUrl` | String (URL) | Where to download the installer |
| `releaseNotesUrl` | String (URL) | Release notes page |
| `minimumOsVersion` | String | Minimum supported Windows version |
| `sha256Hash` | String | SHA-256 hash of the installer EXE |

### 4. Update Result

Written by the updater process to `%AppData%\AKML SQL\update-available.json`.

| Field | Type | Description |
|-------|------|-------------|
| `available` | Boolean | Whether a newer version exists |
| `version` | String (SemVer) | The available version (if any) |
| `downloadUrl` | String (URL) | Direct download link |
| `releaseNotesUrl` | String (URL) | Release notes link |
| `checkedAt` | DateTime (UTC) | When the check was performed |

### 5. Installation Log Entry

Written to `%TEMP%\AKMLSQLSetup.log` during installation. One entry per operation.

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | DateTime | When the operation occurred |
| `operation` | String | What was done (e.g., "CopyFiles", "ClearMefCache", "RegisterUninstall") |
| `target` | String? | Which IDE target (null for global operations) |
| `status` | Enum: Success, Warning, Error | Outcome |
| `message` | String | Human-readable detail |

## Relationships

```
Installation Target (detected at install time)
    └── selected by user ──> Extension Package (deployed to extensionsPath)
                                └── recorded in ──> User Configuration.installedTargets

Update Manifest (remote)
    └── checked by ──> Updater Process
                          └── writes ──> Update Result (local file)
                                            └── read by ──> Extension (on next IDE startup)
```

## File System Layout

```
C:\Program Files\AKML SQL\              # Base installation (core binaries)
├── AkmlSql.Core.dll
├── AkmlSql.Updater.exe                 # Self-contained .NET 10
├── Serilog.dll
├── Serilog.Sinks.File.dll
└── LICENSE.txt

{IDE}\Common7\IDE\Extensions\AkmlSql\   # Per-IDE extension deployment
├── AkmlSql.{Target}.dll                # e.g., AkmlSql.Ssms22.dll
├── AkmlSql.{Target}.pkgdef
├── AkmlSql.Core.dll                    # Copy per target (netstandard2.0 build)
├── Serilog.dll
├── Serilog.Sinks.File.dll
└── extension.vsixmanifest

%AppData%\AKML SQL\                     # User data
├── config.json                         # User Configuration entity
├── update-available.json               # Update Result entity
└── logs/
    ├── akmlsql-20260316.log            # Rolling log files
    └── akmlsql-20260315.log

%LocalAppData%\AKML SQL\cache\          # Cache (safe to delete)
```

## State Transitions

### Extension Load State

```
[Not Installed] ──install──> [Installed]
[Installed] ──IDE launch──> [Loading]
[Loading] ──success──> [Loaded] (green status bar)
[Loading] ──failure──> [Failed] (red status bar, IDE continues)
[Loaded] ──IDE close──> [Installed]
[Installed] ──uninstall──> [Not Installed]
[Installed] ──upgrade──> [Installed] (new version)
```

### Update Check State

```
[Idle] ──IDE startup (>24h since last check)──> [Checking]
[Checking] ──manifest fetched──> [Update Available] or [Up to Date]
[Checking] ──network error──> [Idle] (retry next cycle)
[Update Available] ──next IDE startup──> [Notification Shown]
[Notification Shown] ──user clicks download──> [Browser Opened]
[Notification Shown] ──user dismisses──> [Idle]
```
