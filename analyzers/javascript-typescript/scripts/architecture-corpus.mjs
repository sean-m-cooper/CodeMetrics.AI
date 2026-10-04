// Executes only the bounded fixtures authored in this repository, never public-corpus code.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { analyze } from '../dist/analyzer.js';
import { validateEvidence } from '../dist/evidence-tools.js';

const packageRoot = path.resolve(import.meta.dirname, '..');
const repository = path.resolve(packageRoot, '../..');
const definition = path.join(repository, 'shared/calibration/javascript-typescript-architecture.json');
const corpus = JSON.parse(fs.readFileSync(definition, 'utf8'));
const output = path.resolve(process.argv[2] ?? path.join(repository, 'TestResults/js-ts-architecture-context'));
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'codemetrics-architecture-'));
const sha = data => createHash('sha256').update(data).digest('hex');
const version = JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'), 'utf8')).version;
const samples = [];
fs.mkdirSync(output, { recursive: true });
function write(root, files) {
  for (const [file, content] of Object.entries(files)) {
    const target = path.join(root, file);
    fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, content);
  }
}
function execute(args, cwd) {
  const result = spawnSync(process.execPath, args, { cwd, encoding: 'utf8', timeout: 30_000, windowsHide: true });
  assert.equal(result.status, 0, `${result.error ?? ''}\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
}
function inspect(id, root) {
  const { evidence } = analyze({ project: path.join(root, 'package.json') }, version);
  validateEvidence(evidence);
  assert.equal(evidence.analysis.status, 'complete');
  const architecture = evidence.dimensions.architecture;
  assert.equal(architecture.status, 'skipped');
  assert.equal(architecture.score, undefined);
  assert.equal(architecture.scoringDecision, undefined);
  assert(architecture.findings.every(f => f.severity === 'info' && f.observations.executionOrderEstablished === false));
  const content = JSON.stringify(evidence, null, 2) + '\n';
  fs.writeFileSync(path.join(output, `${id}.json`), content);
  return { graph: architecture.dependencyGraph, evidenceSha256: sha(content), runId: evidence.analysis.runId };
}
try {
  for (const sample of corpus.cases) {
    const root = path.join(temporary, sample.id);
    write(root, { 'package.json': JSON.stringify({ name: sample.id, type: 'module' }), ...sample.files });
    const { graph, ...provenance } = inspect(sample.id, root);
    const observed = { cycles: graph.cycles.length, typeOnly: graph.coverage.explicitTypeOnlyOccurrences,
      fanOut: graph.highestFanOut[0]?.internalValueFanOut ?? 0, barrels: graph.nodes.filter(n => n.reExportOnly).length };
    assert.deepEqual(observed, sample.expected, sample.id);
    const views = { implementation: graph.dependencyViews.implementation.internalValueEdgeCount,
      reExports: graph.dependencyViews.reExports.internalValueEdgeCount, shared: graph.dependencyViews.sharedInternalValueEdgeCount };
    assert.deepEqual(views, sample.expectedViews, sample.id);
    assert.equal(graph.coverage.status, 'observedDependenciesResolved');
    let runtime = null;
    if (sample.runtime) {
      // The expression comes only from our checked-in fixtures. Capture expected
      // exceptions as observations; successful process exit alone is not safety.
      const expression = `try { const value=(${sample.runtime.expression}); console.log(JSON.stringify({value})); }
        catch(error) { console.log(JSON.stringify({errorName:error.name,message:error.message})); }`;
      runtime = JSON.parse(execute(['--input-type=module', '-e', expression], root));
      if (sample.runtime.errorName) {
        assert.equal(runtime.errorName, sample.runtime.errorName);
        assert(runtime.message.includes(sample.runtime.messageIncludes));
      } else assert.deepEqual(runtime, { value: sample.runtime.value });
    }
    samples.push({ id: sample.id, label: sample.label, ...provenance, observed, views, runtime });
    console.log(`${sample.id}: ${observed.cycles} cyclic groups; runtime ${JSON.stringify(runtime)}`);
  }

  // Real offline npm packing/installation checks ordinary resolution against a
  // competing workspace version, including private and missing export targets.
  const npm = process.env.npm_execpath;
  assert(npm, 'Run through npm run test:architecture');
  const library = path.join(temporary, 'library'), consumer = path.join(temporary, 'consumer');
  write(library, {
    'package.json': JSON.stringify({ name: '@fixture/library', version: '1.0.0', type: 'module',
      exports: { '.': './index.js', './missing': './missing.js' },
      scripts: { postinstall: 'node -e "throw new Error(\'Lifecycle script must not run\')"' } }),
    'index.js': 'export const version=1;'
  });
  write(consumer, { 'package.json': '{"name":"consumer","private":true,"type":"module"}' });
  const pack = JSON.parse(execute([npm, 'pack', '--ignore-scripts', '--offline', '--json', '--pack-destination', temporary], library));
  const tarball = path.join(temporary, pack[0].filename);
  execute([npm, 'install', '--ignore-scripts', '--offline', '--no-audit', '--no-fund', '--package-lock=false', tarball], consumer);
  const manifest = JSON.parse(fs.readFileSync(path.join(consumer, 'package.json'), 'utf8'));
  write(consumer, {
    'package.json': JSON.stringify({ ...manifest, workspaces: ['packages/*'] }),
    'tsconfig.json': '{"compilerOptions":{"module":"NodeNext","moduleResolution":"NodeNext"},"include":["index.ts"]}',
    'index.ts': "import '@fixture/library'; import '@fixture/library/private'; import '@fixture/library/missing';",
    'packages/library/package.json': JSON.stringify({ name: '@fixture/library', version: '2.0.0', type: 'module',
      exports: { '.': './index.ts', './private': './index.ts', './missing': './index.ts' } }),
    'packages/library/index.ts': 'export const version=2;'
  });
  const installed = inspect('installed-package-precedence', consumer);
  assert.deepEqual(installed.graph.dependencies.map(d => d.resolution), ['external', 'unresolved', 'unresolved']);
  assert(installed.graph.dependencies.every(d => d.via !== 'workspaceManifest'));
  assert.equal(installed.graph.coverage.status, 'gaps');
  const runtimeVersion = JSON.parse(execute(['--input-type=module', '-e', "console.log(JSON.stringify((await import('@fixture/library')).version))"], consumer));
  assert.equal(runtimeVersion, 1);
  const installedPackage = { runId: installed.runId, evidenceSha256: installed.evidenceSha256,
    tarballSha256: sha(fs.readFileSync(tarball)), runtimeVersion, resolutionOutcomes: installed.graph.dependencies.map(d => ({ specifier: d.specifier, resolution: d.resolution, via: d.via })) };
  const bundle = fs.readdirSync(path.join(packageRoot, 'dist')).filter(file => file.endsWith('.js')).sort()
    .map(file => `${file}:${sha(fs.readFileSync(path.join(packageRoot, 'dist', file)))}`).join('\n');
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ generatedAtUtc: new Date().toISOString(),
    nodeVersion: process.version, fixtureSha256: sha(fs.readFileSync(definition)), analyzerBundleSha256: sha(bundle),
    scope: corpus.scope, architectureScored: false, samples, installedPackage }, null, 2) + '\n');
  console.log('Architecture context fixtures and offline installed-package precedence passed.');
} finally {
  fs.rmSync(temporary, { recursive: true, force: true });
}
