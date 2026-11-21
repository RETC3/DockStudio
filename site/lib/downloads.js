// Versioni e download delle app DockStudio su getdockstudio.it, dal bucket R2 "dockstudio-downloads"
// (collegato come DOWNLOADS, vedi site/wrangler.toml). Ogni app ha la sua cartella nel bucket:
//   dockspace/latest.json                  ultima versione (stesso formato di screenlove.it/latest.json)
//   dockspace/DockSpace-Setup-0.1.0.exe    installer
//   dockspace/DockSpace-0.1.0-win-x64.zip  versione portatile
//   dockspace/SHA256SUMS-0.1.0.txt         impronte
// I file li carica il workflow di release dell'app (.github/workflows/release-dockspace.yml).
// ScreenLove ha ancora il suo sito e il suo bucket (screenlove.it).

import { countDownload } from '../../web/lib/stats.js';
import { apps } from '../data/apps.mjs';

/** App della suite che si scaricano da qui (non ScreenLove, che usa screenlove.it). */
const APPS = new Map(apps.filter(a => a.slug !== 'screenlove').map(a => [a.slug, a.nome]));

/** Nome dell'app per lo slug dell'indirizzo ("dockspace" → "DockSpace"), o null. */
export function appName(slug) {
  return typeof slug === 'string' ? APPS.get(slug) ?? null : null;
}

/** Nomi di file ammessi per un'app: installer, zip portatili, impronte. */
function filePattern(name) {
  return new RegExp(`^${name}-(?:Setup-\\d+\\.\\d+\\.\\d+\\.exe|\\d+\\.\\d+\\.\\d+-win-(?:x64|arm64)\\.zip)$|^SHA256SUMS-\\d+\\.\\d+\\.\\d+\\.txt$`);
}

/** latest.json dell'app dal bucket, o null se non c'è ancora nessuna versione. */
export async function readLatest(env, slug) {
  if (!env.DOWNLOADS) return null;
  const object = await env.DOWNLOADS.get(`${slug}/latest.json`);
  if (!object) return null;
  try { return JSON.parse(await object.text()); } catch { return null; }
}

export async function serveLatest(request, env, slug) {
  if (!appName(slug)) return notFound();
  if (!env.DOWNLOADS) return notFound();
  const object = await env.DOWNLOADS.get(`${slug}/latest.json`);
  if (!object) return notFound();
  return new Response(request.method === 'HEAD' ? null : object.body, {
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      // le app controllano una volta al giorno: pochi minuti di cache bastano
      'Cache-Control': 'public, max-age=300',
      'X-Content-Type-Options': 'nosniff',
    },
  });
}

/**
 *   /<app>/download/latest  -> rimanda all'installer dell'ultima versione
 *   /<app>/download/<file>  -> il file dal bucket
 */
export async function serveDownload(request, env, slug, name, waitUntil) {
  const app = appName(slug);
  if (!app) return notFound();

  if (name === 'latest') {
    const latest = await readLatest(env, slug);
    const setup = (latest?.assets || []).find(a => new RegExp(`^${app}-Setup-.*\\.exe$`).test(a.name));
    if (!setup) return notFound();
    return new Response(null, {
      status: 302,
      headers: { Location: `/${slug}/download/${setup.name}`, 'Cache-Control': 'no-store' },
    });
  }

  if (!filePattern(app).test(name) || !env.DOWNLOADS) return notFound();
  const key = `${slug}/${name}`;
  const object = request.method === 'HEAD' ? await env.DOWNLOADS.head(key) : await env.DOWNLOADS.get(key);
  if (!object) return notFound();

  const type = name.endsWith('.zip') ? 'application/zip'
    : name.endsWith('.txt') ? 'text/plain; charset=utf-8'
    : 'application/vnd.microsoft.portable-executable';
  const headers = new Headers({
    'Content-Type': type,
    'Content-Length': String(object.size),
    ETag: object.httpEtag,
    // i nomi contengono la versione: lo stesso indirizzo non cambia mai contenuto
    'Cache-Control': 'public, max-age=86400',
    'X-Content-Type-Options': 'nosniff',
  });
  if (!name.endsWith('.txt')) headers.set('Content-Disposition', `attachment; filename="${name}"`);

  // Si conta un download completo (GET dall'inizio del file); provenienza: l'aggiornamento dell'app o il sito.
  const range = request.headers.get('Range');
  if (request.method === 'GET' && env.STATS && !name.endsWith('.txt') && (!range || /^bytes=0-/.test(range))) {
    const source = (request.headers.get('User-Agent') || '').startsWith(app) ? 'app' : 'web';
    waitUntil(countDownload(env.STATS, name, source, slug));
  }
  return new Response(request.method === 'HEAD' ? null : object.body, { headers });
}

export function notFound() {
  return new Response('File non trovato', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' } });
}
