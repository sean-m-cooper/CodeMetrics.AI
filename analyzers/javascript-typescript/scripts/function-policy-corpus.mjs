// Source-only, pinned-checkout comparison. Run after building both analyzer versions.
// This script installs nothing and executes no code or lifecycle scripts from the corpus.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';

const [beforeDirectory, repositories, outputDirectory] = process.argv.slice(2).map(p => path.resolve(p));
if (!beforeDirectory || !repositories || !outputDirectory)
  throw new Error('Usage: node scripts/function-policy-corpus.mjs <before-package-directory> <repositories-directory> <output-directory>');
const packageRoot = path.resolve(import.meta.dirname, '..');
const { validateEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/evidence-tools.js')).href);
const samples = [
  { id: 'express', repository: 'express', revision: '7ef98448f8b38099ab1ded55e458538ad47a51e7', package: '.', include: ['index.js', 'lib/**/*.js'] },
  { id: 'zod-v4', repository: 'zod', revision: '0b216ef674e297ebe41d8bf902262e56f8755822', package: 'packages/zod', include: ['src/v4/**/*.ts'], config: 'tsconfig.json' },
  { id: 'tanstack-query-core', repository: 'tanstack-query', revision: '29859ae60c8dca0a5cdbf8abccc775b655cf43e2', package: 'packages/query-core', include: ['src/**/*.ts'], config: 'tsconfig.json' },
  { id: 'tanstack-react-query', repository: 'tanstack-query', revision: '29859ae60c8dca0a5cdbf8abccc775b655cf43e2', package: 'packages/react-query', include: ['src/**/*.ts', 'src/**/*.tsx'], config: 'tsconfig.json' },
];
const sha256 = data => createHash('sha256').update(data).digest('hex');
const slash = text => text.replaceAll('\\', '/');
function bundleHash(root) {
  const files = fs.readdirSync(path.join(root, 'dist')).filter(file => file.endsWith('.js')).sort();
  return sha256(files.map(file => `${file}:${sha256(fs.readFileSync(path.join(root, 'dist', file)))}`).join('\n'));
}
function execute(executable, args, cwd) {
  const result = spawnSync(executable, args, { cwd, encoding: 'utf8', windowsHide: true, timeout: 300_000, maxBuffer: 16 * 1024 * 1024 });
  assert.equal(result.status, 0, result.stdout + result.stderr + (result.error ?? ''));
  return result.stdout.trim();
}
function scores(evidence) {
  return Object.fromEntries(Object.entries(evidence.dimensions).map(([key, dimension]) => [key, dimension.score ?? null]));
}
const summaries = [];
for (const sample of samples) {
  const checkout = path.join(repositories, sample.repository);
  assert.equal(execute('git', ['rev-parse', 'HEAD'], checkout), sample.revision, `Review and re-pin changed input: ${sample.id}`);
  assert.equal(execute('git', ['status', '--porcelain'], checkout), '', `Use a clean checkout: ${sample.id}`);
  const root = path.join(checkout, sample.package);
  const out = path.join(outputDirectory, sample.id);
  fs.mkdirSync(out, { recursive: true });
  const config = { ...(sample.config ? { extends: slash(path.join(root, sample.config)) } : {}),
    compilerOptions: { allowJs: true, noEmit: true }, include: sample.include.map(pattern => slash(path.join(root, pattern))),
    exclude: ['**/node_modules/**', '**/__tests__/**', '**/tests/**', '**/*.test.*', '**/*.spec.*'] };
  const configPath = path.join(out, 'source-scope.json');
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2) + '\n');
  const evidence = {};
  for (const [label, analyzer] of [['before', beforeDirectory], ['after', packageRoot]]) {
    const log = execute(process.execPath, [path.join(analyzer, 'dist/cli.js'), '--project', path.join(root, 'package.json'),
      '--tsconfig', configPath, '--output', path.join(out, `${label}.csv`), '--scorecard-output', path.join(out, `${label}.json`)], root);
    fs.writeFileSync(path.join(out, `${label}.log`), log + '\n');
    evidence[label] = JSON.parse(fs.readFileSync(path.join(out, `${label}.json`), 'utf8'));
    validateEvidence(evidence[label]);
    assert.equal(evidence[label].analysis.status, 'complete');
  }
  assert.deepEqual(evidence.before.population, evidence.after.population);
  assert.deepEqual(evidence.before.filters, evidence.after.filters);
  assert.equal(evidence.before.analysis.configurationFingerprint, evidence.after.analysis.configurationFingerprint);
  assert.notEqual(evidence.before.analysis.runId, evidence.after.analysis.runId);
  assert.equal(fs.readFileSync(path.join(out, 'before.csv'), 'utf8'), fs.readFileSync(path.join(out, 'after.csv'), 'utf8'));
  assert.deepEqual(evidence.before.dimensions.performanceAsync, evidence.after.dimensions.performanceAsync);
  const details = Object.fromEntries(['codeQuality', 'maintainability'].map(key => [key, {
    decision: evidence.after.dimensions[key].scoringDecision,
    topOffenders: evidence.after.dimensions[key].scoring.observations.topOffenders,
  }]));
  const summary = { ...sample, commit: execute('git', ['rev-parse', 'HEAD'], checkout),
    remote: execute('git', ['remote', 'get-url', 'origin'], checkout),
    trackedChanges: execute('git', ['status', '--porcelain'], checkout),
    packageVersion: JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).version,
    configurationSha256: sha256(fs.readFileSync(configPath)), csvSha256: sha256(fs.readFileSync(path.join(out, 'after.csv'))),
    population: evidence.after.population, filters: evidence.after.filters,
    before: { scores: scores(evidence.before), ruleset: evidence.before.analysis.ruleset, runId: evidence.before.analysis.runId },
    after: { scores: scores(evidence.after), ruleset: evidence.after.analysis.ruleset, runId: evidence.after.analysis.runId }, details };
  summaries.push(summary);
  console.log(`${sample.id}: CC ${summary.before.scores.codeQuality} -> ${summary.after.scores.codeQuality}; MI ${summary.before.scores.maintainability} -> ${summary.after.scores.maintainability}; ${summary.population.members} functions; identical raw CSV`);
}
fs.writeFileSync(path.join(outputDirectory, 'summary.json'), JSON.stringify({
  scope: 'Explicit source-only package selections; no dependency install, build, semantic validation or runtime tests. Not whole-repository scorecards or cross-ecosystem calibration.',
  generatedAtUtc: new Date().toISOString(),
  nodeVersion: process.version,
  beforeAnalyzerBundleSha256: bundleHash(beforeDirectory), afterAnalyzerBundleSha256: bundleHash(packageRoot), samples: summaries,
}, null, 2) + '\n');
