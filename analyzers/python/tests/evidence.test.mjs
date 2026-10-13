import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const packageRoot = process.env.CODEMETRICS_PACKAGE_ROOT ?? fileURLToPath(new URL('../../javascript-typescript/', import.meta.url));
const cli = path.join(packageRoot, 'dist/python-cli.js');
const reader = path.join(packageRoot, 'dist/evidence-cli.js');
const { validateEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/evidence-tools.js')));
const { complexityPopulation } = await import(pathToFileURL(path.join(packageRoot, 'dist/shared-source-facts/population.mjs')));

function fixture(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'python evidence '));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const root = path.join(directory, 'source with spaces'); fs.mkdirSync(root);
  const write = (file, text) => { const location = path.join(root, file); fs.mkdirSync(path.dirname(location), { recursive: true }); fs.writeFileSync(location, text); };
  let sequence = 0;
  function run(args = [], expected = 0, project = root) {
    const output = path.join(directory, `evidence-${sequence++}.json`);
    const result = spawnSync(process.execPath, [cli, '--project', project, '--scorecard-output', output, ...args], { encoding: 'utf8', windowsHide: true });
    assert.ifError(result.error); assert.equal(result.status, expected, result.stderr + result.stdout +
      (fs.existsSync(output) ? JSON.stringify(JSON.parse(fs.readFileSync(output)).analysis?.diagnostics) : ''));
    return { output, evidence: JSON.parse(fs.readFileSync(output, 'utf8')) };
  }
  const inspect = (output, args = [], expected = 0) => {
    const result = spawnSync(process.execPath, [reader, '--input', output, ...args], { encoding: 'utf8', windowsHide: true });
    assert.equal(result.status, expected, result.stderr + result.stdout); return result;
  };
  return { directory, root, write, run, inspect };
}

test('packaged CLI emits validated unscored evidence with current IDs and shared preview', t => {
  const f = fixture(t);
  f.write('pyproject.toml', '[build-system]\nbuild-backend="do_not_execute"\n');
  f.write('work.py', 'def work(x):\n' + '    if x: x -= 1\n'.repeat(10) + '    return x\n');
  const ids = ['--run-id', '11111111-1111-4111-8111-111111111111', '--audit-id', '22222222-2222-4222-8222-222222222222'];
  const { output, evidence } = f.run(ids, 0, path.join(f.root, 'pyproject.toml'));
  assert.equal(evidence.analysis.status, 'complete'); assert.equal(evidence.population.members, 1);
  assert.equal(evidence.analysis.calibration, 'uncalibrated');
  assert.ok(Object.values(evidence.dimensions).every(d => d.status === 'skipped' && !('score' in d)));
  assert.deepEqual(evidence.dimensions.codeQuality.experimentalPreview.population, complexityPopulation(evidence.dimensions.codeQuality.sourceFacts));
  assert.equal(evidence.dimensions.codeQuality.findings[0].severity, 'info');
  const inspection = path.join(f.directory, 'inspection.json');
  f.inspect(output, ['--inspect-output', inspection, '--expected-ecosystem', 'python', '--expected-root', f.root,
    '--expected-entry-point', path.join(f.root, 'pyproject.toml'), '--expected-run-id', ids[1], '--expected-audit-id', ids[3]]);
  const inspected = JSON.parse(fs.readFileSync(inspection));
  assert.equal(inspected.usable, true); assert.equal(inspected.compatibility.comparisonAvailable, false);
  f.inspect(output, ['--expected-run-id', '33333333-3333-4333-8333-333333333333'], 2);
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0'], 2);
  const sarif = path.join(f.directory, 'review.sarif'); f.inspect(output, ['--sarif', sarif]);
  assert.equal(JSON.parse(fs.readFileSync(sarif)).runs[0].results[0].level, 'note');
});

test('selected parse failures withhold previews and findings and remain diagnosable', t => {
  const f = fixture(t); f.write('good.py', 'def f(): return 1\n'); f.write('bad.py', 'def broken(:');
  const { output, evidence } = f.run([], 2);
  assert.equal(evidence.analysis.status, 'incomplete'); assert.equal(evidence.dimensions.codeQuality.status, 'failed');
  assert.equal(evidence.dimensions.codeQuality.sourceFacts.functions.length, 1);
  assert.equal(evidence.dimensions.codeQuality.experimentalPreview.population, null);
  assert.deepEqual(evidence.dimensions.codeQuality.findings, []);
  const inspection = path.join(f.directory, 'failed.json'); f.inspect(output, ['--inspect-output', inspection], 2);
  assert.equal(JSON.parse(fs.readFileSync(inspection)).usable, false);
  assert.equal(f.run(['--exclude', 'bad.py']).evidence.analysis.status, 'complete');
});

