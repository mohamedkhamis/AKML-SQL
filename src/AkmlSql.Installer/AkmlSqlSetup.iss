; ============================================================================
; AKML SQL Installer
; Wizard-based Windows EXE installer for SSMS 20/21/22 and VS 2019/2022/2026
; Built with Inno Setup 7
; ============================================================================
;
; --- Silent Installation ---
;
; The installer supports fully silent (unattended) installation. Examples:
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA
;     Install to all detected targets with no UI.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=ssms22,vs2022
;     Install only to SSMS 22 and VS 2022.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /LOG="C:\Logs\install.log"
;     Install with verbose logging. /LOG is a native Inno Setup flag that
;     writes detailed setup activity to the specified file. If no path is
;     given (/LOG without =), defaults to %TEMP%\Setup Log YYYY-MM-DD #NNN.txt.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /NOUPDATE /NOTELEMETRY
;     Install with auto-update and telemetry disabled.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /FORCECLOSEAPPS
;     Force-close running SSMS/VS instances before installing.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /IMPORTSQLPROMPT
;     Import SQL Prompt formatting styles during installation.
;
; Flags:
;   /VERYSILENT       No UI, no progress dialog
;   /ACCEPTEULA       Accept the EULA (required for silent mode)
;   /TARGETS=...      Comma-separated target list: ssms20,ssms21,ssms22,vs2019,vs2022,vs2026
;   /LOG[=file]       Write detailed install log (native Inno Setup feature)
;   /NOUPDATE         Disable built-in auto-update check
;   /TELEMETRY        Enable anonymous usage telemetry (off by default)
;   /NOTELEMETRY      Explicitly disable telemetry
;   /FORCECLOSEAPPS   Force-close running SSMS/VS without prompting
;   /IMPORTSQLPROMPT  Import SQL Prompt styles (only if SQL Prompt config detected)
;
; TODO T096: On uninstall, restore native SSMS IntelliSense if AKML SQL disabled it.
;   Read %AppData%/AKML SQL/config.json, check DisabledNativeIntelliSense flag,
;   and if true, set EnableIntelliSense=1 in the SSMS registry keys:
;     HKCU\Software\Microsoft\SQL Server Management Studio\20.0\Settings\IntelliSense
;     HKCU\Software\Microsoft\SQL Server Management Studio\22.0\Settings\IntelliSense
;     HKCU\Software\Microsoft\SSMS\22.0\Settings\IntelliSense
;

#define MyAppName "AKML SQL"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Mohamed Khamis"
#define MyAppURL "https://akmlsql.com"
#define MyAppId "{{F7E8A9B0-C1D2-E3F4-A5B6-C7D8E9F0A1B2}"

[Setup]
; AppId is fixed — Inno Setup uses this to detect existing installations.
; Together with UsePreviousAppDir=yes, this enables seamless in-place upgrade:
; re-running the installer over an existing installation will upgrade in-place
; without requiring uninstall first. Inno Setup's built-in repair/upgrade logic
; handles version bumps automatically as long as AppId remains the same.
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
LicenseFile=
InfoBeforeFile=LICENSE.txt
OutputBaseFilename=AKMLSQLSetup
OutputDir=Output
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousAppDir=yes
; Assets to be replaced with branded versions from design team (US14 — deferred)
SetupIconFile=assets\icon.ico
WizardImageFile=assets\sidebar.bmp
WizardSmallImageFile=assets\banner.bmp
WizardStyle=modern
WizardSizePercent=120
DisableWelcomePage=no
UninstallDisplayIcon={app}\AkmlSql.Core.dll
; T030: Prompt user to close running SSMS/VS instances before installing.
; Note: AppMutex is not used because SSMS/VS do not create named mutexes that
; we can reliably detect. CloseApplications with CloseApplicationsFilter is the
; correct approach — Inno Setup uses the Windows Restart Manager API to detect
; which apps hold locks on files being replaced.
CloseApplications=yes
CloseApplicationsFilter=Ssms.exe,devenv.exe

; Code signing (configure via iscc.exe /S flag)
; SignTool=mysigntool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Core binaries to base install directory (sourced from shell build output which includes all dependencies)
Source: "..\AkmlSql.Ssms22\bin\Release\net472\AkmlSql.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\bin\Release\net472\Serilog.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\bin\Release\net472\Serilog.Sinks.File.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Updater\bin\Release\net10.0\win-x64\publish\AkmlSql.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion
; Engine is SelfContained + PublishSingleFile=false — deploy ALL published output
Source: "..\AkmlSql.Engine\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}\Engine"; Flags: ignoreversion recursesubdirs
Source: "..\AkmlSql.Analyzer\bin\Release\net10.0\win-x64\publish\AkmlSql.Analyzer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

