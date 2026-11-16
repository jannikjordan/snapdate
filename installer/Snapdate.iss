; Snapdate installer (Inno Setup 6.3 or newer). See docs/build-and-release.md and docs/adr/0006-inno-setup-installer.md.
;
; Do not compile by hand: scripts/build/build.ps1 -Target Installer passes the required defines
;   /DAppVersion=0.1.0              version as shown in Settings > Apps (may carry a -suffix)
;   /DAppVersionNumeric=0.1.0       numeric x.y.z part for the exe version resource
;   /DArch=x64compatible|arm64      Inno architecture identifier
;   /DRid=win-x64|win-arm64         runtime identifier, used in the output file name
;   /DSourceExe=<path>              the published Snapdate.exe
;   /DLicenseFile=<path>            LICENSE of the repository
;   /DIconFile=<path>               app.ico
; and the output folder (/O).
;
; The file is UTF-8 with a byte order mark (needed for the German texts) - keep it that way.

#ifndef AppVersion
  #error Compile with scripts/build/build.ps1 -Target Installer (missing /DAppVersion).
#endif
#ifndef AppVersionNumeric
  #define AppVersionNumeric AppVersion
#endif
#ifndef Arch
  #error Missing /DArch.
#endif
#ifndef Rid
  #error Missing /DRid.
#endif
#ifndef IconFile
  #error Missing /DIconFile.
#endif
#ifndef SourceExe
  #error Missing /DSourceExe.
#endif

