; ============================================
; AVES Revit Plugin - Inno Setup Script
; ============================================

[Setup]
; AppId is a unique GUID that identifies this application across ALL versions.
; This is what allows the installer to detect a previous installation and upgrade it.
; NEVER change this GUID — if you do, users will get duplicate entries in Add/Remove Programs.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}

AppName=AVES Revit Plugin
AppVersion=1.0.1
AppPublisher=Your Company Name
AppPublisherURL=https://github.com/yourcompany/AVES

AppMutex=AVESRevitPlugin_Mutex

DefaultDirName={commonappdata}\Autodesk\Revit\Addins
CreateAppDir=no

OutputDir=installer_output
OutputBaseFilename=AVES_Setup_v1.0.1

Compression=lzma
SolidCompression=yes
WizardStyle=modern

DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
UsePreviousAppDir=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install (or update) the AVES Plugin for Autodesk Revit on your computer.%n%nIf a previous version is detected, it will be automatically replaced.%n%nPlease close Revit before continuing.

[Files]
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\x64\Release R25\AVES.dll";                        DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\x64\Release R25\AVES.deps.json";                  DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\x64\Release R25\AVES.runtimeconfig.json";         DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\x64\Release R25\Nice3point.Revit.Extensions.dll"; DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\x64\Release R25\Nice3point.Revit.Toolkit.dll";    DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\AVES.addin";                                         DestDir: "{code:GetRevitPath}"; Flags: ignoreversion uninsrestartdelete

[Code]

// ── Global variable to store the resolved Revit path ──────────────────────────
var
  RevitPath: String;

// Required by Inno Setup to resolve {code:GetRevitPath} in [Files] and [UninstallDelete]
function GetRevitPath(Param: String): String;
begin
  Result := RevitPath;
end;

// ── Detect the highest installed Revit version ────────────────────────────────
function DetectRevitPath(): String;
var
  Versions: Array of String;
  i: Integer;
  Path: String;
begin
  Versions := ['2022', '2023', '2024', '2025'];
  Result := '';
  for i := 0 to GetArrayLength(Versions) - 1 do
  begin
    Path := ExpandConstant('{commonappdata}') + '\Autodesk\Revit\Addins\' + Versions[i];
    if DirExists(Path) then
      Result := Path;
  end;
end;

// ── Check whether a previous version is already installed ─────────────────────
function PreviousInstallDetected(): Boolean;
begin
  Result := FileExists(RevitPath + '\AVES.dll');
end;

// ── Clean up stale AppData leftovers from older installations ─────────────────
// This handles cases where AVES.addin was previously installed to the user
// AppData folder instead of ProgramData, which causes Revit to fail loading
// the DLL because the two files end up in different locations.
procedure CleanupAppDataLeftovers();
var
  AppDataAddin: String;
  AppDataFolder: String;
begin
  // Remove stale AVES.addin from user AppData if it exists
  AppDataAddin := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\2025\AVES.addin';
  if FileExists(AppDataAddin) then
    DeleteFile(AppDataAddin);

  // Remove stale AVES folder from user AppData if it exists
  AppDataFolder := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\2025\AVES';
  if DirExists(AppDataFolder) then
    DelTree(AppDataFolder, True, True, True);
end;

// ── Entry point ───────────────────────────────────────────────────────────────
function InitializeSetup(): Boolean;
var
  WelcomeMsg: String;
begin
  RevitPath := DetectRevitPath();

  // Abort if Revit is not installed
  if RevitPath = '' then
  begin
    MsgBox(
      'No Revit installation was found on this computer.' + #13#10 +
      'Please install Autodesk Revit before running this installer.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Clean up any leftover AppData files from previous installations
  CleanupAppDataLeftovers();

  // Tailor the welcome message depending on fresh install vs update
  if PreviousInstallDetected() then
    WelcomeMsg :=
      'A previous version of AVES was detected.' + #13#10 +
      'It will be automatically updated - no manual uninstall required.' + #13#10 + #13#10 +
      'Install path:  ' + RevitPath
  else
    WelcomeMsg :=
      'AVES will be installed to:' + #13#10 +
      RevitPath;

  MsgBox(WelcomeMsg, mbInformation, MB_OK);
  Result := True;
end;

[UninstallDelete]
Type: files; Name: "{code:GetRevitPath}\AVES.dll"
Type: files; Name: "{code:GetRevitPath}\AVES.deps.json"
Type: files; Name: "{code:GetRevitPath}\AVES.runtimeconfig.json"
Type: files; Name: "{code:GetRevitPath}\AVES.addin"
Type: files; Name: "{code:GetRevitPath}\Nice3point.Revit.Extensions.dll"
Type: files; Name: "{code:GetRevitPath}\Nice3point.Revit.Toolkit.dll"
