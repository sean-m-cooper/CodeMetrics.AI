import { copyFileSync } from 'node:fs';
copyFileSync(new URL('../../../shared/scorecard-schema/evidence.schema.v3.json', import.meta.url), new URL('../dist/evidence.schema.v3.json', import.meta.url));