; SSMS 20 (x86) extension files — all DLLs from build output plus pkgdef and manifest
Source: "..\AkmlSql.Ssms20\bin\Release\net472\*.dll"; DestDir: "{code:GetSSMS20ExtDir}"; Check: CheckSSMS20; Flags: ignoreversion
Source: "..\AkmlSql.Ssms20\AkmlSql.Ssms20.pkgdef"; DestDir: "{code:GetSSMS20ExtDir}"; Check: CheckSSMS20; Flags: ignoreversion
Source: "..\AkmlSql.Ssms20\source.extension.vsixmanifest"; DestDir: "{code:GetSSMS20ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckSSMS20; Flags: ignoreversion

; SSMS 21 (x64) extension files
Source: "..\AkmlSql.Ssms21\bin\Release\net472\*.dll"; DestDir: "{code:GetSSMS21ExtDir}"; Check: CheckSSMS21; Flags: ignoreversion
Source: "..\AkmlSql.Ssms21\AkmlSql.Ssms21.pkgdef"; DestDir: "{code:GetSSMS21ExtDir}"; Check: CheckSSMS21; Flags: ignoreversion
Source: "..\AkmlSql.Ssms21\source.extension.vsixmanifest"; DestDir: "{code:GetSSMS21ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckSSMS21; Flags: ignoreversion

; SSMS 22 (x64) extension files
Source: "..\AkmlSql.Ssms22\bin\Release\net472\*.dll"; DestDir: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\AkmlSql.Ssms22.pkgdef"; DestDir: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\source.extension.vsixmanifest"; DestDir: "{code:GetSSMS22ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckSSMS22; Flags: ignoreversion

; VS 2019 (x86) extension files
Source: "..\AkmlSql.VS2019\bin\Release\net472\*.dll"; DestDir: "{code:GetVS2019ExtDir}"; Check: CheckVS2019; Flags: ignoreversion
Source: "..\AkmlSql.VS2019\AkmlSql.VS2019.pkgdef"; DestDir: "{code:GetVS2019ExtDir}"; Check: CheckVS2019; Flags: ignoreversion
Source: "..\AkmlSql.VS2019\source.extension.vsixmanifest"; DestDir: "{code:GetVS2019ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckVS2019; Flags: ignoreversion

; VS 2022 (x64) extension files
Source: "..\AkmlSql.VS2022\bin\Release\net472\*.dll"; DestDir: "{code:GetVS2022ExtDir}"; Check: CheckVS2022; Flags: ignoreversion
Source: "..\AkmlSql.VS2022\AkmlSql.VS2022.pkgdef"; DestDir: "{code:GetVS2022ExtDir}"; Check: CheckVS2022; Flags: ignoreversion
Source: "..\AkmlSql.VS2022\source.extension.vsixmanifest"; DestDir: "{code:GetVS2022ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckVS2022; Flags: ignoreversion

; VS 2026 (x64) extension files
Source: "..\AkmlSql.VS2026\bin\Release\net472\*.dll"; DestDir: "{code:GetVS2026ExtDir}"; Check: CheckVS2026; Flags: ignoreversion
Source: "..\AkmlSql.VS2026\AkmlSql.VS2026.pkgdef"; DestDir: "{code:GetVS2026ExtDir}"; Check: CheckVS2026; Flags: ignoreversion
Source: "..\AkmlSql.VS2026\source.extension.vsixmanifest"; DestDir: "{code:GetVS2026ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckVS2026; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName} Settings"; Filename: "{app}\AkmlSql.Core.dll"; Comment: "AKML SQL"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Tasks]
Name: "importsqlprompt"; Description: "Import formatting styles from &SQL Prompt"; GroupDescription: "Migration:"; Check: SqlPromptConfigExists; Flags: unchecked

[UninstallDelete]
; Clean up extension directories for selected targets only
Type: filesandordirs; Name: "{code:GetSSMS20ExtDir}"; Check: CheckSSMS20
Type: filesandordirs; Name: "{code:GetSSMS21ExtDir}"; Check: CheckSSMS21
Type: filesandordirs; Name: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22
Type: filesandordirs; Name: "{code:GetVS2019ExtDir}"; Check: CheckVS2019
Type: filesandordirs; Name: "{code:GetVS2022ExtDir}"; Check: CheckVS2022
Type: filesandordirs; Name: "{code:GetVS2026ExtDir}"; Check: CheckVS2026

