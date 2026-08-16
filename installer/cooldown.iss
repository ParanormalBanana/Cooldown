; Inno Setup 6 — per-user installer (no admin).
; Compiled by build.ps1 / the release workflow.
; Pass /DAppVersion=x.y.z to override the version.

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

#define AppName "Cooldown"
#define SourceDir "..\dist\Cooldown"
#define IconFile "..\src\Cooldown\Assets\hourglass.ico"

[Setup]
AppId={{E4B7A91C-2F18-4D3E-9A55-C00D0D202600}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/ParanormalBanana/Cooldown
AppSupportURL=https://github.com/ParanormalBanana/Cooldown/issues
AppUpdatesURL=https://github.com/ParanormalBanana/Cooldown/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=CooldownSetup-{#AppVersion}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Cooldown.exe
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=Cooldown.exe,Cooldown.Agent.exe
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Cooldown.exe"; IconFilename: "{app}\Cooldown.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Cooldown.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Cooldown.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "CooldownStartup"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "CooldownWatch"; Flags: uninsdeletevalue dontcreatekey

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Cooldown.exe /T"; Flags: runhidden; RunOnceId: "KillCooldown"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Cooldown.Agent.exe /T"; Flags: runhidden; RunOnceId: "KillAgent"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Cooldown\Worker"" /F"; Flags: runhidden; RunOnceId: "DelWorkerTask"
