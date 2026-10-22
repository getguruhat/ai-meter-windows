#define AppName "AI Meter"
#define AppVersion "1.0.0"
[Setup]
AppId={{6C1E2B57-4D0A-4F3B-9E7C-2A8F5D41B9C3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
AppPublisher=GuruHat
AppPublisherURL=https://guruhat.com/apps/ai-meter/
DefaultDirName={autopf}\GuruHat\AI Meter
DefaultGroupName=AI Meter
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=AI-Meter-{#AppVersion}-Windows-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=assets\AIMeter.ico
UninstallDisplayIcon={app}\AIMeter.exe
CloseApplications=force
RestartApplications=no
[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\AI Meter"; Filename: "{app}\AIMeter.exe"
[Tasks]
Name: "startup"; Description: "Start AI Meter when I sign in to Windows"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GuruHat AI Meter"; ValueData: """{app}\AIMeter.exe"""; Tasks: startup; Flags: uninsdeletevalue
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\GuruHat\AIMeter"
[Run]
Filename: "{app}\AIMeter.exe"; Description: "Launch AI Meter"; Flags: nowait postinstall skipifsilent
