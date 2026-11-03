; Pure hosting decisions. Readers and machine changes belong in web-installer.iss.
[Code]
const
  IisNotEnabled = 0;
  IisPartMissing = 1;
  IisDisabled = 2;
  IisToolsUnavailable = 3;
  IisReady = 4;

function ReadConfigString(const Json, Key: String; var Value: String): Boolean;
var P: Integer; Text, C: String;
begin
  Result := False;
  P := Pos('"' + Key + '"', Json);
  if P = 0 then Exit;
  P := P + Length(Key) + 2;
  while (P <= Length(Json)) and (Json[P] <= ' ') do P := P + 1;
  if (P > Length(Json)) or (Json[P] <> ':') then Exit;
  P := P + 1;
  while (P <= Length(Json)) and (Json[P] <= ' ') do P := P + 1;
  if (P > Length(Json)) or (Json[P] <> '"') then Exit;
  P := P + 1;
  Text := '';
  while P <= Length(Json) do
  begin
    C := Json[P];
    if C = '"' then
    begin
      Value := Text;
      Result := True;
      Exit;
    end;
    if C = '\' then
    begin
      P := P + 1;
      if P > Length(Json) then Exit;
      C := Json[P];
      if (C <> '\') and (C <> '"') and (C <> '/') then Exit;
    end;
    Text := Text + C;
    P := P + 1;
  end;
end;

function HostingAddress(Host, Exposure, ComputerName: String; EnginePort, IisPort: Integer): String;
begin
  Result := '';
  if CompareText(Host, 'Service') = 0 then
  begin
    if CompareText(Exposure, 'Lan') = 0 then
      Result := 'https://' + ComputerName + ':' + IntToStr(EnginePort) + '/'
    else Result := 'http://localhost:' + IntToStr(EnginePort) + '/';
  end
  else if CompareText(Host, 'IIS') = 0 then
  begin
    if IisPort = 80 then Result := 'http://localhost/'
    else Result := 'http://localhost:' + IntToStr(IisPort) + '/';
  end;
end;

function IisReadinessFrom(InetStpPresent: Boolean; MajorVersion, W3svc, StaticContent,
  DefaultDocument, HttpErrors, W3svcStart, WasStart: Integer; Probe: String; var Reason: String): Integer;
var Missing: String;
begin
  Reason := '';
  if (not InetStpPresent) or (MajorVersion < 7) or (W3svc <> 1) then
  begin
    Result := IisNotEnabled;
    Reason := 'IIS is not enabled on this computer. To use it, turn on Internet Information Services in Windows Features, then go Back and Next.';
  end
  else if (StaticContent <> 1) or (DefaultDocument <> 1) or (HttpErrors <> 1) then
  begin
    Result := IisPartMissing;
    if StaticContent <> 1 then Missing := 'Static Content'
    else if DefaultDocument <> 1 then Missing := 'Default Document'
    else Missing := 'HTTP Errors';
    Reason := 'IIS is missing ' + Missing + '. Turn it on under Internet Information Services ' +
      '' + #8250 + ' World Wide Web Services ' + #8250 + ' Common HTTP Features in Windows Features, then go Back and Next.';
  end
  else if (W3svcStart = 4) or (WasStart = 4) then
  begin
    Result := IisDisabled;
    Reason := 'The World Wide Web Publishing Service (or the Windows Process Activation Service it needs) is disabled. Set it to Manual or Automatic in Services, then go Back and Next.';
  end
  else if CompareText(Probe, 'failed') = 0 then
  begin
    Result := IisToolsUnavailable;
    Reason := 'Setup cannot use IIS''s management tools. Turn on IIS Management Tools (under Internet Information Services) in Windows Features, then go Back and Next.';
  end
  else Result := IisReady;
end;

function InferPreviousHost(Host: String; HasBridgePort, ServiceExists: Boolean; ApplicationHostConfig: String): String;
begin
  if Host <> '' then Result := Host
  else if (not HasBridgePort) and (not ServiceExists) then Result := ''
  else if Pos('<site name="akmlsqlweb"', Lowercase(ApplicationHostConfig)) > 0 then Result := 'IIS'
  else Result := 'Service';
end;

function InferPreviousExposure(Exposure, Json, IisMode: String): String;
var BindAddress: String;
begin
  if Exposure <> '' then Result := Exposure
  else
  begin
    BindAddress := '';
    ReadConfigString(Json, 'bindAddress', BindAddress);
    if (BindAddress = '0.0.0.0') or (BindAddress = '::') or (CompareText(IisMode, 'Lan') = 0) then Result := 'Lan'
    else Result := 'Localhost';
  end;
end;

function AddressChangeNotice(OldAddress, NewAddress: String): String;
begin
  Result := '';
  if (OldAddress = '') or (NewAddress = '') or (OldAddress = NewAddress) then Exit;
  Result := 'The web edition will move from ' + OldAddress + ' to ' + NewAddress +
    '. Connections, tabs and other data saved in the browser at ' + OldAddress +
    ' will not appear at ' + NewAddress + '. Query history is kept by the engine. ' +
    'To keep your settings, use Settings ' + #8250 + ' Export' + #8230 + ' first.';
end;

function SilentNeedsIisProbe(FlagHost, PreviousHost: String): Boolean;
begin
  Result := (CompareText(FlagHost, 'IIS') = 0) or
    ((FlagHost = '') and (CompareText(PreviousHost, 'IIS') = 0));
end;

function ResolveSilentHost(FlagHost, FlagExposure, PreviousHost: String;
  IisIsReady, IisOnlyPortsGiven: Boolean; IisReason, NewAddress: String; var LogLine: String): String;
var Flag, Source: String;
begin
  Flag := Uppercase(FlagHost);
  Source := '/WEB_HOST';
  LogLine := '';
  if (Flag <> '') and (Flag <> 'SERVICE') and (Flag <> 'IIS') and (Flag <> 'NONE') then
    Result := 'Stop:Unknown /WEB_HOST value: ' + FlagHost
  else if (Flag = 'NONE') and (CompareText(FlagExposure, 'LAN') = 0) then
    Result := 'Stop:/WEB_HOST=NONE with /WEB_EXPOSURE=LAN is invalid'
  else if Flag = 'IIS' then
  begin
    if IisIsReady then Result := 'IIS' else Result := 'Stop:' + IisReason;
  end
  else if (Flag = '') and (CompareText(PreviousHost, 'IIS') = 0) then
  begin
    Source := 'previous host';
    if IisIsReady then Result := 'IIS'
    else
    begin
      Result := 'Service';
      Source := 'IIS fallback: ' + IisReason;
      LogLine := 'IIS is no longer ready (' + IisReason +
        '); the web edition moves to the standalone service at ' + NewAddress;
      if IisOnlyPortsGiven then LogLine := LogLine + '; IIS-only ports ignored';
    end;
  end
  else if IisOnlyPortsGiven then
    Result := 'Stop:/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only'
  else if Flag = 'SERVICE' then Result := 'Service'
  else if Flag = 'NONE' then Result := 'None'
  else
  begin
    Source := 'previous host';
    Result := PreviousHost;
    if Result = '' then begin Result := 'Service'; Source := 'default'; end;
  end;
  if Copy(Result, 1, 5) = 'Stop:' then Exit;
  if LogLine <> '' then LogLine := LogLine + #13#10;
  LogLine := LogLine + 'Web hosting: ' + Result + ' (from ' + Source + ')';
end;

function HasLegacyTempScriptActions(Raw: AnsiString): Boolean;
var Marker, Ending, Text: String; I, C, P: Integer;
begin
  { Build byte markers at runtime so the detector cannot match its own compiled constants. }
  Marker := ''; Ending := '';
  for I := 1 to 4 do
  begin
    case I of 1: C := 92; 2: C := 105; 3: C := 115; else C := 45; end;
    Marker := Marker + Chr(C) + #0;
  end;
  for I := 1 to 5 do
  begin
    case I of 1: C := 46; 2: C := 116; 3: C := 109; 4: C := 112; else C := 92; end;
    Ending := Ending + Chr(C) + #0;
  end;
  Text := Lowercase(String(Raw));
  P := Pos(Marker, Text);
  Result := (P > 0) and (Pos(Ending, Copy(Text, P, Length(Text))) > 0);
end;
