; ============================================================================
; AKML SQL Installer
; Wizard-based Windows EXE installer for SQL Server Management Studio 22
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
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=ssms22
;     Install only to SSMS 22.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /LOG="C:\Logs\install.log"
;     Install with verbose logging. /LOG is a native Inno Setup flag that
;     writes detailed setup activity to the specified file. If no path is
;     given (/LOG without =), defaults to %TEMP%\Setup Log YYYY-MM-DD #NNN.txt.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /NOUPDATE /NOTELEMETRY
;     Install with automatic updates and anonymous error reports turned off.
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /FORCECLOSEAPPS
;     Force-close running SSMS instances before installing. (Redundant in
;     silent mode: silent installs ALWAYS force-close selected running hosts —
;     there is no one to answer the prompt, and a still-open IDE respawns the
;     engine mid-install, failing the file copy with DeleteFile code 5.)
;
;   AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /IMPORTSQLPROMPT
;     Import SQL Prompt formatting styles during installation.
;
; Flags:
;   /VERYSILENT       No UI, no progress dialog
;   /ACCEPTEULA       Accept the EULA (required for silent mode)
;   /TARGETS=...      Target list: ssms22 (vs2026 from older scripts is accepted and ignored)
;   /LOG[=file]       Write detailed install log (native Inno Setup feature)
;   /NOUPDATE         Turn automatic updates off and do not create the update-check scheduled task
;   /NOTELEMETRY      Turn anonymous error reports off (they are on by default)
;   /TELEMETRY        Turn anonymous error reports on (the default; kept for older scripts)
;   A silent UPGRADE keeps the user's existing choices for both unless a flag says otherwise.
;   /FORCECLOSEAPPS   Force-close running SSMS without prompting (default in silent mode)
;   /IMPORTSQLPROMPT  Import SQL Prompt styles (only if SQL Prompt config detected)
;
; TODO T096: On uninstall, restore native SSMS IntelliSense if AKML SQL disabled it.
;   Read %AppData%/AKML SQL/config.json, check DisabledNativeIntelliSense flag,
;   and if true, set EnableIntelliSense=1 in the SSMS registry keys:
;     HKCU\Software\Microsoft\SQL Server Management Studio\22.0\Settings\IntelliSense
;     HKCU\Software\Microsoft\SSMS\22.0\Settings\IntelliSense
;

#define MyAppName "AKML SQL"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Mohamed Khamis"
#define MyAppURL "https://akml.khamis.work"
#define MyAppId "{{F7E8A9B0-C1D2-E3F4-A5B6-C7D8E9F0A1B2}"

; --- Resolve $version$ in VSIX manifests before [Files] references them ---
; Shell csprojs have CreateVsixContainer=false, so VSSDK never substitutes
; $version$. We do it here via a PowerShell helper. Runs at ISCC compile
; time so ANY invocation (build.ps1, Deploy-Build-Release.ps1, bare ISCC)
; produces the generated\<Target>\extension.vsixmanifest files that the
; [Files] section sources below.
#define ManifestExit Exec( \
    "powershell.exe", \
    "-NoProfile -ExecutionPolicy Bypass -File " \
      + AddQuotes(SourcePath + "preprocess-manifests.ps1") \
      + " -Version " + AddQuotes(MyAppVersion), \
    SourcePath)
#if ManifestExit != 0
  #error "preprocess-manifests.ps1 failed (see output above)"
#endif

