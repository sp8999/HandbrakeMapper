#define MyAppName "Handbrake Mapper"
#define MyAppVersion "0.1.5"
#define MyAppExeName "HandbrakeMapper.exe"
#define Root ".."

[Setup]
AppId={{2FC9B553-9BAF-4C4E-B9E5-66BA552FAD43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\Handbrake Mapper
DefaultGroupName={#MyAppName}
OutputDir={#Root}\dist\installer
OutputBaseFilename=HandbrakeMapper_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
WizardStyle=modern

[Files]
Source: "{#Root}\dist\app\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\licenses\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\installer\redist\vJoySetup_v2.2.2.0_Win10_Win11.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#Root}\installer\redist\HidHide_1.5.230_x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成"

[Run]
Filename: "{tmp}\vJoySetup_v2.2.2.0_Win10_Win11.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"; StatusMsg: "vJoyをインストールしています..."; Flags: waituntilterminated; Check: VJoyWasMissing
Filename: "{pf}\vJoy\x64\vJoyConfig.exe"; Parameters: "Enable on"; StatusMsg: "vJoyを有効化しています..."; Flags: waituntilterminated runhidden; Check: VJoyWasMissing
Filename: "{pf}\vJoy\x64\vJoyConfig.exe"; Parameters: "1 -f -a X -b 1 -p 0"; StatusMsg: "ハンドブレーキ用vJoy Device 1を設定しています..."; Flags: waituntilterminated runhidden; Check: VJoyWasMissing
Filename: "{tmp}\HidHide_1.5.230_x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "HidHideをインストールしています..."; Flags: waituntilterminated; Check: HidHideWasMissing
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName}を起動"; Flags: nowait postinstall skipifsilent

[Code]
var
  InitialVJoyMissing: Boolean;
  InitialHidHideMissing: Boolean;

function IsVJoyInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\vJoy\x64\vJoyInterface.dll'));
end;

function IsHidHideInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Nefarius Software Solutions\HidHide\HidHideCLI.exe')) or
            FileExists(ExpandConstant('{pf}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe'));
end;

function InitializeSetup(): Boolean;
begin
  InitialVJoyMissing := not IsVJoyInstalled;
  InitialHidHideMissing := not IsHidHideInstalled;
  Result := True;
end;

function VJoyWasMissing(): Boolean;
begin
  Result := InitialVJoyMissing;
end;

function HidHideWasMissing(): Boolean;
begin
  Result := InitialHidHideMissing;
end;
