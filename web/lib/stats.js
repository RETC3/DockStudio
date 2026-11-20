// Statistiche anonime delle app DockStudio, nel database D1 collegato come STATS (vedi docs/CLOUDFLARE.md).
// Un solo database per tutte le app: ogni riga ha la colonna "app" (es. "screenlove", "dockpdf").
//   downloads: quanti file sono stati scaricati, per giorno, file e provenienza (sito o app)
//   pings:     un'app installata che ha cercato aggiornamenti in un giorno (codice casuale, versione, architettura)
//   usage:     minuti di utilizzo inviati da quell'installazione in quel giorno (ScreenLove dalla 0.6.0)
//   errors:    errori segnalati, raggruppati per giorno, impronta e versione (tipo e punti del codice ripuliti)
// Ping ed errori arrivano solo dalle app in cui l'utente li ha scelti.
// Non si salvano indirizzi IP, user agent o altro: solo questi dati.
//
// Lo usano due siti: screenlove.it (web/, solo ScreenLove) e getdockstudio.it (site/, tutte le app e le
// pagine /statistiche).

/** App delle righe nate prima della colonna "app": tutte di ScreenLove. */
export const DEFAULT_APP = 'screenlove';

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS downloads (
     day TEXT NOT NULL, file TEXT NOT NULL, source TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0,
     app TEXT NOT NULL DEFAULT '${DEFAULT_APP}',
     PRIMARY KEY (day, file, source))`,
  `CREATE TABLE IF NOT EXISTS pings (
     day TEXT NOT NULL, id TEXT NOT NULL, version TEXT NOT NULL, arch TEXT NOT NULL, kind TEXT NOT NULL,
     app TEXT NOT NULL DEFAULT '${DEFAULT_APP}',
     PRIMARY KEY (day, id))`,
  `CREATE INDEX IF NOT EXISTS pings_id ON pings (id, day)`,
  `CREATE TABLE IF NOT EXISTS usage (
     day TEXT NOT NULL, id TEXT NOT NULL, minutes INTEGER NOT NULL DEFAULT 0,
     app TEXT NOT NULL DEFAULT '${DEFAULT_APP}',
     PRIMARY KEY (day, id))`,
  `CREATE TABLE IF NOT EXISTS errors (
     day TEXT NOT NULL, sig TEXT NOT NULL, version TEXT NOT NULL, arch TEXT NOT NULL, os TEXT NOT NULL,
     type TEXT NOT NULL, stack TEXT NOT NULL, fatal INTEGER NOT NULL DEFAULT 0, n INTEGER NOT NULL DEFAULT 0,
     app TEXT NOT NULL DEFAULT '${DEFAULT_APP}',
     PRIMARY KEY (day, sig, version))`,
];

const APP_TABLES = ['downloads', 'pings', 'usage', 'errors'];

const INDEXES = [
  `CREATE INDEX IF NOT EXISTS downloads_app ON downloads (app, day)`,
  `CREATE INDEX IF NOT EXISTS pings_app ON pings (app, day)`,
  `CREATE INDEX IF NOT EXISTS usage_app ON usage (app, day)`,
  `CREATE INDEX IF NOT EXISTS errors_app ON errors (app, day)`,
];

// Gli errori più vecchi di così vengono cancellati.
export const KEEP_ERROR_DAYS = 180;
export const MAX_PING_MINUTES = 7 * 24 * 60;

// I ping più vecchi di così vengono cancellati: per le installazioni attive bastano gli ultimi mesi.
export const KEEP_PING_DAYS = 400;

let schemaReady = null;

/**
 * Crea le tabelle la prima volta (una volta per istanza del worker) e aggiunge la colonna "app" alle tabelle
 * nate prima delle statistiche per più app: le righe già presenti diventano di ScreenLove.
 */
export function ensureSchema(db) {
  if (!schemaReady) {
    schemaReady = (async () => {
      await db.batch(SCHEMA.map(sql => db.prepare(sql)));
      for (const table of APP_TABLES) {
        const { results } = await db.prepare(`PRAGMA table_info(${table})`).all();
        if (!results.some(c => c.name === 'app')) {
          await db.prepare(`ALTER TABLE ${table} ADD COLUMN app TEXT NOT NULL DEFAULT '${DEFAULT_APP}'`).run();
        }
      }
      await db.batch(INDEXES.map(sql => db.prepare(sql)));
    })().catch(err => { schemaReady = null; throw err; });
  }
  return schemaReady;
}

/** Solo per le prove: dimentica che lo schema è pronto (nuovo database). */
export function resetSchemaCache() {
  schemaReady = null;
}

/** Giorno UTC in formato AAAA-MM-GG. */
export function today(now = new Date()) {
  return now.toISOString().slice(0, 10);
}

/** Conta un download. Non fa mai fallire il download: in caso di errore lo registra e basta. */
export async function countDownload(db, file, source, app = DEFAULT_APP) {
  if (!db) return;
  try {
    await ensureSchema(db);
    await db.prepare(
      `INSERT INTO downloads (day, file, source, n, app) VALUES (?1, ?2, ?3, 1, ?4)
       ON CONFLICT (day, file, source) DO UPDATE SET n = n + 1`).bind(today(), file, source, app).run();
  } catch (err) {
    console.error('statistiche: download non contato', err);
  }
}

const APP_NAME = /^[a-z][a-z0-9-]{1,31}$/;
const PING_ID = /^[0-9a-f]{32}$/;
const PING_VERSION = /^\d{1,4}\.\d{1,4}\.\d{1,6}$/;
const PING_ARCH = new Set(['x64', 'arm64', 'x86']);
const PING_KIND = new Set(['setup', 'portable']);

/** Nome dell'app nel corpo di un ping o di una segnalazione: facoltativo, minuscolo ("ScreenLove" → "screenlove"). */
function appFrom(body) {
  if (body.app === undefined) return null;
  if (typeof body.app !== 'string') return undefined;
  const app = body.app.toLowerCase();
  return APP_NAME.test(app) ? app : undefined;
}

/** Controlla il contenuto di un ping; restituisce i campi puliti oppure null. */
export function parsePing(body) {
  if (!body || typeof body !== 'object') return null;
  const { id, version, arch, kind, minutes = 0 } = body;
  const app = appFrom(body);
  if (app === undefined) return null;
  if (typeof id !== 'string' || !PING_ID.test(id)) return null;
  if (typeof version !== 'string' || !PING_VERSION.test(version)) return null;
  if (!PING_ARCH.has(arch) || !PING_KIND.has(kind)) return null;
  // minuti di utilizzo dall'ultimo invio: facoltativi (le versioni fino alla 0.5.0 non li mandano)
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > MAX_PING_MINUTES) return null;
  return { app, id, version, arch, kind, minutes };
}

/** Registra il ping di oggi (uno per installazione al giorno) e ogni tanto elimina i più vecchi. */
export async function savePing(db, ping, app = ping.app || DEFAULT_APP) {
  await ensureSchema(db);
  const stmts = [
    db.prepare(
      `INSERT INTO pings (day, id, version, arch, kind, app) VALUES (?1, ?2, ?3, ?4, ?5, ?6)
       ON CONFLICT (day, id) DO UPDATE SET version = excluded.version, arch = excluded.arch, kind = excluded.kind`)
      .bind(today(), ping.id, ping.version, ping.arch, ping.kind, app),
  ];
  if (ping.minutes > 0) {
    stmts.push(db.prepare(
      `INSERT INTO usage (day, id, minutes, app) VALUES (?1, ?2, ?3, ?4)
       ON CONFLICT (day, id) DO UPDATE SET minutes = minutes + excluded.minutes`).bind(today(), ping.id, ping.minutes, app));
  }
  if (Math.random() < 0.02) {
    stmts.push(db.prepare(`DELETE FROM pings WHERE day < date('now', ?1)`).bind(`-${KEEP_PING_DAYS} days`));
    stmts.push(db.prepare(`DELETE FROM usage WHERE day < date('now', ?1)`).bind(`-${KEEP_PING_DAYS} days`));
  }
  await db.batch(stmts);
}

const ERROR_OS = /^(windows|macos|linux) \d{1,3}\.\d{1,3}\.\d{1,6}$/;
const ERROR_TYPE = /^[\w.`+<>\[\],← -]{1,400}$/u;
const MAX_STACK = 6000;

