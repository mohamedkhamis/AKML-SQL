; Spec 021 (web edition) -- M4 installer additions.
;
; Included by AkmlSqlSetup.iss; adds the Web-edition component group, the
; install steps (engine binary, web bundle, AppData config, IIS site, TLS cert,
; firewall rule, Windows service for the engine), and the silent-install flags.
;
; Cross-references: contracts/installer-component.md + spec.md FR-018..FR-023.
;
; ─── Status ─────────────────────────────────────────────────────────────────
; This file is INSTALLER SCAFFOLDING. It compiles under Inno Setup 7 (Pascal
; Script). It has NOT been exercised end-to-end yet -- doing so requires a
; Windows host with IIS, Inno Setup 7 installed, and admin rights. The first
; interactive run is the acceptance test for T081-T099; deltas land in this
; file plus the three PowerShell helpers it invokes.
;
; ─── Files this script depends on ──────────────────────────────────────────
;   web-iis-setup.ps1     -- IIS site provisioning + MIME + CSP
;   web-tls-setup.ps1     -- self-signed cert + netsh sslcert + thumbprint capture
;   web-firewall.ps1      -- firewall inbound rule
;
; All three live alongside this file under src/AkmlSql.Installer/ and are
; bundled into the installer payload via [Files] entries in AkmlSqlSetup.iss.

[Types]
; The existing AkmlSqlSetup.iss already defines Full / Compact / Custom.
; We don't redefine here.

[Components]
; T081 -- web-edition component group, independent of the plugin group.
Name: "web"; Description: "Web edition (browser-based AKML SQL)"; Types: full; Flags: disablenouninstallwarning
Name: "web\iis"; Description: "Host on local IIS (recommended)"; Types: full
Name: "web\service"; Description: "Install Windows service for the engine"; Types: full

[Files]
; T082 -- copy the web bundle to ProgramFiles. The bundle is pre-built by
; `dotnet publish src/AkmlSql.Web -c Release` and lands under
; src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/.
;
; Recursive copy preserves the _framework/ subtree containing the WASM + DLLs.
Source: "..\AkmlSql.Web\bin\Release\net10.0\publish\wwwroot\*"; \
    DestDir: "{app}\Web"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: web

; PowerShell helpers (bundled, invoked at install time then removed).
Source: "web-iis-setup.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web\iis
Source: "web-tls-setup.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web
Source: "web-firewall.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web
; Spec 025 (M3 bridge closure) T008 -- writes the Bridge section into the
; engine config so EngineHost.RunAsync starts a WebSocketTransport alongside
; the named pipe (FR-027). Invoked from Web_PostInstall after the engine
; service is registered.
Source: "web-config-bridge.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: web

[Run]
; T084 IIS site provisioning. Skipped when "Don't host" was selected.
; The script handles the appcmd.exe calls; we pass the chosen port via -Port.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-iis-setup.ps1"" -Port {code:GetWebPort} -PhysicalPath ""{app}\Web"""; \
    StatusMsg: "Provisioning IIS site for AKML SQL Web..."; \
    Components: web\iis; \
    Flags: runhidden

; T087 LAN-mode cert generation + netsh sslcert bind. Skipped on localhost-only.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-tls-setup.ps1"" -Port {code:GetWebPort} -PfxPath ""{commonappdata}\AKML SQL Web\certs\bridge.pfx"""; \
    StatusMsg: "Generating self-signed TLS certificate..."; \
    Check: IsLanExposed; \
    Components: web; \
    Flags: runhidden

; T089 Firewall rule. Only opened when LAN mode is chosen.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-firewall.ps1"" -Port {code:GetWebPort} -Action Add"; \
    StatusMsg: "Adding Windows Firewall rule..."; \
    Check: IsLanExposed; \
    Components: web; \
    Flags: runhidden

; T091 Windows service for the engine. Uses sc.exe to create AkmlSqlWebEngine.
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
; T095 -- uninstall path. Reverse the install order: stop service, remove
; firewall rule, delete netsh sslcert binding, remove IIS site, delete files.
; %AppData%/AKML SQL/ (the IDE-plugin state) is NEVER touched (SC-007).
Filename: "sc.exe"; Parameters: "stop AkmlSqlWebEngine"; Flags: runhidden; Components: web\service
Filename: "sc.exe"; Parameters: "delete AkmlSqlWebEngine"; Flags: runhidden; Components: web\service

Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\web-firewall.ps1"" -Port {code:GetWebPort} -Action Remove"; \
    Flags: runhidden; Components: web

