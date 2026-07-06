; EDNexus Windows installer (Inno Setup 6 — free, open source).
;
; Installs a self-contained build to:
;     C:\Program Files\Signal & Thread\EDNexus\
; User preferences are NOT stored here — they live in Documents\EDNexus (see SettingsStore),
; which stays writable without admin rights.
;
; Build (version and the published-app dir are supplied on the command line):
;     ISCC.exe /DAppVersion=1.2.3 /DPublishDir=...\publish /Oout ednexus.iss

#define AppName "EDNexus"
#define AppPublisher "Signal & Thread LLC"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
; Stable AppId — keep constant across versions so upgrades replace in place.
AppId={{7F3B2E14-9C6A-4D5E-B8A1-2F4C6E8A0D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Signal-Thread-LLC/EDNexus
DefaultDirName={autopf}\Signal & Thread\EDNexus
DefaultGroupName=EDNexus
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\EDNexus.App.exe
OutputDir=out
OutputBaseFilename=EDNexus-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Program Files install requires elevation; 64-bit only.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\EDNexus"; Filename: "{app}\EDNexus.App.exe"
Name: "{group}\Uninstall EDNexus"; Filename: "{uninstallexe}"
Name: "{autodesktop}\EDNexus"; Filename: "{app}\EDNexus.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EDNexus.App.exe"; Description: "Launch EDNexus"; Flags: nowait postinstall skipifsilent
