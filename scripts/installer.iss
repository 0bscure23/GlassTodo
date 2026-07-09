; 琉璃清单 GlassTodo — Inno Setup 6 安装脚本
; 构建： iscc scripts\installer.iss
; 输入：自包含单文件发布产物 src\GlassTodo\bin\publish-sc\GlassTodo.exe

#define MyAppName "琉璃清单"
#define MyAppNameEn "GlassTodo"
#define MyAppVersion "1.0.0"
#define MyAppExeName "GlassTodo.exe"
#define MyPublishDir "..\src\GlassTodo\bin\publish-sc"

[Setup]
AppId={{8F4B6E1C-9D2A-4C5E-B7F3-A1C2E3D4F506}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=GlassTodo-Setup-{#MyAppVersion}
SetupIconFile=..\src\GlassTodo\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ShowLanguageDialog=no

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; WPF 自包含发布的原生依赖（wpfgfx / PresentationNative 等），缺一不可
Source: "{#MyPublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; 用户数据（%APPDATA%\GlassTodo）刻意保留，卸载不删除待办数据
Type: filesandordirs; Name: "{app}"
