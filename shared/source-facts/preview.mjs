#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { complexityPopulation } from './population.mjs';

// Deliberately not evidence v3 or a scorecard CLI. No cross-language calibration claim.
try {
  if (process.argv.length !== 3) throw new Error('Usage: node shared/source-facts/preview.mjs source-facts.json');
  const facts = JSON.parse(readFileSync(process.argv[2], 'utf8'));
  const population = complexityPopulation(facts);
  console.log(JSON.stringify({ status: population ? 'experimental-preview' : 'unavailable',
    language: facts.language, measurementPolicy: facts.measurementPolicy,
    scoringPolicy: 'experimental/shared-complexity-worst-40-remainder-60/v1',
    comparableAcrossLanguages: false, population }, null, 2));
  if (facts.status === 'incomplete') process.exitCode = 2;
} catch (error) {
  console.error(error.message);
  process.exitCode = 2;
}
