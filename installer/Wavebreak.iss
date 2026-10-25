; Wavebreak Windows installer (Inno Setup 6).
; Build: ISCC.exe installer\Wavebreak.iss  (after exporting to build\windows)
; Output: build\installer\Wavebreak-Setup-<version>.exe

#define AppName "Wavebreak"
; build.ps1 passes the version from project.godot (/DAppVersion=...).
#ifndef AppVersion
  #define AppVersion "1.3.2"
#endif
#define AppPublisher "mPhpMaster"
#define AppExe "Wavebreak.exe"
#define Source "..\build\windows"

[Setup]
AppId={{6F1C2E7A-8B4D-4E2A-9C61-57A0B3D9E1F4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user by default (no admin prompt); the dialog offers all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\build\installer
OutputBaseFilename=Wavebreak-Setup-{#AppVersion}
SetupIconFile=wavebreak.ico
UninstallDisplayIcon={app}\wavebreak.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "steam_api64.dll,steam_appid.txt"; Flags: ignoreversion recursesubdirs createallsubdirs
; Steam co-op: the export puts these next to the exe (addons/wavebreak_steam).
; Listed by name so a build without them fails here instead of shipping a
; game whose CO-OP page can never start Steam. steam_appid.txt holds the App
; ID (480, Valve's Spacewar test app, until Wavebreak has its own).
Source: "{#Source}\steam_api64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Source}\steam_appid.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "wavebreak.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\wavebreak.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\wavebreak.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
