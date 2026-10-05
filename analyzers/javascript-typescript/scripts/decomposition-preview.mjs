// Offline policy experiment only. Never imported by the analyzer or evidence CLI.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { add, fraction, interpolate, multiply, roundScore } from '../dist/score-arithmetic.js';

export const candidates = {
  ownedStatements: [[10, 10], [20, 8], [40, 6], [80, 4], [160, 0]],
};
export function aggregate(values, anchors) {
  if (!values.length) return null;
  const scores = values.map(value => interpolate(value, anchors));
  const worst = values.indexOf(Math.max(...values));
  if (scores.length === 1) return roundScore(scores[0]);
  const rest = scores.filter((_, index) => index !== worst).reduce(add, fraction(0n));
  return roundScore(add(multiply(scores[worst], fraction(2n, 5n)), multiply(rest, fraction(3n, 5n * BigInt(scores.length - 1)))));
}
function ranks(values) {
  const ordered = values.map((value, index) => ({ value, index })).sort((a,b) => a.value-b.value);
  const ranked = new Array(values.length);
  for (let start = 0; start < ordered.length;) {
    let end = start + 1;
    while (end < ordered.length && ordered[end].value === ordered[start].value) end++;
    for (let index = start; index < end; index++) ranked[ordered[index].index] = (start + end - 1) / 2;
    start = end;
  }
  return ranked;
}
export function spearman(left, right) {
  assert.equal(left.length, right.length);
  if (left.length < 2) return null;
  const a = ranks(left), b = ranks(right), mean = (left.length - 1) / 2;
  let numerator = 0, aa = 0, bb = 0;
  for (let index = 0; index < a.length; index++) {
    const x = a[index] - mean, y = b[index] - mean;
    numerator += x * y; aa += x * x; bb += y * y;
  }
  return aa && bb ? Number((numerator / Math.sqrt(aa * bb)).toFixed(3)) : null;
}
export function preview(evidence) {
  assert.equal(evidence.analysis.status, 'complete');
  const profile = evidence.dimensions.codeQuality.componentDetails.decomposition;
  assert.equal(profile.measurement, 'owned-executable-statements-v2');
  const measured = new Map(evidence.dimensions.codeQuality.scoring.observations.functionContributions.map(row => [row.id, row]));
  const rows = profile.modules.flatMap(module => module.functions.map(row => {
    const match = measured.get(row.id); assert.ok(match, `Missing function ${row.id}`);
    return { ...row, file: module.file, ownComplexity: match.ownComplexity, ownMaintainabilityIndex: match.ownMaintainabilityIndex };
  }));
  assert.equal(rows.length, measured.size);
  const vector = name => rows.map(row => row[name]);
  const longest = [...rows].sort((a,b) => b.ownedStatements-a.ownedStatements || a.id.localeCompare(b.id, 'en'));
  const cc = vector('ownComplexity'), miRisk = rows.map(row => 100-row.ownMaintainabilityIndex);
  return { status: 'experimental-unscored', productionScoresUnchanged: true, functions: rows.length,
    primaryMeasure: 'ownedStatements',
    candidateScores: { ownedStatements: aggregate(vector('ownedStatements'), candidates.ownedStatements) },
    rankCorrelation: { linesWithCC: spearman(vector('ownedSourceLines'), cc), statementsWithCC: spearman(vector('ownedStatements'), cc),
      linesWithMIRisk: spearman(vector('ownedSourceLines'), miRisk), statementsWithMIRisk: spearman(vector('ownedStatements'), miRisk) },
    largestFunctions: longest.slice(0, 10),
    largeLowComplexity: longest.filter(row => row.ownedStatements >= 40 && row.ownComplexity <= 5).slice(0, 10),
    largeLowComplexityCount: rows.filter(row => row.ownedStatements >= 40 && row.ownComplexity <= 5).length };
}
function main() {
  const [input, output] = process.argv.slice(2);
  assert.ok(input && output, 'Usage: node scripts/decomposition-preview.mjs <corpus-directory> <output-json>');
  const corpus = JSON.parse(fs.readFileSync(path.join(input, 'summary.json'), 'utf8'));
  const samples = corpus.samples.map(sample => ({ id: sample.id, commit: sample.commit,
    runId: sample.after.runId, ...preview(JSON.parse(fs.readFileSync(path.join(input, sample.id, 'after.json'), 'utf8'))) }));
  fs.writeFileSync(output, JSON.stringify({ status: 'experimental-unscored', generatedAtUtc: new Date().toISOString(),
    policy: { candidates, aggregation: '40% one worst + 60% mean remaining; one half-up rounding to one decimal',
      purpose: 'Statement-based sensitivity preview; thresholds and weights are not yet accepted production policy.',
      limits: 'Size does not establish responsibility. CC and MI overlap must be reviewed before adding any deduction.' }, samples }, null, 2) + '\n');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
