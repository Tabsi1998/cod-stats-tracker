; COD Stats installer.
;
; Built by scripts/local_check.py --release and by the release workflow with:
;   iscc /DAppVersion=0.1.0 /DPayloadDir=..\.local-testing\publish\win-x64 /DOutputDir=..\.local-testing\release codstats.iss
;
; Per user, without administrator rights. The EXE itself is not compressed (a compressed single file unpacks into
; memory at every start); this installer compresses the download instead.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\.local-testing\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\.local-testing\release"
#endif

; VersionInfoVersion takes numbers only, so a pre-release suffix is cut off for it.
#if Pos("-", AppVersion) > 0
  #define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define NumericVersion AppVersion
#endif

#define AppName "COD Stats"
#define AppExe "CodStats.exe"

[Setup]
AppId={{52AFF90D-0A7E-4B52-9A4B-10648B12C9F2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=Fabian Tabelander
AppPublisherURL=https://github.com/Tabsi1998/cod-stats-tracker
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\CodStats
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 (19041): the first version with Windows Graphics Capture for single windows.
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=CodStats-Setup-win-x64
SetupIconFile=..\apps\desktop\CodStats.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\profiles\*"; DestDir: "{app}\profiles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The first start shows the consent window; nothing is recorded before the player agrees.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An update from inside the client runs silently with /RESTARTAPP=1: the client quit for it and comes back here.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent; Check: RestartAfterUpdate

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopCodStats"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Messages]
german.WelcomeLabel2=Dies installiert {#AppName} nur für dich, ohne Administratorrechte.%n%nDer Client läuft im Tray, erkennt Call of Duty und erfasst deine eigenen Matchwerte. Beim ersten Start fragt er dich, ob du zustimmst.
english.WelcomeLabel2=This installs {#AppName} for you only, without administrator rights.%n%nThe client sits in the tray, notices Call of Duty and records your own match stats. On the first start it asks for your consent.

[CustomMessages]
german.RemoveDataPrompt=Sollen auch deine lokalen Daten gelöscht werden?%n%nDas sind die Einstellungen, die erfassten Matches und der Geräteschlüssel dieses PCs. Ohne Schlüssel kann dieser PC dein Profil nicht mehr ändern.%n%nDein Profil auf der Webseite bleibt bestehen. Um es zu löschen, vorher im Client unter Einstellungen „Alle Daten löschen“ wählen.%n%nWähle Nein, um alles zu behalten.
english.RemoveDataPrompt=Also delete your local data?%n%nThat is the settings, the recorded matches and this PC's device key. Without the key this PC can no longer change your profile.%n%nYour profile on the website stays. To delete it, choose "Delete all data" in the client's settings first.%n%nChoose No to keep everything.

[Code]
function RestartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;

// The client sets its own autostart entry; the uninstaller removes it, so nothing starts a deleted program.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodStats');
  // Silent uninstalls answer No, and No is the default: local data is only deleted when somebody says so. For an
  // update nobody needs to uninstall at all; installing over the old version keeps everything.
  if SuppressibleMsgBox(ExpandConstant('{cm:RemoveDataPrompt}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(ExpandConstant('{localappdata}\CodStats'), True, True, True);
end;
