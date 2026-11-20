// Statistiche anonime di ScreenLove, nel database D1 collegato come STATS (vedi docs/CLOUDFLARE.md).
//   downloads: quanti file sono stati scaricati, per giorno, file e provenienza (sito o app)
//   pings:     un'app installata che ha cercato aggiornamenti in un giorno (codice casuale, versione, architettura)
//   usage:     minuti di utilizzo inviati da quell'installazione in quel giorno (dalla 0.6.0)
//   errors:    errori segnalati, raggruppati per giorno, impronta e versione (tipo e punti del codice ripuliti)
// Ping ed errori arrivano solo dalle app in cui l'utente li ha scelti.
// Non si salvano indirizzi IP, user agent o altro: solo questi dati.

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS downloads (
     day TEXT NOT NULL, file TEXT NOT NULL, source TEXT NOT NULL, n INTEGER NOT NULL DEFAULT 0,
     PRIMARY KEY (day, file, source))`,
  `CREATE TABLE IF NOT EXISTS pings (
     day TEXT NOT NULL, id TEXT NOT NULL, version TEXT NOT NULL, arch TEXT NOT NULL, kind TEXT NOT NULL,
     PRIMARY KEY (day, id))`,
  `CREATE INDEX IF NOT EXISTS pings_id ON pings (id, day)`,
  `CREATE TABLE IF NOT EXISTS usage (
     day TEXT NOT NULL, id TEXT NOT NULL, minutes INTEGER NOT NULL DEFAULT 0,
     PRIMARY KEY (day, id))`,
  `CREATE TABLE IF NOT EXISTS errors (
     day TEXT NOT NULL, sig TEXT NOT NULL, version TEXT NOT NULL, arch TEXT NOT NULL, os TEXT NOT NULL,
     type TEXT NOT NULL, stack TEXT NOT NULL, fatal INTEGER NOT NULL DEFAULT 0, n INTEGER NOT NULL DEFAULT 0,
     PRIMARY KEY (day, sig, version))`,
];

// Gli errori più vecchi di così vengono cancellati.
export const KEEP_ERROR_DAYS = 180;
export const MAX_PING_MINUTES = 7 * 24 * 60;

// I ping più vecchi di così vengono cancellati: per le installazioni attive bastano gli ultimi mesi.
export const KEEP_PING_DAYS = 400;

let schemaReady = null;

/** Crea le tabelle la prima volta (una volta per istanza del worker). */
export function ensureSchema(db) {
  if (!schemaReady) {
    schemaReady = db.batch(SCHEMA.map(sql => db.prepare(sql))).catch(err => { schemaReady = null; throw err; });
  }
  return schemaReady;
}

/** Giorno UTC in formato AAAA-MM-GG. */
export function today(now = new Date()) {
  return now.toISOString().slice(0, 10);
}

/** Conta un download. Non fa mai fallire il download: in caso di errore lo registra e basta. */
export async function countDownload(db, file, source) {
  if (!db) return;
  try {
    await ensureSchema(db);
    await db.prepare(
      `INSERT INTO downloads (day, file, source, n) VALUES (?1, ?2, ?3, 1)
       ON CONFLICT (day, file, source) DO UPDATE SET n = n + 1`).bind(today(), file, source).run();
  } catch (err) {
    console.error('statistiche: download non contato', err);
  }
}

const PING_ID = /^[0-9a-f]{32}$/;
const PING_VERSION = /^\d{1,4}\.\d{1,4}\.\d{1,6}$/;
const PING_ARCH = new Set(['x64', 'arm64', 'x86']);
const PING_KIND = new Set(['setup', 'portable']);

/** Controlla il contenuto di un ping; restituisce i campi puliti oppure null. */
export function parsePing(body) {
  if (!body || typeof body !== 'object') return null;
  const { id, version, arch, kind, minutes = 0 } = body;
  if (typeof id !== 'string' || !PING_ID.test(id)) return null;
  if (typeof version !== 'string' || !PING_VERSION.test(version)) return null;
  if (!PING_ARCH.has(arch) || !PING_KIND.has(kind)) return null;
  // minuti di utilizzo dall'ultimo invio: facoltativi (le versioni fino alla 0.5.0 non li mandano)
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > MAX_PING_MINUTES) return null;
  return { id, version, arch, kind, minutes };
}

/** Registra il ping di oggi (uno per installazione al giorno) e ogni tanto elimina i più vecchi. */
export async function savePing(db, ping) {
  await ensureSchema(db);
  const stmts = [
    db.prepare(
      `INSERT INTO pings (day, id, version, arch, kind) VALUES (?1, ?2, ?3, ?4, ?5)
       ON CONFLICT (day, id) DO UPDATE SET version = excluded.version, arch = excluded.arch, kind = excluded.kind`)
      .bind(today(), ping.id, ping.version, ping.arch, ping.kind),
  ];
  if (ping.minutes > 0) {
    stmts.push(db.prepare(
      `INSERT INTO usage (day, id, minutes) VALUES (?1, ?2, ?3)
       ON CONFLICT (day, id) DO UPDATE SET minutes = minutes + excluded.minutes`).bind(today(), ping.id, ping.minutes));
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
  return { version, arch, os, type, stack: clean, fatal };
}

/** Impronta di un errore: stesso tipo e stessi punti del codice, stessa riga nelle statistiche. */
export async function errorSignature(err) {
  const data = new TextEncoder().encode(err.type + '\n' + err.stack);
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', data));
  return [...hash.slice(0, 8)].map(b => b.toString(16).padStart(2, '0')).join('');
}

/** Registra una segnalazione di errore e ogni tanto elimina le più vecchie. */
export async function saveError(db, err) {
  await ensureSchema(db);
  const sig = await errorSignature(err);
  const stmts = [
    db.prepare(
      `INSERT INTO errors (day, sig, version, arch, os, type, stack, fatal, n) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, 1)
       ON CONFLICT (day, sig, version) DO UPDATE SET n = n + 1, fatal = MAX(fatal, excluded.fatal)`)
      .bind(today(), sig, err.version, err.arch, err.os, err.type, err.stack, err.fatal ? 1 : 0),
  ];
  if (Math.random() < 0.02) {
    stmts.push(db.prepare(`DELETE FROM errors WHERE day < date('now', ?1)`).bind(`-${KEEP_ERROR_DAYS} days`));
  }
  await db.batch(stmts);
}

/** Tutti i numeri della pagina delle statistiche. */
export async function readStats(db) {
  await ensureSchema(db);
  const q = (sql, ...args) => db.prepare(sql).bind(...args);
  const [
    downloadsTotal, downloadsByFile, downloadsDaily,
    active, activeByVersion, activeByArch, activeDaily,
    usage30, errors30,
  ] = await db.batch([
    q(`SELECT
         COALESCE(SUM(n), 0) AS all_time,
         COALESCE(SUM(CASE WHEN day >= date('now', '-29 days') THEN n END), 0) AS d30,
         COALESCE(SUM(CASE WHEN day >= date('now', '-6 days') THEN n END), 0) AS d7,
         COALESCE(SUM(CASE WHEN day = date('now') THEN n END), 0) AS today,
         COALESCE(SUM(CASE WHEN source = 'web' THEN n END), 0) AS web,
         COALESCE(SUM(CASE WHEN source = 'app' THEN n END), 0) AS app
       FROM downloads`),
    q(`SELECT file, SUM(n) AS n,
         SUM(CASE WHEN source = 'web' THEN n ELSE 0 END) AS web,
         SUM(CASE WHEN source = 'app' THEN n ELSE 0 END) AS app
       FROM downloads GROUP BY file ORDER BY n DESC`),
    q(`SELECT day, SUM(n) AS n FROM downloads WHERE day >= date('now', '-29 days') GROUP BY day`),
    q(`SELECT
         COUNT(DISTINCT CASE WHEN day = date('now') THEN id END) AS today,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-6 days') THEN id END) AS d7,
         COUNT(DISTINCT CASE WHEN day >= date('now', '-29 days') THEN id END) AS d30,
         COUNT(DISTINCT id) AS ever
       FROM pings`),
    // per ogni installazione vista negli ultimi 30 giorni conta l'ultima versione usata
    q(`SELECT version, kind, COUNT(*) AS n FROM (
         SELECT p.id, p.version, p.kind FROM pings p
         JOIN (SELECT id, MAX(day) AS day FROM pings WHERE day >= date('now', '-29 days') GROUP BY id) last
           ON last.id = p.id AND last.day = p.day)
       GROUP BY version, kind`),
    q(`SELECT arch, COUNT(*) AS n FROM (
         SELECT p.id, p.arch FROM pings p
         JOIN (SELECT id, MAX(day) AS day FROM pings WHERE day >= date('now', '-29 days') GROUP BY id) last
           ON last.id = p.id AND last.day = p.day)
       GROUP BY arch ORDER BY n DESC`),
    q(`SELECT day, COUNT(*) AS n FROM pings WHERE day >= date('now', '-29 days') GROUP BY day`),
    // tempo di utilizzo degli ultimi 30 giorni: minuti totali, quanti invii li riportano e da quante installazioni
    q(`SELECT COALESCE(SUM(minutes), 0) AS minutes, COUNT(*) AS reports, COUNT(DISTINCT id) AS installs
       FROM usage WHERE day >= date('now', '-29 days')`),
    q(`SELECT sig, type, MAX(stack) AS stack, SUM(n) AS n, MAX(fatal) AS fatal, MAX(day) AS last,
         GROUP_CONCAT(DISTINCT version) AS versions, GROUP_CONCAT(DISTINCT os) AS systems
       FROM errors WHERE day >= date('now', '-29 days') GROUP BY sig ORDER BY n DESC LIMIT 50`),
  ]);
  return {
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
    usage: usage30.results[0],
    errors: errors30.results,
  };
}
