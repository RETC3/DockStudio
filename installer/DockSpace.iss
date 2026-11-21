; Installer di DockSpace (Inno Setup 6).
; Di solito si crea con:  scripts\publish-dockspace.ps1 -Installer
; oppure a mano, dopo la pubblicazione x64 e ARM64:  ISCC.exe /DAppVersion=0.1.0 installer\DockSpace.iss
;
; Installazione per l'utente corrente (non chiede i permessi di amministratore):
; %LOCALAPPDATA%\Programs\DockSpace, collegamento nel menu Start, disinstallazione da "App installate".
; Contiene l'exe per x64 e quello per ARM64: si installa quello adatto al computer.

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\dist\dockspace\win-x64\DockSpace.exe")
#endif
#define AppExe "DockSpace.exe"
#define AppMutexName "DockSpace.Running"

[Setup]
AppId={{C236BCE8-3505-4502-958B-ADD55D15F867}
AppName=DockSpace
AppVersion={#AppVersion}
AppVerName=DockSpace {#AppVersion}
AppPublisher=DockStudio
AppPublisherURL=https://getdockstudio.it/dockspace/
AppSupportURL=https://getdockstudio.it/dockspace/
AppUpdatesURL=https://getdockstudio.it/dockspace/
VersionInfoVersion={#AppVersion}
VersionInfoCompany=DockStudio
VersionInfoDescription=Installer di DockSpace
DefaultDirName={autopf}\DockSpace
DefaultGroupName=DockSpace
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SourceDir=..
OutputDir=dist
OutputBaseFilename=DockSpace-Setup-{#AppVersion}
SetupIconFile=src\DockSpace\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=DockSpace
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; se DockSpace è aperto durante un'installazione manuale, l'installer propone di chiuderlo
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.ContextMenu=Aggiungi "Analizza con DockSpace" al menu del tasto destro su cartelle e dischi
english.ContextMenu=Add "Analyze with DockSpace" to the right-click menu of folders and drives
italian.ContextMenuVerb=Analizza con DockSpace
english.ContextMenuVerb=Analyze with DockSpace
italian.StillRunning=DockSpace è aperto.%n%nChiudi tutte le finestre di DockSpace e riprova.
english.StillRunning=DockSpace is open.%n%nClose all DockSpace windows and try again.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Exe nativo per il processore del computer: ARM64 sui PC con Snapdragon, x64 altrimenti.
Source: "dist\dockspace\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsArm64
Source: "dist\dockspace\win-arm64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64

[Icons]
Name: "{autoprograms}\DockSpace"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\DockSpace"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Analizza con DockSpace" sul tasto destro di una cartella, di un disco e dello sfondo di una cartella aperta.
; Su Windows 11 compare in "Mostra altre opzioni". Si toglie con la disinstallazione.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DockSpace"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenuVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DockSpace"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DockSpace\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%V"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DockSpace"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenuVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DockSpace"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DockSpace\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%V"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\DockSpace"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenuVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\DockSpace"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\DockSpace\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%V"""; Tasks: contextmenu

[Run]
; installazione normale: casella "Avvia DockSpace" nell'ultima pagina
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,DockSpace}"; Flags: nowait postinstall skipifsilent
; aggiornamento dall'app (installazione silenziosa): riapre DockSpace alla fine
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[Code]
function IsUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function InitializeSetup(): Boolean;
var
  i: Integer;
begin
  // L'aggiornamento dall'app avvia l'installer e poi chiude DockSpace:
  // si aspetta (al massimo 15 s) che l'app sia uscita prima di copiare i file.
  if IsUpdate() then
  begin
    i := 0;
    while CheckForMutexes('{#AppMutexName}') and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
  end;
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  i: Integer;
begin
  Result := True;
  if UninstallSilent() then
  begin
    // senza finestre non si può chiedere di chiudere l'app: si aspetta un po' e poi si rinuncia
    i := 0;
    while CheckForMutexes('{#AppMutexName}') and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
    Result := not CheckForMutexes('{#AppMutexName}');
    Exit;
  end;
  while CheckForMutexes('{#AppMutexName}') do
  begin
    if MsgBox(CustomMessage('StillRunning'), mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;
