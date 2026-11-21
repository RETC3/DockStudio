// Genera il sito DockStudio in site/dist: copia site/public e crea la home, /novita/, /<app>/ e /<app>/novita/
// dai dati di site/data/apps.mjs (le novità di ScreenLove vengono da web/lib/novita.js).
//   node site/build.mjs
// Nessuna dipendenza: basta Node 22 o successivo.
import { createHash } from 'node:crypto';
import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { apps, suite } from './data/apps.mjs';

const ROOT = dirname(fileURLToPath(import.meta.url));
const DIST = join(ROOT, 'dist');
const SITE = 'https://getdockstudio.it';

// Anteprima dei link (WhatsApp, Telegram, LinkedIn, Facebook, X...): site/public/og-image.png, rifatta con
// scripts/make-dockstudio-og.ps1. Le pagine di un'app con copertina usano la copertina dell'app.
const OG_IMAGE = { src: '/og-image.png', larghezza: 1200, altezza: 630,
  alt: 'DockStudio: tutto il tuo lavoro resta tuo. Una suite di app che funzionano sul tuo computer.' };

function social({ titolo, descrizione, percorso, immagine = OG_IMAGE }) {
  return `<meta property="og:type" content="website">
  <meta property="og:site_name" content="DockStudio">
  <meta property="og:locale" content="it_IT">
  <meta property="og:title" content="${esc(titolo)}">
  <meta property="og:description" content="${esc(descrizione)}">
  <meta property="og:url" content="${SITE}${percorso}">
  <meta property="og:image" content="${SITE}${immagine.src}">
  <meta property="og:image:width" content="${immagine.larghezza}">
  <meta property="og:image:height" content="${immagine.altezza}">
  <meta property="og:image:alt" content="${esc(immagine.alt)}">
  <meta name="twitter:card" content="summary_large_image">`;
}

const MESI = ['gennaio', 'febbraio', 'marzo', 'aprile', 'maggio', 'giugno', 'luglio', 'agosto',
  'settembre', 'ottobre', 'novembre', 'dicembre'];
const TIPI = { nuovo: 'Nuovo', migliorato: 'Migliorato', corretto: 'Corretto' };
const STATI = { sviluppo: 'In sviluppo', programma: 'In programma' };
const STATO_APP = { disponibile: ['ok', 'Disponibile'], sviluppo: ['dev', 'In sviluppo'], arrivo: ['soon', 'In arrivo'] };

// ---------- testo ----------
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const testo = (s) => esc(s).replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/`(.+?)`/g, '<kbd>$1</kbd>');
const piano = (s) => String(s).replace(/\*\*(.+?)\*\*/g, '$1').replace(/`(.+?)`/g, '$1');
function data(iso) {
  const [y, m, d] = iso.split('-').map(Number);
  return `${d} ${MESI[m - 1]} ${y}`;
}
const conNovita = (a) => a.novita && (a.novita.versioni?.length || a.novita.inArrivo?.length);

// ---------- pezzi comuni ----------
const SPRITE = `<svg width="0" height="0" style="position:absolute" aria-hidden="true">
  <symbol id="logo" viewBox="0 0 64 64">
    <rect x="6" y="8" width="12" height="48" rx="3" fill="currentColor"/>
    <path d="M22 8h12a22 22 0 0 1 21.2 16H22z" fill="currentColor"/>
    <rect x="22" y="28" width="35" height="8" rx="2" fill="currentColor"/>
    <path d="M22 40h33.2A22 22 0 0 1 34 56H22z" fill="currentColor"/>
  </symbol>
  <symbol id="i-download" viewBox="0 0 24 24"><path d="M12 3v12m0 0-5-5m5 5 5-5M4 19h16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></symbol>
  <symbol id="i-grid" viewBox="0 0 24 24"><g fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="6" height="6" rx="1.5"/><rect x="14" y="4" width="6" height="6" rx="1.5"/><rect x="4" y="14" width="6" height="6" rx="1.5"/><rect x="14" y="14" width="6" height="6" rx="1.5"/></g></symbol>
  <symbol id="i-moon" viewBox="0 0 24 24"><path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/></symbol>
  <symbol id="i-sun" viewBox="0 0 24 24"><g fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></g></symbol>
  <symbol id="i-check" viewBox="0 0 24 24"><path d="m5 12.5 4.5 4.5L19 7.5" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></symbol>
  <symbol id="i-mail" viewBox="0 0 24 24"><g fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m4 7 8 6 8-6"/></g></symbol>
  <symbol id="i-arrow" viewBox="0 0 24 24"><path d="M5 12h14m0 0-6-6m6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></symbol>
</svg>`;

