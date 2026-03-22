; ============================================================================
; AKML SQL - Environment Scanner
; Detects SSMS 20/21/22 and Visual Studio 2019/2022/2026 installations
; Uses vswhere.exe for reliable detection with filesystem fallbacks
; ============================================================================

[Code]

type
  TTargetInfo = record
    Name: String;
    Version: String;
    Arch: String;
    InstallPath: String;
    ExtensionsPath: String;
    IsCompatible: Boolean;
    IncompatReason: String;
    IsRunning: Boolean;
    IsSelected: Boolean;
  end;

var
  Targets: array of TTargetInfo;
  TargetCount: Integer;
  EnvPage: TWizardPage;
  EnvCheckListBox: TNewCheckListBox;

function TargetAlreadyAdded(Version: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to TargetCount - 1 do
  begin
    if Targets[I].Version = Version then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddTarget(Name, Version, Arch, InstallPath, ExtPath: String; Compatible: Boolean; Reason: String);
begin
  if TargetAlreadyAdded(Version) then
    Exit;
  SetArrayLength(Targets, TargetCount + 1);
  Targets[TargetCount].Name := Name;
  Targets[TargetCount].Version := Version;
  Targets[TargetCount].Arch := Arch;
  Targets[TargetCount].InstallPath := InstallPath;
  Targets[TargetCount].ExtensionsPath := ExtPath;
  Targets[TargetCount].IsCompatible := Compatible;
  Targets[TargetCount].IncompatReason := Reason;
  Targets[TargetCount].IsRunning := False;
  Targets[TargetCount].IsSelected := Compatible;
  TargetCount := TargetCount + 1;
end;

// --- vswhere-based detection ---
// Queries vswhere.exe and parses output to detect all VS/SSMS instances

procedure DetectViaVSWhere;
var
  VSWherePath: String;
  ResultCode: Integer;
  TmpFile: String;
  Lines: TArrayOfString;
  I: Integer;
  Line: String;
  RootPath: String;
begin
  VSWherePath := ExpandConstant('{pf32}') + '\Microsoft Visual Studio\Installer\vswhere.exe';
  if not FileExists(VSWherePath) then
    Exit;

  TmpFile := ExpandConstant('{tmp}') + '\vsinstances.txt';

  // Use cmd.exe /S /C with outer double-quotes to handle spaces in vswhere path
  Exec('cmd.exe',
    '/S /C ""' + VSWherePath + '" -all -products * -format value -property installationPath > "' + TmpFile + '""',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not LoadStringsFromFile(TmpFile, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    if Line = '' then
      Continue;

    // Detect SSMS 22 (x64) — vswhere returns path ending in \Release
    if Pos('SQL Server Management Studio 22', Line) > 0 then
    begin
      // vswhere reports ...\SSMS 22\Release as installationPath
      // The Extensions folder is at the SSMS root level, not under Release
      // Strip \Release suffix to get the root
      RootPath := Line;
      if (Length(RootPath) > 8) and (Copy(RootPath, Length(RootPath) - 6, 7) = 'Release') then
        RootPath := Copy(RootPath, 1, Length(RootPath) - 8);

      if FileExists(Line + '\Common7\IDE\SSMS.exe') or
         FileExists(Line + '\Common7\IDE\Ssms.exe') then
        AddTarget('SSMS 22', '22', 'x64', RootPath,
          RootPath + '\Common7\IDE\Extensions\AkmlSql', True, '')
      else if DirExists(RootPath) then
        AddTarget('SSMS 22', '22', 'x64', RootPath,
          RootPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
    end

    // Detect VS 2019 (x86)
    else if Pos('\2019\', Line) > 0 then
    begin
      if (Pos('BuildTools', Line) = 0) and DirExists(Line + '\Common7\IDE') then
        AddTarget('VS 2019', '2019', 'x86', Line,
          Line + '\Common7\IDE\Extensions\AkmlSql', True, '');
    end

    // Detect VS 2022 (x64)
    else if Pos('\2022\', Line) > 0 then
    begin
      if (Pos('BuildTools', Line) = 0) and DirExists(Line + '\Common7\IDE') then
        AddTarget('VS 2022', '2022', 'x64', Line,
          Line + '\Common7\IDE\Extensions\AkmlSql', True, '');
    end

    // Detect VS 2026 (x64) — uses folder name \18\ or \2026\
    else if (Pos('\18\', Line) > 0) or (Pos('\2026\', Line) > 0) then
    begin
      if (Pos('BuildTools', Line) = 0) and DirExists(Line + '\Common7\IDE') then
        AddTarget('VS 2026', '2026', 'x64', Line,
          Line + '\Common7\IDE\Extensions\AkmlSql', True, '');
    end;
  end;

  DeleteFile(TmpFile);
end;

// --- SSMS Detection (filesystem fallback) ---

procedure DetectSSMS20;
var
  InstallPath: String;
begin
  if TargetAlreadyAdded('20') then Exit;

  // Strategy 1: Registry
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server Management Studio 20',
      'SSMSInstallRoot', InstallPath) then
  begin
    if DirExists(InstallPath) then
    begin
      AddTarget('SSMS 20', '20', 'x86', InstallPath,
        InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
      Exit;
    end;
  end;

  // Strategy 2: File system fallback
  InstallPath := ExpandConstant('{pf32}') + '\Microsoft SQL Server Management Studio 20';
  if FileExists(InstallPath + '\Common7\IDE\Ssms.exe') then
    AddTarget('SSMS 20', '20', 'x86', InstallPath,
      InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
end;

procedure DetectSSMS21;
var
  InstallPath: String;
begin
  if TargetAlreadyAdded('21') then Exit;

  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 21',
      'SSMSInstallRoot', InstallPath) then
  begin
    if DirExists(InstallPath) then
    begin
      AddTarget('SSMS 21', '21', 'x64', InstallPath,
        InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
      Exit;
    end;
  end;

  InstallPath := ExpandConstant('{pf}') + '\Microsoft SQL Server Management Studio 21';
  if FileExists(InstallPath + '\Common7\IDE\Ssms.exe') or
     FileExists(InstallPath + '\Common7\IDE\SSMS.exe') or
     FileExists(InstallPath + '\Release\Common7\IDE\SSMS.exe') then
    AddTarget('SSMS 21', '21', 'x64', InstallPath,
      InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
end;

procedure DetectSSMS22;
var
  InstallPath: String;
begin
  if TargetAlreadyAdded('22') then Exit;

  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 22',
      'SSMSInstallRoot', InstallPath) then
  begin
    if DirExists(InstallPath) then
    begin
      AddTarget('SSMS 22', '22', 'x64', InstallPath,
        InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
      Exit;
    end;
  end;

  // File system fallback — SSMS 22 can have SSMS.exe in Release\Common7\IDE or Common7\IDE
  InstallPath := ExpandConstant('{pf}') + '\Microsoft SQL Server Management Studio 22';
  if DirExists(InstallPath) then
  begin
    if FileExists(InstallPath + '\Release\Common7\IDE\SSMS.exe') then
      AddTarget('SSMS 22', '22', 'x64', InstallPath,
        InstallPath + '\Release\Common7\IDE\Extensions\AkmlSql', True, '')
    else if FileExists(InstallPath + '\Common7\IDE\SSMS.exe') or
            FileExists(InstallPath + '\Common7\IDE\Ssms.exe') then
      AddTarget('SSMS 22', '22', 'x64', InstallPath,
        InstallPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
  end;
end;

// --- Visual Studio Detection (filesystem fallback) ---

procedure DetectVisualStudioFallback;
var
  VSPath: String;
begin
  // VS 2019 fallback
  if not TargetAlreadyAdded('2019') then
  begin
    VSPath := '';
    if DirExists(ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Enterprise') then
      VSPath := ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Enterprise'
    else if DirExists(ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Professional') then
      VSPath := ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Professional'
    else if DirExists(ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Community') then
      VSPath := ExpandConstant('{pf32}') + '\Microsoft Visual Studio\2019\Community'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Enterprise') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Enterprise'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Professional') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Professional'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Community') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2019\Community';

    if VSPath <> '' then
      AddTarget('VS 2019', '2019', 'x86', VSPath,
        VSPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
  end;

  // VS 2022 fallback
  if not TargetAlreadyAdded('2022') then
  begin
    VSPath := '';
    if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Enterprise') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Enterprise'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Professional') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Professional'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Community') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2022\Community';

    if VSPath <> '' then
      AddTarget('VS 2022', '2022', 'x64', VSPath,
        VSPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
  end;

  // VS 2026 fallback — can be under \2026\ or \18\
  if not TargetAlreadyAdded('2026') then
  begin
    VSPath := '';
    if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Enterprise') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Enterprise'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Professional') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Professional'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Community') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Community'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Preview') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\2026\Preview'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Enterprise') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Enterprise'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Professional') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Professional'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Community') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Community'
    else if DirExists(ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Preview') then
      VSPath := ExpandConstant('{pf}') + '\Microsoft Visual Studio\18\Preview';

    if VSPath <> '' then
      AddTarget('VS 2026', '2026', 'x64', VSPath,
        VSPath + '\Common7\IDE\Extensions\AkmlSql', True, '');
  end;
end;

// --- Running IDE Detection ---

function IsProcessRunning(ExeName: String): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}') + '\proccheck.txt';
  if Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq ' + ExeName + '" /NH > "' + TmpFile + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFile, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
      begin
        if Pos(ExeName, Lines[I]) > 0 then
        begin
          Result := True;
          Break;
        end;
      end;
    end;
    DeleteFile(TmpFile);
  end;
end;

procedure CheckRunningIDEs;
var
  I: Integer;
begin
  for I := 0 to TargetCount - 1 do
  begin
    if Pos('SSMS', Targets[I].Name) > 0 then
      Targets[I].IsRunning := IsProcessRunning('Ssms.exe') or IsProcessRunning('SSMS.exe')
    else
      Targets[I].IsRunning := IsProcessRunning('devenv.exe');
  end;
end;

// --- Environment Scan Page ---

procedure RunFullScan;
begin
  TargetCount := 0;
  SetArrayLength(Targets, 0);
  // Primary: vswhere-based detection (finds VS and SSMS 22)
  DetectViaVSWhere;
  // Fallback: filesystem detection for anything vswhere missed
  DetectSSMS20;
  DetectSSMS21;
  DetectSSMS22;
  DetectVisualStudioFallback;
  CheckRunningIDEs;
end;

procedure PopulateEnvCheckList;
var
  I: Integer;
  ItemText: String;
begin
  EnvCheckListBox.Items.Clear;
  for I := 0 to TargetCount - 1 do
  begin
    ItemText := Targets[I].Name + '  (' + Targets[I].Arch + ')  ' + Targets[I].InstallPath;
    if not Targets[I].IsCompatible then
      ItemText := ItemText + '  [' + Targets[I].IncompatReason + ']';
    if Targets[I].IsRunning then
      ItemText := ItemText + '  (RUNNING)';

    EnvCheckListBox.AddCheckBox(ItemText, '', 0, Targets[I].IsSelected, Targets[I].IsCompatible, False, True, nil);
  end;
end;

// Update Next button based on current checkbox state
procedure UpdateEnvNextButton;
var
  I: Integer;
  AnyChecked: Boolean;
begin
  AnyChecked := False;
  for I := 0 to EnvCheckListBox.Items.Count - 1 do
    if EnvCheckListBox.Checked[I] then
    begin
      AnyChecked := True;
      Break;
    end;
  WizardForm.NextButton.Enabled := AnyChecked;
end;

// OnClickCheck handler: keep Next button in sync with checkbox state
procedure EnvCheckListBoxClickCheck(Sender: TObject);
begin
  UpdateEnvNextButton;
end;

function IsTargetSelected(Version: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to TargetCount - 1 do
  begin
    if (Targets[I].Version = Version) and Targets[I].IsSelected then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function GetTargetExtPath(Version: String): String;
var
  I: Integer;
begin
  Result := ExpandConstant('{tmp}');
  for I := 0 to TargetCount - 1 do
  begin
    if Targets[I].Version = Version then
    begin
      Result := Targets[I].ExtensionsPath;
      Exit;
    end;
  end;
end;

// --- Check functions for [Files] section ---

function CheckSSMS20: Boolean; begin Result := IsTargetSelected('20'); end;
function CheckSSMS21: Boolean; begin Result := IsTargetSelected('21'); end;
function CheckSSMS22: Boolean; begin Result := IsTargetSelected('22'); end;
function CheckVS2019: Boolean; begin Result := IsTargetSelected('2019'); end;
function CheckVS2022: Boolean; begin Result := IsTargetSelected('2022'); end;
function CheckVS2026: Boolean; begin Result := IsTargetSelected('2026'); end;

function GetSSMS20ExtDir(Param: String): String; begin Result := GetTargetExtPath('20'); end;
function GetSSMS21ExtDir(Param: String): String; begin Result := GetTargetExtPath('21'); end;
function GetSSMS22ExtDir(Param: String): String; begin Result := GetTargetExtPath('22'); end;
function GetVS2019ExtDir(Param: String): String; begin Result := GetTargetExtPath('2019'); end;
function GetVS2022ExtDir(Param: String): String; begin Result := GetTargetExtPath('2022'); end;
function GetVS2026ExtDir(Param: String): String; begin Result := GetTargetExtPath('2026'); end;

// --- Silent Mode /TARGETS Parsing ---

procedure ApplySilentTargets;
var
  TargetsParam: String;
  I: Integer;
begin
  TargetsParam := ExpandConstant('{param:TARGETS|}');
  if TargetsParam = '' then
    Exit; // Use defaults (all compatible targets selected)

  // Deselect all first
  for I := 0 to TargetCount - 1 do
    Targets[I].IsSelected := False;

  // Select only specified targets
  if Pos('ssms20', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '20' then Targets[I].IsSelected := True;

  if Pos('ssms21', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '21' then Targets[I].IsSelected := True;

  if Pos('ssms22', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '22' then Targets[I].IsSelected := True;

  if Pos('vs2019', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '2019' then Targets[I].IsSelected := True;

  if Pos('vs2022', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '2022' then Targets[I].IsSelected := True;

  if Pos('vs2026', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '2026' then Targets[I].IsSelected := True;
end;
