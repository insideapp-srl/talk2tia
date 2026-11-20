// Renders the social preview image (public/og-image.png, 1200x630) from scripts/og-image.svg.
// Run after editing the SVG: node scripts/og-image.mjs. The PNG is committed so builds do not depend on system fonts.
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const here = dirname(fileURLToPath(import.meta.url));
const input = join(here, 'og-image.svg');
const output = join(here, '..', 'public', 'og-image.png');

await sharp(input, { density: 144 }).resize(1200, 630).png({ compressionLevel: 9 }).toFile(output);
process.stdout.write(`og-image: wrote ${output}\n`);