function header(attiva) {
  const voce = (href, nome, chiave) => `<a href="${href}"${attiva === chiave ? ' aria-current="page"' : ''}>${nome}</a>`;
  return `<header class="top">
  <div class="wrap">
    <a class="brand" href="/" aria-label="DockStudio, home"><svg><use href="#logo"/></svg>DockStudio</a>
    <nav class="nav" aria-label="Sezioni">
      ${voce('/#app', 'App', 'app')}
      ${voce('/#principi', 'Principi')}
      ${voce('/#aziende', 'Aziende')}
      ${voce('/novita/', 'Novità', 'novita')}
    </nav>
    <div class="top-actions">
      <button class="icon-btn" id="theme" type="button" aria-label="Tema chiaro o scuro">
        <svg class="moon"><use href="#i-moon"/></svg><svg class="sun" hidden><use href="#i-sun"/></svg>
      </button>
      <a class="btn btn-dark" href="https://screenlove.it/#scarica"><svg><use href="#i-download"/></svg><span>Scarica</span></a>
    </div>
  </div>
</header>`;
}

const FOOTER = `<footer>
  <div class="wrap">
    <a class="brand" href="/"><svg><use href="#logo"/></svg>DockStudio</a>
    <nav class="mono">
      <a href="/screenlove/">ScreenLove</a>
      <a href="/novita/">Novità</a>
      <a href="/privacy">Privacy</a>
      <a href="mailto:info@getdockstudio.it">info@getdockstudio.it</a>
    </nav>
    <span class="mono">© 2026 DockStudio</span>
  </div>
</footer>`;

function pagina({ titolo, descrizione, percorso, attiva, corpo, immagine }) {
  return `<!doctype html>
<html lang="it">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${esc(titolo)}</title>
  <meta name="description" content="${esc(descrizione)}">
  <link rel="canonical" href="${SITE}${percorso}">
  ${social({ titolo, descrizione, percorso, immagine })}
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/style.css">
  <script>
    // Tema scelto dall'utente (chiaro/scuro), altrimenti quello del sistema.
    try { const t = localStorage.getItem("theme"); if (t) document.documentElement.dataset.theme = t; } catch (e) {}
  </script>
</head>
<body>

${SPRITE}

${header(attiva)}

<main>
${corpo}
</main>

${FOOTER}

<script src="/site.js" defer></script>
</body>
</html>
`;
}

const icona = (a, cls = 'app-icon') => (a.logo
  ? `<span class="${cls} has-logo"><img src="${a.logo}" alt="" style="width:100%;height:100%"></span>`
  : `<span class="${cls}" style="--c:${a.colore}">${esc(a.icona)}</span>`);
const tagStato = (a) => { const [c, t] = STATO_APP[a.stato]; return `<span class="tag ${c}">${t}</span>`; };
const briciole = (...voci) => `<nav class="crumbs mono" aria-label="Percorso">${voci
  .map(([href, nome]) => (href ? `<a href="${href}">${esc(nome)}</a>` : `<span aria-current="page">${esc(nome)}</span>`))
  .join('<i>/</i>')}</nav>`;

function vociInArrivo(lista) {
  return `<ul class="todo">${lista.map((n) => `
        <li class="${esc(n.stato)}">
          <span class="state mono">${esc(STATI[n.stato] ?? n.stato)}</span>
          <div><h4>${testo(n.titolo)}</h4><p>${testo(n.testo)}</p></div>
        </li>`).join('')}
      </ul>`;
}

