// POST /api/ping: le app DockStudio segnalano di essere in uso (al massimo una volta al giorno, solo se l'utente
// ha scelto le statistiche anonime). Corpo come su screenlove.it (web/functions/api/ping.js), con in più il nome
// dell'app: { "app": "dockpdf", "id": "<32 cifre esadecimali>", "version": "1.0.0", "arch": "x64",
// "kind": "setup", "minutes": 95 }. Si accettano solo le app della suite (site/data/apps.mjs). Nessun IP.

import { parsePing, savePing } from '../../../web/lib/stats.js';
import { apps } from '../../data/apps.mjs';
import { readJson, reply } from '../../lib/http.js';

const MAX_BODY = 1024;
const KNOWN = new Set(apps.map(a => a.slug));

export async function onRequestPost({ request, env }) {
  const body = await readJson(request, MAX_BODY);
  if (body instanceof Response) return body;
  const ping = parsePing(body);
  if (!ping || !ping.app || !KNOWN.has(ping.app)) return reply(400);
  if (!env.STATS) return reply(204); // statistiche non configurate: l'app non deve accorgersene
  try {
    await savePing(env.STATS, ping, ping.app);
  } catch (err) {
    console.error('statistiche: ping non salvato', err);
    return reply(503);
  }
  return reply(204);
}

export function onRequest() {
  return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'POST' } });
}