test('installed match policy preserves guards and structural alternatives with versioned evidence', t => {
  const f = fixture(t);
  f.write('match.py', `def choose(value, ready):
    match value:
        case 1 | 2 | 3 if ready and value:
            if ready: return value
        case [x] | {"x": x}:
            return x
        case _:
            return None
`);
  const { output, evidence } = f.run();
  const facts = evidence.dimensions.codeQuality.sourceFacts;
  assert.equal(evidence.tool.version, '0.6.0-experimental');
  assert.equal(facts.producer.version, '0.4.0-experimental');
  assert.equal(facts.measurementPolicy, 'python/owned-function-complexity-v2');
  assert.equal(evidence.dimensions.codeQuality.scope.id, facts.measurementPolicy);
  assert.equal(evidence.analysis.ruleset, 'python-2026-10-13-decomposition-preview');
  const complexity = facts.functions[0].complexity;
  assert.equal(complexity.value, 7);
  assert.equal(complexity.decisions.matchAlternatives, 1);
  assert.equal(complexity.decisions.matchCases, 2);
  assert.equal(complexity.decisions.matchGuards, 1);
  assert.equal(complexity.decisions.booleanOperators, 1);
  assert.equal(complexity.decisions.ifStatements, 1);
  f.inspect(output);
  for (const mutate of [
    value => { value.dimensions.codeQuality.sourceFacts.measurementPolicy = 'python/owned-function-complexity-v1'; },
    value => { value.dimensions.codeQuality.sourceFacts.producer.version = '0.2.0-experimental'; },
    value => { value.dimensions.codeQuality.scope.id = 'python/owned-function-complexity-v1'; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
});

test('empty and declaration-only selections never produce perfect scores', t => {
  const f = fixture(t); f.write('empty.py', 'VALUE = 1\n');
  f.write('types.pyi', 'def stub(): ...\n');
  f.write('protocol.py', 'from typing import Protocol\nclass Reader(Protocol):\n    def read(self): ...\n');
  const { evidence } = f.run();
  assert.equal(evidence.population.members, 0); assert.equal(evidence.dimensions.codeQuality.experimentalPreview.status, 'unavailable');
  assert.equal(evidence.dimensions.codeQuality.sourceFacts.selection.excludedFunctions.length, 1);
});

test('scope overrides survive evidence mapping and affect configuration identity', t => {
  const f = fixture(t); f.write('lib/work.py', 'def work(): return 1\n'); f.write('lib/test/helpers.py', 'def helper(): return 2\n');
  const before = f.run().evidence;
  const after = f.run(['--include', 'lib/**', '--production-path', 'lib/test/**']).evidence;
  assert.equal(before.population.members, 1); assert.equal(after.population.members, 2);
  assert.notEqual(before.analysis.configurationFingerprint, after.analysis.configurationFingerprint);
  const repeat = f.run(['--include', 'lib/**', '--production-path', 'lib/test/**']).evidence;
  assert.equal(after.analysis.configurationFingerprint, repeat.analysis.configurationFingerprint);
  assert.notEqual(after.analysis.runId, repeat.analysis.runId);
});

test('isolated parser does not import target code or project Python startup hooks', t => {
  const f = fixture(t);
  for (const file of ['work.py', 'sitecustomize.py', 'json.py'])
    f.write(file, 'from pathlib import Path\nPath("EXECUTED").write_text("bad")\nraise RuntimeError("Target code executed")\ndef f(): return 1\n');
  const { evidence } = f.run(); assert.equal(evidence.population.members, 3);
  assert.equal(fs.existsSync(path.join(f.root, 'EXECUTED')), false);
});

test('missing interpreter produces incomplete standard evidence rather than stale results', t => {
  const f = fixture(t); f.write('work.py', 'def f(): return 1\n');
  const { output, evidence } = f.run(['--python', path.join(f.directory, 'missing-python')], 2);
  assert.equal(evidence.dimensions.codeQuality.sourceFacts, null);
  assert.equal(evidence.analysis.diagnostics[0].kind, 'pythonExecution'); f.inspect(output, [], 2);
});

test('existing output and source files cannot be overwritten', t => {
  const f = fixture(t); f.write('work.py', 'def f(): return 1\n');
  const { output } = f.run(); const before = fs.readFileSync(output);
  const result = spawnSync(process.execPath, [cli, '--project', f.root, '--scorecard-output', output], { encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 2); assert.deepEqual(fs.readFileSync(output), before);
  const source = path.join(f.root, 'work.py');
  assert.equal(spawnSync(process.execPath, [cli, '--project', f.root, '--scorecard-output', source], { windowsHide: true }).status, 2);
  assert.equal(fs.readFileSync(source, 'utf8'), 'def f(): return 1\n');
});

test('validator rejects preview promotion, forged populations and altered previews', t => {
  const f = fixture(t); f.write('work.py', 'def f(x):\n    if x: return 1\n    return 2\n');
  const { evidence } = f.run();
  for (const mutate of [
    value => { value.dimensions.codeQuality.status = 'scored'; value.dimensions.codeQuality.score = 10; },
    value => { value.population.members++; },
    value => { value.dimensions.codeQuality.experimentalPreview.population.score = 2; },
    value => { value.dimensions.codeQuality.sourceFacts.functions[0].population = 'test'; },
    value => { value.analysis.configurationFingerprint = 'forged'; },
    value => { value.dimensions.codeQuality.sourceFacts.measurementPolicy = 'unknown-policy'; },
    value => { delete value.analysis.runId; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
});

test('invalid selection produces diagnostics and no experimental preview', t => {
  const f = fixture(t); f.write('work.py', 'def f(): return 1\n');
  const { evidence } = f.run(['--include', '../outside/**'], 2);
  assert.equal(evidence.analysis.diagnostics[0].kind, 'invalid-selection');
  assert.equal(evidence.dimensions.codeQuality.experimentalPreview.population, null);
});

test('packaged duplication preserves groups, exclusions, IDs and information-only SARIF', t => {
  const f = fixture(t);
  const body = '    value = items[0]\n    value += 1\n    value *= 2\n    result = str(value)\n    return result\n';
  for (const name of ['one', 'two', 'three']) f.write(`${name}.py`, `def ${name}(items):\n${body}`);
  f.write('tiny.py', 'def tiny(): return 1\n');
  const { output, evidence } = f.run();
  const cq = evidence.dimensions.codeQuality, d = cq.duplication;
  assert.equal(d.runId, evidence.analysis.runId); assert.equal(d.auditId, evidence.analysis.auditId);
  assert.equal(d.summary.selectedFunctions, 4); assert.equal(d.summary.eligibleFunctions, 3);
  assert.equal(d.summary.duplicateGroups, 1); assert.equal(d.summary.matchedFunctions, 3);
  assert.equal(d.excludedFunctions[0].reason, 'below-statement-minimum');
  assert.equal(cq.findings.length, 1); assert.equal(cq.findings[0].severity, 'info');
  assert.equal(cq.findings[0].observations.occurrences.length, 3);
  assert.ok(Object.values(evidence.dimensions).every(value => value.status === 'skipped' && !('score' in value)));
  f.inspect(output);
  const sarif = path.join(f.directory, 'duplication.sarif'); f.inspect(output, ['--sarif', sarif]);
  const result = JSON.parse(fs.readFileSync(sarif)).runs[0].results;
  assert.equal(result.length, 1); assert.equal(result[0].level, 'note');
  for (const mutate of [
    value => { value.runId = '11111111-1111-4111-8111-111111111111'; },
    value => { value.auditId = '11111111-1111-4111-8111-111111111111'; },
    value => { value.subject.root += '-stale'; },
    value => { value.sourceFactsProducer.version = '0.3.0-experimental'; },
    value => { value.selection.files[0].contentSha256 = 'a'.repeat(64); },
    value => { value.summary.matchedFunctions++; },
    value => { value.configuration.minimumStatements = 1; },
    value => { value.configurationFingerprint = 'b'.repeat(64); },
    value => { value.groups[0].occurrences[0].line++; },
    value => { value.groups[0].occurrences[0].id = 'c'.repeat(64); },
    value => { value.groups[0].occurrences[0].sourceSha256 = 'd'.repeat(64); },
    value => { value.groups[0].occurrences.push(value.groups[0].occurrences[0]); },
    value => { value.groups.push(value.groups[0]); },
    value => { value.groups[0].population = 'test'; },
    value => { value.groups[0].statementCount = 4; },
    value => { value.groups[0].disposition = 'scored'; },
    value => { value.groups[0].score = 8; },
    value => { value.status = 'incomplete'; },
    value => { value.excludedFunctions[0].statementCount = 5; },
    value => { value.excludedFunctions.push(value.excludedFunctions[0]); }
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.codeQuality.duplication);
    assert.throws(() => validateEvidence(copy));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.codeQuality.duplication;
  assert.throws(() => validateEvidence(missing));
  f.write('bad.py', 'def broken(:');
  const failed = f.run([], 2).evidence.dimensions.codeQuality;
  assert.deepEqual(failed.duplication.groups, []); assert.equal(failed.duplication.summary, null);
  assert.deepEqual(failed.findings, []); assert.equal(failed.experimentalPreview.population, null);
});

test('duplication failure with complete complexity still withholds the preview and all findings', async t => {
  const f = fixture(t); f.write('one.py', 'def one(): return 1\n');
  const current = f.run().evidence;
  const report = structuredClone(current.dimensions.codeQuality.duplication);
  report.status = 'incomplete'; report.summary = null; report.excludedFunctions = [];
  report.diagnostics = [{ code: 'duplication-analysis', message: 'Selected source changed after population measurement.' }];
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(current.dimensions.codeQuality.sourceFacts, current.subject, current.analysis, undefined, report, current.dimensions.codeQuality.coverageReview, current.dimensions.codeQuality.decomposition);
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.codeQuality.experimentalPreview.population, null);
  assert.deepEqual(failed.dimensions.codeQuality.findings, []);
  const output = path.join(f.directory, 'duplication-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('coverage imports are unscored, explicit about freshness, and arithmetically validated', t => {
  const f = fixture(t);
  f.write('work.py', 'def work(value):\n    if value:\n        return 1\n    return 0\n');
  const coverage = path.join(f.directory, 'coverage.json');
  fs.writeFileSync(coverage, JSON.stringify({ meta: { format: 3, version: '7.6.1', timestamp: '2020-01-01', branch_coverage: true },
    files: { 'work.py': { executed_lines: [1, 2, 3], missing_lines: [4], excluded_lines: [] } } }));
  const absent = f.run().evidence.dimensions.codeQuality.coverageReview;
  assert.equal(absent.functions[0].coverage.reason, 'coverage-not-supplied');
  const { output, evidence } = f.run(['--coverage-json', coverage]);
  const report = evidence.dimensions.codeQuality.coverageReview;
  assert.equal(report.runId, evidence.analysis.runId); assert.equal(report.auditId, evidence.analysis.auditId);
  assert.equal(report.coverageInput.freshness, 'unverified'); assert.equal(report.summary.conditionalEstimates, 1);
  assert.equal(report.functions[0].coverage.fraction, 2 / 3);
  assert.ok(Math.abs(report.functions[0].crapEstimate.value - (2 + 4 / 27)) < 1e-12);
  assert.ok(Object.values(evidence.dimensions).every(d => d.status === 'skipped' && !('score' in d)));
  f.inspect(output);
  for (const mutate of [
    r => { r.runId = '11111111-1111-4111-8111-111111111111'; },
    r => { r.coverageInput.freshness = 'source-matched'; },
    r => { r.functions[0].complexity++; },
    r => { r.functions[0].sourceSha256 = 'a'.repeat(64); },
    r => { r.functions[0].coverage.fraction = 1; },
    r => { r.functions[0].coverage.missingLines = [2]; },
    r => { r.functions[0].coverage.executedLines.push(1); },
    r => { r.functions[0].crapEstimate.value++; },
    r => { r.functions[0].crapEstimate.disposition = 'review-only'; },
    r => { r.functions[0].score = 10; },
    r => { r.functions.push(r.functions[0]); },
    r => { r.functions = []; },
    r => { r.summary.sourceBoundEstimates++; },
    r => { r.summary.conditionalEstimates++; },
    r => { r.summary.unmatchedReportFiles.push('../escape.py'); }
  ]) {
    const altered = structuredClone(evidence); mutate(altered.dimensions.codeQuality.coverageReview);
    assert.throws(() => validateEvidence(altered));
  }
  const altered = structuredClone(evidence); altered.analysis.ruleset = 'unknown'; assert.throws(() => validateEvidence(altered));
  for (const args of [['--coverage-json', coverage + '-missing'], ['--coverage-json', coverage, '--collection-manifest', coverage + '-missing'], ['--collection-manifest', coverage]]) {
    const failed = f.run(args, 2).evidence;
    assert.equal(failed.dimensions.codeQuality.experimentalPreview.population, null);
    assert.deepEqual(failed.dimensions.codeQuality.findings, []);
    assert.equal(failed.dimensions.codeQuality.coverageReview.status, 'incomplete');
  }
});

test('installed explicit collector binds source and rejects stale source or substituted reports', t => {
  const f = fixture(t);
  f.write('work.py', 'def work(value):\n    if value:\n        return 1\n    return 0\n');
  f.write('caf\u00e9.py', 'VALUE = 1\n');
  f.write('exercise.py', 'import work\nassert work.work(True) == 1\n');
  const python = process.env.CODEMETRICS_COVERAGE_PYTHON ?? spawnSync(process.platform === 'win32' ? 'py' : 'python3',
    [...(process.platform === 'win32' ? ['-3.12'] : []), '-c', 'import sys; print(sys.executable)'], { encoding: 'utf8', windowsHide: true }).stdout.trim();
  const directory = path.join(f.directory, 'collection');
  const collector = path.join(packageRoot, 'dist/python-collect-cli.js');
  const command = [collector, f.root, '--python', python, '--module', 'exercise', '--exclude', 'exercise.py', '--output-directory', directory];
  const collected = spawnSync(process.execPath, command, { encoding: 'utf8', windowsHide: true, timeout: 60_000 });
  assert.equal(collected.status, 0, collected.stderr + collected.stdout + (fs.existsSync(path.join(directory, 'tests.log')) ? fs.readFileSync(path.join(directory, 'tests.log'), 'utf8') : ''));
  const coverage = path.join(directory, 'coverage.json'), manifest = path.join(directory, 'provenance.json');
  const args = ['--exclude', 'exercise.py', '--coverage-json', coverage, '--collection-manifest', manifest];
  const { output, evidence } = f.run(args);
  const report = evidence.dimensions.codeQuality.coverageReview;
  assert.equal(report.summary.sourceBoundEstimates, 1); assert.equal(report.summary.conditionalEstimates, 0);
  assert.equal(report.coverageInput.sourceBinding, 'matched-pre-post-snapshots');
  assert.equal(report.collectionProvenance.receipt.reportSha256, report.coverageInput.sha256);
  assert.notEqual(report.collectionProvenance.collectionId, report.runId);
  f.inspect(output);
  for (const mutate of [
    p => { p.receipt.reportSha256 = '0'.repeat(64); },
    p => { p.receipt.beforeSourceSha256 = '0'.repeat(64); },
    p => { p.receipt.afterSourceSha256 = '0'.repeat(64); },
    p => { p.receipt.sourceSelection.files[0].contentSha256 = '0'.repeat(64); },
    p => { p.receipt.testExitCode = 1; },
    p => { p.receipt.configuration.branch = false; },
    p => { p.receipt.runtime.coverageVersion = 'fake'; },
    p => { p.collectionId = '11111111-1111-4111-8111-111111111111'; },
    p => { delete p.receipt; }
  ]) {
    const altered = structuredClone(evidence); mutate(altered.dimensions.codeQuality.coverageReview.collectionProvenance);
    assert.throws(() => validateEvidence(altered));
  }
  f.write('work.py', 'def work(value):\n    if value:\n        return 1\n    return 2\n');
  const stale = f.run(args, 2); f.inspect(stale.output, [], 2);
  f.write('work.py', 'def work(value):\n    if value:\n        return 1\n    return 0\n');
  fs.appendFileSync(coverage, '\n');
  assert.equal(f.run(args, 2).evidence.analysis.status, 'incomplete');
  const original = fs.readFileSync(manifest);
  assert.equal(spawnSync(process.execPath, command, { windowsHide: true }).status, 2);
  assert.deepEqual(fs.readFileSync(manifest), original);
});


test('decomposition preserves separate size, nesting, population and unavailable lambda evidence', t => {
  const f = fixture(t);
  f.write('work.py', 'def large(x):\n' + '    x += 1\n'.repeat(35) + '    return x\n' +
    'def nested(x):\n    if x:\n        for y in x:\n            if y:\n                return y\n    return None\n' +
    'callback = lambda x: x + 1\n');
  f.write('tests/test_work.py', 'def test_work():\n    assert True\n');
  const { evidence, output } = f.run(['--include-tests']);
  const cq = evidence.dimensions.codeQuality, report = cq.decomposition;
  assert.equal(report.runId, evidence.analysis.runId); assert.equal(report.auditId, evidence.analysis.auditId);
  assert.equal(report.summary.selectedFunctions, 4); assert.equal(report.summary.measuredFunctions, 3);
  assert.equal(report.summary.unavailableFunctions, 1);
  assert.deepEqual(report.summary.populations.production.ownedStatements, { count: 2, p50: 5, p90: 36, p95: 36, max: 36 });
  assert.deepEqual(report.summary.populations.production.maxBlockNesting, { count: 2, p50: 0, p90: 3, p95: 3, max: 3 });
  assert.equal(report.summary.populations.test.ownedStatements.max, 1);
  assert.equal(report.functions.find(row => row.functionKind === 'Lambda').measurement, null);
  assert.deepEqual(cq.findings, []); // Size and nesting add no thresholds or findings.
  assert.ok(Object.values(evidence.dimensions).every(d => d.status === 'skipped' && !('score' in d)));
  f.inspect(output);
  const measured = r => r.functions.find(row => row.status === 'measured');
  for (const mutate of [
    r => { r.runId = '11111111-1111-4111-8111-111111111111'; },
    r => { r.auditId = '22222222-2222-4222-8222-222222222222'; },
    r => { r.subject.entryPoint += '-stale'; },
    r => { r.configuration.quantiles = 'interpolated'; },
    r => { r.configurationFingerprint = 'a'.repeat(64); },
    r => { r.sourceFactsPolicy = 'unknown'; },
    r => { r.selection.files[0].contentSha256 = 'b'.repeat(64); },
    r => { measured(r).sourceSha256 = 'c'.repeat(64); },
    r => { measured(r).population = 'support'; },
    r => { measured(r).complexity++; },
    r => { measured(r).measurement.ownedStatements++; },
    r => { measured(r).measurement.maxBlockNesting = 999; },
    r => { measured(r).measurement.statementKinds.Unknown = 1; },
    r => { measured(r).measurement.excludedSyntax.docstring = 2; },
    r => { measured(r).measurement.score = 8; },
    r => { r.functions.find(row => row.functionKind === 'Lambda').status = 'measured'; },
    r => { r.functions.push(r.functions[0]); },
    r => { r.functions.pop(); },
    r => { r.summary.populations.production.ownedStatements.p50++; },
    r => { r.summary.populations.production.maxBlockNesting.count++; },
    r => { r.summary.unavailableReasons = {}; },
    r => { r.status = 'incomplete'; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.codeQuality.decomposition);
    assert.throws(() => validateEvidence(copy));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.codeQuality.decomposition;
  assert.throws(() => validateEvidence(missing));
  const bypass = structuredClone(evidence); bypass.analysis.ruleset = 'unknown'; bypass.tool.version = '0.1.0-experimental';
  delete bypass.dimensions.codeQuality.coverageReview;
  assert.throws(() => validateEvidence(bypass));
});

test('decomposition failure with complete other measurements withholds the combined preview', async t => {
  const f = fixture(t); f.write('work.py', 'def work(): return 1\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality;
  const report = structuredClone(cq.decomposition);
  report.status = 'incomplete'; report.summary = null; report.functions = [];
  report.diagnostics = [{ code: 'decomposition-analysis', message: 'Selected source changed during measurement.' }];
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, report);
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.codeQuality.experimentalPreview.population, null);
  assert.deepEqual(failed.dimensions.codeQuality.findings, []);
  const output = path.join(f.directory, 'decomposition-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('saved coverage-era producer remains valid without fabricated decomposition', () => {
  // Generated by the previously installed 0.5.0-experimental adapter, not the current builder.
  const historical = JSON.parse(fs.readFileSync(new URL('fixtures/python-v05.json', import.meta.url)));
  validateEvidence(historical);
  assert.equal(historical.tool.version, '0.5.0-experimental');
  assert.equal(historical.dimensions.codeQuality.decomposition, undefined);
  for (const mutate of [
    e => { e.dimensions.codeQuality.decomposition = null; },
    e => { e.tool.version = '0.6.0-experimental'; },
    e => { e.analysis.ruleset = 'python-2026-10-13-decomposition-preview'; },
    e => { e.dimensions.codeQuality.coverageReview.runId = '11111111-1111-4111-8111-111111111111'; }
  ]) {
    const copy = structuredClone(historical); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
});