function versione(v, { voci = v.voci, app } = {}) {
  return `<article class="ver" id="v${esc(v.versione)}">
          <div class="ver-head">
            ${app ? `<a class="ver-app" href="/${app.slug}/novita/">${icona(app, 'app-icon sm')}${esc(app.nome)}</a>` : ''}
            <span class="ver-num mono">Versione ${esc(v.versione)}</span>
            <time class="mono" datetime="${esc(v.data)}">${data(v.data)}</time>
          </div>
          <h3>${testo(v.titolo)}</h3>
          <ul class="changes">${voci.map(([tipo, t]) => `
            <li><span class="kind ${esc(tipo)} mono">${esc(TIPI[tipo] ?? tipo)}</span><span>${testo(t)}</span></li>`).join('')}
          </ul>
        </article>`;
}

// ---------- home ----------
function cardApp(a) {
  const tags = [tagStato(a), ...a.piattaforme.map((p) => `<span class="tag">${esc(p.replace(' 10 e 11', ''))}</span>`)].join('');
  return `
        <a class="app" href="/${a.slug}/" style="--c:${a.colore}">
          ${a.copertina ? `<span class="app-shot"><img src="${a.copertina.src}" alt="" width="${a.copertina.larghezza}" height="${a.copertina.altezza}" loading="lazy" decoding="async"></span>` : ''}
          <div class="app-head">${icona(a)}<span class="app-num mono">${a.num}</span></div>
          <span class="app-cat mono">${esc(a.categoria)}</span>
          <h3>${esc(a.nome)}</h3>
          <p>${testo(a.breve)}</p>
          <div class="app-foot">${tags}</div>
          <span class="go" aria-hidden="true">Vai a ${esc(a.nome)} →</span>
        </a>`;
}

function home() {
  const tpl = readFileSync(join(ROOT, 'src', 'index.html'), 'utf8');
  if (!tpl.includes('<!--APPS-->')) throw new Error('site/src/index.html: manca il segnaposto <!--APPS-->');
  return tpl.replace('<!--APPS-->', apps.map(cardApp).join('\n'));
}

