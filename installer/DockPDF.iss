; Installer di DockPDF (Inno Setup 6).
; Di solito si crea con:  scripts\publish-dockpdf.ps1 -Installer
; oppure a mano, dopo la pubblicazione x64 e ARM64:  ISCC.exe /DAppVersion=0.1.0 installer\DockPDF.iss
;
; Installazione per l'utente corrente (non chiede i permessi di amministratore):
; %LOCALAPPDATA%\Programs\DockPDF, collegamento nel menu Start, disinstallazione da "App installate".
; Contiene l'exe per x64 e quello per ARM64: si installa quello adatto al computer.
; DockPDF compare in "Apri con" per i PDF (senza diventare l'app predefinita: lo sceglie l'utente) e,
; se si vuole, parte all'accesso a Windows nell'area di notifica, da dove si apre il cerchio degli strumenti.

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\dist\dockpdf\win-x64\DockPDF.exe")
#endif
#define AppExe "DockPDF.exe"
#define AppMutexName "DockPDF.Running"
#define PrivacyKey "Software\DockPDF\Privacy"
#define ProgId "DockPDF.pdf"

[Setup]
AppId={{6F0B3E5A-2C1D-4E8B-9A47-D5F1C3B8E920}
AppName=DockPDF
AppVersion={#AppVersion}
AppVerName=DockPDF {#AppVersion}
AppPublisher=DockStudio
AppPublisherURL=https://getdockstudio.it/dockpdf/
AppSupportURL=https://getdockstudio.it/dockpdf/
AppUpdatesURL=https://getdockstudio.it/dockpdf/
VersionInfoVersion={#AppVersion}
VersionInfoCompany=DockStudio
VersionInfoDescription=Installer di DockPDF
DefaultDirName={autopf}\DockPDF
DefaultGroupName=DockPDF
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004: serve all'OCR integrato (Windows.Media.Ocr)
MinVersion=10.0.19041
SourceDir=..
OutputDir=dist
OutputBaseFilename=DockPDF-Setup-{#AppVersion}
SetupIconFile=src\DockPDF\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=DockPDF
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; se DockPDF è aperto durante un'installazione manuale, l'installer propone di chiuderlo
CloseApplications=yes
RestartApplications=no
; "Apri con" cambia: Esplora file lo deve sapere
ChangesAssociations=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.OpenWith=Aggiungi DockPDF ad "Apri con" per i file PDF
english.OpenWith=Add DockPDF to "Open with" for PDF files
italian.Startup=Avvia DockPDF all'accesso a Windows, nell'area di notifica (un clic sull'icona apre il cerchio degli strumenti)
english.Startup=Start DockPDF when signing in to Windows, in the notification area (one click on the icon opens the tool circle)
italian.DocumentName=Documento PDF
english.DocumentName=PDF document
italian.AppDescription=Strumenti per i PDF: modifica, firma, unisci, dividi, comprimi e riconosci il testo.
english.AppDescription=PDF tools: edit, sign, merge, split, compress and recognize text.
; pagina "Aiutaci a migliorare": stesso testo della finestra dell'app (src/DockStudio.Core/Lang)
italian.PrivacyTitle=Aiutaci a migliorare DockPDF
english.PrivacyTitle=Help us improve DockPDF
italian.PrivacySubtitle=Scegli tu cosa condividere. Puoi cambiare idea in qualsiasi momento dal menu di DockPDF.
english.PrivacySubtitle=You choose what to share. You can change your mind at any time from the DockPDF menu.
italian.PrivacyIntro=Puoi scegliere di inviarci alcune informazioni anonime. Servono solo a capire quante persone usano l'app, quanto la usano e quali errori correggere: non a seguire quello che fai.%n%nI tuoi PDF, il loro contenuto e quello che fai in DockPDF restano sul tuo computer e non vengono mai inviati. Il sito conta i download in forma anonima, senza indirizzo IP.
english.PrivacyIntro=You can choose to send us some anonymous information. It is only used to understand how many people use the app, how much they use it and which bugs to fix: never to follow what you do.%n%nYour PDFs, their content and what you do in DockPDF stay on your computer and are never sent. The website counts downloads anonymously, without IP addresses.
italian.PrivacyUsage=Statistiche d'uso anonime: un codice casuale, la versione dell'app e il tempo di utilizzo
english.PrivacyUsage=Anonymous usage statistics: a random code, the app version and the time of use
italian.PrivacyErrors=Segnalazioni degli errori: cosa si è rotto e in quale versione, senza nomi di file né dati personali
english.PrivacyErrors=Error reports: what broke and in which version, with no file names or personal data
italian.StillRunning=DockPDF è aperto.%n%nChiudilo (anche dall'icona nell'area di notifica: tasto destro › Esci) e riprova.
english.StillRunning=DockPDF is open.%n%nClose it (also from the notification area icon: right-click › Exit) and try again.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "openwith"; Description: "{cm:OpenWith}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:Startup}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Exe nativo per il processore del computer: ARM64 sui PC con Snapdragon, x64 altrimenti.
Source: "dist\dockpdf\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsArm64
Source: "dist\dockpdf\win-arm64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64

[Icons]
Name: "{autoprograms}\DockPDF"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\DockPDF"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Apri con" per i PDF: un ProgID di DockPDF tra quelli proposti per .pdf. Non cambia l'app predefinita.
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "{cm:DocumentName}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "{cm:DocumentName}"; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "DockPDF"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: openwith
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
; Elenco di Impostazioni › App › App predefinite: da lì l'utente può scegliere DockPDF per i PDF.
Root: HKCU; Subkey: "Software\DockPDF\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "DockPDF"; Flags: uninsdeletekey; Tasks: openwith
Root: HKCU; Subkey: "Software\DockPDF\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"; Tasks: openwith
Root: HKCU; Subkey: "Software\DockPDF\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "{#ProgId}"; Tasks: openwith
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "DockPDF"; ValueData: "Software\DockPDF\Capabilities"; Flags: uninsdeletevalue; Tasks: openwith
; Avvio all'accesso: solo l'icona nell'area di notifica, nessuna finestra.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DockPDF"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
; installazione normale: casella "Avvia DockPDF" nell'ultima pagina
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,DockPDF}"; Flags: nowait postinstall skipifsilent
; aggiornamento dall'app (installazione silenziosa): riapre DockPDF alla fine
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
  // L'aggiornamento dall'app avvia l'installer e poi chiude DockPDF:
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
// La scelta va nel registro (HKCU\Software\DockPDF\Privacy) e l'app la legge al primo avvio
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
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\DockPDF');
  end;
end;