#include "environment-scanner.iss"

[Code]

var
  OptionsPage: TInputOptionWizardPage;
  AutoUpdateEnabled: Boolean;
  TelemetryEnabled: Boolean;
  ImportSqlPromptEnabled: Boolean;

// --- SQL Prompt Detection (T033) ---

// Returns True if Redgate SQL Prompt config directory exists on this machine.
// Used as a [Tasks] Check function to show the import checkbox only when relevant.
function SqlPromptConfigExists: Boolean;
begin
  Result := DirExists(ExpandConstant('{localappdata}\Red Gate\SQL Prompt'));
end;

// --- SQL Prompt Import Staging (T034) ---

// Escapes backslashes in a path for safe embedding in JSON strings.
// e.g. 'C:\Users\foo' -> 'C:\\Users\\foo'
function EscapeJsonPath(const Path: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(Path) do
  begin
    if Path[I] = '\' then
      Result := Result + '\\'
    else
      Result := Result + Path[I];
  end;
end;

// Scans the SQL Prompt config directory for .sqlpromptstylev2 and .sqlpromptstyle files,
// copies them to an import staging directory, and writes a pending-import.json manifest.
// The engine picks up this manifest on next startup and runs SqlPromptImporter.
procedure StageSqlPromptStyles;
var
  SqlPromptDir: String;
  StagingDir: String;
  PendingPath: String;
  FindRec: TFindRec;
  FileList: String;
  FileCount: Integer;
  SourcePath: String;
  DestPath: String;
begin
  SqlPromptDir := ExpandConstant('{localappdata}\Red Gate\SQL Prompt');
  StagingDir := ExpandConstant('{userappdata}\AKML SQL\import-staging');
  PendingPath := ExpandConstant('{userappdata}\AKML SQL\pending-import.json');

  if not DirExists(SqlPromptDir) then
  begin
    Log('SQL Prompt directory not found; skipping import staging.');
    Exit;
  end;

  ForceDirectories(StagingDir);
  FileList := '';
  FileCount := 0;

  // Search for .sqlpromptstylev2 files (newer format)
  if FindFirst(SqlPromptDir + '\*.sqlpromptstylev2', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SourcePath := SqlPromptDir + '\' + FindRec.Name;
          DestPath := StagingDir + '\' + FindRec.Name;
          if FileCopy(SourcePath, DestPath, False) then
          begin
            if FileList <> '' then
              FileList := FileList + ',';
            FileList := FileList + '"' + EscapeJsonPath(StagingDir + '\' + FindRec.Name) + '"';
            FileCount := FileCount + 1;
            Log('Staged SQL Prompt style: ' + FindRec.Name);
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  // Search for .sqlpromptstyle files (older format)
  if FindFirst(SqlPromptDir + '\*.sqlpromptstyle', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SourcePath := SqlPromptDir + '\' + FindRec.Name;
          DestPath := StagingDir + '\' + FindRec.Name;
          if FileCopy(SourcePath, DestPath, False) then
          begin
            if FileList <> '' then
              FileList := FileList + ',';
            FileList := FileList + '"' + EscapeJsonPath(StagingDir + '\' + FindRec.Name) + '"';
            FileCount := FileCount + 1;
            Log('Staged SQL Prompt style: ' + FindRec.Name);
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  // Also check in Styles subdirectory (some SQL Prompt versions store styles there)
  if DirExists(SqlPromptDir + '\Styles') then
  begin
    if FindFirst(SqlPromptDir + '\Styles\*.sqlpromptstylev2', FindRec) then
    begin
      try
        repeat
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
          begin
            SourcePath := SqlPromptDir + '\Styles\' + FindRec.Name;
            DestPath := StagingDir + '\' + FindRec.Name;
            if FileCopy(SourcePath, DestPath, False) then
            begin
              if FileList <> '' then
                FileList := FileList + ',';
              FileList := FileList + '"' + EscapeJsonPath(StagingDir + '\' + FindRec.Name) + '"';
              FileCount := FileCount + 1;
              Log('Staged SQL Prompt style (Styles subdir): ' + FindRec.Name);
            end;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;

    if FindFirst(SqlPromptDir + '\Styles\*.sqlpromptstyle', FindRec) then
    begin
      try
        repeat
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
          begin
            SourcePath := SqlPromptDir + '\Styles\' + FindRec.Name;
            DestPath := StagingDir + '\' + FindRec.Name;
            if FileCopy(SourcePath, DestPath, False) then
            begin
              if FileList <> '' then
                FileList := FileList + ',';
              FileList := FileList + '"' + EscapeJsonPath(StagingDir + '\' + FindRec.Name) + '"';
              FileCount := FileCount + 1;
              Log('Staged SQL Prompt style (Styles subdir): ' + FindRec.Name);
            end;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;
  end;

  if FileCount > 0 then
  begin
    // Write the pending-import.json manifest for the engine to process
    SaveStringToFile(PendingPath,
      '{"source":"SQL Prompt","files":[' + FileList + ']}', False);
    Log('Wrote pending-import.json with ' + IntToStr(FileCount) + ' file(s).');
  end
  else
    Log('No SQL Prompt style files found to stage.');
end;

// --- Wizard Initialization ---

procedure InitializeWizard;
begin
  // Run environment scan
  RunFullScan;

  // Create Environment Scan page (Screen 3)
  EnvPage := CreateCustomPage(wpInfoBefore,
    'Detected Environments',
    'Select which IDEs should have AKML SQL installed.');

  EnvCheckListBox := TNewCheckListBox.Create(EnvPage);
  EnvCheckListBox.Parent := EnvPage.Surface;
  EnvCheckListBox.Left := 0;
  EnvCheckListBox.Top := 0;
  EnvCheckListBox.Width := EnvPage.SurfaceWidth;
  EnvCheckListBox.Height := EnvPage.SurfaceHeight - 30;
  EnvCheckListBox.Flat := True;
  EnvCheckListBox.ShowLines := True;

  PopulateEnvCheckList;
  EnvCheckListBox.OnClickCheck := @EnvCheckListBoxClickCheck;

  // Create Additional Options page (Screen 5)
  OptionsPage := CreateInputOptionPage(wpSelectDir,
    'Additional Options',
    'Configure AKML SQL behavior.',
    'Select the options you prefer:',
    False, False);
  OptionsPage.Add('Check for updates automatically (once per 24h)');
  OptionsPage.Add('Send anonymous usage telemetry (no PII)');
  OptionsPage.Values[0] := True;   // Auto-update ON by default
  OptionsPage.Values[1] := False;  // Telemetry OFF by default
end;

// --- Silent Mode Validation ---

function InitializeSetup: Boolean;
var
  AcceptEula: String;
begin
  Result := True;

  // Check /VERYSILENT requires /ACCEPTEULA
  if WizardSilent then
  begin
    AcceptEula := ExpandConstant('{param:ACCEPTEULA|}');
    if AcceptEula = '' then
    begin
      Log('ERROR: /VERYSILENT requires /ACCEPTEULA. Aborting.');
      MsgBox('/VERYSILENT requires /ACCEPTEULA to be specified.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // In silent mode, scan environment first then apply target selections
  if WizardSilent then
  begin
    RunFullScan;
    ApplySilentTargets;
  end;

  // Apply /NOTELEMETRY, /TELEMETRY, and /NOUPDATE
  AutoUpdateEnabled := ExpandConstant('{param:NOUPDATE|}') = '';
  TelemetryEnabled := ExpandConstant('{param:TELEMETRY|}') <> ''; // Off by default, enable with /TELEMETRY
  if ExpandConstant('{param:NOTELEMETRY|}') <> '' then
    TelemetryEnabled := False;

  // Apply /IMPORTSQLPROMPT for silent mode SQL Prompt style import
  ImportSqlPromptEnabled := ExpandConstant('{param:IMPORTSQLPROMPT|}') <> '';
end;

// --- Sync checkbox state back to targets array ---

procedure CurPageChanged(CurPageID: Integer);
var
  I: Integer;
begin
  if CurPageID = EnvPage.ID then
  begin
    // Sync Next button state with existing checkboxes
    // (scan and populate already done in InitializeWizard)
    UpdateEnvNextButton;
  end;

  // When leaving the environment page, sync selections
  if CurPageID > EnvPage.ID then
  begin
    for I := 0 to TargetCount - 1 do
    begin
      if I < EnvCheckListBox.Items.Count then
        Targets[I].IsSelected := EnvCheckListBox.Checked[I];
    end;
  end;

  // When leaving options page, capture values
  if (CurPageID > OptionsPage.ID) and not WizardSilent then
  begin
    AutoUpdateEnabled := OptionsPage.Values[0];
    TelemetryEnabled := OptionsPage.Values[1];
  end;
end;

// --- No targets selected validation ---

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  AnySelected: Boolean;
begin
  Result := True;

  if CurPageID = EnvPage.ID then
  begin
    AnySelected := False;
    for I := 0 to EnvCheckListBox.Items.Count - 1 do
    begin
      if EnvCheckListBox.Checked[I] then
      begin
        AnySelected := True;
        Break;
      end;
    end;

    if not AnySelected then
    begin
      MsgBox('Please select at least one target environment.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// --- Post-Install Actions ---

// Helper: Generate a pseudo-UUID v4 from timestamp and random values
function GenerateInstallId: String;
var
  S: String;
begin
  S := GetDateTimeString('yyyymmddhhnnss', #0, #0);
  Result := Copy(S, 1, 8) + '-'
    + IntToStr(Random(9999)) + '-4'
    + IntToStr(Random(999)) + '-'
    + IntToStr(8000 + Random(3999)) + '-'
    + IntToStr(Random(999999999999));
end;

// Helper: Clear MEF component model cache for VS with wildcard prefix (e.g. '16.0').
// SAFETY: Only clears ComponentModelCache (MEF composition cache).
// NEVER touches privateregistry.bin — that contains IDE settings, tool window layouts,
// keybindings, and other user state that must not be reset.
procedure ClearVSMefCaches(const VersionPrefix: String);
var
  BasePath: String;
  FindRec: TFindRec;
begin
  BasePath := ExpandConstant('{localappdata}') + '\Microsoft\VisualStudio\';
  if FindFirst(BasePath + VersionPrefix + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          DelTree(BasePath + FindRec.Name + '\ComponentModelCache', True, True, True);
          Log('Cleared ComponentModelCache for VS ' + FindRec.Name);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Helper: Clear MEF component model cache for SSMS with wildcard prefix (e.g. '22.0').
// SAFETY: Only clears ComponentModelCache (MEF composition cache).
// NEVER touches privateregistry.bin, 1033/ CTM cache, or other IDE state.
procedure ClearSSMSMefCaches(const VersionPrefix: String);
var
  BasePath: String;
  FindRec: TFindRec;
begin
  BasePath := ExpandConstant('{localappdata}') + '\Microsoft\SQL Server Management Studio\';
  if FindFirst(BasePath + VersionPrefix + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          DelTree(BasePath + FindRec.Name + '\ComponentModelCache', True, True, True);
          Log('Cleared ComponentModelCache for SSMS ' + FindRec.Name);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  // Also check under Microsoft\SSMS\ (SSMS 22+ uses this path)
  BasePath := ExpandConstant('{localappdata}') + '\Microsoft\SSMS\';
  if FindFirst(BasePath + VersionPrefix + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          DelTree(BasePath + FindRec.Name + '\ComponentModelCache', True, True, True);
          Log('Cleared ComponentModelCache for SSMS ' + FindRec.Name);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I: Integer;
  ConfigDir: String;
  ConfigPath: String;
  ConfigJson: String;
  TelemetryStr: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Clear MEF caches for selected targets using proper directory enumeration
    Log('Clearing MEF component caches...');

    if IsTargetSelected('20') then
      ClearSSMSMefCaches('20.0');
    if IsTargetSelected('21') then
      ClearSSMSMefCaches('21.0');
    if IsTargetSelected('22') then
      ClearSSMSMefCaches('22.0');
    if IsTargetSelected('2019') then
      ClearVSMefCaches('16.0');
    if IsTargetSelected('2022') then
      ClearVSMefCaches('17.0');
    if IsTargetSelected('2026') then
      ClearVSMefCaches('18.0');

    Log('MEF caches cleared.');

    // Touch extensions.configurationchanged marker so the IDE re-scans for new extensions
    Log('Touching extensions.configurationchanged markers...');
    for I := 0 to TargetCount - 1 do
    begin
      if Targets[I].IsSelected and (Targets[I].ExtensionsPath <> '') then
      begin
        // Go up from ..\Extensions\AkmlSql to ..\Extensions\
        ConfigDir := ExtractFilePath(Targets[I].ExtensionsPath);
        ConfigPath := ConfigDir + 'extensions.configurationchanged';
        SaveStringToFile(ConfigPath, GetDateTimeString('yyyy-mm-dd hh:nn:ss', #0, #0), False);
        Log('Touched: ' + ConfigPath);
      end;
    end;

    // Write config.json only if it does not already exist (preserve on upgrade)
    ConfigDir := ExpandConstant('{userappdata}') + '\AKML SQL';
    ConfigPath := ConfigDir + '\config.json';
    ForceDirectories(ConfigDir);

    if not FileExists(ConfigPath) then
    begin
      if TelemetryEnabled then
        TelemetryStr := 'true'
      else
        TelemetryStr := 'false';

      ConfigJson := '{"configVersion":1,"autoUpdateEnabled":';
      if AutoUpdateEnabled then
        ConfigJson := ConfigJson + 'true'
      else
        ConfigJson := ConfigJson + 'false';
      ConfigJson := ConfigJson + ',"telemetryEnabled":' + TelemetryStr
        + ',"installId":"' + GenerateInstallId + '"'
        + ',"installedTargets":[]}';

      SaveStringToFile(ConfigPath, ConfigJson, False);
      Log('Default configuration written to ' + ConfigPath);
    end
    else
      Log('Existing config.json preserved at ' + ConfigPath);

    // Create logs directory
    ForceDirectories(ConfigDir + '\logs');

    // T034: Stage SQL Prompt styles for engine import if user opted in
    // In interactive mode, check the task checkbox; in silent mode, check the /IMPORTSQLPROMPT flag
    if WizardSilent then
    begin
      if ImportSqlPromptEnabled and SqlPromptConfigExists then
      begin
        Log('SQL Prompt import requested via /IMPORTSQLPROMPT flag.');
        StageSqlPromptStyles;
      end;
    end
    else
    begin
      if WizardIsTaskSelected('importsqlprompt') then
      begin
        Log('SQL Prompt import task selected by user.');
        StageSqlPromptStyles;
      end;
    end;
  end;
end;

// --- Uninstall Actions ---

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RemoveUserData: Boolean;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clear MEF ComponentModelCache so the IDE stops trying to load the removed extension.
    // SAFETY: Only clears ComponentModelCache — never touches privateregistry.bin or other IDE state.
    ClearSSMSMefCaches('20.0');
    ClearSSMSMefCaches('21.0');
    ClearSSMSMefCaches('22.0');
    ClearVSMefCaches('16.0');
    ClearVSMefCaches('17.0');
    ClearVSMefCaches('18.0');

    // Ask about user data removal
    RemoveUserData := MsgBox('Do you want to remove your AKML SQL settings and log files?'#13#10#13#10
      + 'Location: ' + ExpandConstant('{userappdata}') + '\AKML SQL',
      mbConfirmation, MB_YESNO) = IDYES;

    if RemoveUserData then
    begin
      DelTree(ExpandConstant('{userappdata}') + '\AKML SQL', True, True, True);
      DelTree(ExpandConstant('{localappdata}') + '\AKML SQL', True, True, True);
      Log('User data removed.');
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
  RunningList: String;
  ForceClose: Boolean;
  ResultCode: Integer;
begin
  Result := '';
  RunningList := '';
  ForceClose := ExpandConstant('{param:FORCECLOSEAPPS|}') <> '';

  for I := 0 to TargetCount - 1 do
  begin
    if Targets[I].IsSelected and Targets[I].IsRunning then
    begin
      if ForceClose then
      begin
        if Pos('SSMS', Targets[I].Name) > 0 then
          Exec('taskkill.exe', '/F /IM Ssms.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
        else
          Exec('taskkill.exe', '/F /IM devenv.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end
      else
      begin
        if RunningList <> '' then
          RunningList := RunningList + ', ';
        RunningList := RunningList + Targets[I].Name;
      end;
    end;
  end;

  if (RunningList <> '') and not WizardSilent then
  begin
    if MsgBox('The following applications are running and should be closed:'#13#10#13#10
      + RunningList + #13#10#13#10
      + 'Click OK to close them automatically, or Cancel to close them manually.',
      mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      if IsProcessRunning('Ssms.exe') then
        Exec('taskkill.exe', '/F /IM Ssms.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if IsProcessRunning('devenv.exe') then
        Exec('taskkill.exe', '/F /IM devenv.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end
    else
    begin
      Result := 'Please close the running applications and try again.';
    end;
  end;

  // MEF cache clearing is done in ssPostInstall (after files are deployed),
  // not here. Clearing before deployment is unnecessary and wasteful.
  // SAFETY: We never touch privateregistry.bin, 1033/ CTM, or other IDE state.
end;
