// Copies the repository documentation (../docs/*.md) into the site so pages render the same source of truth.
import { copyFile, mkdir, readdir } from 'node:fs/promises';
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
  await copyFile(join(source, file), join(target, file));
}
process.stdout.write(`sync-docs: copied ${files.length} file(s) from docs/\n`);
