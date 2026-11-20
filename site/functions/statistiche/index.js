// GET /statistiche: riepilogo delle statistiche di tutte le app, riservato con Cloudflare Access.

import { isAuthorized } from '../../../web/lib/access.js';
import { readOverview } from '../../../web/lib/stats.js';
import { apps } from '../../data/apps.mjs';
import { headers, renderOverview } from '../../lib/statistiche.js';

export async function onRequestGet({ request, env }) {
  if (!(await isAuthorized(request, env))) {
    return new Response('Pagina riservata.', { status: 403, headers: headers('text/plain; charset=utf-8') });
  }
  if (!env.STATS) {
    return new Response('Statistiche non configurate: manca il database STATS.', { status: 503, headers: headers('text/plain; charset=utf-8') });
  }
  return new Response(renderOverview(apps, await readOverview(env.STATS)), { headers: headers() });
}
