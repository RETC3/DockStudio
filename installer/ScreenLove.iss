; Installer di ScreenLove (Inno Setup 6).
; Di solito si crea con:  scripts\publish -Installer
; oppure a mano, dopo la pubblicazione:  ISCC.exe /DAppVersion=0.1.0 installer\ScreenLove.iss
;
; Installazione per l'utente corrente (non chiede i permessi di amministratore):
; %LOCALAPPDATA%\Programs\ScreenLove, collegamento nel menu Start, disinstallazione da "App installate".

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\dist\win-x64\ScreenLove.exe")
#endif
#define AppExe "ScreenLove.exe"

[Setup]
AppId={{1162A7F1-B15B-41DF-8754-16C9B61DCDF8}
AppName=ScreenLove
AppVersion={#AppVersion}
AppVerName=ScreenLove {#AppVersion}
AppPublisher=ScreenLove
AppPublisherURL=https://github.com/RETC3/ScreenLove
AppSupportURL=https://github.com/RETC3/ScreenLove/issues
AppUpdatesURL=https://github.com/RETC3/ScreenLove/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\ScreenLove
DefaultGroupName=ScreenLove
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SourceDir=..
OutputDir=dist
OutputBaseFilename=ScreenLove-Setup-{#AppVersion}
SetupIconFile=src\ScreenLove\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=ScreenLove
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; se ScreenLove è aperto durante un'installazione manuale, l'installer propone di chiuderlo
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.AutoStart=Avvia ScreenLove con Windows (icona nell'area di notifica)
english.AutoStart=Start ScreenLove with Windows (notification area icon)
italian.StillRunning=ScreenLove è in esecuzione.%n%nChiudilo (icona nell'area di notifica → Esci) e riprova.
english.StillRunning=ScreenLove is running.%n%nClose it (notification area icon → Exit) and try again.
; pagina "Aiutaci a migliorare": stesso testo della finestra dell'app (src/DockStudio.Core/Lang)
italian.PrivacyTitle=Aiutaci a migliorare ScreenLove
english.PrivacyTitle=Help us improve ScreenLove
italian.PrivacySubtitle=Scegli tu cosa condividere. Puoi cambiare idea in qualsiasi momento dalle Impostazioni.
english.PrivacySubtitle=You choose what to share. You can change your mind at any time in Settings.
italian.PrivacyIntro=Puoi scegliere di inviarci alcune informazioni anonime. Servono solo a capire quante persone usano l'app, quanto la usano e quali errori correggere: non a seguire quello che fai.%n%nLe tue catture, i tuoi file e quello che fai in ScreenLove restano sul tuo computer e non vengono mai inviati. Il sito conta i download in forma anonima, senza indirizzo IP.
english.PrivacyIntro=You can choose to send us some anonymous information. It is only used to understand how many people use the app, how much they use it and which bugs to fix: never to follow what you do.%n%nYour captures, your files and what you do in ScreenLove stay on your computer and are never sent. The website counts downloads anonymously, without IP addresses.
italian.PrivacyUsage=Statistiche d'uso anonime: un codice casuale, la versione dell'app e il tempo di utilizzo
english.PrivacyUsage=Anonymous usage statistics: a random code, the app version and the time of use
italian.PrivacyErrors=Segnalazioni degli errori: cosa si è rotto e in quale versione, senza nomi di file né dati personali
english.PrivacyErrors=Error reports: what broke and in which version, with no file names or personal data

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "dist\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ScreenLove"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\ScreenLove"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; stesso valore che scrive l'app con "Avvia con Windows": l'opzione resta coerente nei due posti
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenLove"; \
    ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart

[Run]
; installazione normale: casella "Avvia ScreenLove" nell'ultima pagina
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,ScreenLove}"; Flags: nowait postinstall skipifsilent
; aggiornamento dall'app (installazione silenziosa): riapre ScreenLove alla fine
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[Code]
const
  AppMutexName = 'ScreenLove.SingleInstance';
  PrivacyKey = 'Software\ScreenLove\Privacy';

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
  // L'aggiornamento automatico avvia l'installer e poi chiude ScreenLove:
  // si aspetta (al massimo 15 s) che l'app sia uscita prima di copiare i file.
  if IsUpdate() then
  begin
    i := 0;
    while CheckForMutexes(AppMutexName) and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
  end;
  Result := True;
end;

// Pagina "Aiutaci a migliorare": due caselle vuote, dopo la scelta delle attività aggiuntive.
// La scelta va nel registro (HKCU\Software\ScreenLove\Privacy) e l'app la legge al primo avvio
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
    RegWriteDWordValue(HKEY_CURRENT_USER, PrivacyKey, 'UsageStats', Ord(PrivacyPage.Values[0]));
    RegWriteDWordValue(HKEY_CURRENT_USER, PrivacyKey, 'ErrorReports', Ord(PrivacyPage.Values[1]));
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
    while CheckForMutexes(AppMutexName) and (i < 60) do
    begin
      Sleep(250);
      i := i + 1;
    end;
    Result := not CheckForMutexes(AppMutexName);
    Exit;
  end;
  while CheckForMutexes(AppMutexName) do
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
  // "Avvia con Windows" può essere stato attivato anche dall'app: lo si toglie comunque.
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ScreenLove');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, PrivacyKey);
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\ScreenLove');
  end;
end;
