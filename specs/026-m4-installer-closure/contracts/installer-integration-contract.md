# Contract: Installer Integration (US1 + US4)

Defines how `web-installer.iss` is wired into `AkmlSqlSetup.iss`, the two-port page split, and the silent-flag parse. Covers FR-001..FR-007 + FR-021..FR-026.

## C1 — Include + hook calls

`AkmlSqlSetup.iss` gains `#include "web-installer.iss"` at the end of its `[Code]` section (after the existing event procedures are declared, before the section closes). The integration points are inserted at these audited line numbers — four hook calls into existing procedures, the silent-flag parse in `InitializeSetup`, and a new `ShouldSkipPage` for `Web_Skip` (five hook procedures total):

| Existing procedure | Line | Inserted call | Placement |
|--------------------|------|---------------|-----------|
| `InitializeWizard` | 345 | `Web_Init();` | after existing body |
| `InitializeSetup` | 403 | silent-flag parse + validation (C3) | early — before any state created; `Result := False` on invalid |
| `NextButtonClick` | 469 | `if not Web_NextButton(CurPageID) then begin Result := False; Exit; end;` | after existing body, before `Result := True` return |
| `CurStepChanged` | 579 | `if CurStep = ssPostInstall then Web_PostInstall();` | inside the `ssPostInstall` branch |
| `CurUninstallStepChanged` | 674 | `if CurUninstallStep = usUninstall then Web_Uninstall();` | inside the `usUninstall` branch |
| **`ShouldSkipPage`** | **absent** | **new function**: `function ShouldSkipPage(PageID: Integer): Boolean; begin Result := Web_Skip(PageID); end;` | new top-level function |

**Critical**: `ShouldSkipPage` does not exist in `AkmlSqlSetup.iss` today. It MUST be created — without it `Web_Skip` has no caller and the web pages show even when the component is unticked.

**Verification**: `ISCC.exe AkmlSqlSetup.iss` compiles with zero errors; `grep` of `AkmlSqlSetup.iss` for `Web_Init`/`Web_Skip`/`web-installer.iss` returns matches.

## C2 — Two-port page split

`web-installer.iss`'s single `WebPortPage` (default 47291) is replaced by two `CreateInputQueryPage` pages:

| Page var | Title | Default | Range | Routed to |
|----------|-------|---------|-------|-----------|
| `WebIisPortPage` | "IIS site port" | `80` | `80` or `1024..65535` | `web-iis-setup.ps1 -Port` |
| `WebBridgePortPage` | "Engine bridge port" | `47291` | `1024..65535` | `web-config-bridge.ps1 -Port`, `web-tls-setup.ps1 -Port`, `web-firewall.ps1 -Port` |

`Web_NextButton` validates each port's range on its page, and on the bridge-port page additionally rejects `IisPort == BridgePort` with `MsgBox('IIS port and Bridge port must differ.', mbError, MB_OK)`. Helper `GetWebPort` is split into `GetIisPort` + `GetBridgePort` for `[Run]` parameter substitution (or `GetWebPort(Param)` switches on `Param`).

**Verification**: in the wizard, the two pages appear in order (IIS then bridge); entering equal ports blocks Next; the `[Run]` lines invoke each helper with the correct port.

## C3 — Silent-flag parse + cross-validation

In `InitializeSetup` (runs before the wizard), parse from the command line (Inno Setup exposes `{param:NAME|default}` and `GetCmdTail`/`ParamCount`):

| Flag | Stored | Default if absent |
|------|--------|-------------------|
| `/WEB_HOST=IIS\|NONE` | `HostMode` | wizard default (IIS) |
| `/WEB_EXPOSURE=LOCALHOST\|LAN` | `Exposure` | wizard default (Localhost) |
| `/WEB_PORT=<int>` | `IisPort` | `80` |
| `/BRIDGE_PORT=<int>` | `BridgePort` | `47291` |

Two validation rules, each `Result := False` + a `Log`/`MsgBox` (MsgBox only when not silent) + the installer aborts:

1. `WEB_HOST=NONE` and `WEB_EXPOSURE=LAN` → "LAN exposure requires a hosting mode (use /WEB_HOST=IIS)".
2. `WEB_PORT == BRIDGE_PORT` → "IIS port and Bridge port must differ".

When flags are present, `Web_Skip` returns true for the corresponding wizard pages so `/VERYSILENT` never blocks. Sub-step failure (IIS/cert/firewall/service/config) → non-zero `ExitCode` + the uninstall hooks roll back what was provisioned (FR-025).

**Verification**: the three `SC-004`/`SC-005`/`FR-024` silent commands behave as specified (happy path exit 0; the two invalid combos exit non-zero with the log line; no partial state).

## C4 — Success page URL form

`Web_PostInstall` builds the `URL:` line per E5: `http://localhost[:IisPort]/` (omit `:80`) for localhost, `https://<hostname>[:IisPort]/` for LAN. This is the dedicated-`AkmlSqlWeb`-site URL form (not the PRD's `/akmlsql` application path — see spec Assumptions).
