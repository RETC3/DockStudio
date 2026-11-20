// GET /statistiche/<app>: statistiche di un'app (installazioni, tempo di utilizzo, errori, download),
// riservate con Cloudflare Access.

import { isAuthorized } from '../../../web/lib/access.js';
import { readStats } from '../../../web/lib/stats.js';
import { apps } from '../../data/apps.mjs';
import { headers, renderApp } from '../../lib/statistiche.js';

export async function onRequestGet({ request, env, params }) {
  if (!(await isAuthorized(request, env))) {
    return new Response('Pagina riservata.', { status: 403, headers: headers('text/plain; charset=utf-8') });
  }
  const slug = String(params.app || '').toLowerCase();
  if (!/^[a-z][a-z0-9-]{1,31}$/.test(slug)) {
    return new Response('App sconosciuta.', { status: 404, headers: headers('text/plain; charset=utf-8') });
  }
  if (!env.STATS) {
    return new Response('Statistiche non configurate: manca il database STATS.', { status: 503, headers: headers('text/plain; charset=utf-8') });
  }
  const meta = apps.find(a => a.slug === slug) || null;
  const stats = await readStats(env.STATS, slug);
  if (!meta && !Number(stats.installs.active.ever) && !Number(stats.downloads.total.all_time) && !stats.errors.length) {
    return new Response('App sconosciuta.', { status: 404, headers: headers('text/plain; charset=utf-8') });
  }
  return new Response(renderApp(apps, meta, stats), { headers: headers() });
}
