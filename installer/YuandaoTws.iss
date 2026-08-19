#define AppName "原点耳机控制"
#define AppVersion "0.1.5"
#define AppPublisher "NICEHCK OriG-in"
#define AppExeName "YuandaoTws.Desktop.exe"
#define PublishDir "..\artifacts\release-v0.1.5\desktop-standalone"

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
OutputDir=..\artifacts\release-v0.1.5
OutputBaseFilename=YuandaoTws-Setup-v{#AppVersion}
SetupIconFile=..\src\YuandaoTws.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; 日志与开机启动属于当前用户数据；卸载时主动清理，避免留下孤立文件。
UsedUserAreasWarning=no
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
Name: "{autoprograms}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\YuandaoTws"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'YuandaoTws');
end;
