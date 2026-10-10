import { copyFileSync, mkdirSync } from 'node:fs';

// One canonical runtime, copied into the package. No repository-relative runtime imports.
const source = new URL('../../../shared/source-facts/', import.meta.url);
const destination = new URL('../dist/shared-source-facts/', import.meta.url);
mkdirSync(destination, { recursive: true });
for (const name of ['arithmetic', 'contract', 'population']) {
  for (const extension of ['mjs', 'd.mts']) {
    const file = `${name}.${extension}`;
    copyFileSync(new URL(file, source), new URL(file, destination));
  }
}
