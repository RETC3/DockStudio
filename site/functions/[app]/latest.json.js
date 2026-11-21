// GET /<app>/latest.json: ultima versione di un'app DockStudio, letta dal bucket R2 (vedi site/lib/downloads.js).
// L'app la legge per gli aggiornamenti (DockStudio.Core.Updates.UpdateClient).

import { serveLatest, notFound } from '../../lib/downloads.js';

export async function onRequest({ request, env, params }) {
  if (request.method !== 'GET' && request.method !== 'HEAD') return notFound();
  return serveLatest(request, env, params.app);
}
