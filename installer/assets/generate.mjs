// Regenerates the installer assets from the website sources:
//   License.rtf  - legal notice (EN + IT) and MIT licence, shown and accepted in the setup
//   Dialog.bmp   - 493x312 background of the welcome and finish pages
//   Banner.bmp   - 493x58 top banner of the other pages
//   talk2tia.ico - executable, tray and Add/Remove Programs icon
//   insideapp.png, talk2tia-128.png - images of the About window
// Usage (sharp comes from the website dependencies):
//   cd site && npm ci && cd .. && node installer/assets/generate.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..');
const sharp = createRequire(join(root, 'site', 'package.json'))('sharp');
const read = (p) => readFileSync(join(root, p), 'utf8').replace(/\r\n/g, '\n');

// ---------- License.rtf ----------

function rtfText(s) {
  let out = '';
  for (const ch of s) {
    const c = ch.codePointAt(0);
    if (ch === '\\' || ch === '{' || ch === '}') out += '\\' + ch;
    else if (c < 128) out += ch;
    else if (c < 0x10000) out += `\\u${c > 32767 ? c - 65536 : c}?`;
    else out += '?';
  }
  return out.replace(/\*\*(.+?)\*\*/g, '{\\b $1}').replace(/`([^`]+)`/g, '$1');
}

// Minimal Markdown (headings, paragraphs, numbered lists) to RTF paragraphs.
function markdownToRtf(md) {
  const blocks = md.trim().split(/\n{2,}/);
  const out = [];
  for (const block of blocks) {
    if (block.startsWith('## ')) {
      out.push(`\\pard\\sb160\\sa60\\b ${rtfText(block.slice(3))}\\b0\\par`);
      continue;
    }
    const items = block.split(/\n(?=\d+\. )/);
    for (const item of items) {
      const text = item.replace(/\n\s*/g, ' ');
      const list = /^\d+\. /.test(text);
      out.push(`\\pard${list ? '\\li240' : ''}\\sa80 ${rtfText(text)}\\par`);
    }
  }
  return out.join('\n');
}

const licence = read('LICENSE').split('\n---\n')[0].trim();
const rtf = [
  '{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1',
  '{\\fonttbl{\\f0\\fswiss Segoe UI;}}',
  '\\viewkind4\\f0\\fs16',
  '\\pard\\sa120\\b\\fs20 Talk2Tia \\endash  Legal notice / Note legali\\b0\\fs16\\par',
  `\\pard\\sa120 ${rtfText('By installing Talk2Tia you accept the following terms. Installando Talk2Tia accetti le condizioni seguenti (testo italiano più sotto).')}\\par`,
  '\\pard\\sb120\\sa60\\b\\fs18 ENGLISH\\b0\\fs16\\par',
  markdownToRtf(read('site/src/content/legal.en.md')),
  '\\pard\\sb240\\sa60\\b\\fs18 ITALIANO\\b0\\fs16\\par',
  markdownToRtf(read('site/src/content/legal.it.md')),
  '\\pard\\sb240\\sa60\\b\\fs18 MIT LICENSE\\b0\\fs16\\par',
  ...licence.split(/\n{2,}/).map((p) => `\\pard\\sa80 ${rtfText(p.replace(/\n/g, ' '))}\\par`),
  `\\pard\\sb120 ${rtfText('InsideApp S.r.l. — https://talk2tia.insideapp.it')}\\par`,
  '}',
].join('\n');
writeFileSync(join(here, 'License.rtf'), rtf, 'latin1');

// ---------- Bitmaps ----------

const insideapp = readFileSync(join(root, 'site', 'public', 'insideapp-logo.svg'));
const iconSvg = (size) =>
  Buffer.from(
    readFileSync(join(root, 'site', 'public', 'favicon.svg'), 'utf8').replace(
      '<svg ',
      `<svg width="${size}" height="${size}" `,
    ),
  );

async function png(svg, width) {
  return sharp(Buffer.from(svg)).resize({ width }).png().toBuffer();
}

// 24-bit bottom-up BMP, the format Windows Installer expects.
async function writeBmp(image, file) {
  const { data, info } = await image.removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height } = info;
  const stride = Math.ceil((width * 3) / 4) * 4;
  const size = 54 + stride * height;
  const bmp = Buffer.alloc(size);
  bmp.write('BM', 0);
  bmp.writeUInt32LE(size, 2);
  bmp.writeUInt32LE(54, 10);
  bmp.writeUInt32LE(40, 14);
  bmp.writeInt32LE(width, 18);
  bmp.writeInt32LE(height, 22);
  bmp.writeUInt16LE(1, 26);
  bmp.writeUInt16LE(24, 28);
  bmp.writeUInt32LE(stride * height, 34);
  for (let y = 0; y < height; y++) {
    const row = 54 + (height - 1 - y) * stride;
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 3;
      bmp[row + x * 3] = data[i + 2];
      bmp[row + x * 3 + 1] = data[i + 1];
      bmp[row + x * 3 + 2] = data[i];
    }
  }
  writeFileSync(join(here, file), bmp);
}

const font = "font-family=\"Segoe UI, Arial, sans-serif\"";

// Dialog: the left 164 px are free (text starts at ~173 px); the right side stays white for the dialog text.
const dialogSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="493" height="312">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#eef3ff"/><stop offset="1" stop-color="#e3f7f7"/>
    </linearGradient>
    <linearGradient id="edge" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#335EEA"/><stop offset="1" stop-color="#46CDCF"/>
    </linearGradient>
  </defs>
  <rect width="493" height="312" fill="#ffffff"/>
  <rect width="164" height="312" fill="url(#bg)"/>
  <rect x="161" width="3" height="312" fill="url(#edge)"/>
  <text x="82" y="146" text-anchor="middle" ${font} font-size="22" font-weight="700" fill="#1b2a4e">Talk2Tia</text>
  <text x="82" y="166" text-anchor="middle" ${font} font-size="10.5" fill="#506690">MCP server for</text>
  <text x="82" y="179" text-anchor="middle" ${font} font-size="10.5" fill="#506690">Siemens TIA Portal</text>
  <text x="82" y="262" text-anchor="middle" ${font} font-size="9.5" fill="#506690">made by</text>
</svg>`;
const dialog = sharp(Buffer.from(dialogSvg)).composite([
  { input: await png(iconSvg(64), 64), left: 50, top: 52 },
  { input: await png(insideapp, 132), left: 16, top: 270 },
]);
await writeBmp(dialog, 'Dialog.bmp');

