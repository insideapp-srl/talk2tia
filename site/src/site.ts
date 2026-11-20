// Central links and helpers. Repository: github.com/insideapp-srl/talk2tia
export const links = {
  repo: 'https://github.com/insideapp-srl/talk2tia',
  releases: 'https://github.com/insideapp-srl/talk2tia/releases',
  // Not /releases/latest: GitHub excludes pre-releases (all 0.x betas) from it.
  download: 'https://github.com/insideapp-srl/talk2tia/releases',
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
  tools: 107,
  toolsets: 7,
};
