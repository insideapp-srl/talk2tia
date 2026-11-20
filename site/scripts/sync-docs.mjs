// Copies the repository documentation (../docs/*.md) into the site so pages render the same source of truth.
// The leading "# Title" is dropped: the page hero already renders the page <h1>.
import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const source = join(here, '..', '..', 'docs');
const target = join(here, '..', 'src', 'generated');

await mkdir(target, { recursive: true });
const files = (await readdir(source)).filter((f) => f.endsWith('.md'));
if (files.length === 0) {
  throw new Error(`No markdown files found in ${source}`);
}

for (const file of files) {
  const text = await readFile(join(source, file), 'utf8');
  await writeFile(join(target, file), text.replace(/^\uFEFF?# [^\r\n]*(\r?\n)+/, ''));
}
process.stdout.write(`sync-docs: copied ${files.length} file(s) from docs/\n`);
