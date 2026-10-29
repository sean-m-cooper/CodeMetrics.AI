// Offline policy diagnostics; never used to assign a production dimension score.
import fs from 'node:fs';
import { complexityPopulation, aggregatePopulation, complexityAnchors } from '../../shared/source-facts/population.mjs';
const facts = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const weighted = complexityPopulation(facts);
const rows = facts.functions.map(item => ({ id: item.id, value: item.complexity.value }));
const maximum = rows.length ? Math.max(...rows.map(row => row.value)) : null;
const meanOnly = rows.length ? aggregatePopulation(rows, complexityAnchors, true, rows.length) : null;
const ceiling = maximum === null ? null : aggregatePopulation([{ id: 'worst', value: maximum }, { id: 'clean-remainder', value: 3 }], complexityAnchors, true, 1);
console.log(JSON.stringify({ score: weighted?.score ?? null, unroundedScore: weighted?.unroundedScore ?? null,
  worstIndividual: weighted?.contributions[0]?.individualScore ?? null,
  meanOnlyScore: meanOnly?.score ?? null, roundedSeverityCeiling: ceiling?.score ?? null }));
