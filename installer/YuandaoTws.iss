#define AppName "原点耳机控制"
#define AppVersion "0.1.1"
#define AppPublisher "NICEHCK OriG-in"
#define AppExeName "YuandaoTws.Desktop.exe"
#define PublishDir "..\artifacts\release-v0.1.1\desktop-standalone"

[Setup]
AppId={{B8D9D9E2-3A40-4B55-9D1A-8F1A1B4E0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Fat-kevin/NICEHCK_OriG-in
AppSupportURL=https://github.com/Fat-kevin/NICEHCK_OriG-in/issues
AppUpdatesURL=https://github.com/Fat-kevin/NICEHCK_OriG-in/releases
DefaultDirName={autopf}\YuandaoTws
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\release-v0.1.1
OutputBaseFilename=YuandaoTws-Setup-v{#AppVersion}
SetupIconFile=..\src\YuandaoTws.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription={#AppName} Windows 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
