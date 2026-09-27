#define AppVersion "0.1.0"
#define PublishDir "..\dist\publish"

[Setup]
AppId={{795BE0D1-A086-4E52-8C07-EF3C475AE0D4}
AppName=Tuno
AppVersion={#AppVersion}
AppVerName=Tuno {#AppVersion} 预览版
AppPublisher=Tuno
DefaultDirName={localappdata}\Programs\Tuno
DefaultGroupName=Tuno
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
OutputDir=..\dist
OutputBaseFilename=Tuno-Setup-{#AppVersion}-win-x64
SetupIconFile=..\Tuno.PlaybackProbe\Assets\Tuno.ico
UninstallDisplayIcon={app}\Tuno.PlaybackProbe.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "\libvlc\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\libvlc\win-x64\*"; DestDir: "{app}\libvlc\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Tuno"; Filename: "{app}\Tuno.PlaybackProbe.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Tuno"; Filename: "{app}\Tuno.PlaybackProbe.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Tuno.PlaybackProbe.exe"; Description: "启动 Tuno"; Flags: nowait postinstall skipifsilent