[Setup]
; NEVER change AppId: it is how a newer installer finds and upgrades an existing installation.
AppId={{D5F11F70-E97E-44D0-8573-E15843FA60B3}
AppName=Snapdate
AppVersion={#AppVersion}
AppVerName=Snapdate {#AppVersion}
AppPublisher=Snapdate contributors
AppCopyright=Snapdate contributors
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoCompany=Snapdate contributors
VersionInfoProductName=Snapdate
VersionInfoDescription=Snapdate Setup

; Per-user by default (no administrator rights). The first interactive install asks "only for me" or
; "for all users"; /CURRENTUSER and /ALLUSERS answer that question on the command line. An upgrade keeps
; the scope and folder of the existing installation (UsePreviousPrivileges / UsePreviousAppDir, both default).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\Snapdate
UsePreviousAppDir=yes
UsePreviousPrivileges=yes
; The Start menu entry is a single shortcut, no program group.
DisableProgramGroupPage=yes

ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
MinVersion=10.0

; Close a running Snapdate (Restart Manager) before the exe is replaced; nothing else is touched.
CloseApplications=yes
CloseApplicationsFilter=Snapdate.exe
RestartApplications=no

SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Snapdate.exe
UninstallDisplayName=Snapdate
WizardStyle=modern
ShowLanguageDialog=auto
OutputBaseFilename=Snapdate-Setup-v{#AppVersion}-{#Rid}
Compression=lzma2/ultra64
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.RemoveSettingsPrompt=Do you also want to delete your Snapdate settings (remembered folders, file name pattern, language)?%n%nChoose No to keep them, for example if you want to install Snapdate again later.
de.RemoveSettingsPrompt=Möchten Sie auch Ihre Snapdate-Einstellungen löschen (gemerkte Ordner, Dateinamen-Muster, Sprache)?%n%nWählen Sie Nein, um sie zu behalten, zum Beispiel wenn Sie Snapdate später erneut installieren möchten.

en.NewerInstalled=A newer version of Snapdate (%1) is already installed. This installer contains the older version %2 and will not replace it.
de.NewerInstalled=Es ist bereits eine neuere Version von Snapdate (%1) installiert. Dieses Installationsprogramm enthält die ältere Version %2 und ersetzt sie nicht.

[Tasks]
; Checked by default; Setup remembers the choice for upgrades.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Snapdate.exe"; Flags: ignoreversion
#ifdef LicenseFile
Source: "{#LicenseFile}"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\Snapdate"; Filename: "{app}\Snapdate.exe"
Name: "{autodesktop}\Snapdate"; Filename: "{app}\Snapdate.exe"; Tasks: desktopicon

[Run]
; Interactive installs: the usual "Launch Snapdate" checkbox on the last page.
Filename: "{app}\Snapdate.exe"; Description: "{cm:LaunchProgram,Snapdate}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent installs started by the in-app updater pass /RESTARTAPP to get Snapdate started again.
Filename: "{app}\Snapdate.exe"; Flags: nowait postinstall runasoriginaluser; Check: ShouldRestartApp

[UninstallDelete]
Type: files; Name: "{app}\snapdate-install.ini"

[Code]
const
  MarkerFileName = 'snapdate-install.ini';

function HasSwitch(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/' + Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

{ /RESTARTAPP only has an effect in silent mode; interactive installs use the finish-page checkbox. }
function ShouldRestartApp: Boolean;
begin
  Result := WizardSilent and HasSwitch('RESTARTAPP');
end;

{ Tells the app how it was installed (read by Snapdate.Infrastructure.Installation.FileInstallInfo). }
procedure WriteInstallMarker;
var
  Scope: String;
begin
  if IsAdminInstallMode then
    Scope := 'all'
  else
    Scope := 'user';
  if not SaveStringToFile(ExpandConstant('{app}\' + MarkerFileName),
    '[Install]' + #13#10 + 'Scope=' + Scope + #13#10, False) then
    Log('Could not write the install marker.');
end;

{ Reads "1.2.3" (a -suffix is ignored) into three numbers; False when the text is not a version. }
function ParseVersion(const S: String; var Parts: array of Integer): Boolean;
var
  I, P, Value: Integer;
  Rest, Part: String;
begin
  Result := False;
  Rest := S;
  P := Pos('-', Rest);
  if P > 0 then
    Rest := Copy(Rest, 1, P - 1);
  for I := 0 to 2 do
  begin
    if I < 2 then
    begin
      P := Pos('.', Rest);
      if P = 0 then
        Exit;
      Part := Copy(Rest, 1, P - 1);
      Delete(Rest, 1, P);
    end
    else
      Part := Rest;
    Value := StrToIntDef(Part, -1);
    if Value < 0 then
      Exit;
    Parts[I] := Value;
  end;
  Result := True;
end;

{ True when Installed is a strictly newer x.y.z than Mine (pre-release suffixes are ignored, so an rc
  can be replaced by the final release of the same number). }
function IsNewerVersion(const Installed, Mine: String): Boolean;
var
  A, B: array of Integer;
  I: Integer;
begin
  Result := False;
  SetArrayLength(A, 3);
  SetArrayLength(B, 3);
  if not (ParseVersion(Installed, A) and ParseVersion(Mine, B)) then
    Exit;
  for I := 0 to 2 do
  begin
    if A[I] > B[I] then
    begin
      Result := True;
      Exit;
    end;
    if A[I] < B[I] then
      Exit;
  end;
end;

{ Defence in depth for the in-app updater: never install over a NEWER installed version, so a signed but older
  installer (replayed under a newer name) cannot downgrade an existing installation. Silent runs exit with an
  error code (Setup returns 1) without any dialog; interactive runs explain why. }
function InitializeSetup: Boolean;
var
  Installed: String;
begin
  Result := True;
  Installed := '';
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5F11F70-E97E-44D0-8573-E15843FA60B3}_is1',
    'DisplayVersion', Installed) then
    if not RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5F11F70-E97E-44D0-8573-E15843FA60B3}_is1',
      'DisplayVersion', Installed) then
      Installed := '';
  if (Installed <> '') and IsNewerVersion(Installed, '{#AppVersion}') then
  begin
    Log('Refusing to downgrade from ' + Installed + ' to {#AppVersion}.');
    SuppressibleMsgBox(FmtMessage(CustomMessage('NewerInstalled'), [Installed, '{#AppVersion}']),
      mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstallMarker;
end;

{ Settings live in %APPDATA%\Snapdate and survive upgrades. On uninstall they are kept unless the user
  asks for them to be deleted. The question is only asked for per-user installs: an all-users uninstall runs
  elevated and the roaming profile folder may then belong to another administrator, so it never deletes settings
  (it keeps them silently). Silent uninstalls always keep them. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\Snapdate');
    if (not UninstallSilent) and (not IsAdminInstallMode) and DirExists(SettingsDir) then
      if SuppressibleMsgBox(CustomMessage('RemoveSettingsPrompt'), mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(SettingsDir, True, True, True);
  end;
end;
