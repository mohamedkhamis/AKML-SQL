; ============================================================================
; AKML SQL - Environment Scanner
; Detects SSMS 22 installations (vswhere.exe first, then registry / filesystem fallbacks).
; Also finds the AKML SQL extension that earlier releases installed into Visual Studio 2026, so
; setup can remove it: Visual Studio is no longer supported.
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
  // <VS root>\Common7\IDE\Extensions\AkmlSql folders left by releases that supported VS 2026.
  LegacyVSExtDirs: array of String;
  LegacyVSExtCount: Integer;
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

// --- Old Visual Studio 2026 extension (removed on install) ---

procedure AddLegacyVSExtension(VSRoot: String);
var
  Dir: String;
  I: Integer;
begin
  if (VSRoot = '') or (Pos('BuildTools', VSRoot) > 0) then
    Exit;
  Dir := RemoveBackslashUnlessRoot(VSRoot) + '\Common7\IDE\Extensions\AkmlSql';
  if not DirExists(Dir) then
    Exit;
  for I := 0 to LegacyVSExtCount - 1 do
    if CompareText(LegacyVSExtDirs[I], Dir) = 0 then
      Exit;
  SetArrayLength(LegacyVSExtDirs, LegacyVSExtCount + 1);
  LegacyVSExtDirs[LegacyVSExtCount] := Dir;
  LegacyVSExtCount := LegacyVSExtCount + 1;
  Log('Found the old AKML SQL extension for Visual Studio 2026: ' + Dir);
end;

// Filesystem fallback for anything vswhere missed: every edition folder under
// Microsoft Visual Studio\2026\ and \18\ (Enterprise, Professional, Community, Preview, Insiders).
procedure DetectLegacyVSExtensionsFallback;
var
  Bases: array of String;
  B: Integer;
  FindRec: TFindRec;
begin
  SetArrayLength(Bases, 2);
  Bases[0] := ExpandConstant('{commonpf64}') + '\Microsoft Visual Studio\2026\';
  Bases[1] := ExpandConstant('{commonpf64}') + '\Microsoft Visual Studio\18\';
  for B := 0 to GetArrayLength(Bases) - 1 do
  begin
    if FindFirst(Bases[B] + '*', FindRec) then
    begin
      try
        repeat
          if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0)
             and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
            AddLegacyVSExtension(Bases[B] + FindRec.Name);
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;

// --- vswhere-based detection ---
// Queries vswhere.exe: finds SSMS 22, and Visual Studio 2026 instances still carrying the old extension

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
      // Strip \Release suffix to get the display root
      RootPath := Line;
      if (Length(RootPath) > 8) and (Copy(RootPath, Length(RootPath) - 6, 7) = 'Release') then
        RootPath := Copy(RootPath, 1, Length(RootPath) - 8);

      // Extensions must be under Release\Common7\IDE\Extensions (where the IDE runs)
      if FileExists(Line + '\Common7\IDE\SSMS.exe') or
         FileExists(Line + '\Common7\IDE\Ssms.exe') then
        AddTarget('SSMS 22', '22', 'x64', RootPath,
          Line + '\Common7\IDE\Extensions\AkmlSql', True, '')
      else if DirExists(RootPath) then
        AddTarget('SSMS 22', '22', 'x64', RootPath,
          Line + '\Common7\IDE\Extensions\AkmlSql', True, '');
    end

    // Visual Studio 2026 (folder \18\ or \2026\) is not a target any more; only its old extension matters
    else if (Pos('\18\', Line) > 0) or (Pos('\2026\', Line) > 0) then
      AddLegacyVSExtension(Line);
  end;

  DeleteFile(TmpFile);
end;

// --- SSMS 22 Detection (filesystem fallback) ---

procedure DetectSSMS22;
var
  InstallPath: String;
begin
  if TargetAlreadyAdded('22') then Exit;

  // Either registry view: the installer is 64-bit now, and a 32-bit writer would land in WOW6432Node.
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 22',
      'SSMSInstallRoot', InstallPath)
     or RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\Microsoft SQL Server Management Studio 22',
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
    Targets[I].IsRunning := IsProcessRunning('Ssms.exe') or IsProcessRunning('SSMS.exe');
  end;
end;

// --- Environment Scan Page ---

procedure RunFullScan;
begin
  TargetCount := 0;
  SetArrayLength(Targets, 0);
  LegacyVSExtCount := 0;
  SetArrayLength(LegacyVSExtDirs, 0);
  // Primary: vswhere-based detection (finds SSMS 22, and old VS 2026 extensions)
  DetectViaVSWhere;
  // Fallback: registry / filesystem detection for anything vswhere missed
  DetectSSMS22;
  DetectLegacyVSExtensionsFallback;
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
    ItemText := Targets[I].Name + '  -  ' + Targets[I].InstallPath;
    if not Targets[I].IsCompatible then
      ItemText := ItemText + '  (' + Targets[I].IncompatReason + ')';
    if Targets[I].IsRunning then
      ItemText := ItemText + '  (open now - setup will close it)';

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

function CheckSSMS22: Boolean; begin Result := IsTargetSelected('22'); end;

function GetSSMS22ExtDir(Param: String): String; begin Result := GetTargetExtPath('22'); end;

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

  // Select only specified targets. "vs2026" (from older deployment scripts) is accepted and
  // ignored: Visual Studio is no longer supported.
  if Pos('ssms22', LowerCase(TargetsParam)) > 0 then
    for I := 0 to TargetCount - 1 do
      if Targets[I].Version = '22' then Targets[I].IsSelected := True;
end;
