; Spec 021 (web edition) -- M4 installer additions.  Spec 026 (M4 closure) -- two-port split.
;
; Included by AkmlSqlSetup.iss (via `#include "web-installer.iss"` placed BEFORE the main
; [Code] section so the Web_* hook procedures are declared ahead of the event handlers that
; call them). Adds the Web-edition component group, the install steps (engine binary, web
; bundle, AppData config, IIS site, TLS cert, firewall rule, Windows service for the engine),
; and the Web_* hook procedures the main installer invokes.
;
; Cross-references: contracts/installer-integration-contract.md + spec.md FR-001..FR-007 + FR-003a.
;
; ─── Status ─────────────────────────────────────────────────────────────────
; Spec 026: the integration (#include + hook calls + ShouldSkipPage) is wired into AkmlSqlSetup.iss,
; and the single port input is split into an IIS port (default 80) and a bridge port (default 47291)
; -- they must differ (HTTP.SYS cannot share one port between IIS and the engine's HttpListener).
; COMPILE-VERIFIED: ISCC compiles AkmlSqlSetup.iss (with this include) cleanly. The first interactive
; install on a Windows host with IIS + Inno Setup 7 + admin is the RUNTIME acceptance test (spec 026
; T041) and the verification gate for SC-001..SC-003.
;
; IMPLEMENTED (spec 026 US2-installer / US3 / US4): the IIS-not-installed three-path dialog
; (FR-014..FR-020), the silent-install flags (FR-021..FR-026), the Administrators+SYSTEM ACL on the
; CommonAppData dir + the 30 s pairing-pin.txt poll + the service-start check (FR-007a, FR-010,
; FR-011), plus uninstall cleanup of the sslcert binding / web-state dir / registry marker.
; GENUINELY REMAINING: full FR-025 rollback verification on a real elevated install.
;
; ─── Files this script depends on ──────────────────────────────────────────
;   web-iis-setup.ps1     -- IIS site provisioning + MIME + CSP   (receives the IIS port)
;   web-tls-setup.ps1     -- self-signed cert + netsh sslcert      (receives the bridge port)
;   web-firewall.ps1      -- firewall inbound rule                 (receives the bridge port)
;   web-config-bridge.ps1 -- writes the engine config bridge section (receives the bridge port)

[Types]
; Spec 026 (M4 closure) L7: no explicit types are defined here OR in AkmlSqlSetup.iss, so ISCC
; auto-generates the default Full / Compact / Custom. The web components below carry `Types: full`,
; so the Web edition is SELECTED BY DEFAULT (opt-out) under the default Full type. A user who wants
; the IDE plugins only must untick it -- Web_Skip and Web_PostInstall both gate on IsWebSelected, so
; unticking is fully honoured (no IIS site / service / firewall changes). To make it opt-IN instead
; (matching US1's "tick the Web edition" wording), define an explicit [Types] with a Custom default
; and drop `Types: full` from the web components.

[Components]
; FR-001 -- web-edition component group, independent of the plugin group.
Name: "web"; Description: "Web edition (browser-based AKML SQL)"; Types: full; Flags: disablenouninstallwarning
Name: "web\iis"; Description: "Host on local IIS (recommended)"; Types: full
Name: "web\service"; Description: "Install Windows service for the engine"; Types: full

[Files]
; FR-007 -- copy the web bundle to ProgramFiles. Built by
; `dotnet publish src/AkmlSql.Web -c Release`; lands under
; src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/. Recursive copy preserves _framework/.
Source: "..\AkmlSql.Web\bin\Release\net10.0\publish\wwwroot\*"; \
    DestDir: "{app}\Web"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: web

; PowerShell helpers (bundled, invoked at install time then removed).
Source: "web-iis-setup.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web\iis
Source: "web-tls-setup.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web
Source: "web-firewall.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web
Source: "web-config-bridge.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web

[Run]
; FR-004 -- web-iis-setup.ps1 receives the IIS port (the static-bundle site).
; Skipped when "Don't host" was selected (only runs for the web\iis component).
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-iis-setup.ps1"" -Port {code:GetIisPort} -PhysicalPath ""{app}\Web"" -Mode {code:GetBridgeMode}"; \
    StatusMsg: "Provisioning IIS site for AKML SQL Web..."; \
    Components: web\iis; \
    Flags: runhidden

; FR-004 -- web-tls-setup.ps1 receives the BRIDGE port (the cert is bound to the engine's
; WebSocket listener, not the IIS site). LAN mode only.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-tls-setup.ps1"" -Port {code:GetBridgePort} -PfxPath ""{commonappdata}\AKML SQL Web\certs\bridge.pfx"""; \
    StatusMsg: "Generating self-signed TLS certificate..."; \
    Check: IsLanExposed; \
    Components: web; \
    Flags: runhidden

; FR-004 -- web-firewall.ps1 receives the BRIDGE port. LAN mode only.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-firewall.ps1"" -Port {code:GetBridgePort} -Action Add"; \
    StatusMsg: "Adding Windows Firewall rule..."; \
    Check: IsLanExposed; \
    Components: web; \
    Flags: runhidden

; Windows service for the engine. Uses sc.exe to create AkmlSqlWebEngine.
; Spec 026 (M4 closure) C2/C3: launch in web mode (--web; the engine's web host reports
; SERVICE_RUNNING to the SCM) and read the web-edition config under {commonappdata} (machine-wide,
; readable by the LocalSystem service account) -- NOT {userappdata}\...\AKML SQL\config.json (the
; per-user IDE-plugin config). web-config-bridge.ps1 writes the bridge section to this same path.
Filename: "sc.exe"; \
    Parameters: "create AkmlSqlWebEngine binPath= ""\""{app}\Engine\AkmlSql.Engine.exe\"" --web --config \""{commonappdata}\AKML SQL Web\config.json\"""" start= auto DisplayName= ""AKML SQL Web Engine"""; \
    StatusMsg: "Installing AKML SQL Web Engine service..."; \
    Components: web\service; \
    Flags: runhidden

; Spec 026 (M4 closure) C2/C3 ordering fix: the service is NOT started here. Starting it from
; [Run] would launch the engine BEFORE web-config-bridge.ps1 writes config.json (that runs later in
; Web_PostInstall / ssPostInstall) -- the engine would find no enabled bridge and exit, so the
; bridge never came up on first install. `sc start` now runs in Web_PostInstall, after the config is
; written AND the shared-state dir is ACL-locked (so pairing-pin.txt is never world-readable). The
; service is still created with start= auto above, so it also auto-starts on subsequent boots.

[UninstallRun]
; Reverse the install order: stop service, remove firewall rule, delete netsh sslcert binding,
; remove IIS site, delete files. %AppData%/AKML SQL/ (IDE-plugin state) is NEVER touched (SC-007).
Filename: "sc.exe"; Parameters: "stop AkmlSqlWebEngine"; Flags: runhidden; Components: web\service
Filename: "sc.exe"; Parameters: "delete AkmlSqlWebEngine"; Flags: runhidden; Components: web\service

Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-firewall.ps1"" -Port {code:GetBridgePort} -Action Remove"; \
    Flags: runhidden; Components: web

; Note: cert + IIS site removal is done in Web_Uninstall (CurUninstallStepChanged / usUninstall)
; so we can inspect the install-summary file for the cert thumbprint.

[Code]
{ ─── Integration ───────────────────────────────────────────────────────────
  AkmlSqlSetup.iss includes this file BEFORE its main [Code] section and calls the named hooks
  from its event handlers: Web_Init() in InitializeWizard; Web_NextButton(CurPageID) in
  NextButtonClick; Web_Skip(PageID) in the (newly created) ShouldSkipPage; Web_PostInstall() in
  CurStepChanged(ssPostInstall); Web_Uninstall() in CurUninstallStepChanged(usUninstall).
  Pascal Script forbids declaring a procedure twice, so this file owns NONE of those event
  procedures -- only the Web_* hooks. }

{ ─── State carried across pages ───────────────────────────────────────────── }

var
    WebHostPage: TInputOptionWizardPage;
    WebNetworkPage: TInputOptionWizardPage;
    WebIisPortPage: TInputQueryWizardPage;       { FR-003: IIS site port (default 80) }
    WebBridgePortPage: TInputQueryWizardPage;     { FR-003: engine bridge port (default 47291) }
    InstallSummaryPage: TOutputMsgWizardPage;
    WebIisPort: Integer;
    WebBridgePort: Integer;
    PairingPin: String;
    TlsThumbprint: String;
    WebSilentActive: Boolean;     { US4: a /WEB_HOST / /WEB_EXPOSURE / /WEB_PORT / /BRIDGE_PORT flag was passed }
    WebSilentLan: Boolean;        { US4: /WEB_EXPOSURE=LAN }
    WebSilentDontHost: Boolean;   { US4: /WEB_HOST=NONE }

procedure Web_Init();
begin
    { Hosting choice }
    WebHostPage := CreateInputOptionPage(
        wpSelectComponents,
        'Web edition hosting',
        'Choose how to serve the web bundle.',
        'Pick how the web edition will be reachable. You can change this by re-running the installer.',
        True, False);
    WebHostPage.Add('Host on local IIS (recommended)');
    WebHostPage.Add('Don''t host -- I''ll serve the files myself');
    WebHostPage.SelectedValueIndex := 0;

    { Network-exposure choice }
    WebNetworkPage := CreateInputOptionPage(
        WebHostPage.ID,
        'Network exposure',
        'Decide who can reach the engine bridge.',
        'Localhost-only keeps the web edition reachable from your machine only. LAN-exposed lets other machines on your network pair via the printed PIN.',
        True, False);
    WebNetworkPage.Add('Localhost only -- only my machine can browse');
    WebNetworkPage.Add('LAN exposed -- other machines on my network can browse');
    WebNetworkPage.SelectedValueIndex := 0;

    { FR-003: IIS site port (where the browser opens the app). Default 80. }
    WebIisPortPage := CreateInputQueryPage(
        WebNetworkPage.ID,
        'IIS site port',
        'Pick the TCP port the IIS site serves the web bundle on.',
        'This is the port you browse to (e.g. http://localhost/ for port 80). Use 80 or 1024..65535. Must differ from the bridge port.');
    WebIisPortPage.Add('IIS port:', False);
    WebIisPortPage.Values[0] := '80';

    { FR-003: engine bridge port (the WebSocket transport). Default 47291. Must differ from IIS. }
    WebBridgePortPage := CreateInputQueryPage(
        WebIisPortPage.ID,
        'Engine bridge port',
        'Pick a TCP port for the engine bridge (WebSocket).',
        'The engine serves WebSocket frames on this port. Must be 1024..65535 and differ from the IIS port. Default 47291.');
    WebBridgePortPage.Add('Bridge port:', False);
    WebBridgePortPage.Values[0] := '47291';

    { Install-summary page (shown on the post-install success path). }
    InstallSummaryPage := CreateOutputMsgPage(
        wpInstalling,
        'Install summary',
        'AKML SQL Web is ready.',
        'The pairing PIN + TLS thumbprint + browser URL are below. They are also written to "%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt".');

    { Seed the port globals with the page defaults UNLESS the silent flags already set them
      (Web_ValidateSilentFlags runs in InitializeSetup, before this). }
    if not WebSilentActive then
    begin
        WebIisPort := 80;
        WebBridgePort := 47291;
    end;
end;

function GetIisPort(Param: String): String;
begin
    Result := IntToStr(WebIisPort);
end;

function GetBridgePort(Param: String): String;
begin
    Result := IntToStr(WebBridgePort);
end;

function IsWebSelected(): Boolean;
begin
    Result := WizardIsComponentSelected('web');
end;

{ US3 / FR-014: canonical IIS-installed check (PRD §4.2). Stronger than the PowerShell-side
  Get-Module check -- the registry key + appcmd.exe presence is the definitive signal. }
function IsIisInstalled(): Boolean;
begin
    Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')
          and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'));
end;

{ US4 / FR-021..FR-024 + US3 silent / FR-019. Parse the web silent-install flags into the module
  globals and validate. Returns False (abort the install) on an invalid combination. No-op
  (returns True, leaves WebSilentActive false) when no web flags are present -- interactive and
  plugin-only installs are unaffected. Called from AkmlSqlSetup.iss InitializeSetup. }
function Web_ValidateSilentFlags(): Boolean;
var
    host, exposure, portStr, bridgeStr: String;
    iisPort, bridgePort: Integer;
begin
    Result := True;
    host := Uppercase(Trim(ExpandConstant('{param:WEB_HOST|}')));
    exposure := Uppercase(Trim(ExpandConstant('{param:WEB_EXPOSURE|}')));
    portStr := Trim(ExpandConstant('{param:WEB_PORT|}'));
    bridgeStr := Trim(ExpandConstant('{param:BRIDGE_PORT|}'));

    if (host = '') and (exposure = '') and (portStr = '') and (bridgeStr = '') then
        Exit;   { no web flags -> nothing to do }

    WebSilentActive := True;
    WebSilentLan := (exposure = 'LAN');
    WebSilentDontHost := (host = 'NONE');

    { FR-023: NONE + LAN is invalid -- LAN exposure needs a hosting endpoint. }
    if WebSilentDontHost and WebSilentLan then
    begin
        Log('ERROR: /WEB_HOST=NONE with /WEB_EXPOSURE=LAN is invalid -- LAN exposure requires a hosting mode (use /WEB_HOST=IIS).');
        Result := False; Exit;
    end;

    if portStr = '' then iisPort := 80 else iisPort := StrToIntDef(portStr, -1);
    if bridgeStr = '' then bridgePort := 47291 else bridgePort := StrToIntDef(bridgeStr, -1);

    if (iisPort <> 80) and ((iisPort < 1024) or (iisPort > 65535)) then
    begin
        Log('ERROR: /WEB_PORT must be 80 or in the range 1024..65535.');
        Result := False; Exit;
    end;
    if (bridgePort < 1024) or (bridgePort > 65535) then
    begin
        Log('ERROR: /BRIDGE_PORT must be in the range 1024..65535.');
        Result := False; Exit;
    end;
    { FR-024: ports must differ. }
    if iisPort = bridgePort then
    begin
        Log('ERROR: IIS port and Bridge port must differ.');
        Result := False; Exit;
    end;
    WebIisPort := iisPort;
    WebBridgePort := bridgePort;

    { FR-019: silent + Host on IIS + IIS missing -> abort (no dialog in silent mode). }
    if WizardSilent and (not WebSilentDontHost) and (not IsIisInstalled()) then
    begin
        Log('ERROR: IIS not installed -- pass /WEB_HOST=NONE to skip IIS provisioning.');
        Result := False; Exit;
    end;
end;

function IsLanExposed(): Boolean;
begin
    { Spec 026 (M4 closure) L4: only let the flag-derived exposure win in a genuinely silent install
      (WizardSilent). Previously any single web flag (e.g. /WEB_PORT) set WebSilentActive and made an
      otherwise-interactive run ignore the LAN/localhost choice on WebNetworkPage. }
    if WizardSilent then
        Result := WebSilentLan
    else
        Result := IsWebSelected() and (WebNetworkPage.SelectedValueIndex = 1);
end;

{ Spec 026 (M4 closure) M6: the bridge exposure mode as a string, passed to web-iis-setup.ps1 so it
  can widen the CSP connect-src for LAN installs. Declared after IsLanExposed (Inno Pascal has no
  forward references). }
function GetBridgeMode(Param: String): String;
begin
    if IsLanExposed() then
        Result := 'Lan'
    else
        Result := 'Localhost';
end;

{ FR-003 + FR-003a: validate the IIS and bridge ports as the user leaves their pages. Returns
  False to block Next (range / equal-port errors); a bridge-port-in-use hit only WARNS. }
function Web_NextButton(CurPageID: Integer): Boolean;
var
    portStr: String;
    portInt: Integer;
    resultCode: Integer;
    dismResult: Integer;
begin
    Result := True;

    { US3 / FR-015..FR-018: when leaving the Hosting page with "Host on IIS" selected and IIS is
      absent, offer the three-path dialog. Yes = enable IIS now (dism); No = switch to Don't host;
      Cancel = cancel the installer. }
    if (CurPageID = WebHostPage.ID) and (WebHostPage.SelectedValueIndex = 0) and not IsIisInstalled() then
    begin
        case MsgBox(
            'IIS is required to host the web edition, but it is not installed on this machine.' + #13#10 + #13#10 +
            'Yes' + #9 + '= Enable IIS now (runs dism; may take up to a minute)' + #13#10 +
            'No' + #9 + '= Switch to "Don''t host" (lay the files down; serve them yourself)' + #13#10 +
            'Cancel' + #9 + '= Cancel the installation',
            mbConfirmation, MB_YESNOCANCEL) of
          IDYES:
            begin
                { FR-016: best-effort wait notice, then the synchronous dism call. }
                WizardForm.StatusLabel.Caption := 'Enabling IIS... this can take up to a minute.';
                WizardForm.Repaint;
                Exec('dism.exe',
                    '/online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart',
                    '', SW_HIDE, ewWaitUntilTerminated, dismResult);
                if not IsIisInstalled() then
                begin
                    MsgBox('IIS could not be enabled automatically (dism exit ' + IntToStr(dismResult) + ').' + #13#10 +
                           'Enable "Internet Information Services" via Windows Features, then re-run -- ' +
                           'or go Back and choose "Don''t host".', mbError, MB_OK);
                    Result := False;   { stay on the page }
                    Exit;
                end;
            end;
          IDNO:
            WebHostPage.SelectedValueIndex := 1;   { FR-017: switch to "Don't host" }
          IDCANCEL:
            begin
                WizardForm.Close();   { FR-018: cancel the installer }
                Result := False;
                Exit;
            end;
        end;
    end;

    { IIS port: 80 or 1024..65535. }
    if CurPageID = WebIisPortPage.ID then
    begin
        portStr := Trim(WebIisPortPage.Values[0]);
        portInt := StrToIntDef(portStr, -1);
        if (portInt <> 80) and ((portInt < 1024) or (portInt > 65535)) then
        begin
            MsgBox('IIS port must be 80 or in the range 1024..65535.', mbError, MB_OK);
            Result := False;
            Exit;
        end;
        WebIisPort := portInt;
    end;

    { Bridge port: 1024..65535, and MUST differ from the IIS port (FR-003). }
    if CurPageID = WebBridgePortPage.ID then
    begin
        portStr := Trim(WebBridgePortPage.Values[0]);
        portInt := StrToIntDef(portStr, -1);
        if (portInt < 1024) or (portInt > 65535) then
        begin
            MsgBox('Bridge port must be between 1024 and 65535.', mbError, MB_OK);
            Result := False;
            Exit;
        end;
        if portInt = WebIisPort then
        begin
            MsgBox('IIS port and Bridge port must differ.' + #13#10 +
                   'IIS serves the web files; the engine bridge serves WebSocket frames -- ' +
                   'they cannot share one TCP port.', mbError, MB_OK);
            Result := False;
            Exit;
        end;
        WebBridgePort := portInt;

        { FR-003a: non-blocking warning if the bridge port is already in use. Degrades to
          no-warning when PowerShell / Test-NetConnection is unavailable. }
        if Exec('powershell.exe',
            '-NoProfile -ExecutionPolicy Bypass -Command "if ((Test-NetConnection -ComputerName 127.0.0.1 -Port ' +
            IntToStr(portInt) + ' -InformationLevel Quiet -WarningAction SilentlyContinue)) { exit 9 } else { exit 0 }"',
            '', SW_HIDE, ewWaitUntilTerminated, resultCode) then
        begin
            if resultCode = 9 then
                MsgBox('Port ' + IntToStr(portInt) + ' appears to be in use already.' + #13#10 +
                       'You can continue (the engine will report a bind error if it really is taken) ' +
                       'or go back and pick another port.', mbInformation, MB_OK);
        end;
    end;
end;

{ FR-006: hide the web-edition pages when the web component is unticked. Also skip the hosting
  page when only web\service was selected (no web\iis). }
function Web_Skip(PageID: Integer): Boolean;
begin
    Result := False;

    if not IsWebSelected() then
    begin
        { Spec 026 (M4 closure) M4 / FR-006: also skip the install-summary page for plugin-only
          installs -- otherwise a user who unticked the Web edition still sees an "AKML SQL Web is
          ready" page describing a PIN / URL / thumbprint that were never produced. }
        if (PageID = WebHostPage.ID) or (PageID = WebNetworkPage.ID) or
           (PageID = WebIisPortPage.ID) or (PageID = WebBridgePortPage.ID) or
           (PageID = InstallSummaryPage.ID) then
            Result := True;
        Exit;
    end;

    { Web selected but IIS hosting not chosen -> the hosting page is moot. }
    if (PageID = WebHostPage.ID) and not WizardIsComponentSelected('web\iis') then
        Result := True;
end;

procedure Web_PostInstall();
var
    summaryPath: String;
    summary: TStringList;
    appdata: String;
    bridgeMode: String;
    bridgeArgs: String;
    bridgeResult: Integer;
    iisPortSuffix: String;
    pollTries: Integer;
    svcTries: Integer;
    aclResult: Integer;
    aclOk: Boolean;
    resultCode: Integer;
    serviceRunning: Boolean;
    rawText: AnsiString;   { LoadStringFromFile requires a var AnsiString in Unicode Inno Setup }
begin
    if not IsWebSelected() then Exit;

    { Spec 026 (M4 closure) M3: persist the bridge port machine-wide so Web_Uninstall (a separate
      process that never runs the setup wizard) can delete the netsh sslcert binding on the REAL
      port. Without this WebBridgePort is 0 at uninstall and the binding leaks. }
    RegWriteDWordValue(HKLM, 'Software\AKML SQL\Web', 'BridgePort', WebBridgePort);

    { Spec 025 (M3 bridge closure) T008 / FR-027 -- write the Bridge section into the engine
      config.json so EngineHost.RunAsync starts a WebSocketTransport alongside the named pipe.
      Receives the BRIDGE port (not the IIS port). Idempotent on re-run. }
    if IsLanExposed() then
        bridgeMode := 'Lan'
    else
        bridgeMode := 'Localhost';
    { Spec 026 (M4 closure) C3: write the bridge section to the WEB-edition config under
      CommonAppData\AKML SQL Web\config.json -- the exact file the AkmlSqlWebEngine service
      reads (see the sc.exe binPath above). Passing -ConfigPath explicitly keeps the per-user
      IDE-plugin config (AppData\AKML SQL\config.json) byte-for-byte untouched (FR-006/SC-007). }
    bridgeArgs := '-NoProfile -ExecutionPolicy Bypass -File "' +
        ExpandConstant('{tmp}\web-config-bridge.ps1') +
        '" -Port ' + IntToStr(WebBridgePort) +
        ' -Mode ' + bridgeMode +
        ' -ConfigPath "' + ExpandConstant('{commonappdata}\AKML SQL Web\config.json') + '"';
    Exec('powershell.exe', bridgeArgs, '', SW_HIDE, ewWaitUntilTerminated, bridgeResult);

    { Capture the engine-generated pairing PIN. The engine writes it to
      %CommonAppData%\AKML SQL Web\pairing-pin.txt on first start (spec 026 FR-008). }
    appdata := ExpandConstant('{commonappdata}\AKML SQL Web');

    { T021 / FR-010: lock the shared-state dir to Administrators + SYSTEM only (no standard-user
      read -- a leaked PIN allows local operator impersonation). SIDs (not names) keep this
      locale-independent: S-1-5-32-544 = Administrators, S-1-5-18 = SYSTEM. Best-effort. }
    ForceDirectories(appdata);
    aclOk := Exec('icacls.exe',
        '"' + appdata + '" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" /grant:r "*S-1-5-18:(OI)(CI)F"',
        '', SW_HIDE, ewWaitUntilTerminated, aclResult) and (aclResult = 0);

    { Spec 026 (M4 closure) C2/C3 ordering fix: start the service ONLY NOW -- after the config
      (web-config-bridge.ps1 above) is written and the shared-state dir is locked down. The engine
      then reads an enabled bridge config and writes pairing-pin.txt into the already-hardened dir.
      (Moved here from [Run]; the service was created with start= auto so boots still auto-start it.) }
    if WizardIsComponentSelected('web\service') then
        Exec('sc.exe', 'start AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { T022 / FR-011: poll for the engine-written pairing PIN (LAN mode only) for up to 30 s. }
    PairingPin := '';
    if IsLanExposed() then
    begin
        pollTries := 0;
        while (pollTries < 30) and (PairingPin = '') do
        begin
            if FileExists(appdata + '\pairing-pin.txt') then
                if LoadStringFromFile(appdata + '\pairing-pin.txt', rawText) then
                    PairingPin := Trim(String(rawText));
            if PairingPin = '' then
            begin
                Sleep(1000);
                pollTries := pollTries + 1;
            end;
        end;
    end;

    { Read the cert thumbprint web-tls-setup.ps1 wrote. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
    begin
        if LoadStringFromFile(appdata + '\certs\thumbprint.txt', rawText) then
            TlsThumbprint := Trim(String(rawText));
    end;

    { FR-007a: confirm the engine service reached Running within ~10 s (when the service
      component was installed). The summary flags a non-running service; the install is not failed. }
    serviceRunning := True;
    if WizardIsComponentSelected('web\service') then
    begin
        serviceRunning := False;
        svcTries := 0;
        while (svcTries < 10) and (not serviceRunning) do
        begin
            if Exec('powershell.exe',
                '-NoProfile -ExecutionPolicy Bypass -Command "if ((Get-Service AkmlSqlWebEngine -ErrorAction SilentlyContinue).Status -eq ''Running'') { exit 0 } else { exit 1 }"',
                '', SW_HIDE, ewWaitUntilTerminated, resultCode) then
            begin
                if resultCode = 0 then serviceRunning := True;
            end;
            if not serviceRunning then
            begin
                Sleep(1000);
                svcTries := svcTries + 1;
            end;
        end;
    end;

    { FR-005 (reconciled): the IIS bundle is served over HTTP in both modes (localhost and LAN) --
      only the engine bridge uses TLS (wss on the bridge port); see spec 026 Out-of-scope #3. The
      URL line shows the IIS port, omitting ':80'. The separate 'Bridge port' line notes wss/TLS. }
    if WebIisPort = 80 then
        iisPortSuffix := ''
    else
        iisPortSuffix := ':' + IntToStr(WebIisPort);

    summary := TStringList.Create;
    try
        summary.Add('AKML SQL Web -- install summary');
        summary.Add('=================================');
        summary.Add('');
        if IsLanExposed() then
        begin
            summary.Add('URL:         http://' + GetComputerNameString() + iisPortSuffix + '/');
            summary.Add('Bridge port: ' + IntToStr(WebBridgePort) + ' (wss, TLS)');
            if PairingPin <> '' then
                summary.Add('Pairing PIN: ' + PairingPin)
            else
                summary.Add('Pairing PIN: not yet generated -- start the AkmlSqlWebEngine service, then re-read this file.');
            summary.Add('TLS thumb:   ' + TlsThumbprint);
            summary.Add('');
            summary.Add('To trust the certificate on a different machine:');
            summary.Add('  1. Open ' + appdata + '\certs\bridge.cer');
            summary.Add('  2. Install certificate -> Local Machine -> Trusted Root Certification Authorities');
        end
        else
        begin
            summary.Add('URL:         http://localhost' + iisPortSuffix + '/');
            summary.Add('Bridge port: ' + IntToStr(WebBridgePort) + ' (localhost only)');
            summary.Add('Localhost only -- no LAN access. No pairing PIN required.');
        end;
        { FR-007a: flag a service that did not start. }
        if not serviceRunning then
        begin
            summary.Add('');
            summary.Add('WARNING: the AkmlSqlWebEngine service did not reach Running within 10s.');
            summary.Add('  Check Event Viewer + ' + appdata + '\install.log, then run: sc start AkmlSqlWebEngine');
        end;
        { Spec 026 (M4 closure) M1: flag a failed ACL lockdown -- the PIN / token store may be
          standard-user readable until the operator hardens the directory manually. }
        if not aclOk then
        begin
            summary.Add('');
            summary.Add('WARNING: could not lock down ' + appdata + ' (icacls exit ' + IntToStr(aclResult) + ').');
            summary.Add('  The pairing PIN / token store may be readable by standard users on this host. Re-run:');
            summary.Add('  icacls "' + appdata + '" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F');
        end;
        if not WizardIsComponentSelected('web\iis') then
        begin
            summary.Add('');
            summary.Add('Host it yourself: the web files are at  ' + ExpandConstant('{app}\Web'));
            summary.Add('  Quick local server:  cd "' + ExpandConstant('{app}\Web') + '" && python -m http.server 8080');
            summary.Add('  Then browse to:      http://localhost:8080/');
        end;
        summaryPath := appdata + '\INSTALL-SUMMARY.txt';
        summary.SaveToFile(summaryPath);

        { Spec 026 (M4 closure) M4 / FR-005: mirror the summary onto the wizard success page. The
          page was created promising "the pairing PIN + TLS thumbprint + browser URL are below" but
          its body was never populated -- now it shows the same content written to the file. }
        InstallSummaryPage.MsgLabel.Caption := summary.Text;
    finally
        summary.Free;
    end;
end;

procedure Web_Uninstall();
var
    appdata: String;
    resultCode: Integer;
    bridgePort: Cardinal;
begin
    appdata := ExpandConstant('{commonappdata}\AKML SQL Web');

    { Spec 026 (M4 closure) M3: read the bridge port persisted at install. The uninstaller never
      runs the wizard, so the WebBridgePort global is 0 here -- using it would delete the sslcert
      binding on port 0 and leak the real one. Fall back to the default if the value is missing. }
    if not RegQueryDWordValue(HKLM, 'Software\AKML SQL\Web', 'BridgePort', bridgePort) then
        bridgePort := 47291;

    { Remove the netsh sslcert binding on the REAL bridge port (LAN installs only). Done before the
      DelTree below so certs\thumbprint.txt still exists as the LAN-install marker. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
        Exec('netsh.exe', 'http delete sslcert ipport=0.0.0.0:' + IntToStr(bridgePort),
             '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { Remove the IIS site if installed. }
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -Command "Import-Module WebAdministration; Remove-WebSite -Name AkmlSqlWeb -ErrorAction SilentlyContinue"',
         '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { Spec 026 (M4 closure) M3: the web-edition state (PIN, tokens, certs, config, summary) lives
      under CommonAppData\AKML SQL Web -- the previous code deleted UserAppData\AKML SQL Web, which
      is empty, leaving the real secrets (incl. pairing-pin.txt + tokens.json) on disk. Ask, then
      delete the CORRECT directory. }
    if MsgBox('Delete AKML SQL Web data at "' + appdata + '"?' + #13#10 +
              '(This includes the pairing PIN, bearer tokens, TLS cert, and config.)',
              mbConfirmation, MB_YESNO) = IDYES then
        DelTree(appdata, True, True, True);

    { Clean up the persisted bridge-port marker. }
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\AKML SQL\Web');

    { Never touch %AppData%/AKML SQL/ -- that's IDE plugin state (SC-007). }
end;

{ Note: the LAN URL uses Inno Setup's built-in GetComputerNameString (no custom helper -- a
  custom GetComputerNameString would collide with the built-in, and Inno Pascal Script has no
  forward references, so a differently-named helper would have to precede Web_PostInstall). }