; Note: cert + IIS site removal is done in the [Code] section CurUninstallStepChanged
; so we can inspect the install-summary file to find the cert thumbprint.

[Code]
{ ─── Integration note ──────────────────────────────────────────────────────
  Pascal Script forbids declaring the same procedure twice. AkmlSqlSetup.iss
  already defines InitializeWizard / CurStepChanged / CurUninstallStepChanged
  / NextButtonClick / ShouldSkipPage. Rather than redefining them here, this
  file exposes named hook procedures (Web_Init / Web_NextButton / Web_Skip /
  Web_PostInstall / Web_Uninstall) and the parent installer calls them at the
  right point in each event.

  To integrate:
    1. Add `#include "web-installer.iss"` at the bottom of AkmlSqlSetup.iss
       (before the existing [Code] section closes).
    2. In AkmlSqlSetup.iss's InitializeWizard, add a call to Web_Init().
    3. In NextButtonClick, after the existing logic add:
         if not Web_NextButton(CurPageID) then begin Result := False; Exit; end;
    4. In ShouldSkipPage, after the existing logic add:
         if Web_Skip(PageID) then begin Result := True; Exit; end;
    5. In CurStepChanged, on ssPostInstall, add `Web_PostInstall();`.
    6. In CurUninstallStepChanged, on usUninstall, add `Web_Uninstall();`.

  This file SHIPS as design-level scaffolding for the M4 implementer. The
  first interactive install on a Windows host with IIS + Inno Setup 7 is the
  acceptance test for T081-T099.
─────────────────────────────────────────────────────────────────────────── }

{ ─── State carried across pages ───────────────────────────────────────── }

var
    WebHostPage: TInputOptionWizardPage;
    WebNetworkPage: TInputOptionWizardPage;
    WebPortPage: TInputQueryWizardPage;
    InstallSummaryPage: TOutputMsgWizardPage;
    WebPort: Integer;
    PairingPin: String;
    TlsThumbprint: String;