// ---------- /<app>/ ----------
function paginaApp(a, i) {
  const prima = apps[(i - 1 + apps.length) % apps.length];
  const dopo = apps[(i + 1) % apps.length];
  const uscita = a.stato === 'disponibile';
  const nov = conNovita(a);
  const ultima = nov && a.novita.versioni?.[0];

  // App che si scaricano da getdockstudio.it: il pulsante porta alla sezione "Scarica" della pagina.
  const scarica = a.scaricabile ? '#scarica' : a.scarica;
  const azioni = uscita || a.scaricabile
    ? `<a class="btn btn-primary" href="${scarica}"><svg><use href="#i-download"/></svg>Scarica ${esc(a.nome)}</a>
        ${nov ? `<a class="btn btn-ghost" href="/${a.slug}/novita/">Novità</a>` : ''}
        ${a.sito ? `<a class="btn btn-ghost" href="${a.sito}">${esc(a.sito.replace('https://', ''))}</a>` : ''}`
    : `<a class="btn btn-primary" href="mailto:info@getdockstudio.it?subject=${encodeURIComponent(`Avvisatemi quando esce ${a.nome}`)}"><svg><use href="#i-mail"/></svg>Avvisami quando esce</a>
        ${nov ? `<a class="btn btn-ghost" href="/${a.slug}/novita/">Novità</a>` : ''}
        <a class="btn btn-ghost" href="/#app"><svg><use href="#i-grid"/></svg>Tutte le app</a>`;

  let n = 1;
  const kicker = (t) => `<div class="kicker mono"><b>${String(n++).padStart(2, '0')}</b> ${t}</div>`;

  const sezFunzioni = `
  <section class="block">
    <div class="wrap">
      ${kicker(uscita || a.stato === 'sviluppo' ? 'Cosa fa' : 'Cosa farà')}
      <h2>${uscita ? 'Tutto quello che serve, niente di più.' : a.stato === 'sviluppo' ? 'Quello che c\'è già.' : 'Le funzioni previste.'}</h2>
      ${uscita ? '' : a.stato === 'sviluppo'
        ? `<p class="intro">${esc(a.nome)} è in sviluppo: queste funzioni ci sono già nella prima versione.</p>`
        : `<p class="intro">${esc(a.nome)} non è ancora in sviluppo: questo è il progetto di partenza e può cambiare strada facendo.</p>`}
      <div class="features">${a.funzioni.map(([t, d], k) => `
        <div class="feature"><span class="n mono">${String(k + 1).padStart(2, '0')}</span><h3>${testo(t)}</h3><p>${testo(d)}</p></div>`).join('')}
      </div>
    </div>
  </section>`;

  // Versione, dimensione e link li mette site.js leggendo /<app>/latest.json: la pagina non va rigenerata a ogni uscita.
  const sezScarica = a.scaricabile ? `
  <section class="block" id="scarica">
    <div class="wrap">
      ${kicker('Scarica')}
      <h2>Scarica ${esc(a.nome)}.</h2>
      <div class="download card-soft" data-latest="/${a.slug}/latest.json" data-app="${esc(a.nome)}">
        <p class="download-wait">La prima versione di ${esc(a.nome)} è quasi pronta: questa sezione si riempie da sola quando esce.</p>
        <div class="download-ready" hidden>
          <span class="mono label download-version"></span>
          <div class="cta left">
            <a class="btn btn-primary download-setup" href="/${a.slug}/download/latest"><svg><use href="#i-download"/></svg>Installer per Windows</a>
          </div>
          <p class="download-note">Per Windows 10 e 11, computer x64 e ARM64. Si installa senza permessi di amministratore e si aggiorna da solo.</p>
          <ul class="download-files mono"></ul>
        </div>
        <noscript><p><a href="/${a.slug}/download/latest">Scarica l'ultima versione dell'installer</a></p></noscript>
      </div>
    </div>
  </section>` : '';

  const sezNovita = nov ? `
  <section class="block">
    <div class="wrap">
      ${kicker('Novità')}
      <h2>${ultima ? `Ultima versione: ${esc(ultima.versione)}.` : 'A cosa stiamo lavorando.'}</h2>
      <div class="split">
        ${ultima ? `<div>${versione(ultima)}</div>` : ''}
        ${a.novita.inArrivo?.length ? `<div class="card-soft">
          <span class="mono label">In arrivo</span>
          ${vociInArrivo(a.novita.inArrivo.slice(0, 4))}
        </div>` : ''}
      </div>
      <p class="more"><a class="btn btn-ghost" href="/${a.slug}/novita/">Tutte le novità di ${esc(a.nome)} <svg><use href="#i-arrow"/></svg></a></p>
    </div>
  </section>` : '';

  const cop = a.copertina;
  const corpo = `
  <section class="phero${cop ? ' has-cover' : ''}" style="--c:${a.colore}">
    <div class="wrap">
      ${briciole(['/', 'DockStudio'], ['/#app', 'App'], [null, a.nome])}
      ${cop ? '<div class="phero-grid"><div>' : ''}
      <div class="phero-id">${icona(a, 'app-icon xl')}<span class="mono">${a.num} · ${esc(a.categoria)}</span></div>
      <h1>${esc(a.nome)}</h1>
      <p class="lead">${testo(a.lead)}</p>
      <div class="app-foot">${tagStato(a)}${a.piattaforme.map((p) => `<span class="tag">${esc(p)}</span>`).join('')}</div>
      <div class="cta left">
        ${azioni}
      </div>
      ${cop ? `</div>
      <figure class="cover"><img src="${cop.src}" alt="${esc(cop.alt)}" width="${cop.larghezza}" height="${cop.altezza}"></figure>
      </div>` : ''}
    </div>
  </section>
${sezFunzioni}
${sezScarica}
${sezNovita}
  <nav class="pager wrap" aria-label="Altre app">
    <a href="/${prima.slug}/"><span class="mono">← Precedente</span>${icona(prima, 'app-icon sm')}<b>${esc(prima.nome)}</b></a>
    <a href="/${dopo.slug}/"><span class="mono">Successiva →</span>${icona(dopo, 'app-icon sm')}<b>${esc(dopo.nome)}</b></a>
  </nav>`;

  return pagina({
    titolo: `${a.nome} — ${a.categoria} · DockStudio`,
    descrizione: piano(a.lead),
    percorso: `/${a.slug}/`,
    attiva: 'app',
    immagine: a.copertina,
    corpo,
  });
}

