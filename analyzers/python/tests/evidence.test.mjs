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
    assert.ifError(result.error); assert.equal(result.status, expected, result.stderr + result.stdout);
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
  assert.equal(evidence.tool.version, '0.4.0-experimental');
  assert.equal(facts.producer.version, evidence.tool.version);
  assert.equal(facts.measurementPolicy, 'python/owned-function-complexity-v2');
  assert.equal(evidence.dimensions.codeQuality.scope.id, facts.measurementPolicy);
  assert.equal(evidence.analysis.ruleset, 'python-2026-10-10-duplication-preview');
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
  const failed = pythonEvidence(current.dimensions.codeQuality.sourceFacts, current.subject, current.analysis, undefined, report);
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.codeQuality.experimentalPreview.population, null);
  assert.deepEqual(failed.dimensions.codeQuality.findings, []);
  const output = path.join(f.directory, 'duplication-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});
