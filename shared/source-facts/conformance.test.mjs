import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { validateSourceFacts } from './contract.mjs';
import { aggregatePopulation, complexityAnchors, complexityPopulation } from './population.mjs';
import { decimal, roundScore } from './arithmetic.mjs';

const fixture = () => ({ schemaVersion: 1, language: 'fixture',
  producer: { name: 'conformance', version: '1', parser: 'fixture', parserVersion: '1' },
  measurementPolicy: 'fixture/v1', status: 'complete',
  capabilities: { functionComplexity: 'available', maintainability: 'unsupported', coverage: 'unsupported' },
  scope: { description: 'Conformance fixture.', excludes: [] }, diagnostics: [],
  functions: [3, 5, 10, 20, 40].map((value, i) => ({ id: String(i), file: 'fixture', member: `f${i}`, line: i + 1,
    ownership: 'exclusive-function-body', complexity: { value, baseline: 1, decisions: { branches: value - 1 } } })) });

test('fixed anchors, one worst owner, exact weighting, and half-up ties', () => {
  const result = complexityPopulation(fixture());
  assert.equal(result.score, 4.2);
  assert.equal(result.weakCount, 1);
  assert.deepEqual(result.contributions.map(row => row.individualScore), [0, 4, 6, 8, 10]);
  assert.deepEqual(result.contributions.map(row => [row.weightNumerator, row.weightDenominator]), [[2, 5], [3, 20], [3, 20], [3, 20], [3, 20]]);
  assert.equal(roundScore(decimal(7.25)), 7.3);
  const one = fixture(); one.functions = [one.functions[1]];
  assert.equal(complexityPopulation(one).score, 8);
  const reversed = fixture(); reversed.functions.reverse();
  assert.deepEqual(complexityPopulation(reversed), result);
});

test('missing, incomplete and empty populations do not become perfect scores', () => {
  const facts = fixture(); facts.functions = [];
  assert.equal(complexityPopulation(facts), null);
  facts.functions = fixture().functions; facts.status = 'incomplete';
  facts.diagnostics = [{ code: 'parse-error', message: 'One source file could not be parsed.' }];
  assert.equal(complexityPopulation(facts), null);
  facts.status = 'complete'; facts.diagnostics = [];
  facts.capabilities.functionComplexity = 'unsupported';
  facts.functions.forEach(item => { item.complexity = null; });
  assert.equal(complexityPopulation(facts), null);
});

test('contract rejects malformed facts before aggregation', () => {
  const mutations = [
    f => { f.schemaVersion = 2; }, f => { delete f.producer.parserVersion; },
    f => { f.functions.push(f.functions[0]); }, f => { f.functions[0].complexity.value = NaN; },
    f => { f.functions[0].complexity.value = 0; }, f => { f.functions[0].complexity.decisions.branches = -1; },
    f => { f.functions[0].complexity.value++; }, f => { f.functions[0].line = 0; },
    f => { f.status = 'incomplete'; }, f => { f.capabilities.coverage = 'available'; },
    f => { f.functions[0].ownership = 'inclusive'; }, f => { f.score = 10; },
    f => { f.capabilities.functionComplexity = 'unsupported'; }, f => { f.functions[0].complexity = null; },
  ];
  for (const mutate of mutations) {
    const facts = fixture(); mutate(facts);
    assert.throws(() => complexityPopulation(facts), /Invalid source facts/);
  }
});

test('aggregation rejects duplicate IDs and invalid policy parameters', () => {
  const rows = [{ id: 'a', value: 5 }];
  assert.throws(() => aggregatePopulation([...rows, ...rows], complexityAnchors, true, 1), /Unique IDs/);
  assert.throws(() => aggregatePopulation(rows, complexityAnchors, true, 0), /weakest/);
  assert.throws(() => aggregatePopulation(rows, [[3, 10], [2, 8]], true, 1), /anchors/);
});

test('Python and TypeScript producers conform and feed the same engine', async () => {
  const python = process.env.PYTHON ?? (process.platform === 'win32' ? 'py' : 'python3');
  const args = process.platform === 'win32' && !process.env.PYTHON ? ['-3.12'] : [];
  const run = spawnSync(python, [...args, fileURLToPath(new URL('../../analyzers/python/source_facts.py', import.meta.url)),
    fileURLToPath(new URL('./fixtures/owned-functions.py', import.meta.url))], { encoding: 'utf8', windowsHide: true });
  assert.equal(run.status, 0, run.stderr || run.error?.message);
  const pythonFacts = JSON.parse(run.stdout);
  validateSourceFacts(pythonFacts);
  assert.deepEqual(pythonFacts.functions.map(row => [row.member, row.complexity.value]), [['straight', 1], ['branches', 5], ['outer', 1], ['outer.inner', 2]]);

  // Use the actual TS parser and measurement implementation, not hand-made TS facts.
  const { default: ts } = await import('../../analyzers/javascript-typescript/node_modules/typescript/lib/typescript.js');
  const { measureFunction } = await import('../../analyzers/javascript-typescript/dist/function-measurements.js');
  const { functionSourceFacts } = await import('../../analyzers/javascript-typescript/dist/source-facts.js');
  const source = ts.createSourceFile('owned-functions.ts', readFileSync(new URL('./fixtures/owned-functions.ts', import.meta.url), 'utf8'), ts.ScriptTarget.Latest, true);
  const measurements = [];
  function visit(node) {
    if (ts.isFunctionDeclaration(node) && node.body) measurements.push(measureFunction(node, source, 'owned-functions.ts', 'fixture', node.name.text));
    ts.forEachChild(node, visit);
  }
  visit(source);
  const typescriptFacts = functionSourceFacts(measurements);
  validateSourceFacts(typescriptFacts);
  assert.deepEqual(typescriptFacts.functions.map(row => row.complexity.value), pythonFacts.functions.map(row => row.complexity.value));
  assert.equal(complexityPopulation(typescriptFacts).score, complexityPopulation(pythonFacts).score);
  assert.equal(complexityPopulation(pythonFacts).score, 9.2);
  // Same numeric facts produce identical policy results regardless of language provenance.
  const tsPopulation = fixture(); tsPopulation.language = typescriptFacts.language;
  const pyPopulation = fixture(); pyPopulation.language = pythonFacts.language;
  assert.deepEqual(complexityPopulation(tsPopulation), complexityPopulation(pyPopulation));
});
