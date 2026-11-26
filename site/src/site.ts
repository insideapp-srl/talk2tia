// Central links and helpers. Repository: github.com/insideapp-srl/talk2tia
import { latest } from './release';

export const links = {
  repo: 'https://github.com/insideapp-srl/talk2tia',
  releases: 'https://github.com/insideapp-srl/talk2tia/releases',
  // Direct link to the latest signed MSI (read at build time, see release.ts); the releases page if unavailable.
  download: latest?.msi.url ?? 'https://github.com/insideapp-srl/talk2tia/releases',
  issues: 'https://github.com/insideapp-srl/talk2tia/issues',
  kofi: 'https://ko-fi.com/insideappsrl',
  insideapp: 'https://www.insideapp.it',
  contact: 'https://www.insideapp.it/contact.html',
};

/** Prefixes a site-relative path with the configured base ('/' on the custom domain talk2tia.insideapp.it). */
export function url(path: string): string {
  const base = import.meta.env.BASE_URL.replace(/\/$/, '');
  return `${base}${path.startsWith('/') ? path : `/${path}`}`;
}

export const stats = {
  tools: 124,
  toolsets: 9,
};

/** Google Analytics 4 measurement ID. Loaded only after the visitor accepts analytics cookies. */
export const analytics = {
  gaId: 'G-C07F2K0QVH',
};
