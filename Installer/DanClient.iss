#define MyAppName "DanClient"
#define MyAppVersion "0.1.5"
#define MyAppPublisher "DanClient"
#define MyAppExeName "Launcher.UI.exe"
#define MyAppDescription "DanClient Minecraft Launcher"
#define MyAppMutex "DanClientLauncherMutex"

#ifndef PublishDir
  #define PublishDir "..\Launcher.UI\bin\Release\net10.0\win-x64\publish\"
#endif

[Setup]
AppId={{A7E3F2B1-8C4D-4E9A-B5F6-1D2C3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=DanClientSetup
SetupIconFile=..\Launcher.UI\Assets\danclient.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
MinVersion=10.0.17763
PrivilegesRequired=admin
DisableWelcomePage=no
ShowLanguageDialog=no
ArchitecturesInstallIn64BitMode=x64
UsePreviousAppDir=yes
DisableDirPage=auto
WizardImageFile=Assets\WizardLarge.bmp
WizardSmallImageFile=Assets\WizardSmall.bmp
AppMutex={#MyAppMutex}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.WelcomeLabel2=This will install or update [name/ver] on your computer.%n%nExisting files will be replaced automatically — you do not need to uninstall first.%n%nDanClient is a Minecraft launcher with Microsoft sign-in, Fabric mod support, and Modrinth integration.
english.UpdateLabel=An existing installation was detected. Setup will update DanClient in place and overwrite application files.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppDescription}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  IsUpgrade: Boolean;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
begin
  IsUpgrade := RegQueryStringValue(
    HKLM,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3F2B1-8C4D-4E9A-B5F6-1D2C3E4F5A6B}_is1',
    'DisplayVersion',
    InstalledVersion);
  Result := True;
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := 'Welcome to DanClient Setup';
  if IsUpgrade then
    WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:UpdateLabel}')
  else
    WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:WelcomeLabel2}');
  WizardForm.Color := $0C0C0C;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Retries: Integer;
begin
  Result := '';
  NeedsRestart := False;
  Retries := 0;

  while Retries < 8 do
  begin
    if Exec('taskkill', '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 128 then
        Break;
    end
    else
      Break;

    Sleep(400);
    Inc(Retries);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if IsUpgrade then
    begin
      WizardForm.FinishedLabel.Caption :=
        'DanClient has been updated successfully.' + #13#10 +
        'Your settings and game files were not affected.';
    end
    else
    begin
      WizardForm.FinishedLabel.Caption :=
        'DanClient has been installed successfully.' + #13#10 +
        'Click Finish to close the installer.';
    end;
  end;
end;
