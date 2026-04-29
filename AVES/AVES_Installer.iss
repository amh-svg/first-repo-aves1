; ============================================
; AVES Revit Plugin - Inno Setup Script
; ============================================

[Setup]
AppName=AVES Revit Plugin
AppVersion=1.0.0
AppPublisher=Your Company Name
AppPublisherURL=https://github.com/yourcompany/AVES
DefaultDirName={commonappdata}\Autodesk\Revit\Addins
CreateAppDir=no
OutputDir=installer_output
OutputBaseFilename=AVES_Setup_v1.0.0
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Customize the welcome message
WelcomeLabel2=This will install the AVES Plugin for Autodesk Revit on your computer.%n%nThe plugin will be installed for all detected Revit versions.

[Files]
; All your plugin files - source is relative to this .iss file
[Files]
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\Release R25\AVES.dll";                        DestDir: "{code:GetRevitPath}"; Flags: ignoreversion
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\Release R25\AVES.deps.json";                  DestDir: "{code:GetRevitPath}"; Flags: ignoreversion
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\Release R25\AVES.runtimeconfig.json";         DestDir: "{code:GetRevitPath}"; Flags: ignoreversion
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\Release R25\Nice3point.Revit.Extensions.dll"; DestDir: "{code:GetRevitPath}"; Flags: ignoreversion
Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\bin\Release R25\Nice3point.Revit.Toolkit.dll";    DestDir: "{code:GetRevitPath}"; Flags: ignoreversion


Source: "C:\Users\anghi\Desktop\Noruegos 2.0\00 - Dynamo Scripts\C#\AVES\AVES\AVES.addin"; DestDir: "{code:GetRevitPath}"; Flags: ignoreversion

[Code]
// --- Auto-detect installed Revit versions ---
var
  RevitPath: String;

function GetRevitPath(Param: String): String;
begin
  Result := RevitPath;
end;

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
    begin
      Result := Path;  // Uses the latest version found
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  RevitPath := DetectRevitPath();
  if RevitPath = '' then
  begin
    MsgBox('No Revit installation was found on this computer.' + #13#10 +
           'Please install Autodesk Revit before running this installer.',
           mbError, MB_OK);
    Result := False;
  end else
  begin
    MsgBox('Revit found! The plugin will be installed to:' + #13#10 + RevitPath,
           mbInformation, MB_OK);
    Result := True;
  end;
end;

[UninstallDelete]
; Clean up all plugin files on uninstall
Type: files; Name: "{code:GetRevitPath}\AVES.dll"
Type: files; Name: "{code:GetRevitPath}\AVES.deps.json"
Type: files; Name: "{code:GetRevitPath}\AVES.runtimeconfig.json"
Type: files; Name: "{code:GetRevitPath}\AVES.addin"
Type: files; Name: "{code:GetRevitPath}\Nice3point.Revit.Extensions.dll"
Type: files; Name: "{code:GetRevitPath}\Nice3point.Revit.Toolkit.dll"