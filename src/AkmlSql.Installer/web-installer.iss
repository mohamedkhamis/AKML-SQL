; Spec 045. The WebHost choice owns provisioning; legacy components are retained only
; for upgrade compatibility pending the disposable-VM component migration spike.
[Components]
Name: "web"; Description: "Web edition (browser-based AKML SQL)"; Types: full; Flags: disablenouninstallwarning
Name: "web\iis"; Description: "Legacy IIS selection"; Check: WebLegacyComponent
Name: "web\service"; Description: "Legacy service selection"; Check: WebLegacyComponent

[InstallDelete]
Type: filesandordirs; Name: "{app}\Web\_framework"; Components: web

[Files]
Source: "..\AkmlSql.Web\bin\Release\net10.0\publish\wwwroot\*"; Excludes: "*.br,*.gz"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: web
Source: "web-iis-setup.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-https-binding.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-iis-repair.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-tls-setup.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-firewall.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-config-bridge.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-hosting-check.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-iis-remove.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web
Source: "web-hosting-repair.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Support\web-tls-setup.ps1"" -Port {code:GetBridgePort} -PfxPath ""{commonappdata}\AKML SQL Web\certs\bridge.pfx"""; Check: IsLanExposed; Components: web; Flags: runhidden
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Support\web-iis-setup.ps1"" -Port {code:GetIisPort} -PhysicalPath ""{app}\Web"" -Mode {code:GetBridgeMode} -HttpsPort {code:GetHttpsPortArg}"; Check: WebHostIsIis; Components: web; Flags: runhidden
Filename: "sc.exe"; Parameters: "create AkmlSqlWebEngine binPath= ""\""{app}\Engine\AkmlSql.Engine.exe\"" --web --config \""{commonappdata}\AKML SQL Web\config.json\"""" start= auto DisplayName= ""AKML SQL Web Engine"""; Check: IsWebSelected; Components: web; Flags: runhidden
Filename: "sc.exe"; Parameters: "config AkmlSqlWebEngine start= auto"; Check: AkmlWebServiceExists and WebHostIsService; Flags: runhidden
Filename: "sc.exe"; Parameters: "config AkmlSqlWebEngine start= delayed-auto"; Check: AkmlWebServiceExists and not WebHostIsService; Flags: runhidden
Filename: "sc.exe"; Parameters: "failure AkmlSqlWebEngine reset= 86400 actions= restart/5000/restart/10000/restart/60000"; Check: AkmlWebServiceExists; Flags: runhidden
Filename: "sc.exe"; Parameters: "failureflag AkmlSqlWebEngine 1"; Check: AkmlWebServiceExists; Flags: runhidden

