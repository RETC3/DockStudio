// Pagine riservate delle statistiche di getdockstudio.it: riepilogo di tutte le app (/statistiche) e
// dettaglio di un'app (/statistiche/<app>). Solo numeri anonimi e punti del codice ripuliti
// (dati in web/lib/stats.js). Le pagine sono generate a ogni richiesta e non finiscono in cache.

const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const num = n => Number(n || 0).toLocaleString('it-IT');
const STATI = { disponibile: ['ok', 'Disponibile'], sviluppo: ['dev', 'In sviluppo'], arrivo: ['soon', 'In arrivo'] };

export function headers(type = 'text/html; charset=utf-8') {
  return {
    'Content-Type': type,
    'Cache-Control': 'no-store',
    'X-Robots-Tag': 'noindex, nofollow',
    'X-Frame-Options': 'DENY',
  };
}

/** Gli ultimi 30 giorni (UTC), dal più vecchio a oggi, con 0 dove non ci sono dati. */
function last30(rows, now) {
  const byDay = new Map(rows.map(r => [r.day, Number(r.n)]));
  const days = [];
  for (let i = 29; i >= 0; i--) {
    const d = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - i));
    const key = d.toISOString().slice(0, 10);
    days.push({ day: key, n: byDay.get(key) || 0 });
  }
  return days;
}

const shortDate = day => new Date(day + 'T00:00:00Z').toLocaleDateString('it-IT', { day: 'numeric', month: 'short', timeZone: 'UTC' });

function chart(days, label, unit = '') {
  const max = Math.max(1, ...days.map(d => d.n));
  const w = 600, h = 140, gap = 4, bw = (w - gap * (days.length - 1)) / days.length;
  const bars = days.map((d, i) => {
    const bh = d.n ? Math.max(3, (d.n / max) * (h - 18)) : 0;
    const x = i * (bw + gap), y = h - bh;
    return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${bw.toFixed(1)}" height="${bh.toFixed(1)}" rx="2"><title>${esc(shortDate(d.day))}: ${num(d.n)}${unit}</title></rect>`;
  }).join('');
  return `<figure class="st-chart">
      <figcaption>${esc(label)} <span>· ${days.some(d => d.n) ? `massimo ${num(max)}${unit} al giorno` : 'nessun dato'}</span></figcaption>
      <svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="${esc(label)}">${bars}</svg>
      <div class="st-axis"><span>${esc(shortDate(days[0].day))}</span><span>oggi</span></div>
    </figure>`;
}

function table(head, rows, empty = 'Nessun dato.') {
  if (!rows.length) return `<p class="st-empty">${esc(empty)}</p>`;
  return `<div class="st-table"><table><thead><tr>${head.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${
    rows.map(r => `<tr>${r.map((c, i) => `<td${i ? ' class="n"' : ''}>${c}</td>`).join('')}</tr>`).join('')
  }</tbody></table></div>`;
}

function compareVersions(a, b) {
  const pa = String(a).split('.').map(Number), pb = String(b).split('.').map(Number);
  for (let i = 0; i < 3; i++) if (pa[i] !== pb[i]) return (pb[i] || 0) - (pa[i] || 0);
  return 0;
}

const card = (value, label, main = false) => `<div class="st-card${main ? ' main' : ''}"><b>${value}</b><span>${esc(label)}</span></div>`;

function icon(app) {
  if (!app) return '<span class="st-icon">?</span>';
  return app.logo
    ? `<span class="st-icon logo"><img src="${esc(app.logo)}" alt="" style="width:100%;height:100%"></span>`
    : `<span class="st-icon" style="--c:${esc(app.colore)}">${esc(app.icona)}</span>`;
}

