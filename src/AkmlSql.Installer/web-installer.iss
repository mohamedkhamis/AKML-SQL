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
; Spec 026: the integration (#include + 5 hook calls + a new ShouldSkipPage) is now wired into
; AkmlSqlSetup.iss, and the single port input is split into an IIS port (default 80) and a bridge
; port (default 47291) -- they must differ (HTTP.SYS cannot share one port between IIS and the
; engine's HttpListener). NOT YET COMPILE-VERIFIED in this environment: a full ISCC compile needs
; the entire product built (shell VSIXes via VS MSBuild + engine/updater/analyzer publishes + the
; web bundle). The first interactive install on a Windows host with IIS + Inno Setup 7 + admin is
; the acceptance test (spec 026 T041) and the verification gate for SC-001..SC-003.
;
; DEFERRED to a later spec-026 turn (additive to this file's Web_* procedures): the IIS-not-installed
; three-path dialog (US3 / FR-014..FR-020), the silent-install flags (US4 / FR-021..FR-026), the
; Administrators+SYSTEM ACL on the CommonAppData dir + the 30 s pairing-pin.txt poll + the
; service-start check (US2-installer / FR-007a, FR-010, FR-011).
;
; ─── Files this script depends on ──────────────────────────────────────────
;   web-iis-setup.ps1     -- IIS site provisioning + MIME + CSP   (receives the IIS port)
;   web-tls-setup.ps1     -- self-signed cert + netsh sslcert      (receives the bridge port)
;   web-firewall.ps1      -- firewall inbound rule                 (receives the bridge port)
;   web-config-bridge.ps1 -- writes the engine config bridge section (receives the bridge port)

[Types]
; The existing AkmlSqlSetup.iss already defines Full / Compact / Custom. Not redefined here.

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
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-iis-setup.ps1"" -Port {code:GetIisPort} -PhysicalPath ""{app}\Web"""; \
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
Filename: "sc.exe"; \
    Parameters: "create AkmlSqlWebEngine binPath= ""\""{app}\Engine\AkmlSql.Engine.exe\"" --config \""{userappdata}\AKML SQL Web\config.json\"""" start= auto DisplayName= ""AKML SQL Web Engine"""; \
    StatusMsg: "Installing AKML SQL Web Engine service..."; \
    Components: web\service; \
    Flags: runhidden

Filename: "sc.exe"; \
    Parameters: "start AkmlSqlWebEngine"; \
    StatusMsg: "Starting AKML SQL Web Engine service..."; \
    Components: web\service; \
    Flags: runhidden

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

    { Seed the port globals with the page defaults so a silent / skipped flow still has values. }
    WebIisPort := 80;
    WebBridgePort := 47291;
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

function IsLanExposed(): Boolean;
begin
    Result := IsWebSelected() and (WebNetworkPage.SelectedValueIndex = 1);
end;

{ FR-003 + FR-003a: validate the IIS and bridge ports as the user leaves their pages. Returns
  False to block Next (range / equal-port errors); a bridge-port-in-use hit only WARNS. }
function Web_NextButton(CurPageID: Integer): Boolean;
var
    portStr: String;
    portInt: Integer;
    resultCode: Integer;
begin
    Result := True;

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
        if (PageID = WebHostPage.ID) or (PageID = WebNetworkPage.ID) or
           (PageID = WebIisPortPage.ID) or (PageID = WebBridgePortPage.ID) then
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
begin
    if not IsWebSelected() then Exit;

    { Spec 025 (M3 bridge closure) T008 / FR-027 -- write the Bridge section into the engine
      config.json so EngineHost.RunAsync starts a WebSocketTransport alongside the named pipe.
      Receives the BRIDGE port (not the IIS port). Idempotent on re-run. }
    if IsLanExposed() then
        bridgeMode := 'Lan'
    else
        bridgeMode := 'Localhost';
    bridgeArgs := '-NoProfile -ExecutionPolicy Bypass -File "' +
        ExpandConstant('{tmp}\web-config-bridge.ps1') +
        '" -Port ' + IntToStr(WebBridgePort) +
        ' -Mode ' + bridgeMode;
    Exec('powershell.exe', bridgeArgs, '', SW_HIDE, ewWaitUntilTerminated, bridgeResult);

    { Capture the engine-generated pairing PIN. The engine writes it to
      %CommonAppData%\AKML SQL Web\pairing-pin.txt on first start (spec 026 FR-008). }
    appdata := ExpandConstant('{commonappdata}\AKML SQL Web');
    if FileExists(appdata + '\pairing-pin.txt') then
    begin
        if LoadStringFromFile(appdata + '\pairing-pin.txt', PairingPin) then
            PairingPin := Trim(PairingPin);
    end;

    { Read the cert thumbprint web-tls-setup.ps1 wrote. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
    begin
        if LoadStringFromFile(appdata + '\certs\thumbprint.txt', TlsThumbprint) then
            TlsThumbprint := Trim(TlsThumbprint);
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
            summary.Add('Pairing PIN: ' + PairingPin);
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
        if not WizardIsComponentSelected('web\iis') then
        begin
            summary.Add('');
            summary.Add('Host it yourself: the web files are at  ' + ExpandConstant('{app}\Web'));
            summary.Add('  Quick local server:  cd "' + ExpandConstant('{app}\Web') + '" && python -m http.server 8080');
            summary.Add('  Then browse to:      http://localhost:8080/');
        end;
        summaryPath := appdata + '\INSTALL-SUMMARY.txt';
        summary.SaveToFile(summaryPath);
    finally
        summary.Free;
    end;
end;

procedure Web_Uninstall();
var
    appdata: String;
    resultCode: Integer;
begin
    appdata := ExpandConstant('{commonappdata}\AKML SQL Web');

    { Remove the netsh sslcert binding on the BRIDGE port. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
        Exec('netsh.exe', 'http delete sslcert ipport=0.0.0.0:' + IntToStr(WebBridgePort),
             '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { Remove the IIS site if installed. }
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -Command "Import-Module WebAdministration; Remove-WebSite -Name AkmlSqlWeb -ErrorAction SilentlyContinue"',
         '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { Ask before deleting %AppData%/AKML SQL Web/. }
    if MsgBox('Delete user data at "%AppData%\AKML SQL Web"? ' +
              '(Keeps your AI keys + connection records if you say No.)',
              mbConfirmation, MB_YESNO) = IDYES then
        DelTree(ExpandConstant('{userappdata}\AKML SQL Web'), True, True, True);

    { Never touch %AppData%/AKML SQL/ -- that's IDE plugin state (SC-007). }
end;

{ Helper -- the computer name for the LAN URL. }
function GetComputerNameString(): String;
var
    name: String;
begin
    if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName', 'ComputerName', name) then
        name := 'localhost';
    Result := name;
end;
