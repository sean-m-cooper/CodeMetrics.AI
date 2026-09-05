import { copyFileSync, cpSync } from 'node:fs';
for (const version of [2, 3]) copyFileSync(new URL(`../../../shared/scorecard-schema/evidence.schema.v${version}.json`, import.meta.url), new URL(`../dist/evidence.schema.v${version}.json`, import.meta.url));
cpSync(new URL('../../../shared/scorecard-schema/examples/', import.meta.url), new URL('../dist/contract-examples/', import.meta.url), { recursive: true });