// ---------- /<app>/novita/ ----------
function paginaNovitaApp(a) {
  const { versioni = [], inArrivo = [] } = a.novita;
  const corpo = `
  <section class="phero" style="--c:${a.colore}">
    <div class="wrap">
      ${briciole(['/', 'DockStudio'], [`/${a.slug}/`, a.nome], [null, 'Novità'])}
      <div class="phero-id">${icona(a, 'app-icon xl')}<span class="mono">${esc(a.nome)} · Novità</span></div>
      <h1>Novità di ${esc(a.nome)}.</h1>
      <p class="lead">Cosa stiamo preparando e cosa è cambiato in ogni versione.</p>
      <div class="cta left">
        ${a.scarica || a.scaricabile ? `<a class="btn btn-primary" href="${a.scaricabile ? `/${a.slug}/#scarica` : a.scarica}"><svg><use href="#i-download"/></svg>Scarica ${esc(a.nome)}</a>` : ''}
        <a class="btn btn-ghost" href="/${a.slug}/">Pagina di ${esc(a.nome)}</a>
      </div>
    </div>
  </section>
${inArrivo.length ? `
  <section class="block">
    <div class="wrap">
      <div class="kicker mono"><b>01</b> In arrivo</div>
      <h2>A cosa stiamo lavorando.</h2>
      ${vociInArrivo(inArrivo)}
    </div>
  </section>` : ''}
${versioni.length ? `
  <section class="block">
    <div class="wrap">
      <div class="kicker mono"><b>${inArrivo.length ? '02' : '01'}</b> Versioni</div>
      <h2>Cosa è cambiato.</h2>
      <div class="timeline">${versioni.map((v) => `
        ${versione(v)}`).join('')}
      </div>
    </div>
  </section>` : ''}`;

  return pagina({
    titolo: `Novità di ${a.nome} · DockStudio`,
    descrizione: `Le novità di ${a.nome}: le versioni uscite e quelle in arrivo.`,
    percorso: `/${a.slug}/novita/`,
    attiva: 'novita',
    immagine: a.copertina,
    corpo,
  });
}

// ---------- /novita/ ----------
function paginaNovita() {
  const inCorso = apps.filter((a) => a.novita?.inArrivo?.length);
  const ferme = apps.filter((a) => !conNovita(a));
  const uscite = apps
    .flatMap((a) => (a.novita?.versioni ?? []).map((v) => ({ a, v })))
    .sort((x, y) => y.v.data.localeCompare(x.v.data) || y.v.versione.localeCompare(x.v.versione, undefined, { numeric: true }))
    .slice(0, 4);

  const gruppo = (intest, lista, link) => `
        <div class="group">
          <div class="group-head">${intest}${link ?? ''}</div>
          ${vociInArrivo(lista)}
        </div>`;

  const corpo = `
  <section class="phero">
    <div class="wrap">
      ${briciole(['/', 'DockStudio'], [null, 'Novità'])}
      <h1>A cosa stiamo lavorando.</h1>
      <p class="lead">Le novità di tutte le app DockStudio in un posto solo: quello che è in sviluppo, quello che è in programma
        e le ultime versioni uscite.</p>
    </div>
  </section>

  <section class="block">
    <div class="wrap">
      <div class="kicker mono"><b>01</b> In corso</div>
      <h2>In sviluppo e in programma.</h2>
      <div class="groups">
        ${gruppo(`<span class="app-icon sm suite"><svg><use href="#logo"/></svg></span><b>Tutta la suite</b>`, suite.inArrivo)}
        ${inCorso.map((a) => gruppo(`${icona(a, 'app-icon sm')}<b>${esc(a.nome)}</b>`, a.novita.inArrivo,
          `<a class="mono" href="/${a.slug}/novita/">Tutte le novità →</a>`)).join('')}
      </div>
      ${ferme.length ? `<div class="later">
        <p><b>Le altre app sono in programma.</b> Le loro novità compariranno qui quando inizia lo sviluppo.</p>
        <div class="chips">${ferme.map((a) => `<a href="/${a.slug}/">${icona(a, 'app-icon xs')}${esc(a.nome)}</a>`).join('')}</div>
      </div>` : ''}
    </div>
  </section>
${uscite.length ? `
  <section class="block">
    <div class="wrap">
      <div class="kicker mono"><b>02</b> Ultime uscite</div>
      <h2>Le ultime versioni.</h2>
      <div class="timeline">${uscite.map(({ a, v }) => `
        ${versione(v, { app: a })}`).join('')}
      </div>
    </div>
  </section>` : ''}`;

  return pagina({
    titolo: 'Novità · DockStudio',
    descrizione: 'A cosa stiamo lavorando: le novità in sviluppo e in programma di tutte le app DockStudio e le ultime versioni uscite.',
    percorso: '/novita/',
    attiva: 'novita',
    corpo,
  });
}