// Banner: title and description are drawn on the left; brand on the right.
const bannerSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="493" height="58">
  <rect width="493" height="58" fill="#ffffff"/>
</svg>`;
const banner = sharp(Buffer.from(bannerSvg)).composite([
  { input: await png(insideapp, 100), left: 340, top: 22 },
  { input: await png(iconSvg(40), 40), left: 445, top: 9 },
]);
await writeBmp(banner, 'Banner.bmp');

// ---------- Images for the About window (2x for high-DPI screens) ----------
writeFileSync(join(here, 'insideapp.png'), await sharp(await png(insideapp, 1040)).trim().resize({ width: 520 }).png().toBuffer());
writeFileSync(join(here, 'talk2tia-128.png'), await sharp(iconSvg(128)).resize(128, 128).png().toBuffer());

// ---------- talk2tia.ico ----------
// Executable, tray and Add/Remove Programs icon. Small sizes as 32-bit DIBs (the classic format System.Drawing reads
// reliably), 256 px as PNG.

async function dibEntry(size) {
  const { data } = await sharp(iconSvg(size)).resize(size, size).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const maskStride = Math.ceil(size / 32) * 4;
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8); // XOR + AND masks
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  header.writeUInt32LE(size * size * 4 + maskStride * size, 20);
  const pixels = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const src = (y * size + x) * 4;
      const dst = ((size - 1 - y) * size + x) * 4; // bottom-up
      pixels[dst] = data[src + 2];
      pixels[dst + 1] = data[src + 1];
      pixels[dst + 2] = data[src];
      pixels[dst + 3] = data[src + 3];
    }
  }
  return Buffer.concat([header, pixels, Buffer.alloc(maskStride * size)]);
}

const sizes = [16, 20, 24, 32, 40, 48, 64, 256];
const images = [];
for (const size of sizes) {
  images.push(size === 256 ? await sharp(iconSvg(256)).resize(256, 256).png().toBuffer() : await dibEntry(size));
}
const dir = Buffer.alloc(6 + 16 * sizes.length);
dir.writeUInt16LE(0, 0);
dir.writeUInt16LE(1, 2); // icon
dir.writeUInt16LE(sizes.length, 4);
let offset = dir.length;
sizes.forEach((size, i) => {
  const e = 6 + i * 16;
  dir.writeUInt8(size === 256 ? 0 : size, e);
  dir.writeUInt8(size === 256 ? 0 : size, e + 1);
  dir.writeUInt16LE(1, e + 4); // planes
  dir.writeUInt16LE(32, e + 6); // bit count
  dir.writeUInt32LE(images[i].length, e + 8);
  dir.writeUInt32LE(offset, e + 12);
  offset += images[i].length;
});
writeFileSync(join(here, 'talk2tia.ico'), Buffer.concat([dir, ...images]));

console.log('installer assets generated');