function page(title, crumbs, body, now) {
  const updated = now.toLocaleString('it-IT', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Rome' });
  return `<!doctype html>
<html lang="it">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>${esc(title)} · DockStudio</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="/style.css">
<script>try { const t = localStorage.getItem("theme"); if (t) document.documentElement.dataset.theme = t; } catch (e) {}</script>
<style>
  .st { padding: 36px 0 80px; }
  .st h1 { font: 640 clamp(30px, 4.6vw, 52px)/1.05 var(--display); letter-spacing: -.035em; margin: 18px 0 8px; }
  .st h2 { font: 640 24px/1.2 var(--display); letter-spacing: -.02em; margin: 48px 0 14px; }
  .st .sub { color: var(--muted); margin: 0 0 8px; }
  .st-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
  .st-card { background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); padding: 16px 18px; }
  .st-card b { display: block; font: 640 30px/1.1 var(--display); letter-spacing: -.02em; }
  .st-card span { color: var(--muted); font-size: 14px; }
  .st-card.main b { color: var(--accent); }
  .st-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
  .st-chart { margin: 16px 0 0; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); padding: 16px 18px; }
  .st-chart figcaption { font-size: 14.5px; margin-bottom: 10px; } .st-chart figcaption span { color: var(--muted); }
  .st-chart svg { width: 100%; height: 140px; display: block; } .st-chart rect { fill: var(--accent); }
  .st-axis { display: flex; justify-content: space-between; color: var(--muted); font-size: 12.5px; margin-top: 6px; }
  .st-table { overflow-x: auto; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); }
  .st table { width: 100%; border-collapse: collapse; font-size: 15px; }
  .st th, .st td { padding: 10px 14px; text-align: left; border-bottom: 1px solid var(--line); vertical-align: top; }
  .st th { color: var(--muted); font: 500 11px/1.4 var(--mono); letter-spacing: .12em; text-transform: uppercase; }
  .st tr:last-child td { border-bottom: 0; }
  .st td.n, .st th:not(:first-child) { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
  .st-empty { color: var(--muted); }
  .st-app { display: flex; align-items: center; gap: 10px; text-decoration: none; font-weight: 600; }
  .st-icon { width: 28px; height: 28px; border-radius: 8px; display: grid; place-items: center; flex: none; color: #fff;
    font: 800 12px/1 var(--display); background: var(--c, var(--muted)); }
  .st-icon.logo { background: none; }
  .st h1 { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
  .st-icon.big { width: 48px; height: 48px; border-radius: 12px; font-size: 20px; }
  .st details summary { cursor: pointer; word-break: break-word; }
  .st details pre { white-space: pre-wrap; word-break: break-all; color: var(--muted); font: 12.5px/1.5 var(--mono); margin: 8px 0 0; }
  .st .flag { font: 600 10.5px/1 var(--mono); letter-spacing: .1em; text-transform: uppercase; color: var(--warn, #B45309);
    background: var(--warn-soft, rgba(245,158,11,.14)); border-radius: 6px; padding: 3px 6px; margin-left: 6px; white-space: nowrap; }
  .st .note { color: var(--muted); font-size: 14px; margin-top: 48px; border-top: 1px solid var(--line); padding-top: 16px; }
  .st .apps-nav { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 18px; }
  .st .apps-nav a { display: inline-flex; align-items: center; gap: 8px; padding: 5px 12px 5px 5px; border: 1px solid var(--line);
    border-radius: 999px; text-decoration: none; font-size: 14px; background: var(--surface); }
  .st .apps-nav a[aria-current] { border-color: var(--accent); }
  .st .apps-nav .st-icon { width: 22px; height: 22px; border-radius: 6px; font-size: 9px; }
  @media (max-width: 760px) { .st-grid { grid-template-columns: 1fr; } }
</style>
</head>
<body>
<header class="top">
  <div class="wrap">
    <a class="brand" href="/"><svg viewBox="0 0 64 64"><rect x="6" y="8" width="12" height="48" rx="3" fill="currentColor"/><path d="M22 8h12a22 22 0 0 1 21.2 16H22z" fill="currentColor"/><rect x="22" y="28" width="35" height="8" rx="2" fill="currentColor"/><path d="M22 40h33.2A22 22 0 0 1 34 56H22z" fill="currentColor"/></svg>DockStudio</a>
    <nav class="nav"><a href="/statistiche"${title === 'Statistiche' ? ' aria-current="page"' : ''}>Statistiche</a><a href="/">Sito</a></nav>
  </div>
</header>
<main class="st wrap">
  <nav class="crumbs mono" aria-label="Percorso">${crumbs}</nav>
  ${body}
  <p class="note">Aggiornate al ${esc(updated)} (date in UTC). Solo numeri anonimi: nessun indirizzo IP, nessun dato personale.
    Un'installazione è "attiva" se l'app ha inviato le statistiche in quel periodo, al massimo una volta al giorno e solo
    se l'utente le ha scelte (fino alla 0.5.0 di ScreenLove erano attive finché non si spegnevano). Il tempo di utilizzo conta
    i minuti con una finestra dell'app in primo piano. I download contano i file scaricati per intero, dal sito o dall'aggiornamento automatico.</p>
</main>
</body>
</html>`;
}

/** Riepilogo: una riga per ogni app della suite, più quelle con dati ma non in elenco. */
export function renderOverview(apps, overview, now = new Date()) {
  const empty = { installs: { d7: 0, d30: 0, ever: 0 }, downloads: { all_time: 0, d30: 0 }, usage: { minutes: 0, reports: 0 }, errors: { n: 0, kinds: 0, fatal: 0 } };
  const known = apps.map(a => ({ meta: a, slug: a.slug, data: overview.get(a.slug) || empty }));
  const unknown = [...overview.keys()].filter(k => !apps.some(a => a.slug === k)).map(k => ({ meta: null, slug: k, data: overview.get(k) }));
  const rows = [...known, ...unknown];

  const sum = f => rows.reduce((t, r) => t + Number(f(r.data) || 0), 0);
  const minutes = sum(d => d.usage.minutes), reports = sum(d => d.usage.reports);

  const tableRows = rows.map(({ meta, slug, data: d }) => {
    const [cls, label] = meta ? STATI[meta.stato] || ['', meta.stato] : ['', 'Sconosciuta'];
    const avg = d.usage.reports ? `${num(Math.round(d.usage.minutes / d.usage.reports))} min` : '—';
    const hasData = d.installs.ever || d.downloads.all_time || d.errors.n;
    return [
      `<a class="st-app" href="/statistiche/${esc(slug)}">${icon(meta)}${esc(meta ? meta.nome : slug)}</a> <span class="tag ${cls}">${esc(label)}</span>`,
      hasData ? num(d.installs.d7) : '—',
      hasData ? num(d.installs.d30) : '—',
      hasData ? num(d.downloads.d30) : '—',
      hasData ? num(d.downloads.all_time) : '—',
      avg,
      d.errors.n ? `${num(d.errors.n)}${d.errors.fatal ? '<span class="flag">chiusure</span>' : ''}` : (hasData ? '0' : '—'),
    ];
  });

  const body = `
  <h1>Statistiche delle app.</h1>
  <p class="sub">Tutte le app DockStudio in un posto solo. Clicca un'app per i dettagli.</p>
  <div class="st-cards">
    ${card(num(sum(d => d.installs.d7)), 'installazioni attive, ultimi 7 giorni', true)}
    ${card(num(sum(d => d.installs.d30)), 'installazioni attive, ultimi 30 giorni')}
    ${card(num(sum(d => d.downloads.d30)), 'download, ultimi 30 giorni')}
    ${card(reports ? `${num(Math.round(minutes / reports))} min` : '—', 'tempo medio per invio, 30 giorni')}
    ${card(num(sum(d => d.errors.n)), 'errori segnalati, 30 giorni')}
  </div>
  <h2>Per app</h2>
  ${table(['App', 'Attive 7 g', 'Attive 30 g', 'Download 30 g', 'Download totali', 'Tempo medio', 'Errori 30 g'], tableRows)}`;

  return page('Statistiche', `<span aria-current="page">Statistiche</span>`, body, now);
}

/** Dettaglio di un'app: installazioni, tempo di utilizzo, errori, download. */
export function renderApp(apps, meta, stats, now = new Date()) {
  const name = meta ? meta.nome : stats.app;
  const dl = stats.downloads, inst = stats.installs, t = dl.total, a = inst.active, u = stats.usage;

  const versions = new Map();
  for (const r of inst.byVersion) {
    const v = versions.get(r.version) || { setup: 0, portable: 0 };
    v[r.kind] = (v[r.kind] || 0) + Number(r.n);
    versions.set(r.version, v);
  }
  const versionRows = [...versions.entries()].sort((x, y) => compareVersions(x[0], y[0]))
    .map(([v, c]) => [esc(v), num(c.setup + c.portable), num(c.setup), num(c.portable)]);
  const archRows = inst.byArch.map(r => [esc(r.arch), num(r.n)]);
  const fileRows = dl.byFile.map(r => [esc(r.file), num(r.n), num(r.web), num(r.app)]);

  const avg = u.reports ? Math.round(Number(u.minutes) / Number(u.reports)) : 0;
  const errorRows = stats.errors.map(r => [
    `<details><summary>${esc(r.type)}${r.fatal ? '<span class="flag">ha chiuso l\'app</span>' : ''}</summary><pre>${esc(r.stack || '(nessun punto del codice)')}</pre></details>`,
    num(r.n),
    esc(String(r.versions || '').split(',').sort(compareVersions).join(', ')),
    esc(shortDate(r.last)),
  ]);

  const nav = apps.map(x => `<a href="/statistiche/${esc(x.slug)}"${x.slug === stats.app ? ' aria-current="page"' : ''}>${icon(x)}${esc(x.nome)}</a>`).join('');
  const noData = !Number(a.ever) && !Number(t.all_time) && !stats.errors.length;

  const body = `
  <h1>${icon(meta).replace('st-icon', 'st-icon big')} ${esc(name)}</h1>
  <p class="sub">${noData ? "Nessun dato per ora: l'app non ha ancora inviato statistiche né è stata scaricata." : 'Statistiche anonime di questa app.'}</p>
  <div class="apps-nav">${nav}</div>

  <h2>Installazioni attive</h2>
  <div class="st-cards">
    ${card(num(a.d7), 'ultimi 7 giorni', true)}
    ${card(num(a.d30), 'ultimi 30 giorni')}
    ${card(num(a.today), 'oggi')}
    ${card(num(a.ever), 'viste in totale')}
  </div>
  ${chart(last30(inst.daily, now), 'App attive al giorno, ultimi 30 giorni')}
  <div class="st-grid">
    <div><h2>Per versione</h2>${table(['Versione', 'Totale', 'Installate', 'Portatili'], versionRows)}</div>
    <div><h2>Per architettura</h2>${table(['Architettura', 'Installazioni'], archRows)}</div>
  </div>

  <h2>Tempo di utilizzo</h2>
  <div class="st-cards">
    ${card(`${num(avg)} min`, 'in media per invio, ultimi 30 giorni', true)}
    ${card(`${num(Math.round(Number(u.minutes) / 60))} h`, 'in totale, ultimi 30 giorni')}
    ${card(num(u.installs), 'installazioni che lo inviano')}
  </div>
  ${chart(last30(u.daily || [], now), 'Minuti di utilizzo al giorno, ultimi 30 giorni', ' min')}

  <h2>Errori segnalati</h2>
  <p class="sub">Ultimi 30 giorni, raggruppati per tipo e punto del codice. Clicca un errore per vedere dove è successo.</p>
  ${table(['Errore', 'Volte', 'Versioni', 'Ultima'], errorRows, 'Nessun errore segnalato negli ultimi 30 giorni.')}

  <h2>Download</h2>
  <div class="st-cards">
    ${card(num(t.all_time), 'in totale', true)}
    ${card(num(t.d30), 'ultimi 30 giorni')}
    ${card(num(t.d7), 'ultimi 7 giorni')}
    ${card(num(t.today), 'oggi')}
  </div>
  ${chart(last30(dl.daily, now), 'Download al giorno, ultimi 30 giorni')}
  <h2>Per file</h2>
  ${table(['File', 'Totale', 'Dal sito', 'Aggiornamenti app'], fileRows)}`;

  return page(`Statistiche di ${name}`,
    `<a href="/statistiche">Statistiche</a><i>/</i><span aria-current="page">${esc(name)}</span>`, body, now);
}
