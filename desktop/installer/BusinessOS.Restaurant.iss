#define MyAppName "BusinessOS Restaurant"
#define MyAppPublisher "BusinessOS.af"
#define MyAppExeName "BusinessOS.Restaurant.Desktop.exe"

[Setup]
AppId={{D81A9CB5-684D-4D6A-A4B9-5A9B73C6EAF1}
AppName={#MyAppName}
AppVersion=1.0.0
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BusinessOS\Restaurant
DefaultGroupName=BusinessOS Restaurant
OutputDir=output
OutputBaseFilename=BusinessOS-Restaurant-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName=BusinessOS Restaurant
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BusinessOS Restaurant"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\BusinessOS Restaurant"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch BusinessOS Restaurant"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  { Restaurant operational data lives under LocalAppData and is intentionally
    outside the installation directory. Upgrades therefore preserve SQLite,
    activation, backups, diagnostics and pending restore state. }
end;