[Icons]
Name: "{group}\Repair AKML SQL Web hosting"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Support\web-hosting-repair.ps1"""; WorkingDir: "{app}\Support"; Check: WebHostHasHost; Components: web

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Support"
Type: filesandordirs; Name: "{app}\Web"

[Code]
const
  WebHostServiceIdx = 0;
  WebHostIisIdx = 1;
  WebHostNoneIdx = 2;
var
  WebHostPage, WebNetworkPage: TInputOptionWizardPage;
  WebPortsPage: TInputQueryWizardPage;
  InstallSummaryPage: TOutputMsgWizardPage;
  WebHostNotice: TNewStaticText;
  WebHost, Exposure, PreviousHost, PreviousExposure, WebHostingSource: String;
  WebIisPort, WebBridgePort, WebHttpsPort, PreviousIisPort, PreviousBridgePort: Integer;
  WebSilentActive, WebHostShown, WebStateAclOk: Boolean;
  WebReadiness: Integer;
  WebReadinessReason: String;
  WebFieldTop: Integer;
  PairingPin, TlsThumbprint: String;

function WebRegQueryDWord(const Name: String; var Value: Cardinal): Boolean;
begin
    Result := RegQueryDWordValue(HKLM64, 'Software\AKML SQL\Web', Name, Value)
              or RegQueryDWordValue(HKLM32, 'Software\AKML SQL\Web', Name, Value);
end;

function WebRegQueryString(const Name: String; var Value: String): Boolean;
begin
    Result := RegQueryStringValue(HKLM64, 'Software\AKML SQL\Web', Name, Value)
              or RegQueryStringValue(HKLM32, 'Software\AKML SQL\Web', Name, Value);
end;

{ Locale-safe service presence check, also used while inferring a pre-045 install. }
function AkmlWebServiceExists(): Boolean;
var
    resultCode: Integer;
begin
    Result := Exec('sc.exe', 'query AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, resultCode)
              and (resultCode = 0);
end;

function ReadHostingText(const Path: String): String;
var
    Raw: AnsiString;
begin
    Result := '';
    if LoadStringFromFile(Path, Raw) then Result := Utf8Decode(Raw);
    StringChangeEx(Result, #65279, '', True);
end;

procedure ReadPreviousHosting();
var
    Host, Exposure, IisMode, AppHost, Config: String;
    Port: Cardinal;
    HasPort, ServiceExists: Boolean;
begin
    Host := ''; Exposure := ''; IisMode := '';
    WebRegQueryString('Host', Host);
    WebRegQueryString('Exposure', Exposure);
    WebRegQueryString('IisMode', IisMode);
    HasPort := WebRegQueryDWord('BridgePort', Port);
    ServiceExists := AkmlWebServiceExists();
    AppHost := ReadHostingText(ExpandConstant('{sys}\inetsrv\config\applicationHost.config'));
    Config := ReadHostingText(ExpandConstant('{commonappdata}\AKML SQL Web\config.json'));
    PreviousHost := InferPreviousHost(Host, HasPort, ServiceExists, AppHost);
    PreviousExposure := InferPreviousExposure(Exposure, Config, IisMode);
end;

function ReadIisReadiness(RunProbe: Boolean; var Reason: String): Integer;
var
    Present: Boolean;
    MajorVersion, W3svc, StaticContent, DefaultDocument, HttpErrors, W3svcStart, WasStart: Cardinal;
    Probe, StateName, PresentText: String;
    Code: Integer;
begin
    Present := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\InetStp');
    MajorVersion := 0; W3svc := 0; StaticContent := 0; DefaultDocument := 0; HttpErrors := 0;
    W3svcStart := 4; WasStart := 4;
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\InetStp', 'MajorVersion', MajorVersion);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\InetStp\Components', 'W3SVC', W3svc);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\InetStp\Components', 'StaticContent', StaticContent);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\InetStp\Components', 'DefaultDocument', DefaultDocument);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\InetStp\Components', 'HttpErrors', HttpErrors);
    RegQueryDWordValue(HKLM64, 'SYSTEM\CurrentControlSet\Services\W3SVC', 'Start', W3svcStart);
    RegQueryDWordValue(HKLM64, 'SYSTEM\CurrentControlSet\Services\WAS', 'Start', WasStart);
    Probe := 'skipped';
    Result := IisReadinessFrom(Present, MajorVersion, W3svc, StaticContent, DefaultDocument, HttpErrors, W3svcStart, WasStart, Probe, Reason);
    if RunProbe and (Result = IisReady) then
    begin
        Code := -1;
        if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
            '-NoProfile -NonInteractive -Command "Import-Module WebAdministration -ErrorAction Stop; $null = Get-ChildItem IIS:\Sites -ErrorAction Stop"',
            '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then Probe := 'ok'
        else Probe := 'failed';
        Result := IisReadinessFrom(Present, MajorVersion, W3svc, StaticContent, DefaultDocument, HttpErrors, W3svcStart, WasStart, Probe, Reason);
    end;
    case Result of
        IisNotEnabled: StateName := 'NotEnabled';
        IisPartMissing: StateName := 'PartMissing';
        IisDisabled: StateName := 'Disabled';
        IisToolsUnavailable: StateName := 'ToolsUnavailable';
    else StateName := 'Ready';
    end;
    if Present then PresentText := 'yes' else PresentText := 'no';
    Log('IIS readiness: ' + StateName + ' (InetStp=' + PresentText + ', W3SVC=' + IntToStr(W3svc) +
        ', Parts=' + IntToStr(StaticContent) + '/' + IntToStr(DefaultDocument) + '/' + IntToStr(HttpErrors) +
        ', W3svcStart=' + IntToStr(W3svcStart) + ', WasStart=' + IntToStr(WasStart) + ', Probe=' + Probe + ') ' + Reason);
end;

function IsWebSelected(): Boolean;
begin Result := WizardIsComponentSelected('web'); end;

function WebLegacyComponent(): Boolean;
begin Result := False; end;

function Web_PsLiteral(Value: String): String;
begin
  StringChangeEx(Value, '''', '''''', True);
  Result := '''' + Value + '''';
end;

function Web_PowerShell(Code: String; var ExitCode: Integer): Boolean;
begin
  ExitCode := -1;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Code + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function Web_Support(Name, Args: String; var ExitCode: Integer): Boolean;
begin
  ExitCode := -1;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Support\') +
    Name + '" ' + Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function WebHostIsService(): Boolean;
begin Result := WebHost = 'Service'; end;

function WebHostIsIis(): Boolean;
begin Result := IsWebSelected() and (WebHost = 'IIS'); end;

function WebHostHasHost(): Boolean;
begin Result := IsWebSelected() and ((WebHost = 'Service') or (WebHost = 'IIS')); end;

function Web_ServiceActive(): Boolean;
var Code: Integer;
begin
  Result := Web_PowerShell('$s=Get-Service AkmlSqlWebEngine -ErrorAction SilentlyContinue; ' +
    'if ($s -and $s.Status -in @(''Running'',''StartPending'')) {exit 0}; exit 1', Code) and (Code = 0);
end;

function IsOwnPreviousBridgePort(Port: Integer): Boolean;
var Previous: Cardinal;
begin
  Result := WebRegQueryDWord('BridgePort', Previous) and (Integer(Previous) = Port) and Web_ServiceActive();
end;

function Web_PortInUse(Port: Integer): Boolean;
var Code: Integer;
begin
  { An unavailable probe is a blocking uncertainty for standalone, not permission to overwrite a listener. }
  Result := not Web_PowerShell('foreach($h in @(''127.0.0.1'',''::1'')) {' +
    '$c=New-Object Net.Sockets.TcpClient; try {$t=$c.ConnectAsync($h,' + IntToStr(Port) +
    '); if($t.Wait(500) -and $c.Connected){exit 9}} catch {} finally {$c.Dispose()}}; exit 0', Code);
  Result := Result or (Code <> 0);
end;

function Web_ServicePortError(Port: Integer; Silent: Boolean): String;
begin
  Result := '';
  if (PreviousHost = 'IIS') and (PreviousIisPort = Port) and
    (Pos('<site name="akmlsqlweb"', Lowercase(ReadHostingText(ExpandConstant('{sys}\inetsrv\config\applicationHost.config')))) > 0) then
    Result := 'Port ' + IntToStr(Port) + ' belongs to the AKML SQL site in IIS, which this change removes. Choose a different port for the standalone service.'
  else if (not IsOwnPreviousBridgePort(Port)) and Web_PortInUse(Port) then
  begin
    Result := 'Port ' + IntToStr(Port) + ' is already used by another program.';
    if not Silent then Result := Result + ' Choose another port.';
  end;
end;

procedure Web_LoadPorts();
var P: Cardinal;
begin
  WebIisPort := 80; WebBridgePort := 47291; WebHttpsPort := 443;
  if WebRegQueryDWord('IisPort', P) and (P > 0) then WebIisPort := P;
  if WebRegQueryDWord('BridgePort', P) and (P > 0) then WebBridgePort := P;
  if WebRegQueryDWord('HttpsPort', P) and (P > 0) then WebHttpsPort := P;
  PreviousIisPort := WebIisPort; PreviousBridgePort := WebBridgePort;
end;

function Web_ValidateSilentFlags(): Boolean;
var Flag, FlagExposure, Iis, Bridge, Https, Reason, Line, PortError: String;
  RequestedComponents: String; Ready: Integer; CheckStandalonePort: Boolean;
begin
  Result := False;
  ReadPreviousHosting();
  Web_LoadPorts();
  Exposure := PreviousExposure;
  WebHost := PreviousHost;
  if WebHost = '' then WebHost := 'Service';
  Flag := Uppercase(Trim(ExpandConstant('{param:WEB_HOST|}')));
  FlagExposure := Uppercase(Trim(ExpandConstant('{param:WEB_EXPOSURE|}')));
  Iis := Trim(ExpandConstant('{param:WEB_PORT|}'));
  Bridge := Trim(ExpandConstant('{param:BRIDGE_PORT|}'));
  Https := Trim(ExpandConstant('{param:WEB_HTTPS_PORT|}'));
  WebSilentActive := WizardSilent or (Flag <> '') or (FlagExposure <> '') or (Iis <> '') or (Bridge <> '') or (Https <> '');
  if (FlagExposure <> '') and (FlagExposure <> 'LOCALHOST') and (FlagExposure <> 'LAN') then
  begin Log('ERROR: Unknown /WEB_EXPOSURE value: ' + FlagExposure); Exit; end;
  if FlagExposure = 'LAN' then Exposure := 'Lan';
  if FlagExposure = 'LOCALHOST' then Exposure := 'Localhost';
  if Bridge <> '' then WebBridgePort := StrToIntDef(Bridge, -1);
  if (WebBridgePort < 1024) or (WebBridgePort > 65535) then
  begin Log('ERROR: /BRIDGE_PORT must be in the range 1024..65535.'); Exit; end;
  if WebSilentActive then
  begin
    Ready := ReadIisReadiness(SilentNeedsIisProbe(Flag, PreviousHost), Reason);
    WebHost := ResolveSilentHost(Flag, FlagExposure, PreviousHost, Ready = IisReady,
      (Iis <> '') or (Https <> ''), Reason,
      HostingAddress('Service', Exposure, GetComputerNameString(), WebBridgePort, WebIisPort), Line);
    if Copy(WebHost, 1, 5) = 'Stop:' then begin Log('ERROR: ' + Copy(WebHost, 6, MaxInt)); Exit; end;
    Log(Line);
    WebHostingSource := 'previous host';
    if Flag <> '' then WebHostingSource := '/WEB_HOST';
    if (Flag = '') and (PreviousHost = 'IIS') and (Ready <> IisReady) then WebHostingSource := 'IIS fallback: ' + Reason;
  end;
  if WebHost = 'IIS' then
  begin
    if Iis <> '' then WebIisPort := StrToIntDef(Iis, -1);
    if Https <> '' then WebHttpsPort := StrToIntDef(Https, -1);
    if ((WebIisPort <> 80) and ((WebIisPort < 1024) or (WebIisPort > 65535))) or
      ((WebHttpsPort <> 443) and ((WebHttpsPort < 1024) or (WebHttpsPort > 65535))) then
    begin Log('ERROR: invalid IIS port.'); Exit; end;
    if (WebIisPort = WebBridgePort) or ((Exposure = 'Lan') and
      ((WebHttpsPort = WebIisPort) or (WebHttpsPort = WebBridgePort))) then
    begin Log('ERROR: IIS site and engine ports must differ.'); Exit; end;
  end;
  RequestedComponents := Lowercase(ExpandConstant('{param:COMPONENTS|}'));
  CheckStandalonePort := (RequestedComponents = '') or
    (Pos(',web,', ',' + RequestedComponents + ',') > 0) or (Pos('web\', RequestedComponents) > 0);
  if WizardSilent and CheckStandalonePort and (WebHost = 'Service') then
  begin
    PortError := Web_ServicePortError(WebBridgePort, True);
    if PortError <> '' then begin Log('ERROR: ' + PortError); Exit; end;
  end;
  Result := True;
end;

function IsLanExposed(): Boolean;
begin
  Result := Exposure = 'Lan';
  if not WizardSilent then Result := Result and IsWebSelected();
end;

function GetIisPort(Param: String): String;
begin Result := IntToStr(WebIisPort); end;
function GetBridgePort(Param: String): String;
begin Result := IntToStr(WebBridgePort); end;
function GetBridgeMode(Param: String): String;
begin Result := Exposure; end;
function GetHttpsPortArg(Param: String): String;
begin if Exposure = 'Lan' then Result := IntToStr(WebHttpsPort) else Result := '0'; end;

function Web_PreviousAddress(): String;
begin Result := HostingAddress(PreviousHost, PreviousExposure, GetComputerNameString(), PreviousBridgePort, PreviousIisPort); end;
function Web_CurrentAddress(): String;
begin Result := HostingAddress(WebHost, Exposure, GetComputerNameString(), WebBridgePort, WebIisPort); end;

function Web_HostAtIndex(Index: Integer): String;
begin
  case Index of
    WebHostIisIdx: Result := 'IIS';
    WebHostNoneIdx: Result := 'None';
  else Result := 'Service'; end;
end;

procedure Web_RefreshNotice();
var Text, MoveText: String;
begin
  Text := WebReadinessReason;
  if (PreviousHost = 'IIS') and (WebReadiness <> IisReady) then
    Text := Text + #13#10 + 'IIS is no longer available on this computer, so the web edition will move to the standalone service.';
  MoveText := AddressChangeNotice(Web_PreviousAddress(),
    HostingAddress(Web_HostAtIndex(WebHostPage.SelectedValueIndex), Exposure, GetComputerNameString(), WebBridgePort, WebIisPort));
  if MoveText <> '' then Text := Text + #13#10 + MoveText;
  WebHostNotice.Caption := Trim(Text);
end;

procedure WebHostActivate(Page: TWizardPage);
begin
  if WizardSilent then Exit;
  WebReadiness := ReadIisReadiness(True, WebReadinessReason);
  WebHostPage.CheckListBox.ItemEnabled[WebHostIisIdx] := WebReadiness = IisReady;
  if not WebHostShown then
  begin
    WebHostPage.SelectedValueIndex := WebHostServiceIdx;
    if PreviousHost = 'IIS' then WebHostPage.SelectedValueIndex := WebHostIisIdx;
    if PreviousHost = 'None' then WebHostPage.SelectedValueIndex := WebHostNoneIdx;
    WebHostShown := True;
  end;
  if (WebHostPage.SelectedValueIndex = WebHostIisIdx) and (WebReadiness <> IisReady) then
    WebHostPage.SelectedValueIndex := WebHostServiceIdx;
  Web_RefreshNotice();
end;

procedure WebHostClick(Sender: TObject);
begin
  if WizardSilent then Exit;
  Web_RefreshNotice();
end;

procedure WebPortsActivate(Page: TWizardPage);
var ShowIis, ShowHttps: Boolean;
begin
  if WizardSilent then Exit;
  ShowIis := WebHost = 'IIS'; ShowHttps := ShowIis and (Exposure = 'Lan');
  WebPortsPage.Edits[0].Visible := ShowIis;
  WebPortsPage.PromptLabels[0].Visible := ShowIis;
  WebPortsPage.Edits[2].Visible := ShowHttps;
  WebPortsPage.PromptLabels[2].Visible := ShowHttps;
  if ShowIis then
  begin
    WebPortsPage.PromptLabels[1].Caption := 'Engine port -- 1024 to 65535:';
    WebPortsPage.PromptLabels[1].Top := WebFieldTop + ScaleY(60);
    WebPortsPage.Edits[1].Top := WebPortsPage.Edits[0].Top + ScaleY(60);
  end
  else
  begin
    WebPortsPage.PromptLabels[1].Top := WebFieldTop;
    WebPortsPage.Edits[1].Top := WebPortsPage.Edits[0].Top;
    if WebHost = 'Service' then
      WebPortsPage.PromptLabels[1].Caption := 'Web edition port -- 1024 to 65535. The web edition and its engine share this address:'
    else WebPortsPage.PromptLabels[1].Caption := 'Engine port -- 1024 to 65535:';
  end;
end;

procedure Web_Init();
begin
  { InitializeSetup already resolved silent state; do not overwrite it with page defaults. }
  WebStateAclOk := True;
  WebHostPage := CreateInputOptionPage(wpSelectComponents, 'Web edition hosting',
    'The standalone service needs nothing else on this computer. Choose IIS if you manage your sites in IIS.',
    'Choose how to host the web edition:', True, False);
  WebHostPage.Add('Standalone Windows service (recommended)');
  WebHostPage.Add('IIS');
  WebHostPage.Add('Don''t host (files only)');
  WebHostPage.CheckListBox.Height := ScaleY(82);
  WebHostNotice := TNewStaticText.Create(WebHostPage);
  WebHostNotice.Parent := WebHostPage.Surface;
  WebHostNotice.AutoSize := False; WebHostNotice.WordWrap := True;
  WebHostNotice.SetBounds(0, WebHostPage.CheckListBox.Top + WebHostPage.CheckListBox.Height + ScaleY(10), WebHostPage.SurfaceWidth, WebHostPage.SurfaceHeight - ScaleY(104));
  WebHostPage.OnActivate := @WebHostActivate;
  WebHostPage.CheckListBox.OnClickCheck := @WebHostClick;
  WebNetworkPage := CreateInputOptionPage(WebHostPage.ID, 'Network exposure', 'Decide who can reach the engine bridge.',
    'Localhost is limited to this computer. LAN uses HTTPS and a pairing PIN.', True, False);
  WebNetworkPage.Add('Localhost only -- only my machine can browse');
  WebNetworkPage.Add('LAN exposed -- other machines on my network can browse');
  WebNetworkPage.SelectedValueIndex := 0;
  if Exposure = 'Lan' then WebNetworkPage.SelectedValueIndex := 1;
  WebPortsPage := CreateInputQueryPage(WebNetworkPage.ID, 'Ports', 'Choose the ports the web edition uses.', 'Keep the previous ports unless another program uses them.');
  WebPortsPage.Add('Site port (IIS) -- 80, or 1024 to 65535:', False);
  WebPortsPage.Add('Engine port -- 1024 to 65535:', False);
  WebPortsPage.Add('Secure site port (IIS LAN only) -- 443, or 1024 to 65535:', False);
  WebPortsPage.Values[0] := IntToStr(WebIisPort); WebPortsPage.Values[1] := IntToStr(WebBridgePort); WebPortsPage.Values[2] := IntToStr(WebHttpsPort);
  WebFieldTop := WebPortsPage.PromptLabels[0].Top;
  WebPortsPage.OnActivate := @WebPortsActivate;
  InstallSummaryPage := CreateOutputMsgPage(wpInstalling, 'Install summary', 'Review the web hosting result.', 'Writing the install summary...');
  InstallSummaryPage.MsgLabel.AutoSize := False; InstallSummaryPage.MsgLabel.WordWrap := True;
  InstallSummaryPage.MsgLabel.Width := InstallSummaryPage.SurfaceWidth;
  InstallSummaryPage.MsgLabel.Height := InstallSummaryPage.SurfaceHeight - InstallSummaryPage.MsgLabel.Top;
end;

function Web_NextButton(CurPageID: Integer): Boolean;
var Error: String;
begin
  Result := True;
  if WizardSilent then Exit;
  if not IsWebSelected() then begin WebHost := PreviousHost; Exit; end;
  if CurPageID = WebHostPage.ID then
  begin WebHost := Web_HostAtIndex(WebHostPage.SelectedValueIndex); WebHostingSource := 'page'; end;
  if CurPageID = WebNetworkPage.ID then
  begin
    if WebNetworkPage.SelectedValueIndex = 1 then Exposure := 'Lan' else Exposure := 'Localhost';
  end;
  if CurPageID <> WebPortsPage.ID then Exit;
  WebBridgePort := StrToIntDef(Trim(WebPortsPage.Values[1]), -1);
  Error := '';
  if (WebBridgePort < 1024) or (WebBridgePort > 65535) then Error := 'The engine port must be between 1024 and 65535.';
  if (Error = '') and (WebHost = 'IIS') then
  begin
    WebIisPort := StrToIntDef(Trim(WebPortsPage.Values[0]), -1);
    WebHttpsPort := StrToIntDef(Trim(WebPortsPage.Values[2]), -1);
    if (WebIisPort <> 80) and ((WebIisPort < 1024) or (WebIisPort > 65535)) then Error := 'The site port must be 80 or between 1024 and 65535.'
    else if WebIisPort = WebBridgePort then Error := 'The site port and the engine port must be different.'
    else if Exposure = 'Lan' then
    begin
      if (WebHttpsPort <> 443) and ((WebHttpsPort < 1024) or (WebHttpsPort > 65535)) then Error := 'The secure site port must be 443 or between 1024 and 65535.'
      else if (WebHttpsPort = WebIisPort) or (WebHttpsPort = WebBridgePort) then Error := 'The secure site port must differ from the site port and the engine port.';
    end;
  end;
  if (Error = '') and (WebHost = 'Service') then Error := Web_ServicePortError(WebBridgePort, False);
  if Error <> '' then begin MsgBox(Error, mbError, MB_OK); Result := False; Exit; end;
  if (WebHost <> 'Service') and (not IsOwnPreviousBridgePort(WebBridgePort)) and Web_PortInUse(WebBridgePort) then
    MsgBox('Port ' + IntToStr(WebBridgePort) + ' appears to be in use already. You can continue or choose another engine port.', mbInformation, MB_OK);
end;

function Web_Skip(PageID: Integer): Boolean;
begin
  Result := (not IsWebSelected()) and ((PageID = WebHostPage.ID) or (PageID = WebNetworkPage.ID) or
    (PageID = WebPortsPage.ID) or (PageID = InstallSummaryPage.ID));
  if not IsWebSelected() then WebHost := PreviousHost;
end;

{ Set an exact protected DACL, including existing explicit entries. Never walk a reparse point. }
function Web_ExactAclCode(Path: String; PublicTree: Boolean): String;
begin
  Result := '$ErrorActionPreference=''Stop''; $root=' + Web_PsLiteral(Path) +
    '; $todo=New-Object ''Collections.Generic.Stack[string]''; $todo.Push($root); while($todo.Count){' +
    '$p=$todo.Pop(); $i=Get-Item -LiteralPath $p -Force; if($i.Attributes -band [IO.FileAttributes]::ReparsePoint){throw ''Reparse point refused''}; ' +
    '$a=$i.GetAccessControl(); $a.SetAccessRuleProtection($true,$false); ' +
    'foreach($r in @($a.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]))){$a.RemoveAccessRuleSpecific($r)}; ' +
    '$a.SetOwner((New-Object Security.Principal.SecurityIdentifier(''S-1-5-32-544''))); ' +
    '$f=[Security.AccessControl.InheritanceFlags]::None; if($i.PSIsContainer){$f=[Security.AccessControl.InheritanceFlags]''ContainerInherit,ObjectInherit''}; ' +
    'foreach($s in @(''S-1-5-32-544'',''S-1-5-18'')){$a.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($s)),''FullControl'',$f,''None'',''Allow'')))}; ';
  if PublicTree then Result := Result +
    'foreach($s in @(''S-1-5-32-545'',''S-1-5-32-568'')){$a.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($s)),''ReadAndExecute'',$f,''None'',''Allow'')))}; ';
  Result := Result + '$i.SetAccessControl($a); if($i.PSIsContainer){Get-ChildItem -LiteralPath $p -Force | ForEach-Object {$todo.Push($_.FullName)}}}; exit 0';
end;

function Web_NoReparseTree(Path: String): Boolean;
var Code: Integer;
begin
  Result := Web_PowerShell('$ErrorActionPreference=''Stop''; $todo=New-Object ''Collections.Generic.Stack[string]''; $todo.Push(' +
    Web_PsLiteral(Path) + '); while($todo.Count){$p=$todo.Pop();$i=Get-Item -LiteralPath $p -Force;' +
    'if($i.Attributes -band [IO.FileAttributes]::ReparsePoint){exit 1};' +
    'if($i.PSIsContainer){Get-ChildItem -LiteralPath $p -Force | ForEach-Object {$todo.Push($_.FullName)}}};exit 0', Code) and (Code = 0);
end;

function Web_LockState(): Boolean;
var State: String; Code: Integer;
begin
  State := ExpandConstant('{commonappdata}\AKML SQL Web');
  Result := ForceDirectories(State);
  if Result then Result := Web_NoReparseTree(State);
  if Result then Result := Exec('icacls.exe', '"' + State + '" /setowner *S-1-5-32-544 /T /C', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if Result then Result := Web_PowerShell(Web_ExactAclCode(State, False), Code) and (Code = 0);
end;

function Web_PrepareFolders(): String;
var App, Pf64, Pf32, CodeText: String; Code: Integer;
begin
  Result := '';
  if not IsWebSelected() then begin WebHost := PreviousHost; Exit; end;
  if WebHost = 'Service' then
  begin
    Result := Web_ServicePortError(WebBridgePort, WizardSilent);
    if Result <> '' then Exit;
  end;
  App := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  Pf64 := AddBackslash(ExpandConstant('{commonpf64}')); Pf32 := AddBackslash(ExpandConstant('{commonpf32}'));
  if (CompareText(Copy(AddBackslash(App), 1, Length(Pf64)), Pf64) <> 0) and
    (CompareText(Copy(AddBackslash(App), 1, Length(Pf32)), Pf32) <> 0) then
  begin
    CodeText := '$ErrorActionPreference=''Stop''; $p=' + Web_PsLiteral(App) +
      '; if([IO.Path]::GetPathRoot($p).TrimEnd(''\'') -eq $p.TrimEnd(''\'')){exit 1}; ' +
      '$i=Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue; if($i){' +
      'if($i.Attributes -band [IO.FileAttributes]::ReparsePoint){exit 1}; ' +
      'if(@(Get-ChildItem -LiteralPath $p -Force).Count -gt 0){$o=$i.GetAccessControl().GetOwner([Security.Principal.SecurityIdentifier]).Value; ' +
      'if($o -notin @(''S-1-5-32-544'',''S-1-5-18'',''S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'')){exit 1}}}; exit 0';
    if not (Web_PowerShell(CodeText, Code) and (Code = 0)) then
    begin Result := 'The custom install folder is a reparse point, a volume root, or a non-empty folder with a foreign owner. Choose an administrator-owned folder.'; Exit; end;
    if not ForceDirectories(App) then begin Result := 'Could not create the custom install folder.'; Exit; end;
    if not Web_NoReparseTree(App) then begin Result := 'Reparse points in the custom install tree are refused.'; Exit; end;
    if not (Exec('icacls.exe', '"' + App + '" /setowner *S-1-5-32-544 /T /C', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0)) or
      not (Web_PowerShell(Web_ExactAclCode(App, True), Code) and (Code = 0)) then
    begin Result := 'Could not protect the custom install folder. Setup stopped before closing applications.'; Exit; end;
  end;
  WebStateAclOk := Web_LockState();
  if not WebStateAclOk then Log('WARNING: could not protect the AKML SQL Web state folder.');
end;

procedure Web_PostInstall();
var State, Args, Root, Url, Reason, Failure, Move, Heading: String;
  Summary: TStringList; Code, I: Integer; Running, Answered: Boolean;
begin
  if not IsWebSelected() then Exit;
  State := ExpandConstant('{commonappdata}\AKML SQL Web');
  ForceDirectories(State);
  Summary := TStringList.Create;
  Running := False; Answered := True; Failure := ''; Reason := '';
  try
    try
      RegWriteStringValue(HKLM64, 'Software\AKML SQL\Web', 'Host', WebHost);
      RegWriteStringValue(HKLM64, 'Software\AKML SQL\Web', 'Exposure', Exposure);
      RegWriteDWordValue(HKLM64, 'Software\AKML SQL\Web', 'BridgePort', WebBridgePort);
      RegWriteStringValue(HKLM64, 'Software\AKML SQL\Web', 'WebRoot', ExpandConstant('{app}\Web'));
      if WebHost = 'IIS' then
      begin
        RegWriteDWordValue(HKLM64, 'Software\AKML SQL\Web', 'IisPort', WebIisPort);
        RegWriteDWordValue(HKLM64, 'Software\AKML SQL\Web', 'HttpsPort', WebHttpsPort);
        RegWriteStringValue(HKLM64, 'Software\AKML SQL\Web', 'IisMode', Exposure);
      end;
      Log('Web hosting: ' + WebHost + ' (from ' + WebHostingSource + ')');
      if (PreviousHost = 'IIS') and (WebHost <> 'IIS') then
      begin
        Web_Support('web-iis-remove.ps1', '-AppDir "' + ExpandConstant('{app}') + '"', Code);
        if FileExists(State + '\iis-remove.failed') then Failure := ReadHostingText(State + '\iis-remove.failed');
      end;
      Root := ''; if WebHost = 'Service' then Root := ExpandConstant('{app}\Web');
      Args := '-Port ' + IntToStr(WebBridgePort) + ' -Mode ' + Exposure + ' -ConfigPath "' + State + '\config.json"';
      if Root <> '' then Args := Args + ' -WebRoot "' + Root + '"';
      if not (Web_Support('web-config-bridge.ps1', Args, Code) and (Code = 0)) then Failure := Failure + #13#10 + 'Bridge configuration failed; see install.log.';
      Args := '-Apply -WebHost ' + WebHost + ' -Exposure ' + Exposure + ' -EnginePort ' + IntToStr(WebBridgePort) + ' -HttpsPort ' + IntToStr(WebHttpsPort);
      if not (Web_Support('web-firewall.ps1', Args, Code) and (Code = 0)) then Failure := Failure + #13#10 + 'Firewall configuration failed; see install.log.';
      WebStateAclOk := Web_LockState() and WebStateAclOk;
      if WebHost = 'Service' then RegWriteDWordValue(HKLM64, 'SYSTEM\CurrentControlSet\Services\AkmlSqlWebEngine', 'DelayedAutostart', 0);
      Exec('sc.exe', 'start AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, Code);
      Running := Web_PowerShell('$s=Get-Service AkmlSqlWebEngine -ErrorAction SilentlyContinue; if(!$s){exit 1}; try {$s.WaitForStatus(''Running'',[TimeSpan]::FromSeconds(10)); exit 0} catch {exit 1}', Code) and (Code = 0);
      PairingPin := ''; TlsThumbprint := '';
      if Exposure = 'Lan' then
      begin
        for I := 1 to 30 do
        begin
          PairingPin := Trim(ReadHostingText(State + '\pairing-pin.txt'));
          if PairingPin <> '' then Break;
          Sleep(1000);
        end;
        TlsThumbprint := Trim(ReadHostingText(State + '\certs\thumbprint.txt'));
      end;
      Url := ''; Args := '';
      if WebHost = 'Service' then
      begin
        if Exposure = 'Lan' then Url := 'https://localhost:' + IntToStr(WebBridgePort) + '/'
        else Url := 'http://127.0.0.1:' + IntToStr(WebBridgePort) + '/';
        Args := '-ExpectEngine';
      end
      else if WebHost = 'IIS' then
      begin Url := 'http://localhost:' + IntToStr(WebIisPort) + '/'; Args := '-ExpectBundle'; end;
      if Url <> '' then
      begin
        DeleteFile(State + '\hosting-reason.txt');
        Answered := Web_Support('web-hosting-check.ps1', '-Url "' + Url + '" ' + Args + ' -ReasonPath "' + State + '\hosting-reason.txt"', Code) and (Code = 0);
        if not Answered then
        begin
          Reason := Trim(ReadHostingText(State + '\hosting-reason.txt'));
          if Code = 2 then Reason := 'answered but is not serving the web edition. ' + Reason;
        end;
      end;
      if WebHost = 'None' then DeleteFile(ExpandConstant('{group}\Repair AKML SQL Web hosting.lnk'));
    except Failure := Failure + #13#10 + GetExceptionMessage; end;
    Summary.Add('AKML SQL Web -- install summary'); Summary.Add('================================='); Summary.Add('');
    Heading := WebHost;
    if WebHost = 'Service' then Heading := 'Standalone Windows service';
    if WebHost = 'None' then Heading := 'Not hosted';
    Summary.Add('Hosting:     ' + Heading);
    Url := Web_CurrentAddress();
    if (WebHost = 'Service') and (Exposure = 'Lan') then Url := Url + '  (every computer, this one included)';
    if (WebHost = 'IIS') and (Exposure = 'Lan') and FileExists(State + '\https-site.ok') then
    begin
      Url := 'https://' + GetComputerNameString() + ':' + IntToStr(WebHttpsPort) + '/';
      Summary.Add('URL:         ' + Url);
      Summary.Add('             (on this computer also ' + Web_CurrentAddress() + ')');
    end
    else Summary.Add('URL:         ' + Url);
    if WebHost = 'Service' then Summary.Add('Bridge port: ' + IntToStr(WebBridgePort) + ' (the same address serves the pages)')
    else Summary.Add('Bridge port: ' + IntToStr(WebBridgePort));
    if WebHost = 'None' then Summary.Add('Files only: ' + ExpandConstant('{app}\Web'));
    if Exposure = 'Lan' then
    begin
      Summary.Add('Pairing PIN: ' + PairingPin); Summary.Add('TLS thumb:   ' + TlsThumbprint);
      Summary.Add('Recommended: trust ' + State + '\certs\bridge.cer on each computer:');
      Summary.Add('  Install certificate -> Local Machine -> Trusted Root Certification Authorities.');
      Summary.Add('The firewall rule applies to every network profile, including Public. Use a Private or Domain network.');
      if PairingPin = '' then Summary.Add('WARNING: no pairing PIN was read. Start the service and re-read pairing-pin.txt.');
      if (WebHost = 'IIS') and not FileExists(State + '\https-site.ok') then
        Summary.Add('WARNING: IIS has no verified HTTPS address. ' + ReadHostingText(State + '\https-site.failed'));
    end;
    if not WebStateAclOk then Summary.Add('WARNING: state-folder owner/ACL protection failed. Re-run setup elevated.');
    if not Running then Summary.Add('WARNING: the service is not running. Run: sc start AkmlSqlWebEngine');
    if not Answered then Summary.Add('WARNING: the address did not answer as expected: ' + Reason + ' Run "Repair AKML SQL Web hosting" from the Start menu.');
    if Trim(Failure) <> '' then Summary.Add('WARNING: ' + Trim(Failure));
    Move := AddressChangeNotice(Web_PreviousAddress(), Web_CurrentAddress());
    if Move <> '' then Summary.Add(Move);
    SaveStringToFile(State + '\INSTALL-SUMMARY.txt', Utf8Encode(Summary.Text), False);
    InstallSummaryPage.MsgLabel.Caption := Summary.Text;
  finally Summary.Free; end;
end;

procedure Web_Uninstall();
var State, CodeText: String; Port: Cardinal; Code: Integer;
begin
  State := ExpandConstant('{commonappdata}\AKML SQL Web');
  Exec('sc.exe', 'stop AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Web_PowerShell('$s=Get-Service AkmlSqlWebEngine -ErrorAction SilentlyContinue; if($s){try{$s.WaitForStatus(''Stopped'',[TimeSpan]::FromSeconds(10))}catch{}}', Code);
  Exec('sc.exe', 'delete AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec('netsh.exe', 'advfirewall firewall delete rule name="AKML SQL Web Engine"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec('netsh.exe', 'advfirewall firewall delete rule name="AKML SQL Web Site (HTTPS)"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if not WebRegQueryDWord('BridgePort', Port) then Port := 47291;
  { Keep a replacement application's certificate binding. }
  if FileExists(ExpandConstant('{app}\Support\web-https-binding.ps1')) then
  begin
    CodeText := '. ' + Web_PsLiteral(ExpandConstant('{app}\Support\web-https-binding.ps1')) +
      '; $t=(Get-Content -LiteralPath ' + Web_PsLiteral(State + '\certs\thumbprint.txt') + ' -ErrorAction SilentlyContinue | Out-String).Trim(); ' +
      'if($t -and (Read-BoundHash ' + IntToStr(Port) + ') -eq $t){& netsh http delete sslcert ipport=0.0.0.0:' + IntToStr(Port) + ' | Out-Null}; Remove-OwnedHttpsBinding | Out-Null';
    Web_PowerShell(CodeText, Code);
  end;
  if FileExists(ExpandConstant('{app}\Support\web-iis-remove.ps1')) then
    Web_Support('web-iis-remove.ps1', '-AppDir "' + ExpandConstant('{app}') + '"', Code)
  else Web_PowerShell('Import-Module WebAdministration -ErrorAction SilentlyContinue; Remove-WebSite -Name AkmlSqlWeb -ErrorAction SilentlyContinue', Code);
  CodeText := '$t=(Get-Content -LiteralPath ' + Web_PsLiteral(State + '\certs\thumbprint.txt') +
    ' -ErrorAction SilentlyContinue | Out-String).Trim(); $bindings=& netsh http show sslcert | Out-String; if($LASTEXITCODE -ne 0){exit 0}; ' +
    'Get-ChildItem Cert:\LocalMachine\My | Where-Object {if($t){$_.Thumbprint -eq $t}else{$_.Subject -eq ''CN=AKML SQL Web Engine''}} | ' +
    'ForEach-Object {if($bindings -notmatch [regex]::Escape($_.Thumbprint)){Remove-Item -LiteralPath $_.PSPath -Force}}';
  Web_PowerShell(CodeText, Code);
  if UninstallSilent then Log('keeping AKML SQL Web data')
  else if MsgBox('Delete AKML SQL Web data at "' + State + '"? This includes PIN, tokens, certificate and config.', mbConfirmation, MB_YESNO) = IDYES then
  begin
    if Web_PowerShell('if((Get-Item -LiteralPath ' + Web_PsLiteral(State) + ' -Force -ErrorAction SilentlyContinue).Attributes -band [IO.FileAttributes]::ReparsePoint){exit 1}; exit 0', Code) and (Code = 0) then
      DelTree(State, True, True, True);
  end;
  RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\AKML SQL\Web');
  RegDeleteKeyIncludingSubkeys(HKLM32, 'Software\AKML SQL\Web');
end;