/** Controlla una segnalazione di errore; restituisce i campi puliti oppure null. */
export function parseError(body) {
  if (!body || typeof body !== 'object') return null;
  const { version, arch, os, type, stack, fatal = false } = body;
  const app = appFrom(body);
  if (app === undefined) return null;
  if (typeof version !== 'string' || !PING_VERSION.test(version)) return null;
  if (!PING_ARCH.has(arch)) return null;
  if (typeof os !== 'string' || !ERROR_OS.test(os)) return null;
  if (typeof type !== 'string' || !ERROR_TYPE.test(type)) return null;
  if (typeof stack !== 'string' || stack.length > MAX_STACK) return null;
  if (typeof fatal !== 'boolean') return null;
  // le app tolgono già i percorsi: per sicurezza li si toglie anche qui
  const clean = stack
    .replace(/[A-Za-z]:\\[^\s:*?"<>|()]*/g, '<percorso>')
    .replace(/(?<![\w.])\/(Users|home|private|var|tmp|Volumes|mnt)\/[^\s:()]*/g, '<percorso>');
  return { app, version, arch, os, type, stack: clean, fatal };
}

/** Impronta di un errore: stessa app, stesso tipo e stessi punti del codice, stessa riga nelle statistiche. */
export async function errorSignature(err, app = err.app || DEFAULT_APP) {
  const data = new TextEncoder().encode(app + '\n' + err.type + '\n' + err.stack);
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', data));
  return [...hash.slice(0, 8)].map(b => b.toString(16).padStart(2, '0')).join('');
}

/** Registra una segnalazione di errore e ogni tanto elimina le più vecchie. */
export async function saveError(db, err, app = err.app || DEFAULT_APP) {
  await ensureSchema(db);
  const sig = await errorSignature(err, app);
  const stmts = [
    db.prepare(
      `INSERT INTO errors (day, sig, version, arch, os, type, stack, fatal, n, app) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, 1, ?9)
       ON CONFLICT (day, sig, version) DO UPDATE SET n = n + 1, fatal = MAX(fatal, excluded.fatal)`)
      .bind(today(), sig, err.version, err.arch, err.os, err.type, err.stack, err.fatal ? 1 : 0, app),
  ];
  if (Math.random() < 0.02) {
    stmts.push(db.prepare(`DELETE FROM errors WHERE day < date('now', ?1)`).bind(`-${KEEP_ERROR_DAYS} days`));
  }
  await db.batch(stmts);
}

/** Tutti i numeri della pagina delle statistiche di un'app. */
export async function readStats(db, app = DEFAULT_APP) {
  await ensureSchema(db);
  const q = (sql, ...args) => db.prepare(sql).bind(app, ...args);
  const [
    downloadsTotal, downloadsByFile, downloadsDaily,
    active, activeByVersion, activeByArch, activeDaily,
    usage30, usageDaily, errors30,
  ] = await db.batch([
    q(`SELECT
         COALESCE(SUM(n), 0) AS all_time,
         COALESCE(SUM(CASE WHEN day >= date('now', '-29 days') THEN n END), 0) AS d30,
         COALESCE(SUM(CASE WHEN day >= date('now', '-6 days') THEN n END), 0) AS d7,
         COALESCE(SUM(CASE WHEN day = date('now') THEN n END), 0) AS today,
         COALESCE(SUM(CASE WHEN source = 'web' THEN n END), 0) AS web,
         COALESCE(SUM(CASE WHEN source = 'app' THEN n END), 0) AS app
       FROM downloads WHERE app = ?1`),
    q(`SELECT file, SUM(n) AS n,
         SUM(CASE WHEN source = 'web' THEN n ELSE 0 END) AS web,
         SUM(CASE WHEN source = 'app' THEN n ELSE 0 END) AS app
       FROM downloads WHERE app = ?1 GROUP BY file ORDER BY n DESC`),
    q(`SELECT day, SUM(n) AS n FROM downloads WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY day`),
    q(`SELECT
         COUNT(DISTINCT CASE WHEN day = date('now') THEN id END) AS today,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-6 days') THEN id END) AS d7,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-29 days') THEN id END) AS d30,
         COUNT(DISTINCT id) AS ever
       FROM pings WHERE app = ?1`),
    // per ogni installazione vista negli ultimi 30 giorni conta l'ultima versione usata
    q(`SELECT version, kind, COUNT(*) AS n FROM (
         SELECT p.id, p.version, p.kind FROM pings p
         JOIN (SELECT id, MAX(day) AS day FROM pings WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY id) last
           ON last.id = p.id AND last.day = p.day)
       GROUP BY version, kind`),
    q(`SELECT arch, COUNT(*) AS n FROM (
         SELECT p.id, p.arch FROM pings p
         JOIN (SELECT id, MAX(day) AS day FROM pings WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY id) last
           ON last.id = p.id AND last.day = p.day)
       GROUP BY arch ORDER BY n DESC`),
    q(`SELECT day, COUNT(*) AS n FROM pings WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY day`),
    // tempo di utilizzo degli ultimi 30 giorni: minuti totali, quanti invii li riportano e da quante installazioni
    q(`SELECT COALESCE(SUM(minutes), 0) AS minutes, COUNT(*) AS reports, COUNT(DISTINCT id) AS installs
       FROM usage WHERE app = ?1 AND day >= date('now', '-29 days')`),
    q(`SELECT day, SUM(minutes) AS n FROM usage WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY day`),
    q(`SELECT sig, type, MAX(stack) AS stack, SUM(n) AS n, MAX(fatal) AS fatal, MAX(day) AS last,
         GROUP_CONCAT(DISTINCT version) AS versions, GROUP_CONCAT(DISTINCT os) AS systems
       FROM errors WHERE app = ?1 AND day >= date('now', '-29 days') GROUP BY sig ORDER BY n DESC LIMIT 50`),
  ]);
  return {
    app,
    downloads: {
      total: downloadsTotal.results[0],
      byFile: downloadsByFile.results,
      daily: downloadsDaily.results,
    },
    installs: {
      active: active.results[0],
      byVersion: activeByVersion.results,
      byArch: activeByArch.results,
      daily: activeDaily.results,
    },
    usage: { ...usage30.results[0], daily: usageDaily.results },
    errors: errors30.results,
  };
}

/** I numeri principali di tutte le app, per il riepilogo: una riga per app che ha dati. */
export async function readOverview(db) {
  await ensureSchema(db);
  const [installs, downloads, usage, errors] = await db.batch([
    db.prepare(`SELECT app,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-6 days') THEN id END) AS d7,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-29 days') THEN id END) AS d30,
         COUNT(DISTINCT id) AS ever
       FROM pings GROUP BY app`),
    db.prepare(`SELECT app, COALESCE(SUM(n), 0) AS all_time,
         COALESCE(SUM(CASE WHEN day >= date('now', '-29 days') THEN n END), 0) AS d30
       FROM downloads GROUP BY app`),
    db.prepare(`SELECT app, COALESCE(SUM(minutes), 0) AS minutes, COUNT(*) AS reports
       FROM usage WHERE day >= date('now', '-29 days') GROUP BY app`),
    db.prepare(`SELECT app, COALESCE(SUM(n), 0) AS n, COUNT(DISTINCT sig) AS kinds, MAX(fatal) AS fatal
       FROM errors WHERE day >= date('now', '-29 days') GROUP BY app`),
  ]);
  const byApp = new Map();
  const row = app => {
    if (!byApp.has(app)) {
      byApp.set(app, { app, installs: { d7: 0, d30: 0, ever: 0 }, downloads: { all_time: 0, d30: 0 },
        usage: { minutes: 0, reports: 0 }, errors: { n: 0, kinds: 0, fatal: 0 } });
    }
    return byApp.get(app);
  };
  for (const r of installs.results) row(r.app).installs = { d7: r.d7, d30: r.d30, ever: r.ever };
  for (const r of downloads.results) row(r.app).downloads = { all_time: r.all_time, d30: r.d30 };
  for (const r of usage.results) row(r.app).usage = { minutes: r.minutes, reports: r.reports };
  for (const r of errors.results) row(r.app).errors = { n: r.n, kinds: r.kinds, fatal: r.fatal };
  return byApp;
}
