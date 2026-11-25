; Installer del launcher DockStudio (Inno Setup 6).
; Di solito si crea con:  scripts\publish-dockstudio.ps1 -Installer
; oppure a mano, dopo la pubblicazione x64 e ARM64:  ISCC.exe /DAppVersion=0.1.0 installer\DockStudio.iss
;
; Installazione per l'utente corrente (non chiede i permessi di amministratore):
; %LOCALAPPDATA%\Programs\DockStudio, collegamento nel menu Start, disinstallazione da "App installate".
; Contiene l'exe per x64 e quello per ARM64: si installa quello adatto al computer.
; Il launcher è facoltativo: mostra le app della suite, le apre, le installa e le aggiorna, e se si vuole parte
; all'accesso a Windows nell'area di notifica (al posto delle icone delle singole app).

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\dist\dockstudio\win-x64\DockStudio.exe")
#endif
#define AppExe "DockStudio.exe"
#define AppMutexName "DockStudio.Running"
#define PrivacyKey "Software\DockStudio\Privacy"

[Setup]
AppId={{9928AB35-59FA-4C74-B952-7631AD889C68}
AppName=DockStudio
AppVersion={#AppVersion}
AppVerName=DockStudio {#AppVersion}
AppPublisher=DockStudio
AppPublisherURL=https://getdockstudio.it/
AppSupportURL=https://getdockstudio.it/
AppUpdatesURL=https://getdockstudio.it/
VersionInfoVersion={#AppVersion}
VersionInfoCompany=DockStudio
VersionInfoDescription=Installer di DockStudio
DefaultDirName={autopf}\DockStudio
DefaultGroupName=DockStudio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SourceDir=..
OutputDir=dist
OutputBaseFilename=DockStudio-Setup-{#AppVersion}
SetupIconFile=src\DockStudio.Launcher\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=DockStudio
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; se DockStudio è aperto durante un'installazione manuale, l'installer propone di chiuderlo
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.Startup=Avvia DockStudio all'accesso a Windows, nell'area di notifica
english.Startup=Start DockStudio when you sign in to Windows, in the notification area
; pagina "Aiutaci a migliorare": stesso testo della finestra dell'app (src/DockStudio.Core/Lang)
italian.PrivacyTitle=Aiutaci a migliorare DockStudio
english.PrivacyTitle=Help us improve DockStudio
italian.PrivacySubtitle=Scegli tu cosa condividere. Puoi cambiare idea in qualsiasi momento dal menu di DockStudio.
english.PrivacySubtitle=You choose what to share. You can change your mind at any time from the DockStudio menu.
italian.PrivacyIntro=Puoi scegliere di inviarci alcune informazioni anonime. Servono solo a capire quante persone usano l'app, quanto la usano e quali errori correggere: non a seguire quello che fai.%n%nI tuoi file e quello che fai con le app restano sul tuo computer e non vengono mai inviati. Il sito conta i download in forma anonima, senza indirizzo IP.
english.PrivacyIntro=You can choose to send us some anonymous information. It is only used to understand how many people use the app, how much they use it and which bugs to fix: never to follow what you do.%n%nYour files and what you do with the apps stay on your computer and are never sent. The website counts downloads anonymously, without IP addresses.
italian.PrivacyUsage=Statistiche d'uso anonime: un codice casuale, la versione dell'app e il tempo di utilizzo
english.PrivacyUsage=Anonymous usage statistics: a random code, the app version and the time of use
italian.PrivacyErrors=Segnalazioni degli errori: cosa si è rotto e in quale versione, senza nomi di file né dati personali
english.PrivacyErrors=Error reports: what broke and in which version, with no file names or personal data
italian.StillRunning=DockStudio è aperto.%n%nEsci da DockStudio (tasto destro sulla sua icona accanto all'orologio, Esci) e riprova.
english.StillRunning=DockStudio is open.%n%nQuit DockStudio (right-click its icon next to the clock, Quit) and try again.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:Startup}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Exe nativo per il processore del computer: ARM64 sui PC con Snapdragon, x64 altrimenti.
Source: "dist\dockstudio\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsArm64
Source: "dist\dockstudio\win-arm64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64

[Icons]
Name: "{autoprograms}\DockStudio"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\DockStudio"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Avvio all'accesso: solo l'icona nell'area di notifica, nessuna finestra (si cambia anche dal menu dell'app).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DockStudio"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
; installazione normale: casella "Avvia DockStudio" nell'ultima pagina
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,DockStudio}"; Flags: nowait postinstall skipifsilent
; aggiornamento dall'app (installazione silenziosa): riapre DockStudio alla fine
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
  // L'aggiornamento dall'app avvia l'installer e poi chiude DockStudio:
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
// La scelta va nel registro (HKCU\Software\DockStudio\Privacy) e l'app la legge al primo avvio
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
  code: Integer;
begin
  Result := True;
  // Prima si chiede al launcher di chiudersi (vive nell'area di notifica), poi si aspetta al massimo 10 s.
  if CheckForMutexes('{#AppMutexName}') then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--exit', '', SW_HIDE, ewNoWait, code);
  i := 0;
  while CheckForMutexes('{#AppMutexName}') and (i < 40) do
  begin
    Sleep(250);
    i := i + 1;
  end;
  if UninstallSilent() then
  begin
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
  // Licenza, registro delle app e impostazioni in %APPDATA%\DockStudio restano: li usano anche le app.
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, '{#PrivacyKey}');
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\DockStudio');
  end;
end;
