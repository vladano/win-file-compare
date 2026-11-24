; File Compare installer (Inno Setup 7).
; Build it with scripts\build-installer.ps1, which publishes the app and passes the version and folders in.
;
; Installs for the current user only: no administrator rights, nothing outside the user's profile and
; HKEY_CURRENT_USER. The program goes to %LocalAppData%\Programs\File Compare.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef LicensesDir
  #define LicensesDir "..\build\licenses"
#endif

#define AppName "File Compare"
#define AppExe "FileCompare.exe"
#define AppUrl "https://github.com/vladano/win-file-compare"

[Setup]
; The AppId identifies the app for upgrades and uninstall. Never change it.
AppId={{8F4C6E2A-3B1D-4F7A-9C55-2E6B1A9D0C47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=vladano
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 vladano
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

; Per-user install: {autopf} is %LocalAppData%\Programs and {autoprograms} the user's Start menu.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; The running app holds this mutex (AppHost.InstallerMutexName), so Setup and Uninstall ask to close it first.
AppMutex=FileCompare.Running
CloseApplications=yes
RestartApplications=no

LicenseFile=..\LICENSE
SetupIconFile=..\build\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; The published exe is a 64-bit self-contained build (x64 also runs on Windows 11 on Arm).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 version 1809 or newer.
MinVersion=10.0.17763

OutputDir=..\dist
OutputBaseFilename=FileCompare-Setup-{#AppVersion}-x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "explorermenu"; Description: "Add File Compare to the Explorer right-click menu (Select left side, Compare to, Compare selected items)"; GroupDescription: "Explorer integration:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"
Source: "{#LicensesDir}\*.txt"; DestDir: "{app}\licenses"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The app writes (or removes) its own Explorer menu entries, so they always point at this copy.
Filename: "{app}\{#AppExe}"; Parameters: "--register-shell"; Tasks: explorermenu; Flags: runhidden waituntilterminated; StatusMsg: "Adding the Explorer menu entries..."
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Tasks: not explorermenu; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveExplorerMenu"

; The user's settings (%AppData%\FileCompare: theme, rules, recent list, sessions) are kept on purpose,
; so a reinstall or upgrade keeps them. Delete that folder by hand to remove them too.
