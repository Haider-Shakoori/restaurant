#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "BusinessOS Restaurant"
#define MyAppPublisher "BusinessOS.af"
#define MyAppExeName "BusinessOS.Restaurant.Desktop.exe"
#define TrialUrl "https://restaurant.businessos.af"

[Setup]
AppId={{D81A9CB5-684D-4D6A-A4B9-5A9B73C6EAF1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://businessos.af
AppSupportURL={#TrialUrl}
DefaultDirName={autopf64}\BusinessOS\Restaurant
DefaultGroupName=BusinessOS
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
OutputDir=output
OutputBaseFilename=BusinessOS-Restaurant-Setup-{#MyAppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
; Keep native dark-mode labels, buttons and progress text readable over the
; dimmed restaurant art regardless of the Windows host's light preference.
WizardStyle=modern dark
; Build-generated PNG uses the original restaurant art, softened and dimmed
; exclusively for Setup. The desktop Glass JPG remains untouched.
; Dark wizard styling supplies light native control text across ALL pages.
WizardBackImageFile=..\src\BusinessOS.Restaurant.Desktop\Assets\RestaurantInstallerBackground.png
WizardBackImageFileDynamicDark=..\src\BusinessOS.Restaurant.Desktop\Assets\RestaurantInstallerBackground.png
WizardBackImageOpacity=225
WizardBackColor=#101C28
WizardBackColorDynamicDark=#101C28
WizardImageFile=
WizardSmallImageFile=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName=BusinessOS Restaurant
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\BusinessOS.Restaurant.Desktop\Assets\BusinessOS.Restaurant.ico
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
MinVersion=10.0.17763
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=BusinessOS Restaurant Management System
VersionInfoProductName={#MyAppName}

[Files]
; Pack the published single-file executable once as an installer helper as well
; as the installed application. The helper performs the same signed activation
; verification used by the desktop app, so Inno Setup never handles device
; secrets or the protected activation payload itself.
Source: "..\artifacts\publish\{#MyAppExeName}"; Flags: dontcopy
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BusinessOS Restaurant"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\BusinessOS Restaurant"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch BusinessOS Restaurant"; Flags: nowait postinstall skipifsilent

[Code]
var
  ActivationPage: TWizardPage;
  LicenseEdit: TNewEdit;
  LicenseStatusLabel: TNewStaticText;
  LicenseHelpLabel: TNewStaticText;
  TrialButton: TNewButton;
  ExistingActivation: Boolean;
  ExistingDays: Integer;
  ExistingPlan: String;

function HelperPath: String;
begin
  Result := ExpandConstant('{tmp}\{#MyAppExeName}');
end;

function ReadStatusValue(const FileName, Key: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
  Prefix: String;
begin
  Result := '';
  Prefix := Key + '=';

  if not LoadStringsFromFile(FileName, Lines) then
    exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos(Prefix, Lines[I]) = 1 then
    begin
      Result := Copy(Lines[I], Length(Prefix) + 1, MaxInt);
      exit;
    end;
  end;
end;

procedure ShowExistingActivation(const StatusFile: String);
var
  DaysText: String;
begin
  ExistingActivation := ReadStatusValue(StatusFile, 'valid') = '1';

  if ExistingActivation then
  begin
    ExistingDays := StrToIntDef(ReadStatusValue(StatusFile, 'days_remaining'), 0);
    ExistingPlan := ReadStatusValue(StatusFile, 'plan');

    DaysText := IntToStr(ExistingDays) + ' day(s) remaining';
    LicenseStatusLabel.Caption :=
      'Already activated on this computer' + #13#10 +
      ExistingPlan + '  •  ' + DaysText;
    LicenseStatusLabel.Font.Color := $00A9DEA7;
    LicenseEdit.Enabled := False;
    LicenseEdit.Text := 'Activation stored securely on this PC';
  end
  else
  begin
    LicenseStatusLabel.Caption :=
      'Enter the license key generated for your restaurant.' + #13#10 +
      'The first successful Windows activation binds the license to this computer.';
    LicenseStatusLabel.Font.Color := $00DBD2C3;
    LicenseEdit.Enabled := True;
    LicenseEdit.Text := '';
  end;
end;

procedure CheckExistingActivation;
var
  StatusFile: String;
  ResultCode: Integer;
begin
  StatusFile := ExpandConstant('{tmp}\restaurant-license-status.txt');
  DeleteFile(StatusFile);

  if Exec(
      HelperPath,
      '--installer-license-status="' + StatusFile + '"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and
     (ResultCode = 0) and
     FileExists(StatusFile) then
  begin
    ShowExistingActivation(StatusFile);
  end;
end;

procedure TrialButtonClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec(
    '',
    '{#TrialUrl}',
    '',
    '',
    SW_SHOWNORMAL,
    ewNoWait,
    ErrorCode);
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('{#MyAppExeName}');

  ActivationPage := CreateCustomPage(
    wpSelectDir,
    'Activate BusinessOS Restaurant',
    'Secure license activation and 7-day trial');

  LicenseStatusLabel := TNewStaticText.Create(ActivationPage);
  LicenseStatusLabel.Parent := ActivationPage.Surface;
  LicenseStatusLabel.Left := ScaleX(0);
  LicenseStatusLabel.Top := ScaleY(8);
  LicenseStatusLabel.Width := ActivationPage.SurfaceWidth;
  LicenseStatusLabel.Height := ScaleY(54);
  LicenseStatusLabel.WordWrap := True;
  LicenseStatusLabel.Font.Size := 10;

  LicenseHelpLabel := TNewStaticText.Create(ActivationPage);
  LicenseHelpLabel.Parent := ActivationPage.Surface;
  LicenseHelpLabel.Left := ScaleX(0);
  LicenseHelpLabel.Top := ScaleY(78);
  LicenseHelpLabel.Width := ActivationPage.SurfaceWidth;
  LicenseHelpLabel.Height := ScaleY(42);
  LicenseHelpLabel.WordWrap := True;
  LicenseHelpLabel.Font.Color := $00DBD2C3;
  LicenseHelpLabel.Caption :=
    'License Key' + #13#10 +
    'Initial activation requires internet. Future setup runs detect the protected activation automatically.';

  LicenseEdit := TNewEdit.Create(ActivationPage);
  LicenseEdit.Parent := ActivationPage.Surface;
  LicenseEdit.Left := ScaleX(0);
  LicenseEdit.Top := ScaleY(128);
  LicenseEdit.Width := ActivationPage.SurfaceWidth;
  LicenseEdit.Height := ScaleY(30);

  TrialButton := TNewButton.Create(ActivationPage);
  TrialButton.Parent := ActivationPage.Surface;
  TrialButton.Left := ScaleX(0);
  TrialButton.Top := ScaleY(178);
  TrialButton.Width := ScaleX(190);
  TrialButton.Height := ScaleY(34);
  TrialButton.Caption := 'Start 7-Day Trial';
  TrialButton.OnClick := @TrialButtonClick;

  CheckExistingActivation;
end;

function ActivateLicenseFromInstaller: Boolean;
var
  LicenseFile: String;
  ResultFile: String;
  ResultCode: Integer;
  ErrorText: String;
  PlanText: String;
  Days: Integer;
begin
  Result := False;

  if ExistingActivation then
  begin
    Result := True;
    exit;
  end;

  if Trim(LicenseEdit.Text) = '' then
  begin
    MsgBox(
      'Enter your Restaurant license key, or click "Start 7-Day Trial" first.',
      mbError,
      MB_OK);
    exit;
  end;

  LicenseFile := ExpandConstant('{tmp}\restaurant-license-input.txt');
  ResultFile := ExpandConstant('{tmp}\restaurant-license-result.txt');
  DeleteFile(LicenseFile);
  DeleteFile(ResultFile);

  if not SaveStringToFile(LicenseFile, Trim(LicenseEdit.Text), False) then
  begin
    MsgBox('The installer could not prepare the activation request.', mbError, MB_OK);
    exit;
  end;

  LicenseStatusLabel.Caption := 'Verifying license with BusinessOS…';

  if not Exec(
      HelperPath,
      '--installer-activate-file="' + LicenseFile + '" --installer-result="' + ResultFile + '"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
  begin
    DeleteFile(LicenseFile);
    MsgBox('The activation helper could not be started.', mbError, MB_OK);
    exit;
  end;

  DeleteFile(LicenseFile);

  if FileExists(ResultFile) and
     (ReadStatusValue(ResultFile, 'valid') = '1') then
  begin
    ExistingActivation := True;
    Days := StrToIntDef(ReadStatusValue(ResultFile, 'days_remaining'), 0);
    PlanText := ReadStatusValue(ResultFile, 'plan');
    ExistingDays := Days;
    ExistingPlan := PlanText;

    LicenseStatusLabel.Caption :=
      'Activation successful' + #13#10 +
      PlanText + '  •  ' + IntToStr(Days) + ' day(s) remaining';
    LicenseStatusLabel.Font.Color := $00A9DEA7;
    LicenseEdit.Enabled := False;
    LicenseEdit.Text := 'Activation stored securely on this PC';
    Result := True;
    exit;
  end;

  ErrorText := ReadStatusValue(ResultFile, 'error');
  if ErrorText = '' then
    ErrorText := 'Activation failed. Check the license key and internet connection.';

  LicenseStatusLabel.Caption := ErrorText;
  LicenseStatusLabel.Font.Color := $00AAAAFF;
  MsgBox(ErrorText, mbError, MB_OK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = ActivationPage.ID then
    Result := ActivateLicenseFromInstaller;
end;

function UpdateReadyMemo(
  Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  LicenseInfo: String;
begin
  if ExistingActivation then
    LicenseInfo :=
      NewLine + 'License: ' + ExistingPlan + ' • ' +
      IntToStr(ExistingDays) + ' day(s) remaining' + NewLine
  else
    LicenseInfo := '';

  Result :=
    MemoDirInfo + NewLine +
    LicenseInfo +
    MemoGroupInfo + NewLine +
    MemoTasksInfo;
end;
