; Inno Setup 6 script for Hugoer
; Build via scripts/publish.ps1 (auto) or:
;   ISCC installer\hugoer.iss /DMyAppVersion=1.2.0 /DMyPublishDir=..\dist\publish\win-x64

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#ifndef MyPublishDir
  #define MyPublishDir "..\dist\publish\win-x64"
#endif

#define MyAppName "Hugoer"
#define MyAppPublisher "Hugoer"
#define MyAppURL "https://github.com/"
#define MyAppExeName "Hugoer.exe"

[Setup]
AppId={{A8E4C2B1-9F3D-4E6A-8C1B-2D5E7F9A0B3C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\releases\inno
OutputBaseFilename=Hugoer-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Assets\avalonia-logo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; If publish is not fully single-file on some runtimes, include folder:
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
