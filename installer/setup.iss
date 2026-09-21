; Script generated for PC Control Center
; Supports non-elevated per-user installation (PrivilegesRequired=lowest)

#define MyAppName "PC Control Center"
#define MyAppVersion "0.1.0-alpha.14"
#define MyAppPublisher "PC Control Center"
#define MyAppURL "https://github.com/202200800669-commits/PCControlCenter"
#define MyAppExeName "pc-control-desktop.exe"

[Setup]
AppId={{D37E88BC-31F4-4AC5-9D22-F3AE78E49A9B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\PCControlCenter
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=PCControlCenter-Setup-{#MyAppVersion}
SetupIconFile=..\src\PCControlCenter.Desktop\Control.ico
UninstallDisplayIcon={app}\Control.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "开机自动启动 (最小化到托盘)"; GroupDescription: "系统集成:"

[Files]
; Distribute all files from the published artifact directory
Source: "..\artifacts\pc-control-alpha-*\pc-control-desktop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\pc-control-alpha-*\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Control.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Control.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PCControlCenter"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
