// GET /statistiche: le statistiche di tutte le app ora stanno su getdockstudio.it/statistiche (riservata con
// Cloudflare Access). Qui resta solo il rimando alla pagina di ScreenLove.

export function onRequest() {
  return new Response(null, {
    status: 302,
    headers: {
      Location: 'https://getdockstudio.it/statistiche/screenlove',
      'Cache-Control': 'no-store',
      'X-Robots-Tag': 'noindex, nofollow',
    },
  });
}
