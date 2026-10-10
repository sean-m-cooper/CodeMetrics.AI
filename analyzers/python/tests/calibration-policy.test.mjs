import fs from 'node:fs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { aggregatePopulation, complexityAnchors } from '../../../shared/source-facts/population.mjs';
const scenarios = JSON.parse(fs.readFileSync(new URL('../../../shared/calibration/corpus/python-complexity-scenarios.json', import.meta.url)));
const rows = groups => groups.flatMap(([value, count], g) => Array.from({ length: count }, (_, i) => ({ id: `${g}-${i}`, value })));

test('Python calibration scenarios preserve the accepted product expectations', () => {
  for (const scenario of scenarios) {
    const result = aggregatePopulation(rows(scenario.groups), complexityAnchors, true, 1);
    assert.equal(result?.score ?? null, scenario.expectedScore, scenario.name);
  }
});

test('additional hotspots reduce the underlying score even when rounded scores tie', () => {
  const score = groups => aggregatePopulation(rows(groups), complexityAnchors, true, 1);
  const first = score([[21, 1], [3, 99]]), second = score([[21, 2], [3, 98]]);
  assert.equal(first.score, second.score);
  assert.ok(second.unroundedScore < first.unroundedScore);
  for (let worst = 21; worst <= 50; worst++) {
    const current = score([[worst, 1], [3, 99]]);
    assert.ok(current.score < 8);
    assert.ok(score([[worst + 1, 1], [3, 99]]).unroundedScore <= current.unroundedScore);
  }
  const ordinaryMean = aggregatePopulation(rows([[40, 1], [3, 99]]), complexityAnchors, true, 100);
  assert.equal(ordinaryMean.score, 9.9);
  assert.equal(score([[40, 1], [3, 99]]).score, 6);
});