[Setup]
; AppId is fixed — Inno Setup uses this to detect existing installations.
; Together with UsePreviousAppDir=yes, this enables seamless in-place upgrade:
; re-running the installer over an existing installation will upgrade in-place
; without requiring uninstall first. Inno Setup's built-in repair/upgrade logic
; handles version bumps automatically as long as AppId remains the same.
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; AppVerName is what Windows "Programs and Features" shows as the display name
; — by setting it explicitly to "Name Version" we guarantee the version is
; always visible in the installed-programs list (otherwise newer Windows
; builds sometimes show only AppName without the version column populated).
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/feedback
AppUpdatesURL={#MyAppURL}/download
AppContact={#MyAppURL}/feedback
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
; VersionInfoVersion stamps the compiled installer .EXE's own file-properties
; (Details tab in Explorer → "File version" / "Product version"). Inno Setup
; requires the 4-segment x.y.z.w form here; our MyAppVersion already matches.
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
; New installs: Program Files\AKML SQL (64-bit). An install made by the earlier 32-bit installer
; stays in its Program Files (x86) folder -- see DefaultInstallDir and MigrateLegacy32BitInstall.
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName={#MyAppName}
; A real license page with "I accept" (it used to be an information page with nothing to accept).
; Silent installs still need /ACCEPTEULA.
LicenseFile=LICENSE.txt
; Keep the installer filename stable — Deploy-Build-Release.ps1 and any
; existing download links expect AKMLSQLSetup.exe. The version is embedded
; in the EXE's file properties via VersionInfoVersion above.
OutputBaseFilename=AKMLSQLSetup
OutputDir=Output
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousAppDir=yes
; 64-bit installer and install mode: every AKML SQL component is 64-bit. Switching modes on its own
; would install a SECOND copy beside a 32-bit-mode install (Inno only looks for the previous
; install in the new mode's registry view); DefaultInstallDir / MigrateLegacy32BitInstall take the
; existing install over instead. Proven with probe installers before this was switched on.
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 / Server 2019: the oldest system the updater's notifications support.
MinVersion=10.0.17763
; Start-menu folder page: every product installs into "AKML SQL"; one less page to click through.
DisableProgramGroupPage=yes
; Always keep a setup log (%TEMP%\Setup Log *.txt) -- the first thing support asks for.
SetupLogging=yes
; The akmlsql-update: URL scheme ([Registry]) -- tell Explorer about it.
ChangesAssociations=yes
; Branded assets, generated from the design canvas artboards (TURN 5/6).
; icon.ico carries 10 entries (16-256), all using the 5a glyph-only mark — the
; design keeps the wordmark off the icon because it smudges below ~48px.
; The wizard images list 100/125/150/200% variants; Inno picks the one matching
; the current DPI. WizardSmallImageFile renders into a 55x55 box so those must
; stay square — the design's 497x58 header strip is an NSIS size and has no
; equivalent control in Inno's modern wizard.
SetupIconFile=assets\icon.ico
WizardImageFile=assets\sidebar.bmp,assets\sidebar-125.bmp,assets\sidebar-150.bmp,assets\sidebar-200.bmp
WizardSmallImageFile=assets\banner.bmp,assets\banner-125.bmp,assets\banner-150.bmp,assets\banner-200.bmp
; Windows 11 look that follows the system's light/dark setting; no bevel lines.
WizardStyle=modern dynamic windows11 hidebevels
WizardSizePercent=120
DisableWelcomePage=no
; An icon file, not a DLL: AkmlSql.Core.dll carries no icon, so Apps & features showed a blank.
UninstallDisplayIcon={app}\AKMLSQL.ico
UninstallDisplayName={#MyAppName}
; T030: Prompt user to close running SSMS instances before installing.
; Note: AppMutex is not used because SSMS does not create named mutexes that
; we can reliably detect. CloseApplications with CloseApplicationsFilter is the
; correct approach — Inno Setup uses the Windows Restart Manager API to detect
; which apps hold locks on files being replaced.
CloseApplications=yes
CloseApplicationsFilter=Ssms.exe

; --- Code signing (off unless a thumbprint is supplied) ------------------------------
; SmartScreen builds reputation from the SIGNATURE, not the download domain: an unsigned
; installer from a young domain gets held/flagged by the browser (the "download doesn't
; start until I click twice" report). Sign by passing the cert's SHA-1 thumbprint:
;   ISCC /DCodeSignThumbprint=<thumbprint> AkmlSqlSetup.iss
; build.ps1 wires this automatically when AKML_CODESIGN_THUMBPRINT is set AND signtool.exe
; is available (Windows Kits or PATH), passing the matching /Sakmlsign command. The cert
; (with private key) must be in the CurrentUser or LocalMachine "My" store. Timestamping
; keeps the signature valid after the cert itself expires.
#ifndef CodeSignThumbprint
  #define CodeSignThumbprint ""
#endif
#if CodeSignThumbprint != ""
SignTool=akmlsign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Core binaries to base install directory (sourced from shell build output which includes all dependencies)
Source: "..\AkmlSql.Ssms22\bin\Release\net472\AkmlSql.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\bin\Release\net472\Serilog.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\bin\Release\net472\Serilog.Sinks.File.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AkmlSql.Updater\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\AkmlSql.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion
; Icon for Apps & features, the Start-menu shortcuts and the akmlsql-update: scheme.
Source: "assets\icon.ico"; DestDir: "{app}"; DestName: "AKMLSQL.ico"; Flags: ignoreversion
; Registers / removes the update-check scheduled task (see [Run] / [UninstallRun]).
Source: "update-task.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion
; Engine is SelfContained + PublishSingleFile=false — deploy ALL published output
Source: "..\AkmlSql.Engine\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}\Engine"; Flags: ignoreversion recursesubdirs
Source: "..\AkmlSql.Analyzer\bin\Release\net10.0\win-x64\publish\AkmlSql.Analyzer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

; SSMS 22 (x64) extension files — all DLLs from build output plus pkgdef and manifest
Source: "..\AkmlSql.Ssms22\bin\Release\net472\*.dll"; DestDir: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22; Flags: ignoreversion
Source: "..\AkmlSql.Ssms22\AkmlSql.Ssms22.pkgdef"; DestDir: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22; Flags: ignoreversion
Source: "generated\AkmlSql.Ssms22\extension.vsixmanifest"; DestDir: "{code:GetSSMS22ExtDir}"; DestName: "extension.vsixmanifest"; Check: CheckSSMS22; Flags: ignoreversion


[Icons]
; "Settings" pointed at AkmlSql.Core.dll, which cannot be opened -- settings live inside SSMS
; (Tools > AKML SQL > Options). The update shortcut's AppUserModelID is what lets the updater
; show Windows notifications at all: an unpackaged app's notifications are attributed through a
; Start-menu shortcut carrying its ID. Keep it equal to Constants.AppUserModelId.
Name: "{group}\Check for {#MyAppName} updates"; Filename: "{app}\AkmlSql.Updater.exe"; Parameters: "--check-now"; \
    IconFilename: "{app}\AKMLSQL.ico"; AppUserModelID: "AKML.AKMLSQL"; Comment: "Check for a newer version of AKML SQL now"
Name: "{group}\{#MyAppName} documentation"; Filename: "{#MyAppURL}/docs"
Name: "{group}\Report a problem with {#MyAppName}"; Filename: "{#MyAppURL}/feedback"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\AKMLSQL.ico"

[Registry]
; akmlsql-update: -- what the update notification's "Install now" and body click run. The URL
; carries no data the updater acts on: it only ever launches the installer it downloaded and
; verified itself, verified again right before launch, with the normal installer UI.
Root: HKLM; Subkey: "Software\Classes\akmlsql-update"; ValueType: string; ValueName: ""; ValueData: "URL:AKML SQL update"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\akmlsql-update"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\akmlsql-update\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\AKMLSQL.ico"
Root: HKLM; Subkey: "Software\Classes\akmlsql-update\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\AkmlSql.Updater.exe"" --install ""%1"""

[Run]
; Automatic updates: the "AKML SQL\Update Check" scheduled task (daily and at sign-in) downloads,
; verifies and announces updates; it never installs. Present only while automatic updates are on
; -- unticking the option on an upgrade removes an existing task.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Support\update-task.ps1"" -Action Add -UpdaterPath ""{app}\AkmlSql.Updater.exe"""; \
    StatusMsg: "Scheduling automatic update checks..."; Check: AutoUpdateWanted; Flags: runhidden
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Support\update-task.ps1"" -Action Remove"; \
    Check: not AutoUpdateWanted; Flags: runhidden
; The options page's choices, written by the updater AS THE SIGNED-IN USER -- not by this elevated
; installer -- so they land in that user's own %AppData%\AKML SQL\config.json.
Filename: "{app}\AkmlSql.Updater.exe"; Parameters: "--configure {code:PreferenceArgs}"; \
    StatusMsg: "Saving your preferences..."; Check: ShouldWritePreferences; Flags: runasoriginaluser runhidden
; Finish page.
Filename: "{code:WebSiteUrl}"; Description: "Open AKML SQL Web"; Check: IsWebSiteHosted; \
    Flags: postinstall shellexec skipifsilent nowait
Filename: "{#MyAppURL}/download"; Description: "See what's new in {#MyAppName} {#MyAppVersion}"; \
    Flags: postinstall shellexec skipifsilent nowait unchecked

[UninstallRun]
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Support\update-task.ps1"" -Action Remove"; \
    RunOnceId: "RemoveUpdateTask"; Flags: runhidden

[Tasks]
Name: "importsqlprompt"; Description: "Import formatting styles from &SQL Prompt"; GroupDescription: "Migration:"; Check: SqlPromptConfigExists; Flags: unchecked

[UninstallDelete]
; Clean up extension directories for selected targets only
Type: filesandordirs; Name: "{code:GetSSMS22ExtDir}"; Check: CheckSSMS22
; An install taken over from the 32-bit installer starts a fresh uninstall log, which does not
; record that {app} was created (it already existed) -- without these the folder, and any file
; only an old version installed, would be left behind.
Type: filesandordirs; Name: "{app}\Engine"
Type: dirifempty; Name: "{app}"

#include "environment-scanner.iss"

[UninstallDelete]
; Spec 026 (M4 closure) FR-001: bring in the Web-edition installer (the component group, [Files],
; [Run]/[UninstallRun] steps, and the Web_* hook procedures). environment-scanner.iss (above) ends
; in [Code]; the [UninstallDelete] header immediately above re-opens an ini-section context (a
; section header at column 1 is recognised even inside [Code]; it adds no entries), so these ;
; comments AND web-installer.iss's own leading ; comments parse as ini comments, not Pascal.
; web-installer.iss's [Code] procedures still land before the main [Code] event handlers below --
; Inno Pascal has no forward references, so the hooks (Web_Init / Web_NextButton / Web_Skip /
; Web_PostInstall / Web_Uninstall / Web_ValidateSilentFlags) must be declared before the
; InitializeWizard / NextButtonClick / ShouldSkipPage / CurStepChanged / etc. that call them.
#include "web-installer.iss"

[Code]

var
  OptionsPage: TInputOptionWizardPage;
  AutoUpdateEnabled: Boolean;
  TelemetryEnabled: Boolean;
  ImportSqlPromptEnabled: Boolean;
  // True when this run should write the two preferences for the signed-in user (a new install,
  // an interactive run -- the page shows their current values -- or an explicit silent flag).
  PreferencesChosen: Boolean;
  // Set when an install made by the earlier 32-bit installer is being taken over (see
  // MigrateLegacy32BitInstall); read again at ssPostInstall and on failure.
  LegacyInstallDir: String;
  LegacyUninstallerRetired: Boolean;
  InstallSucceeded: Boolean;
  EnvEmptyLabel: TNewStaticText;
  OptionsNote: TNewStaticText;
  // PR-249 review follow-up: set by TerminateAkmlBackgroundProcesses when the AkmlSqlWebEngine
  // service existed AND was running before install; read by CurStepChanged(ssPostInstall) to
  // restart it on a desktop-only upgrade (web component unticked, so Web_PostInstall won't).
  // Declared here (not next to its writer) because CurStepChanged appears earlier in the file
  // and Inno Pascal has no forward references.
  GWebServiceWasRunning: Boolean;

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
          if CopyFile(SourcePath, DestPath, False) then
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
          if CopyFile(SourcePath, DestPath, False) then
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
            if CopyFile(SourcePath, DestPath, False) then
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
            if CopyFile(SourcePath, DestPath, False) then
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

// --- 32-bit install takeover ---------------------------------------------------------------
//
// Installs up to 1.26.09xx were made by a 32-bit installer in 32-bit mode: files under Program
// Files (x86), uninstall entry in the 32-bit registry view. This installer is 64-bit and Inno only
// looks for a previous install in the 64-bit view, so on its own it would install a SECOND copy
// under Program Files and leave two entries in Apps & features. Instead it takes the old install
// over: same folder, a fresh uninstall log (the old one mixes 32- and 64-bit records and then
// cannot remove itself), and the old 32-bit uninstall entry removed once the install succeeds.
// Every step here was proven with small probe installers (upgrade, re-upgrade, uninstall, fresh).

const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F7E8A9B0-C1D2-E3F4-A5B6-C7D8E9F0A1B2}_is1';

function Legacy32BitInstallDir(var Dir: String): Boolean;
begin
  Result := (not RegKeyExists(HKLM64, UninstallKey))
            and RegQueryStringValue(HKLM32, UninstallKey, 'InstallLocation', Dir)
            and (Dir <> '');
  if Result then
    Dir := RemoveBackslashUnlessRoot(Dir);
end;

// [Setup] DefaultDirName: the old folder when taking over a 32-bit install, else Program Files.
function DefaultInstallDir(Param: String): String;
begin
  if not Legacy32BitInstallDir(Result) then
    Result := ExpandConstant('{autopf}\{#MyAppName}');
end;

// The components chosen last time live in the old 32-bit entry, which this installer cannot see
// on its own -- without this, an upgrade would quietly re-select the web edition for someone who
// had left it out.
procedure RestoreLegacyComponents;
var
  Selected, Deselected, Negated, Item: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKLM32, UninstallKey, 'Inno Setup: Selected Components', Selected) then
    Exit;
  RegQueryStringValue(HKLM32, UninstallKey, 'Inno Setup: Deselected Components', Deselected);

  Negated := '';
  while Deselected <> '' do
  begin
    P := Pos(',', Deselected);
    if P = 0 then P := Length(Deselected) + 1;
    Item := Trim(Copy(Deselected, 1, P - 1));
    Delete(Deselected, 1, P);
    if Item <> '' then
    begin
      if Negated <> '' then Negated := Negated + ',';
      Negated := Negated + '!' + Item;
    end;
  end;

  if Selected <> '' then WizardSelectComponents(Selected);
  if Negated <> '' then WizardSelectComponents(Negated);
  Log('Restored components from the 32-bit install: [' + Selected + '] deselected [' + Negated + ']');
end;

// Before any file is copied: set the old uninstaller aside, so this install starts its own log.
procedure MigrateLegacy32BitInstall;
var
  Dir: String;
begin
  if not Legacy32BitInstallDir(Dir) then
    Exit;
  LegacyInstallDir := Dir;
  if FileExists(AddBackslash(Dir) + 'unins000.dat') then
  begin
    RenameFile(AddBackslash(Dir) + 'unins000.dat', AddBackslash(Dir) + 'unins000.dat.x86');
    RenameFile(AddBackslash(Dir) + 'unins000.exe', AddBackslash(Dir) + 'unins000.exe.x86');
    LegacyUninstallerRetired := True;
    Log('Taking over the 32-bit install in ' + Dir + ': old uninstaller set aside');
  end;
end;

// After a successful install: the old entry and uninstaller go; the new 64-bit entry manages all.
procedure FinishLegacy32BitMigration;
begin
  if LegacyInstallDir = '' then
    Exit;
  RegDeleteKeyIncludingSubkeys(HKLM32, UninstallKey);
  DeleteFile(AddBackslash(LegacyInstallDir) + 'unins000.dat.x86');
  DeleteFile(AddBackslash(LegacyInstallDir) + 'unins000.exe.x86');
  Log('32-bit install taken over; its uninstall entry removed');
end;

// Setup failed or was cancelled after the old uninstaller was set aside: put it back, so the
// existing install can still be uninstalled.
procedure DeinitializeSetup;
begin
  if LegacyUninstallerRetired and not InstallSucceeded then
  begin
    RenameFile(AddBackslash(LegacyInstallDir) + 'unins000.dat.x86', AddBackslash(LegacyInstallDir) + 'unins000.dat');
    RenameFile(AddBackslash(LegacyInstallDir) + 'unins000.exe.x86', AddBackslash(LegacyInstallDir) + 'unins000.exe');
    Log('Setup did not finish: the 32-bit install''s uninstaller was restored');
  end;
end;

// --- Preferences: read the signed-in user's current values ------------------------------------

// A deliberately small reader for one boolean in config.json: '"key"' then ':' then true/false.
// Returns False when the file or the key is absent (the caller keeps its default).
function ReadConfigBool(const Json, Key: String; var Value: Boolean): Boolean;
var
  P, I: Integer;
  Rest: String;
begin
  Result := False;
  P := Pos('"' + Key + '"', Json);
  if P = 0 then Exit;
  Rest := Copy(Json, P + Length(Key) + 2, 40);
  I := Pos(':', Rest);
  if I = 0 then Exit;
  Rest := Trim(Copy(Rest, I + 1, 10));
  if Copy(Rest, 1, 4) = 'true' then begin Value := True; Result := True; end
  else if Copy(Rest, 1, 5) = 'false' then begin Value := False; Result := True; end;
end;

// config.json of the user running setup ({userappdata} is theirs: setup elevates the same account).
function ExistingConfig(var Json: String): Boolean;
var
  Raw: AnsiString;
begin
  Result := LoadStringFromFile(ExpandConstant('{userappdata}\AKML SQL\config.json'), Raw);
  if Result then Json := String(Raw);
end;

function OnOff(const Value: Boolean): String;
begin
  if Value then Result := 'on' else Result := 'off';
end;

// [Run] --configure arguments.
function PreferenceArgs(Param: String): String;
begin
  Result := 'auto-update=' + OnOff(AutoUpdateEnabled) + ' error-reports=' + OnOff(TelemetryEnabled);
end;

function ShouldWritePreferences: Boolean;
begin
  Result := PreferencesChosen;
end;

function AutoUpdateWanted: Boolean;
begin
  Result := AutoUpdateEnabled;
end;

// Finish page: the web edition's address, when this run hosts it on IIS.
function IsWebSiteHosted: Boolean;
begin
  Result := IsWebSelected() and WizardIsComponentSelected('web\iis') and (WebHostPage.SelectedValueIndex = 0);
end;

function WebSiteUrl(Param: String): String;
begin
  if WebIisPort = 80 then
    Result := 'http://localhost/'
  else
    Result := 'http://localhost:' + IntToStr(WebIisPort) + '/';
end;

// --- Wizard Initialization ---

procedure InitializeWizard;
begin
  // Run environment scan
  RunFullScan;

  // Environment page, right after the license.
  EnvPage := CreateCustomPage(wpLicense,
    'Where to install',
    'Choose the SQL tools that should get AKML SQL.');

  EnvCheckListBox := TNewCheckListBox.Create(EnvPage);
  EnvCheckListBox.Parent := EnvPage.Surface;
  EnvCheckListBox.Left := 0;
  EnvCheckListBox.Top := 0;
  EnvCheckListBox.Width := EnvPage.SurfaceWidth;
  EnvCheckListBox.Height := EnvPage.SurfaceHeight - ScaleY(40);
  EnvCheckListBox.Flat := True;
  EnvCheckListBox.ShowLines := True;

  PopulateEnvCheckList;
  EnvCheckListBox.OnClickCheck := @EnvCheckListBoxClickCheck;

  // No SSMS 22: say so, and let the web edition be installed on its own
  // (this page used to refuse to continue with nothing ticked, so a server could never get it).
  EnvEmptyLabel := TNewStaticText.Create(EnvPage);
  EnvEmptyLabel.Parent := EnvPage.Surface;
  EnvEmptyLabel.Left := 0;
  EnvEmptyLabel.Top := EnvCheckListBox.Top + EnvCheckListBox.Height + ScaleY(8);
  EnvEmptyLabel.Width := EnvPage.SurfaceWidth;
  EnvEmptyLabel.AutoSize := False;
  EnvEmptyLabel.WordWrap := True;
  EnvEmptyLabel.Height := ScaleY(32);
  if TargetCount = 0 then
  begin
    EnvCheckListBox.Visible := False;
    EnvEmptyLabel.Top := 0;
    EnvEmptyLabel.Height := ScaleY(60);
    EnvEmptyLabel.Caption := 'SQL Server Management Studio 22 was not found on this computer.' + #13#10 + #13#10 +
      'You can still install the AKML SQL web edition. Install SSMS 22 first and run setup again to add AKML SQL to it.';
  end
  else
    EnvEmptyLabel.Caption := 'AKML SQL works in SQL Server Management Studio 22. Untick it to install only the web edition.';

  // Options page. Error reports are on by default for a new install; an upgrade shows the user's
  // current choices (from their config.json), so nothing changes unless they change it.
  // Last page before Ready: after the components and the web edition's pages (custom pages placed
  // after wpSelectDir would come BEFORE the components page).
  OptionsPage := CreateInputOptionPage(wpSelectTasks,
    'Updates and error reports',
    'Keep AKML SQL current and help fix what goes wrong.',
    'You can change both later in SSMS: Tools > AKML SQL > Options > General.',
    False, False);
  OptionsPage.Add('Check for updates automatically, and download them in the background');
  OptionsPage.Add('Send anonymous error reports (recommended)');
  OptionsPage.Values[0] := AutoUpdateEnabled;
  OptionsPage.Values[1] := TelemetryEnabled;

  // The option list fills the whole page by default, which would push the note out of sight.
  OptionsPage.CheckListBox.Height := ScaleY(52);

  OptionsNote := TNewStaticText.Create(OptionsPage);
  OptionsNote.Parent := OptionsPage.Surface;
  OptionsNote.Left := 0;
  OptionsNote.Top := OptionsPage.CheckListBox.Top + OptionsPage.CheckListBox.Height + ScaleY(12);
  OptionsNote.Width := OptionsPage.SurfaceWidth;
  OptionsNote.AutoSize := False;
  OptionsNote.WordWrap := True;
  OptionsNote.Height := ScaleY(90);
  OptionsNote.Caption :=
    'Updates are downloaded and checked in the background; you are told when one is ready, and nothing is installed without your OK.' + #13#10 + #13#10 +
    'An error report is the error message and where in AKML SQL it happened -- never your queries or data. ' +
    'User, computer, server and database names are removed before it is sent. Details: {#MyAppURL}/privacy#app';

  // Taking over a 32-bit install: bring back what was installed last time.
  if Legacy32BitInstallDir(LegacyInstallDir) then
    RestoreLegacyComponents;
  LegacyInstallDir := '';

  // Spec 026 (M4 closure) FR-002: create the Web-edition wizard pages (Hosting / Network /
  // IIS Port / Bridge Port / install-summary). No-op visually when the web component is unticked
  // -- ShouldSkipPage hides them.
  Web_Init();
end;

// --- Silent Mode Validation ---

// Returns True if the named command-line param was passed either as a
// flag (/Name) or as a key=value (/Name=anything). {param:Name|} on its
// own returns '' for the flag-only form, which would reject the documented
// `/VERYSILENT /ACCEPTEULA` invocation.
function CmdLineParamExists(const Name: String): Boolean;
var
  I: Integer;
  Param, Target: String;
begin
  Result := False;
  Target := '/' + UpperCase(Name);
  for I := 1 to ParamCount do
  begin
    Param := UpperCase(ParamStr(I));
    if (Param = Target) or (Pos(Target + '=', Param) = 1) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ConfigText: String;
begin
  Result := True;

  // Check /VERYSILENT requires /ACCEPTEULA (flag or key=value form)
  if WizardSilent then
  begin
    if not CmdLineParamExists('ACCEPTEULA') then
    begin
      Log('ERROR: /VERYSILENT requires /ACCEPTEULA. Aborting.');
      MsgBox('/VERYSILENT requires /ACCEPTEULA to be specified.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // Spec 026 (M4 closure) US4 (FR-021..FR-024) + US3 silent (FR-019): parse + validate the
  // web silent-install flags (/WEB_HOST, /WEB_EXPOSURE, /WEB_PORT, /BRIDGE_PORT). No-op when no
  // web flags are passed; aborts (non-zero exit + logged reason) on an invalid combination.
  if not Web_ValidateSilentFlags() then
  begin
    Result := False;
    Exit;
  end;

  // In silent mode, scan environment first then apply target selections
  if WizardSilent then
  begin
    RunFullScan;
    ApplySilentTargets;
  end;

  // Both on by default. An existing user's config.json wins over the defaults (an upgrade keeps
  // their choice), and an explicit flag wins over both.
  AutoUpdateEnabled := True;
  TelemetryEnabled := True;
  if ExistingConfig(ConfigText) then
  begin
    ReadConfigBool(ConfigText, 'autoUpdateEnabled', AutoUpdateEnabled);
    ReadConfigBool(ConfigText, 'telemetryEnabled', TelemetryEnabled);
    PreferencesChosen := not WizardSilent;   // interactive: the page shows them; save what it says
  end
  else
    PreferencesChosen := True;               // first install for this user: record the defaults

  if CmdLineParamExists('NOUPDATE') then begin AutoUpdateEnabled := False; PreferencesChosen := True; end;
  if CmdLineParamExists('TELEMETRY') then begin TelemetryEnabled := True; PreferencesChosen := True; end;
  if CmdLineParamExists('NOTELEMETRY') then begin TelemetryEnabled := False; PreferencesChosen := True; end;

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
    PreferencesChosen := True;
  end;
end;

// --- No targets selected validation ---

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  AnySelected: Boolean;
begin
  Result := True;

  // No IDE ticked is allowed -- the web edition can be installed on its own -- but installing
  // nothing at all is not: checked once the components are known.
  if CurPageID = wpSelectComponents then
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

    if not AnySelected and not IsWebSelected() then
    begin
      MsgBox('There is nothing to install.' + #13#10 + #13#10 +
        'Tick the web edition here, or go back and choose SQL Server Management Studio 22.',
        mbError, MB_OK);
      Result := False;
    end;
  end;

  // Spec 026 (M4 closure) FR-002/FR-003/FR-003a/FR-015: web-edition page validation --
  // port ranges, IIS-port != bridge-port, bridge-port-in-use warning, IIS-missing dialog.
  if Result then
    Result := Web_NextButton(CurPageID);
end;

// Spec 026 (M4 closure) FR-002/FR-006: AkmlSqlSetup.iss had no ShouldSkipPage; created here to
// host the Web_Skip call that hides the web-edition pages when the web component is unticked
// (and for service-only / silent installs). Returns False for all non-web pages.
function ShouldSkipPage(PageID: Integer): Boolean;
var
  Dir: String;
begin
  // Taking over a 32-bit install is an upgrade, and like any upgrade it keeps its folder without
  // asking. Showing the page also made Inno warn "the folder already exists -- install anyway?",
  // because to this installer's registry view the install looks new.
  if (PageID = wpSelectDir) and Legacy32BitInstallDir(Dir) then
  begin
    Result := True;
    Exit;
  end;
  Result := Web_Skip(PageID);
end;

// --- Post-Install Actions ---

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

// Releases before SSMS-only shipped an extension for Visual Studio 2026. It is removed so VS stops
// loading a copy that no longer matches the engine (and stops starting that engine, which would keep
// {app}\Engine locked on the next upgrade). PrepareToInstall has already closed Visual Studio.
procedure RemoveLegacyVSExtensions;
var
  I: Integer;
begin
  if LegacyVSExtCount = 0 then
    Exit;
  for I := 0 to LegacyVSExtCount - 1 do
  begin
    DelTree(LegacyVSExtDirs[I], True, True, True);
    if DirExists(LegacyVSExtDirs[I]) then
      Log('WARNING: could not fully remove the old Visual Studio extension: ' + LegacyVSExtDirs[I])
    else
      Log('Removed the old Visual Studio extension: ' + LegacyVSExtDirs[I]);
    // Tell Visual Studio its extension set changed.
    SaveStringToFile(ExtractFilePath(RemoveBackslashUnlessRoot(LegacyVSExtDirs[I])) + 'extensions.configurationchanged',
      GetDateTimeString('yyyy-mm-dd hh:nn:ss', #0, #0), False);
  end;
  ClearVSMefCaches('18.0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I: Integer;
  ConfigDir: String;
  ConfigPath: String;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    MigrateLegacy32BitInstall;
    RemoveLegacyVSExtensions;
  end;

  if CurStep = ssPostInstall then
  begin
    InstallSucceeded := True;
    FinishLegacy32BitMigration;

    // Clear MEF caches for selected targets using proper directory enumeration
    Log('Clearing MEF component caches...');

    if IsTargetSelected('20') then
      ClearSSMSMefCaches('20.0');
    if IsTargetSelected('21') then
      ClearSSMSMefCaches('21.0');
    if IsTargetSelected('22') then
      ClearSSMSMefCaches('22.0');

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

    // config.json is no longer written here: the two preferences are recorded by
    // "AkmlSql.Updater.exe --configure" as the signed-in user ([Run]), and everything else -- the
    // anonymous install id included (a real GUID, not the old timestamp+Random() string) -- is
    // created by the app on first use.
    ConfigDir := ExpandConstant('{userappdata}') + '\AKML SQL';
    ForceDirectories(ConfigDir + '\logs');

    // The update this run just installed is no longer "ready": without this, SSMS would offer to
    // install the version already running.
    DeleteFile(ConfigDir + '\update-available.json');

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

    // Spec 026 (M4 closure) FR-008..FR-012 / FR-007a: configure the engine bridge, capture the
    // pairing PIN + TLS thumbprint, verify the service started, and write INSTALL-SUMMARY.txt.
    // Returns early internally when the web component is unticked.
    Web_PostInstall();

    // PR-249 review follow-up (desktop-only upgrade): TerminateAkmlBackgroundProcesses stopped a
    // running AkmlSqlWebEngine service to unlock Engine\*.dll. When the web component is part of
    // this run, Web_PostInstall (above) restarts the service itself; when it is NOT, nothing would
    // — so restart it here. Best-effort (ignore the result): a failed restart must never fail the
    // install, and the service was created with start= auto so a reboot recovers it regardless.
    if GWebServiceWasRunning and (not IsWebSelected()) then
    begin
      Log('Restarting AkmlSqlWebEngine (was running before install; web component not selected this run).');
      Exec('sc.exe', 'start AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// --- Uninstall Actions ---

// Removes AKML SQL *settings and log files* while PRESERVING user-created data: the SQL query
// history database (history\), saved snippets (snippets\) and custom formatting styles (profiles\).
// History etc. are kept regardless of the confirmation answer because an UPGRADE runs a SILENT
// uninstall, where a MB_YESNO MsgBox returns its default (Yes) — the old blanket DelTree therefore
// wiped the user's query history on every upgrade. config.json (a setting) is regenerated with
// defaults on next launch / by post-install, so deleting it here is safe.
procedure RemoveSettingsPreservingUserData;
var
  AppData: String;
  LocalAppData: String;
begin
  AppData := ExpandConstant('{userappdata}') + '\AKML SQL';
  LocalAppData := ExpandConstant('{localappdata}') + '\AKML SQL';

  // Settings + logs + transient import scratch only.
  DeleteFile(AppData + '\config.json');
  DeleteFile(AppData + '\pending-import.json');
  DeleteFile(AppData + '\update-available.json');
  DelTree(AppData + '\logs', True, True, True);
  DelTree(AppData + '\import-staging', True, True, True);

  // %LocalAppData%\AKML SQL holds only the regenerable update cache.
  DelTree(LocalAppData + '\cache', True, True, True);

  Log('AKML SQL settings and logs removed; SQL history, snippets and profiles preserved.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RemoveUserData: Boolean;
begin
  // Spec 026 (M4 closure): remove the IIS site + netsh sslcert binding and prompt about
  // %AppData%/AKML SQL Web data. Runs at usUninstall (before program files are removed).
  // No-op internally when the web edition was not installed. NEVER touches %AppData%/AKML SQL/
  // (the IDE-plugin state -- SC-007).
  if CurUninstallStep = usUninstall then
    Web_Uninstall();

  if CurUninstallStep = usPostUninstall then
  begin
    // Clear MEF ComponentModelCache so the IDE stops trying to load the removed extension.
    // SAFETY: Only clears ComponentModelCache — never touches privateregistry.bin or other IDE state.
    ClearSSMSMefCaches('20.0');
    ClearSSMSMefCaches('21.0');
    ClearSSMSMefCaches('22.0');

    // Ask about settings/log removal. SQL history, snippets and saved styles are ALWAYS kept —
    // see RemoveSettingsPreservingUserData (an upgrade's silent uninstall would otherwise default
    // to Yes and destroy the user's query history).
    RemoveUserData := MsgBox('Do you want to remove your AKML SQL settings and log files?'#13#10#13#10
      + 'Your SQL query history, snippets and saved formatting styles will be KEPT.'#13#10
      + 'To remove those too, delete this folder manually after uninstalling:'#13#10
      + ExpandConstant('{userappdata}') + '\AKML SQL',
      mbConfirmation, MB_YESNO) = IDYES;

    if RemoveUserData then
      RemoveSettingsPreservingUserData;
  end;
end;

// Waits until the named process is really gone (bounded). taskkill only ISSUES the kill — the
// process unwinds (and unmaps its DLLs) asynchronously, and file copy must not start while
// those DLLs are still locked (DeleteFile failed; code 5). Returns instantly when the process
// is not running at all.
procedure WaitForProcessExit(ExeName: String; TimeoutSeconds: Integer);
var
  tries: Integer;
begin
  tries := 0;
  while (tries < TimeoutSeconds) and IsProcessRunning(ExeName) do
  begin
    Sleep(1000);
    tries := tries + 1;
  end;
  if IsProcessRunning(ExeName) then
    Log('WARNING: ' + ExeName + ' still running after ' + IntToStr(TimeoutSeconds) + 's wait.');
end;

// The AKML engine/updater/analyzer are HEADLESS out-of-process helpers deployed under {app}.
// The engine in particular is spawned by the shell but OUTLIVES it — it can linger as an orphan
// after SSMS closes — keeping Engine\*.dll memory-mapped. If any is still alive when Inno starts
// copying files, replacement fails with "DeleteFile failed; code 5. Access is denied." They hold no
// user state, so terminate them silently. MUST run AFTER the IDE hosts are closed: a live shell
// would otherwise immediately respawn the engine and re-lock the files.

// PR-249 review follow-up: the AkmlSqlWebEngine Windows service (web edition, spec 026) runs the
// SAME AkmlSql.Engine.exe binary in --web mode, so the taskkill below also takes down the web
// service's engine. On a DESKTOP-ONLY upgrade (web component unticked this run) Web_PostInstall
// exits early and never restarts it — a working web edition would stay silently dead until reboot.
// Remember whether the service existed AND was running (GWebServiceWasRunning, declared with the
// other globals at the top of [Code]) so CurStepChanged(ssPostInstall) can restart it.
// All service handling is best-effort: it must never fail the install.
procedure TerminateAkmlBackgroundProcesses;
var
  ResultCode: Integer;
begin
  // Detect first — locale-safe: Get-Service compares the Status ENUM, not sc.exe's localized
  // STATE text (findstr /C:"RUNNING" silently fails on non-English Windows). Same pattern as
  // web-installer.iss's post-start service check. Missing service → SilentlyContinue → exit 1.
  GWebServiceWasRunning := False;
  if Exec('powershell.exe',
          '-NoProfile -ExecutionPolicy Bypass -Command "if ((Get-Service AkmlSqlWebEngine -ErrorAction SilentlyContinue).Status -eq ''Running'') { exit 0 } else { exit 1 }"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    GWebServiceWasRunning := (ResultCode = 0);

  // Graceful SCM stop before the force-kill so the service engine can shut down cleanly.
  // Ignore failures — the service may not exist, or may already be stopping.
  if GWebServiceWasRunning then
  begin
    Exec('sc.exe', 'stop AkmlSqlWebEngine', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000); // sc stop is async — give the SCM a moment before the /F taskkill below
  end;

  if IsProcessRunning('AkmlSql.Engine.exe') then
  begin
    Exec('taskkill.exe', '/F /T /IM AkmlSql.Engine.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    WaitForProcessExit('AkmlSql.Engine.exe', 10);
  end;
  if IsProcessRunning('AkmlSql.Updater.exe') then
  begin
    Exec('taskkill.exe', '/F /IM AkmlSql.Updater.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    WaitForProcessExit('AkmlSql.Updater.exe', 5);
  end;
  if IsProcessRunning('AkmlSql.Analyzer.exe') then
  begin
    Exec('taskkill.exe', '/F /IM AkmlSql.Analyzer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    WaitForProcessExit('AkmlSql.Analyzer.exe', 5);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
  RunningList: String;
  ForceClose: Boolean;
  ResultCode: Integer;
  KillSsms: Boolean;
  KillDevenv: Boolean;
begin
  Result := '';
  RunningList := '';
  KillSsms := False;
  KillDevenv := False;
  // Silent installs (the SSMS "Check for updates" flow runs the installer with its normal UI,
  // but unattended /VERYSILENT deploys have no one to answer either) cannot show the close-apps
  // prompt below — and skipping the close is fatal: a still-running SSMS RESPAWNS the
  // engine the moment TerminateAkmlBackgroundProcesses kills it, re-locking Engine\*.dll so the
  // file copy dies with "DeleteFile failed; code 5". Force-close the selected running hosts in
  // silent mode, exactly as /FORCECLOSEAPPS does interactively-by-request.
  ForceClose := (ExpandConstant('{param:FORCECLOSEAPPS|}') <> '') or WizardSilent;

  // First pass: flag WHICH hosts need closing (selected + running only).
  for I := 0 to TargetCount - 1 do
  begin
    if Targets[I].IsSelected and Targets[I].IsRunning then
    begin
      KillSsms := True;

      if not ForceClose then
      begin
        if RunningList <> '' then
          RunningList := RunningList + ', ';
        RunningList := RunningList + Targets[I].Name;
      end;
    end;
  end;

  // Visual Studio is no longer supported, but one still carrying the old AKML SQL extension keeps
  // starting the engine (locking Engine\*.dll) and holds the extension files setup removes.
  if (LegacyVSExtCount > 0) and IsProcessRunning('devenv.exe') then
  begin
    KillDevenv := True;
    if not ForceClose then
    begin
      if RunningList <> '' then
        RunningList := RunningList + ', ';
      RunningList := RunningList + 'Visual Studio (to remove the old AKML SQL extension)';
    end;
  end;

  if ForceClose then
  begin
    // Silent/forced: kill + wait, then VERIFY — a survivor would keep Engine\*.dll locked, so
    // at minimum the install log must say so honestly.
    if KillSsms then
    begin
      Exec('taskkill.exe', '/F /IM Ssms.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      WaitForProcessExit('Ssms.exe', 15);
      if IsProcessRunning('Ssms.exe') then
        Log('WARNING: Ssms.exe survived the silent force-close — Engine\*.dll may still be locked.');
    end;
    if KillDevenv then
    begin
      Exec('taskkill.exe', '/F /IM devenv.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      WaitForProcessExit('devenv.exe', 15);
      if IsProcessRunning('devenv.exe') then
        Log('WARNING: devenv.exe survived the silent force-close — Engine\*.dll may still be locked.');
    end;
  end
  else if RunningList <> '' then
  begin
    // Interactive: one confirm, then a VERIFIED close — the loop re-kills until the hosts are
    // really gone, so setup never reaches a file copy that is guaranteed to fail with
    // "DeleteFile failed; code 5". Cancel aborts BEFORE any file is touched.
    if MsgBox('The following applications are running and must be closed to install:'#13#10#13#10
      + RunningList + #13#10#13#10
      + 'Click OK to close them automatically (unsaved query windows will be lost),'#13#10
      + 'or Cancel to close them yourself and run setup again.',
      mbConfirmation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := 'Please close the running applications and try again.';
      Exit;
    end;

    while (KillSsms and IsProcessRunning('Ssms.exe'))
       or (KillDevenv and IsProcessRunning('devenv.exe')) do
    begin
      if KillSsms and IsProcessRunning('Ssms.exe') then
      begin
        Exec('taskkill.exe', '/F /IM Ssms.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        WaitForProcessExit('Ssms.exe', 15);
      end;
      if KillDevenv and IsProcessRunning('devenv.exe') then
      begin
        Exec('taskkill.exe', '/F /IM devenv.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        WaitForProcessExit('devenv.exe', 15);
      end;

      if (KillSsms and IsProcessRunning('Ssms.exe'))
         or (KillDevenv and IsProcessRunning('devenv.exe')) then
      begin
        Log('WARNING: a running IDE host survived taskkill /F — asking the user to close it.');
        if MsgBox('Setup could not close the application automatically.'#13#10#13#10
          + 'Close ' + RunningList + ' yourself, then click OK to check again,'#13#10
          + 'or click Cancel to abort setup (nothing has been installed yet).',
          mbError, MB_OKCANCEL) = IDCANCEL then
        begin
          Result := 'Please close the running applications and try again.';
          Exit;
        end;
      end;
    end;
  end;

  // Now that the IDE hosts are handled, terminate the headless AKML helpers that would otherwise
  // keep Engine\*.dll locked (DeleteFile code 5) — including an orphaned engine left running after
  // the hosts already exited (that path leaves RunningList empty, skips the prompt, and reaches here
  // with Result=''). Only when we are actually proceeding (Result=''), never on the manual-close abort.
  if Result = '' then
    TerminateAkmlBackgroundProcesses;

  // MEF cache clearing is done in ssPostInstall (after files are deployed),
  // not here. Clearing before deployment is unnecessary and wasteful.
  // SAFETY: We never touch privateregistry.bin, 1033/ CTM, or other IDE state.
end;
