# Sito e download su Cloudflare

Il sito **https://screenlove.it** (Cloudflare Pages, progetto `screenlove`; `www.screenlove.it` e `screenlove.pages.dev` rimandano lì) mostra ScreenLove e permette di scaricarlo:
- **la pagina:** `web/public/index.html`;
- **i download:** passano da `/download/...` (`web/functions/download/[[path]].js`);
- **i file** stanno in un bucket **Cloudflare R2** (`screenlove-downloads`). Se R2 non è attivo, il sito
  inoltra i file della release su GitHub, e chi scarica vede comunque solo il sito;
- **la pagina delle novità** (`/novita`, `web/functions/novita.js`) legge versioni e novità in arrivo da
  `web/lib/novita.js`. Le stesse voci diventano le note della release su GitHub;
- **l'anteprima per i social** (`web/public/og-image.png`, 1200x630) e **le icone** (`favicon.ico`,
  `apple-touch-icon.png`, `icon-192/512.png`, `icon-maskable-512.png`, elencate in `site.webmanifest`)
  nascono da `web/og/og-image.html` e `web/og/icon.html`: dopo una modifica si rigenerano con
  `scripts/make-site-images.ps1` (usa Edge). `favicon.svg` va tenuto uguale a `icon.html`;
- **`latest.json`** descrive l'ultima versione. Lo generano i workflow con `scripts/make-latest-json.ps1`.
  L'app lo legge per gli aggiornamenti (con GitHub come riserva) e controlla l'impronta SHA-256 dell'installer.

Tutto viene pubblicato dai workflow di GitHub:
- **`Release`** (con un tag `vX.Y.Z`) carica i file su R2, crea la release e aggiorna il sito;
- **`Sito`** ripubblica la pagina quando cambia qualcosa in `web/`.

## Configurazione (una volta sola)

### 1. Attiva R2 (consigliato)

1. Pannello Cloudflare → **R2 Object Storage**.
2. Clic su **Purchase R2 Plan / Attiva** e scegli il piano **gratuito**: 10 GB di spazio e download senza
   costi di traffico. Cloudflare chiede un metodo di pagamento anche per il piano gratuito, ma non addebita
   nulla entro i limiti.
3. Il bucket non va creato: lo crea il workflow **Sito**. Il workflow carica anche i file dell'ultima release
   che mancano, quindi basta rilanciarlo (Actions → Sito → Run workflow) dopo l'attivazione.

Se preferisci non attivare R2, salta questo passo: il sito funziona lo stesso, con i file presi dalla release
su GitHub.

### 2. Copia l'Account ID

Nel pannello Cloudflare, pagina **Panoramica dell'account** (o qualsiasi pagina di Workers & Pages): nella
colonna di destra c'è **Account ID**. Copialo.

### 3. Crea il token API

1. In alto a destra: icona del profilo → **My Profile** → **API Tokens** → **Create Token**.
2. In fondo, **Custom token** → **Get started**.
3. Nome: `ScreenLove GitHub`.
4. Permissions, due righe:
   - **Account** · **Cloudflare Pages** · **Edit**
   - **Account** · **Workers R2 Storage** · **Edit**
5. Account Resources: **Include** · il tuo account.
6. **Continue to summary** → **Create Token** e copia il token. Viene mostrato una volta sola.

### 4. Salva i due valori su GitHub

Repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:

| Name | Secret |
|---|---|
| `CLOUDFLARE_ACCOUNT_ID` | l'Account ID del passo 2 |
| `CLOUDFLARE_API_TOKEN` | il token del passo 3 |

I valori restano cifrati su GitHub e nessuno li può rileggere, nemmeno tu.

### 5. Prima pubblicazione

- **Il sito:** scheda **Actions** → **Sito** → **Run workflow**. Il workflow crea il progetto Pages
  `screenlove` e pubblica la pagina.
- **I file su R2** arrivano con la prossima release (`git tag -a v0.1.1 -m "Novità…"` e push del tag).
  Fino ad allora i download del sito passano dalla release su GitHub.

## Statistiche anonime (tutte le app)

Solo numeri anonimi, e dalle app solo quello che l'utente ha scelto (vedi https://screenlove.it/privacy):
- **download:** `web/functions/download/[[path]].js` conta ogni file scaricato per intero, per giorno, file e
  provenienza (sito o aggiornamento dell'app);
- **installazioni attive e tempo di utilizzo:** l'app, insieme al controllo automatico degli aggiornamenti
  (al massimo una volta al giorno), invia a `POST /api/ping` il nome dell'app, un codice casuale, la versione,
  l'architettura, se è installata o portatile e i minuti di utilizzo (`DockStudio.Core/Updates/UsageStats.cs`);
- **errori:** `POST /api/errore` con tipo e punti del codice ripuliti (`DockStudio.Core/Updates/ErrorReports.cs`).

ScreenLove manda tutto a **screenlove.it** (le versioni installate lo fanno da sempre); le nuove app a
**getdockstudio.it** (`AppIdentity.StatsUrl = AppIdentity.SuiteStatsUrl`), che accetta solo le app di
`site/data/apps.mjs`. I due siti scrivono nello **stesso database D1** `screenlove-stats` (giurisdizione UE,
collegato come `STATS` in `web/wrangler.toml` e `site/wrangler.toml`). Ogni riga ha la colonna `app`; le
tabelle e la colonna le crea il sito alla prima richiesta (`web/lib/stats.js`; le righe nate prima della
colonna sono di ScreenLove). Ping e minuti si cancellano dopo 400 giorni, gli errori dopo 180.

Le pagine **https://getdockstudio.it/statistiche** (riepilogo di tutte le app) e
**/statistiche/&lt;app&gt;** (dettaglio) mostrano i numeri; https://screenlove.it/statistiche rimanda a
quella di ScreenLove. Sono protette da **Cloudflare Access** (Zero Trust, piano gratuito): un'unica
applicazione self-hosted con gli indirizzi `screenlove.it/statistiche` e `getdockstudio.it/statistiche`,
con un criterio che ammette solo il tuo indirizzo email. Metodo di accesso: **One-time PIN**
(Integrations → Identity providers), cioè un codice via email; il team è `frosty-sun-e1a2`. Le funzioni
controllano anche la firma del token di Access (`web/lib/access.js`): in entrambi i `wrangler.toml` vanno
`ACCESS_TEAM_DOMAIN` e `ACCESS_AUD` (Application Audience tag dell'applicazione). Senza questi valori, o senza
Access davanti, le pagine rispondono 403.

In locale le pagine sono aperte: `.dev.vars` con `STATS_DEV_OPEN = "1"` (file non versionato).

## Google Search Console

La proprietà di tipo **Dominio** `screenlove.it` è verificata con un record DNS **TXT** su Cloudflare
(`google-site-verification=...`, nome `@`): **non cancellarlo**, altrimenti Google toglie la verifica.
La sitemap `https://screenlove.it/sitemap.xml` è già inviata; se aggiungi pagine, aggiungile anche lì.

## Controlli

- `https://screenlove.it/latest.json`: ultima versione, dimensioni e impronte.
- `https://screenlove.it/download/latest`: scarica l'installer più recente.
- In locale: `cd web`, poi `npx wrangler@4 pages dev` e apri http://localhost:8788. Serve
  `web/public/latest.json`, che si crea con `scripts/make-latest-json.ps1`.
