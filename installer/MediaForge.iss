; Inno Setup 6 script for MediaForge. Build with ..\build-installer.ps1, which publishes the app first
; and passes the version from MediaForge.csproj.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "MediaForge"
#define MyAppExe "MediaForge.exe"
#define MyPublisher "Siammods.com"
#define MyPublisherUrl "https://siammods.com"

[Setup]
AppId={{6E0F2C1B-8D5A-4B7E-9C3F-2A1D7E4B9F60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyPublisher}
AppPublisherURL={#MyPublisherUrl}
AppSupportURL={#MyPublisherUrl}
AppUpdatesURL={#MyPublisherUrl}
AppCopyright=© 2026 {#MyPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyPublisher}
VersionInfoDescription={#MyAppName} Setup
; Per-user install: {autopf} resolves to %LOCALAPPDATA%\Programs, no UAC prompt.
PrivilegesRequired=lowest
; winget passes /CURRENTUSER (or /ALLUSERS for --scope machine); Inno rejects them unless overrides are allowed.
PrivilegesRequiredOverridesAllowed=commandline
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
WizardSmallImageFile=wizard-small.bmp
SetupIconFile=..\src\MediaForge\Assets\MediaForge.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
OutputDir=Output
OutputBaseFilename=MediaForge-Setup-{#MyAppVersion}
; Ask Thai / English up front, preselected from the Windows UI language.
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=yes

[Languages]
Name: "th"; MessagesFile: "compiler:Languages\Thai.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run "setup.exe /SILENT ... /UPDATE=1": start the new version once it is installed.
; runasoriginaluser keeps the app out of Setup's RedirectionGuard, which it would otherwise inherit:
; yt-dlp then fails with WinError 448 on any user-made junction in PATH.
Filename: "{app}\{#MyAppExe}"; Flags: nowait runasoriginaluser; Check: IsInAppUpdate

; Settings (%APPDATA%\MediaForge) and downloaded tools (%LOCALAPPDATA%\MediaForge) are left in place on uninstall.

[Code]
function IsInAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
