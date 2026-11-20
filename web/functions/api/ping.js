// POST /api/ping: l'app installata segnala di essere in uso (al massimo una volta al giorno, solo se
// l'utente ha scelto le statistiche anonime; fino alla 0.5.0 erano attive finché non si spegnevano).
// Corpo JSON: { "id": "<32 cifre esadecimali casuali>", "version": "0.6.0", "arch": "x64", "kind": "setup",
//               "minutes": 95 }   (minutes: minuti di utilizzo dall'ultimo invio, dalla 0.6.0)
// Si salvano solo questi campi e il giorno: niente IP né altri dati (web/lib/stats.js).

import { parsePing, savePing } from '../../lib/stats.js';

const MAX_BODY = 1024;

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

  const ping = parsePing(body);
  if (!ping) return reply(400);
  if (!env.STATS) return reply(204); // statistiche non configurate: l'app non deve accorgersene

  try {
    await savePing(env.STATS, ping);
  } catch (err) {
    console.error('statistiche: ping non salvato', err);
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
