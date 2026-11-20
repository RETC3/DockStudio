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
