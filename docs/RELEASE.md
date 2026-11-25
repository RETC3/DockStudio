# Release di ScreenLove dal repository privato

Il codice di ScreenLove e del nucleo DockStudio sta nel repository **privato** `RETC3/DockStudio`.
Le release invece escono nel repository **pubblico** `RETC3/ScreenLove`, perché lì le leggono:

- il sito https://screenlove.it, che crea `latest.json` dall'ultima release (`scripts/make-latest-json.ps1`);
- il ripiego dei download del sito, quando un file non è su Cloudflare R2 (`web/functions/download/[[path]].js`);
- le app già installate, quando il sito non risponde (`Updater`, riserva GitHub).

Il workflow `.github/workflows/release.yml` gira in DockStudio e crea la release in ScreenLove con
`gh release create --repo RETC3/ScreenLove`. Per farlo gli serve un token con accesso a quel repository.

## Il token `SCREENLOVE_RELEASES_TOKEN`

Si crea una volta, dall'account GitHub che possiede i repository:

1. GitHub → foto profilo → **Settings** → **Developer settings** → **Personal access tokens** → **Fine-grained tokens** → **Generate new token**.
2. Nome: `ScreenLove releases`. Scadenza: la più lunga consentita, e un promemoria per rinnovarlo.
3. **Repository access** → *Only select repositories* → solo `RETC3/ScreenLove`.
4. **Permissions** → *Repository permissions* → **Contents: Read and write**. Nient'altro.
5. Genera il token e copialo: GitHub lo mostra una volta sola.
6. Repository `RETC3/DockStudio` → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:
   nome `SCREENLOVE_RELEASES_TOKEN`, valore il token copiato.

Il token resta solo nei secret di GitHub: non va scritto in file, chat o messaggi.
Se manca o è scaduto, il workflow si ferma all'inizio, prima di compilare e di caricare file su R2.

## Note

- Il tag della release (per esempio `v0.6.0`) si spinge in DockStudio, come prima: `git push origin v0.6.0`.
- Nel repository pubblico GitHub crea un tag con lo stesso nome sull'ultimo commit di `RETC3/ScreenLove`,
  che non contiene più il codice nuovo: serve solo a tenere insieme i file della release.
- Quando ScreenLove passerà al sito DockStudio (getdockstudio.it/screenlove), `/latest.json`, `/download/` e
  `/api/ping` devono restare serviti su screenlove.it per le versioni già installate.

## DockSpace (e le prossime app della suite)

Le app nuove non hanno un repository pubblico: installer, zip e `latest.json` stanno nel bucket R2
`dockstudio-downloads` (una cartella per app) e li serve getdockstudio.it (`site/lib/downloads.js`):

- `https://getdockstudio.it/dockspace/latest.json`: ultima versione, letta dall'app per gli aggiornamenti;
- `https://getdockstudio.it/dockspace/download/<file>` e `.../download/latest`: i file da scaricare.

Per pubblicare una versione:

```powershell
git tag -a dockspace-v0.2.0 -m "Novità della versione (testo che l'app mostra nell'aggiornamento)"
git push origin dockspace-v0.2.0
```

Il workflow `release-dockspace.yml` esegue i controlli, compila exe (x64 e ARM64), zip e installer, crea
`latest.json` (`scripts/make-app-latest.ps1`), carica tutto su R2 (per ultimo `latest.json`) e archivia la release
in questo repository. Servono solo i secret `CLOUDFLARE_API_TOKEN` e `CLOUDFLARE_ACCOUNT_ID`.

Prova in locale senza pubblicare: la variabile `DOCKSPACE_UPDATE_FEED` indica un `latest.json` sul disco
(con il link `file:///` dell'installer) al posto di quello del sito.

## Il launcher DockStudio

Il launcher (`src/DockStudio.Launcher`, exe `DockStudio.exe`) si pubblica come le app, con il tag
`dockstudio-vX.Y.Z` e il workflow `release-dockstudio.yml`: i file finiscono nella cartella `dockstudio/` del bucket
e li serve `https://getdockstudio.it/dockstudio/...`.

L'elenco delle app lo legge da `https://getdockstudio.it/apps.json`, generato da `site/build.mjs` con i dati di
`site/data/apps.mjs`. Quando un'app passa a "disponibile", basta pubblicare il sito: il launcher la propone da
installare senza una sua nuova versione. La copia di riserva inclusa nel launcher
(`src/DockStudio.Launcher/Catalog/apps.json`) si ricopia da `site/dist/apps.json` prima di una sua release.

Le app le installa e le aggiorna scaricando l'installer dal loro sito (impronta SHA-256 controllata) e avviandolo
con `/VERYSILENT`. Un'app aperta non si aggiorna: DockPDF dalla 0.1.1 si chiude da sola con `--exit`, le altre
vanno chiuse a mano. Prove in locale: `DOCKSTUDIO_CATALOG` (un apps.json sul disco) e le variabili
`<APP>_UPDATE_FEED` delle singole app.