// ---------- scrittura ----------
function scrivi(percorso, html) {
  const file = join(DIST, percorso, percorso.endsWith('.html') || percorso.endsWith('.xml') || percorso.endsWith('.txt') ? '' : 'index.html');
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, html);
}

rmSync(DIST, { recursive: true, force: true });
cpSync(join(ROOT, 'public'), DIST, { recursive: true });

const slugs = new Set();
for (const a of apps) {
  if (slugs.has(a.slug)) throw new Error(`slug ripetuto: ${a.slug}`);
  slugs.add(a.slug);
  if (!STATO_APP[a.stato]) throw new Error(`${a.slug}: stato sconosciuto "${a.stato}"`);
}

const pagine = ['/', '/novita/'];
scrivi('index.html', home());
scrivi('novita', paginaNovita());
apps.forEach((a, i) => {
  scrivi(a.slug, paginaApp(a, i));
  pagine.push(`/${a.slug}/`);
  if (conNovita(a)) {
    scrivi(`${a.slug}/novita`, paginaNovitaApp(a));
    pagine.push(`/${a.slug}/novita/`);
  }
});
pagine.push('/privacy');

scrivi('sitemap.xml', `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${pagine.map((p) => `  <url><loc>${SITE}${p}</loc></url>`).join('\n')}
</urlset>
`);
scrivi('robots.txt', `User-agent: *\nAllow: /\n\nSitemap: ${SITE}/sitemap.xml\n`);

// CSS e JS con la versione nell'indirizzo: Cloudflare li fa tenere in cache al browser per ore, e senza
// versione una pagina nuova potrebbe arrivare con il foglio di stile vecchio.
const impronta = (f) => createHash('sha256').update(readFileSync(join(DIST, f))).digest('hex').slice(0, 10);
const asset = { "/style.css": impronta("style.css"), "/site.js": impronta("site.js") };
(function versiona(dir) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) versiona(p);
    else if (e.name.endsWith('.html')) {
      let html = readFileSync(p, 'utf8');
      for (const [url, v] of Object.entries(asset)) html = html.replaceAll(`"${url}"`, `"${url}?v=${v}"`);
      writeFileSync(p, html);
    }
  }
})(DIST);

// Quali indirizzi passano dalle funzioni (site/functions): solo statistiche, API e versioni/download delle app.
// Senza questo file Cloudflare manderebbe ogni richiesta alla funzione /[app]/..., anche le pagine statiche.
const funzioni = ['/api/*', '/statistiche', '/statistiche/*'];
for (const a of apps) if (a.slug !== 'screenlove') funzioni.push(`/${a.slug}/latest.json`, `/${a.slug}/download/*`);
writeFileSync(join(DIST, '_routes.json'), JSON.stringify({ version: 1, include: funzioni, exclude: [] }, null, 2) + '\n');

console.log(`Sito generato in ${DIST}: ${pagine.length} pagine.`);
