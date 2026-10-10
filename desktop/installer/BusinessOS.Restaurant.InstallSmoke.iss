; INTERNAL CI-ONLY INSTALL HARNESS. DO NOT DISTRIBUTE.
; This packages the exact same published Windows binaries as the real installer
; but intentionally provides no activation shortcut or production install identity.
; The shipping BusinessOS.Restaurant.iss and its signed license gate are unchanged.
#define MyAppName "BusinessOS Restaurant [CI Install Test]"
#define MyAppVersion "1.0.0"
#define MyAppExeName "BusinessOS.Restaurant.Desktop.exe"

[Setup]
AppId={{5EBE78C4-6613-49FA-9CB3-369FEE96CC3A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=BusinessOS CI
DefaultDirName={localappdata}\BusinessOS\Restaurant-CI-Install-Smoke
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
Uninstallable=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=ci-output
OutputBaseFilename=BusinessOS-Restaurant-CI-Install-Smoke-DO-NOT-DISTRIBUTE
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=no
RestartApplications=no
MinVersion=10.0.17763
; Never use actual production app identity or write to production install path.

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
