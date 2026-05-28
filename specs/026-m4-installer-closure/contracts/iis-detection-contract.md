# Contract: IIS Detection + Three-Path Dialog (US3)

Defines the IIS-installed check and the remediation dialog. Covers FR-014..FR-020.

## C1 — `IsIisInstalled()` predicate (`web-installer.iss`)

```pascal
function IsIisInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')
        and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'));
end;
```

This is the canonical IIS-installed signal (PRD §4.2). It is stronger than the current `web-iis-setup.ps1` check (`Get-Module -ListAvailable -Name WebAdministration`), which can pass on a host where the role is not installed. The PowerShell check stays in the helper as defence-in-depth.

## C2 — Interactive three-path dialog

Trigger: the web component is selected, `HostMode == IIS`, and `IsIisInstalled()` is false. Fire **before** the Hosting choice commits (in `Web_NextButton` on the relevant page, or just before the Hosting page in `Web_Init`/skip logic). Use a `MsgBox` (or `TaskDialogMsgBox` where available) presenting three actions:

| Button | Action |
|--------|--------|
| **Enable IIS now** | Show a "Enabling IIS… this can take up to a minute" notice first (a `CreateOutputMsgPage` before the call, or a `WizardForm` status label + `WizardForm.Repaint` + wait cursor), then `Exec('dism.exe', '/online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart', ..., ewWaitUntilTerminated, ...)`. The wizard thread is necessarily frozen during the synchronous call — no live progress bar (DISM reports none to Pascal Script); the notice explains the freeze. On exit 0 → re-check `IsIisInstalled()`, continue. On non-zero → show the error code, re-present the three buttons (FR-016). |
| **Switch to Don't host** | `WebHostPage.SelectedValueIndex := 1;` (Don't host) and continue. |
| **Cancel install** | abort Inno Setup with exit code 0 (user-cancelled); no partial state. |

## C3 — Silent-mode behaviour (FR-019)

Under `/VERYSILENT` with `/WEB_HOST=IIS` and `IsIisInstalled()` false: **no dialog**. Log "IIS not installed — pass /WEB_HOST=NONE to skip IIS provisioning" and exit non-zero. Under `/WEB_HOST=NONE` the IIS check is not performed (Don't-host path).

## C4 — Don't-host success text (FR-020)

When `HostMode == DontHost` (chosen directly, via the dialog, or via `/WEB_HOST=NONE`): the bundle still lands at `{app}\Web\` and the `AkmlSqlWebEngine` service still installs; `web-iis-setup.ps1` is skipped. The success page + `INSTALL-SUMMARY.txt` show a "host it yourself" subsection:

```
Host it yourself: the web files are at  {app}\Web\
Quick local server:   cd "{app}\Web" && python -m http.server 8080
Then browse to:       http://localhost:8080/
(Bridge still runs on port <BridgePort>.)
```

**Verification**: on a host with IIS removed, the dialog fires with three working buttons; "Enable IIS now" runs `dism` and continues; "Switch to Don't host" lands the bundle + service + the host-it-yourself text; `/VERYSILENT /WEB_HOST=IIS` on the same host exits non-zero with the log line.
