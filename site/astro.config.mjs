import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

// GitHub Pages (custom domain): SITE_URL=https://talk2tia.insideapp.it SITE_BASE=/
// Local development: defaults below.
const site = process.env.SITE_URL ?? 'http://localhost:4321';
const base = process.env.SITE_BASE ?? '/';


export default defineConfig({
  site,
  base,
  trailingSlash: 'ignore',
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'it'],
    routing: { prefixDefaultLocale: false },
  },
  integrations: [
    sitemap({
      filter: (page) => !page.includes('/404'),
      i18n: { defaultLocale: 'en', locales: { en: 'en-US', it: 'it-IT' } },
    }),
  ],
});
