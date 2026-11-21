// GET /<app>/download/<file> e /<app>/download/latest: installer e zip delle app DockStudio dal bucket R2
// (vedi site/lib/downloads.js). L'aggiornamento dall'app accetta solo installer scaricati da qui.

import { serveDownload } from '../../../lib/downloads.js';

export async function onRequest({ request, env, params, waitUntil }) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Metodo non consentito', { status: 405, headers: { Allow: 'GET, HEAD' } });
  }
  const parts = Array.isArray(params.path) ? params.path : [params.path];
  return serveDownload(request, env, params.app, parts.join('/'), waitUntil);
}
