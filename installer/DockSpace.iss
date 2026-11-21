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
#define PrivacyKey "Software\DockSpace\Privacy"

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
; pagina "Aiutaci a migliorare": stesso testo della finestra dell'app (src/DockStudio.Core/Lang)
italian.PrivacyTitle=Aiutaci a migliorare DockSpace
english.PrivacyTitle=Help us improve DockSpace
italian.PrivacySubtitle=Scegli tu cosa condividere. Puoi cambiare idea in qualsiasi momento dal menu di DockSpace.
english.PrivacySubtitle=You choose what to share. You can change your mind at any time from the DockSpace menu.
italian.PrivacyIntro=Puoi scegliere di inviarci alcune informazioni anonime. Servono solo a capire quante persone usano l'app, quanto la usano e quali errori correggere: non a seguire quello che fai.%n%nI tuoi file, le cartelle che analizzi e quello che fai in DockSpace restano sul tuo computer e non vengono mai inviati. Il sito conta i download in forma anonima, senza indirizzo IP.
english.PrivacyIntro=You can choose to send us some anonymous information. It is only used to understand how many people use the app, how much they use it and which bugs to fix: never to follow what you do.%n%nYour files, the folders you analyze and what you do in DockSpace stay on your computer and are never sent. The website counts downloads anonymously, without IP addresses.
italian.PrivacyUsage=Statistiche d'uso anonime: un codice casuale, la versione dell'app e il tempo di utilizzo
english.PrivacyUsage=Anonymous usage statistics: a random code, the app version and the time of use
italian.PrivacyErrors=Segnalazioni degli errori: cosa si è rotto e in quale versione, senza nomi di file né dati personali
english.PrivacyErrors=Error reports: what broke and in which version, with no file names or personal data
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
var
  PrivacyPage: TInputOptionWizardPage;

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

// Pagina "Aiutaci a migliorare": due caselle vuote, dopo la scelta delle attività aggiuntive.
// La scelta va nel registro (HKCU\Software\DockSpace\Privacy) e l'app la legge al primo avvio
// (DockStudio.Core.Windows.InstallerPrivacyChoice). Negli aggiornamenti e nelle installazioni silenziose
// la pagina non c'è: se serve, chiede l'app.
procedure InitializeWizard();
begin
  PrivacyPage := CreateInputOptionPage(wpSelectTasks, CustomMessage('PrivacyTitle'), CustomMessage('PrivacySubtitle'),
    CustomMessage('PrivacyIntro'), False, False);
  PrivacyPage.Add(CustomMessage('PrivacyUsage'));
  PrivacyPage.Add(CustomMessage('PrivacyErrors'));
  PrivacyPage.Values[0] := False;
  PrivacyPage.Values[1] := False;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PrivacyPage <> nil) and (PageID = PrivacyPage.ID) and IsUpdate();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not IsUpdate() and not WizardSilent() then
  begin
    RegWriteDWordValue(HKEY_CURRENT_USER, '{#PrivacyKey}', 'UsageStats', Ord(PrivacyPage.Values[0]));
    RegWriteDWordValue(HKEY_CURRENT_USER, '{#PrivacyKey}', 'ErrorReports', Ord(PrivacyPage.Values[1]));
  end;
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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Una scelta dell'installer mai letta dall'app non deve restare nel registro.
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, '{#PrivacyKey}');
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\DockSpace');
  end;
end;