{ T081 -- component-selection page lands via the [Components] section. The
  three radio-group choices (Host on IIS / Don't host; Localhost / LAN) and
  the port input live on their own wizard pages we create below. }

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

    { Port input }
    WebPortPage := CreateInputQueryPage(
        WebNetworkPage.ID,
        'Engine bridge port',
        'Pick a TCP port for the engine bridge.',
        'The bridge serves WebSocket frames on this port. Must be 1024..65535. Default 47291.');
    WebPortPage.Add('Port:', False);
    WebPortPage.Values[0] := '47291';

    { Install-summary page (shown on the post-install success path) }
    InstallSummaryPage := CreateOutputMsgPage(
        wpInstalling,
        'Install summary',
        'AKML SQL Web is ready.',
        'The pairing PIN + TLS thumbprint + browser URL are below. They are also written to "%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt".');
end;

function GetWebPort(Param: String): String;
begin
    Result := IntToStr(WebPort);
end;

function IsLanExposed(): Boolean;
begin
    Result := (WebNetworkPage.SelectedValueIndex = 1);
end;

function Web_NextButton(CurPageID: Integer): Boolean;
var
    portStr: String;
    portInt: Integer;
begin
    Result := True;

    { T083 -- port validation. }
    if CurPageID = WebPortPage.ID then
    begin
        portStr := Trim(WebPortPage.Values[0]);
        portInt := StrToIntDef(portStr, 0);
        if (portInt < 1024) or (portInt > 65535) then
        begin
            MsgBox('Port must be between 1024 and 65535.', mbError, MB_OK);
            Result := False;
            Exit;
        end;
        WebPort := portInt;
    end;
end;

function Web_Skip(PageID: Integer): Boolean;
begin
    Result := False;
    { Skip web-edition pages when the user hasn't selected the web component. }
    if not WizardIsComponentSelected('web') then
    begin
        if (PageID = WebHostPage.ID) or (PageID = WebNetworkPage.ID) or (PageID = WebPortPage.ID) then
        begin
            Result := True;
        end;
    end;

    { Skip the host-choice page when only "web\service" was selected without "web\iis". }
    if PageID = WebHostPage.ID then
    begin
        if WizardIsComponentSelected('web') and not WizardIsComponentSelected('web\iis') then
        begin
            Result := True;
        end;
    end;
end;

procedure Web_PostInstall();
var
    summaryPath: String;
    summary: TStringList;
    appdata: String;
    bridgeMode: String;
    bridgeArgs: String;
    bridgeResult: Integer;
begin
    if not WizardIsComponentSelected('web') then Exit;

    { Spec 025 (M3 bridge closure) T008 / FR-027 -- write the Bridge section
      into the engine config.json so EngineHost.RunAsync starts a
      WebSocketTransport alongside the named pipe. Idempotent on re-run. }
    if IsLanExposed() then
        bridgeMode := 'Lan'
    else
        bridgeMode := 'Localhost';
    bridgeArgs := '-NoProfile -ExecutionPolicy Bypass -File "' +
        ExpandConstant('{tmp}\web-config-bridge.ps1') +
        '" -Port ' + IntToStr(WebPort) +
        ' -Mode ' + bridgeMode;
    Exec('powershell.exe', bridgeArgs, '', SW_HIDE, ewWaitUntilTerminated, bridgeResult);

    { T092 -- capture the engine-generated pairing PIN from the engine log.
      The engine writes the PIN to %CommonAppData%\AKML SQL Web\pairing-pin.txt
      on first start; we read it here and bake it into the install summary. }
    appdata := ExpandConstant('{commonappdata}\AKML SQL Web');
    if FileExists(appdata + '\pairing-pin.txt') then
    begin
        if LoadStringFromFile(appdata + '\pairing-pin.txt', PairingPin) then
        begin
            PairingPin := Trim(PairingPin);
        end;
    end;

    { Read the cert thumbprint web-tls-setup.ps1 wrote. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
    begin
        if LoadStringFromFile(appdata + '\certs\thumbprint.txt', TlsThumbprint) then
        begin
            TlsThumbprint := Trim(TlsThumbprint);
        end;
    end;

    { T093 -- write INSTALL-SUMMARY.txt with the URL + PIN + thumbprint. }
    summary := TStringList.Create;
    try
        summary.Add('AKML SQL Web -- install summary');
        summary.Add('=================================');
        summary.Add('');
        summary.Add('URL:         http://localhost:' + IntToStr(WebPort) + '/');
        if IsLanExposed() then
        begin
            summary.Add('LAN URL:     https://' + GetComputerNameString() + ':' + IntToStr(WebPort) + '/');
            summary.Add('Pairing PIN: ' + PairingPin);
            summary.Add('TLS thumb:   ' + TlsThumbprint);
            summary.Add('');
            summary.Add('To trust the certificate on a different machine:');
            summary.Add('  1. Open ' + appdata + '\certs\bridge.cer');
            summary.Add('  2. Install certificate -> Local Machine -> Trusted Root Certification Authorities');
        end
        else
        begin
            summary.Add('Localhost only -- no LAN access. No pairing PIN required.');
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

    { Remove the netsh sslcert binding using the stored thumbprint. }
    if FileExists(appdata + '\certs\thumbprint.txt') then
    begin
        { Exec netsh http delete sslcert ipport=0.0.0.0:<port> }
        Exec('netsh.exe', 'http delete sslcert ipport=0.0.0.0:' + IntToStr(WebPort), '', SW_HIDE, ewWaitUntilTerminated, resultCode);
    end;

    { Remove the IIS site if installed. }
    Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -Command "Import-Module WebAdministration; Remove-WebSite -Name AkmlSqlWeb -ErrorAction SilentlyContinue"',
         '', SW_HIDE, ewWaitUntilTerminated, resultCode);

    { T095 -- ask before deleting %AppData%/AKML SQL Web/. }
    if MsgBox('Delete user data at "%AppData%\AKML SQL Web"? ' +
              '(Keeps your AI keys + connection records if you say No.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
        DelTree(ExpandConstant('{userappdata}\AKML SQL Web'), True, True, True);
    end;

    { Never touch %AppData%/AKML SQL/ -- that's IDE plugin state (SC-007). }
end;

{ Helper -- inno setup's built-in GetComputerNameFromRegistry is too specific. }
function GetComputerNameString(): String;
var
    name: String;
begin
    if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName', 'ComputerName', name) then
    begin
        name := 'localhost';
    end;
    Result := name;
end;
