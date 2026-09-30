; The StreamDecky installer. scripts\Build-Installer.ps1 builds it from the published app:
;   ISCC.exe /DAppVersion=<version> /DPublishDir=<publish folder> /O<output folder> installer\StreamDecky.iss
; plus /DSign and /Sstreamdecky=<sign command> when the release is signed.
;
; StreamDecky's own updater runs it silently with two extra parameters:
;   /WAITPID=<id>          the StreamDecky that started it; its files stay locked until it exits
;   /UPDATECLEANUP=<dir>   the download folder, which the restarted StreamDecky deletes

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
; Windows and this installer recognise an existing StreamDecky install by this id. Never change it.
AppId={{66FD292D-D8C7-4FD3-8917-8148211563D9}
AppName=StreamDecky
AppVersion={#AppVersion}
AppVerName=StreamDecky {#AppVersion}
AppPublisher=BenjiButten
AppPublisherURL=https://benjibutten.github.io/StreamDecky/
AppSupportURL=https://github.com/benjibutten/StreamDecky/issues
AppUpdatesURL=https://github.com/benjibutten/StreamDecky/releases
VersionInfoVersion={#AppVersion}
; Program Files, because only a folder that nothing without administrator rights can
; change may start StreamDecky as administrator at sign-in.
DefaultDirName={autopf}\StreamDecky
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; Uninstalling removes the startup entries of the account that runs it, which is the
; account that set them up in every case but an administrator uninstalling for someone else.
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=StreamDecky-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\StreamDecky\StreamDecky.ico
UninstallDisplayIcon={app}\StreamDecky.exe
UninstallDisplayName=StreamDecky
WizardStyle=modern
; Setup picks the image that best fits the display scaling.
WizardImageFile=WizardImage100.png,WizardImage150.png,WizardImage200.png
WizardSmallImageFile=WizardSmallImage100.png,WizardSmallImage150.png,WizardSmallImage200.png
DisableWelcomePage=no
InfoBeforeFile=BeforeInstall.txt
Compression=lzma2
SolidCompression=yes
; The Finished page and the update both start StreamDecky again. Restart Manager must
; not start a second copy.
RestartApplications=no
UsePreviousTasks=no
#ifdef Sign
; Signs the installer and the uninstaller it writes into {app}.
SignTool=streamdecky
#endif

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nStreamDecky opens a deck of buttons, chat lines, forms and notes on top of your game with a hotkey, and sends what you click back into the game.%n%nIt is free and open source, and every release is built from the public code on GitHub.
WizardInfoBefore=Before you install
InfoBeforeLabel=What gets installed, what StreamDecky connects to, and its license.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nBuild your deck in the editor and set the overlay hotkey in Settings.

[Tasks]
; Ticking it turns the setting on; unticked leaves the setting as it is in StreamDecky.
; Setup therefore does not remember the tick, or a reinstall would turn the setting
; back on after the user turned it off in StreamDecky.
Name: "runasadmin"; Description: "Run StreamDecky as administrator, so the keys it sends also reach games and programs running as administrator"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\StreamDecky"; Filename: "{app}\StreamDecky.exe"

[Run]
; As the account that started Setup, not the one that approved it: they differ when a
; standard account enters an administrator's password, and StreamDecky and its settings
; belong to the first. With the setting on, StreamDecky then asks for elevation itself.
Filename: "{app}\StreamDecky.exe"; Parameters: "--run-as-administrator"; Flags: waituntilterminated runasoriginaluser; Tasks: runasadmin; Check: not WizardSilent
Filename: "{app}\StreamDecky.exe"; Description: "Start StreamDecky"; Flags: nowait postinstall skipifsilent runasoriginaluser
; After an update, StreamDecky comes back with the rights it had: those of whoever started this installer.
Filename: "{app}\StreamDecky.exe"; Parameters: "--update-cleanup ""{param:UPDATECLEANUP}"""; Flags: nowait runasoriginaluser; Check: IsUpdateFromStreamDecky

[UninstallRun]
; The task that starts StreamDecky as administrator at sign-in, for the account that uninstalls.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""StreamDecky ({username})"" /F"; Flags: runhidden; RunOnceId: "DeleteStartupTask"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\StreamDecky-update-*"

[Code]
const
  SYNCHRONIZE = $00100000;
  EVENT_MODIFY_STATE = $0002;
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  HWND_TOPMOST = -1;
  HWND_NOTOPMOST = -2;
  SWP_NOSIZE = $0001;
  SWP_NOMOVE = $0002;

var
  WizardRaised: Boolean;

function OpenProcess(DesiredAccess: Cardinal; InheritHandle: Boolean; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function OpenEvent(DesiredAccess: Cardinal; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function SetWindowPos(Window: HWND; InsertAfter: Integer; X, Y, Width, Height: Integer; Flags: Cardinal): Boolean;
  external 'SetWindowPos@user32.dll stdcall';

procedure CurPageChanged(CurPageID: Integer);
begin
  if WizardRaised or WizardSilent then
    Exit;
  WizardRaised := True;

  // After the UAC prompt the wizard can open behind the window it was started from,
  // since Windows lets only the process the user last used take the focus. Passing
  // through the topmost band puts it on top anyway; the focus follows when allowed.
  SetWindowPos(WizardForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE);
  SetWindowPos(WizardForm.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE);
  BringToFrontAndRestore;
end;

function IsUpdateFromStreamDecky: Boolean;
begin
  Result := ExpandConstant('{param:UPDATECLEANUP}') <> '';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessId: Integer;
  Process: THandle;
  ExitRequest: THandle;
begin
  Result := '';
  ProcessId := StrToIntDef(ExpandConstant('{param:WAITPID|0}'), 0);
  if ProcessId = 0 then
    Exit;

  // The StreamDecky that started this update keeps running until Windows has approved
  // the installer, and exits when told so here. Name must match ExitForUpdateEventName
  // in GitHubUpdateService.cs.
  ExitRequest := OpenEvent(EVENT_MODIFY_STATE, False, 'StreamDecky.ExitForUpdate.' + IntToStr(ProcessId));
  if ExitRequest <> 0 then
  begin
    SetEvent(ExitRequest);
    CloseHandle(ExitRequest);
  end;

  Process := OpenProcess(SYNCHRONIZE, False, ProcessId);
  if Process <> 0 then
  begin
    WaitForSingleObject(Process, 30000);
    CloseHandle(Process);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // "Start with Windows" without administrator rights, unless it starts a copy of
  // StreamDecky somewhere else.
  if RegQueryStringValue(HKCU, RunKey, 'StreamDecky', Command)
    and (Pos(Uppercase(ExpandConstant('{app}\')), Uppercase(Command)) > 0) then
    RegDeleteValue(HKCU, RunKey, 'StreamDecky');
end;
