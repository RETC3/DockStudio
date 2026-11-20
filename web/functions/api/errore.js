// POST /api/errore: un'app segnala un errore (solo se l'utente ha scelto le segnalazioni degli errori).
// Corpo JSON: { "version": "0.6.0", "arch": "x64", "os": "windows 10.0.19045",
//               "type": "System.InvalidOperationException", "stack": "at ScreenLove.…", "fatal": false }
// Niente messaggio dell'errore, niente percorsi, niente codice dell'installazione: si salvano solo questi
// campi con il giorno e un contatore (web/lib/stats.js). Nessun IP.

import { parseError, saveError } from '../../lib/stats.js';

const MAX_BODY = 16 * 1024;

export async function onRequestPost({ request, env }) {
  const length = Number(request.headers.get('Content-Length') || 0);
  if (length > MAX_BODY) return reply(413);

  let body;
  try {
    const text = await request.text();
    if (text.length > MAX_BODY) return reply(413);
    body = JSON.parse(text);
  } catch {
    return reply(400);
  }

  const report = parseError(body);
  // da screenlove.it arrivano solo gli errori di ScreenLove (le altre app li mandano a getdockstudio.it)
  if (!report || (report.app && report.app !== 'screenlove')) return reply(400);
  if (!env.STATS) return reply(204); // statistiche non configurate: l'app non deve accorgersene

  try {
    await saveError(env.STATS, report, 'screenlove');
  } catch (err) {
    console.error('statistiche: errore non salvato', err);
    return reply(503);
  }
  return reply(204);
}

export function onRequest() {
  return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'POST' } });
}

function reply(status) {
  return new Response(null, { status, headers: { 'Cache-Control': 'no-store' } });
}
