// Central links and helpers. Repository: github.com/insideapp-srl/talk2tia
export const links = {
  repo: 'https://github.com/insideapp-srl/talk2tia',
  releases: 'https://github.com/insideapp-srl/talk2tia/releases',
  download: 'https://github.com/insideapp-srl/talk2tia/releases/latest',
  issues: 'https://github.com/insideapp-srl/talk2tia/issues',
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
