// Source-only, pinned-checkout comparison. Run after building both analyzer versions.
// This script installs nothing and executes no code or lifecycle scripts from the corpus.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';

const [beforeDirectory, repositories, outputDirectory] = process.argv.slice(2, 5).map(p => path.resolve(p));
const statementDecomposition = process.argv[5] === '--statement-decomposition';
const applicationContext = process.argv[5] === '--application-context';
const ownerPopulationTransition = process.argv[5] === '--owner-population-transition';
assert(process.argv.length === 5 || (process.argv.length === 6 && (ownerPopulationTransition || applicationContext || statementDecomposition)), 'Unknown corpus mode');
if (!beforeDirectory || !repositories || !outputDirectory)
  throw new Error('Usage: node scripts/function-policy-corpus.mjs <before-package-directory> <repositories-directory> <output-directory>');
const packageRoot = path.resolve(import.meta.dirname, '..');
const { validateEvidence, compare, gate } = await import(pathToFileURL(path.join(packageRoot, 'dist/evidence-tools.js')).href);
const samples = [
  { id: 'express', repository: 'express', revision: '7ef98448f8b38099ab1ded55e458538ad47a51e7', package: '.', include: ['index.js', 'lib/**/*.js'] },
  { id: 'zod-v4', repository: 'zod', revision: '0b216ef674e297ebe41d8bf902262e56f8755822', package: 'packages/zod', include: ['src/v4/**/*.ts'], config: 'tsconfig.json' },
  { id: 'tanstack-query-core', repository: 'tanstack-query', revision: '29859ae60c8dca0a5cdbf8abccc775b655cf43e2', package: 'packages/query-core', include: ['src/**/*.ts'], config: 'tsconfig.json' },
  { id: 'tanstack-react-query', repository: 'tanstack-query', revision: '29859ae60c8dca0a5cdbf8abccc775b655cf43e2', package: 'packages/react-query', include: ['src/**/*.ts', 'src/**/*.tsx'], config: 'tsconfig.json' },
];
if (applicationContext || statementDecomposition) samples.push(
  { id: 'excalidraw-app', repository: 'excalidraw', revision: 'ed10ac7dca7e40f3f4a31269b4bfba980d0db41e', package: 'excalidraw-app', include: ['**/*.ts', '**/*.tsx'], config: '../tsconfig.json' },
  { id: 'uptime-kuma-server', repository: 'uptime-kuma', revision: 'e7420f8fa546a8baa7ac4bf9d10a32545d4346e0', package: '.', include: ['server/**/*.js', 'server/**/*.ts'] },
);
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
  if (statementDecomposition) {
    assert.equal(evidence.before.analysis.ruleset, evidence.after.analysis.ruleset);
    for (const key of Object.keys(evidence.after.dimensions).filter(key => key !== 'codeQuality'))
      assert.deepEqual(evidence.before.dimensions[key], evidence.after.dimensions[key], `${sample.id}: changed ${key}`);
    const beforeQuality = structuredClone(evidence.before.dimensions.codeQuality);
    const afterQuality = structuredClone(evidence.after.dimensions.codeQuality);
    assert.equal(beforeQuality.componentDetails.decomposition.version, 1);
    assert.equal(afterQuality.componentDetails.decomposition.version, 2);
    assert.equal(afterQuality.componentDetails.decomposition.primaryMeasure, 'ownedStatements');
    delete beforeQuality.componentDetails.decomposition; delete afterQuality.componentDetails.decomposition;
    assert.deepEqual(beforeQuality, afterQuality);
    assert.equal(evidence.after.dimensions.codeQuality.componentDetails.decomposition.score, null);
    const comparison = compare(evidence.after, evidence.before);
    assert.equal(comparison.new.length, 0); assert.equal(comparison.resolved.length, 0);
    assert.equal(gate(comparison, 'warning', 0), false);
  } else if (applicationContext) {
    assert.equal(evidence.before.analysis.ruleset, 'javascript-typescript-2026-10-03-owner-population');
    assert.equal(evidence.after.analysis.ruleset, 'javascript-typescript-2026-10-04-local-handlers');
    for (const key of Object.keys(evidence.after.dimensions).filter(key => key !== 'errorHandling'))
      assert.deepEqual(evidence.before.dimensions[key], evidence.after.dimensions[key], `${sample.id}: changed ${key}`);
    const handlers = evidence.after.dimensions.errorHandling.handlerEvidence;
    assert.equal(handlers.version, 2);
    assert.equal(handlers.totalHandlers, handlers.unexplainedEmptyHandlers + handlers.documentedEmptyHandlers + handlers.handlersContainingCode);
    assert.equal(handlers.handlers.length, new Set(handlers.handlers.map(handler => handler.id)).size);
    assert.equal(handlers.handlerUseSites, handlers.handlers.reduce((sum, handler) => sum + handler.uses.length, 0));
    assert.equal(evidence.after.dimensions.errorHandling.score, undefined);
    assert.throws(() => compare(evidence.after, evidence.before), /Incompatible baseline/);
    assert.throws(() => gate(compare(evidence.after, evidence.before, true), 'warning'), /incompatible/);
  } else if (ownerPopulationTransition) {
    assert.equal(evidence.before.analysis.ruleset, 'javascript-typescript-2026-10-02-module-graph');
    assert.equal(evidence.after.analysis.ruleset, 'javascript-typescript-2026-10-03-owner-population');
    for (const key of ['codeQuality', 'maintainability']) assert.equal(evidence.before.dimensions[key].score, evidence.after.dimensions[key].score);
    assert.deepEqual(evidence.before.dimensions.architecture, evidence.after.dimensions.architecture);
    assert.deepEqual(evidence.before.dimensions.maintainability, evidence.after.dimensions.maintainability);
    assert.deepEqual(evidence.before.dimensions.codeQuality.scoring, evidence.after.dimensions.codeQuality.scoring);
    assert.deepEqual(evidence.before.dimensions.codeQuality.findings, evidence.after.dimensions.codeQuality.findings);
    assert.deepEqual(evidence.before.dimensions.performanceAsync.findings.map(f => f.fingerprint).sort(),
      evidence.after.dimensions.performanceAsync.findings.map(f => f.fingerprint).sort());
    assert.equal(evidence.after.dimensions.errorHandling.score, undefined);
    assert.equal(evidence.after.dimensions.codeQuality.componentDetails.decomposition.score, null);
    assert.throws(() => compare(evidence.after, evidence.before), /Incompatible baseline/);
    const exploratory = compare(evidence.after, evidence.before, true);
    assert.throws(() => gate(exploratory, 'warning'), /incompatible/);
  } else assert.deepEqual(evidence.before.dimensions.performanceAsync, evidence.after.dimensions.performanceAsync);
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
    ...(evidence.after.dimensions.architecture.dependencyGraph ? { moduleGraph: {
      coverage: evidence.after.dimensions.architecture.dependencyGraph.coverage,
      cycleCount: evidence.after.dimensions.architecture.dependencyGraph.cycles.length,
      cycles: evidence.after.dimensions.architecture.dependencyGraph.cycles,
      reExportOnlyModules: evidence.after.dimensions.architecture.dependencyGraph.nodes.filter(node => node.reExportOnly).length,
      highestFanOut: evidence.after.dimensions.architecture.dependencyGraph.highestFanOut,
      highestFanIn: evidence.after.dimensions.architecture.dependencyGraph.highestFanIn,
      dependencyViews: evidence.after.dimensions.architecture.dependencyGraph.dependencyViews,
    } } : {}),
    ...((ownerPopulationTransition || applicationContext || statementDecomposition) ? { asyncPopulation: evidence.after.dimensions.performanceAsync.scoring?.observations,
      decomposition: evidence.after.dimensions.codeQuality.componentDetails.decomposition,
      errorHandlingBefore: evidence.before.dimensions.errorHandling.handlerEvidence,
      errorHandling: evidence.after.dimensions.errorHandling.handlerEvidence } : {}),
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
