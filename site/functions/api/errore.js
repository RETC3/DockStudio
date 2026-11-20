// POST /api/errore: le app DockStudio segnalano un errore (solo se l'utente ha scelto le segnalazioni).
// Corpo come su screenlove.it (web/functions/api/errore.js), con in più il nome dell'app ("app": "dockpdf").
// Si accettano solo le app della suite (site/data/apps.mjs). Nessun IP, nessun codice dell'installazione.

import { parseError, saveError } from '../../../web/lib/stats.js';
import { apps } from '../../data/apps.mjs';
import { readJson, reply } from '../../lib/http.js';

const MAX_BODY = 16 * 1024;
const KNOWN = new Set(apps.map(a => a.slug));

export async function onRequestPost({ request, env }) {
  const body = await readJson(request, MAX_BODY);
  if (body instanceof Response) return body;
  const report = parseError(body);
  if (!report || !report.app || !KNOWN.has(report.app)) return reply(400);
  if (!env.STATS) return reply(204);
  try {
    await saveError(env.STATS, report, report.app);
  } catch (err) {
    console.error('statistiche: errore non salvato', err);
    return reply(503);
  }
  return reply(204);
}

export function onRequest() {
  return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'POST' } });
}
