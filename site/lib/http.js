// Piccoli aiuti per le funzioni di getdockstudio.it (site/functions).

/** Corpo JSON della richiesta, al massimo max caratteri; in caso di problemi una risposta 400/413 già pronta. */
export async function readJson(request, max) {
  const length = Number(request.headers.get('Content-Length') || 0);
  if (length > max) return reply(413);
  try {
    const text = await request.text();
    if (text.length > max) return reply(413);
    return JSON.parse(text);
  } catch {
    return reply(400);
  }
}

/** Risposta vuota, mai in cache. */
export function reply(status) {
  return new Response(null, { status, headers: { 'Cache-Control': 'no-store' } });
}
