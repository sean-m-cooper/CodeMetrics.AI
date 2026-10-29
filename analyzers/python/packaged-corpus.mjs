import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID, createHash } from 'node:crypto';
import { parseArgs } from 'node:util';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';

const repository = fileURLToPath(new URL('../../', import.meta.url));
const { values } = parseArgs({ options: { package: { type: 'string' }, repos: { type: 'string' }, output: { type: 'string' } } });
assert.ok(values.package && values.repos && values.output, '--package, --repos and --output are required');
const archive = path.resolve(values.package), output = path.resolve(values.output);
fs.mkdirSync(output); // A new directory is mandatory; preserve earlier verification.
const tooling = path.join(output, 'tooling'); fs.mkdirSync(tooling);
function run(command, args, cwd = repository) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', windowsHide: true, timeout: 300_000, maxBuffer: 128 * 1024 * 1024 });
  assert.ifError(result.error); assert.equal(result.status, 0, result.stderr + result.stdout); return result.stdout.trim();
}
const npm = process.env.npm_execpath ?? path.join(path.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
run(process.execPath, [npm, 'install', '--ignore-scripts', '--omit=dev', '--no-audit', '--no-fund', archive], tooling);
const dist = path.join(tooling, 'node_modules/codemetrics-ai/dist');
const read = file => JSON.parse(fs.readFileSync(path.join(repository, file), 'utf8'));
const manifest = read('shared/calibration/corpus/python-selection.json');
const original = read('shared/scorecard-schema/calibration-runs/python-duplication-2026-10-10.json');
const complexity = read('shared/calibration/baselines/python-selection.json');
const auditId = randomUUID();
const summary = { auditId, archiveSha256: createHash('sha256').update(fs.readFileSync(archive)).digest('hex'), samples: [] };
for (const sample of manifest.samples) {
  const root = path.resolve(values.repos, sample.repository);
  assert.equal(run('git', ['rev-parse', 'HEAD'], root), sample.revision);
  assert.equal(run('git', ['status', '--porcelain'], root), '');
  const runId = randomUUID(), evidencePath = path.join(output, `${sample.id}.json`), inspectionPath = path.join(output, `${sample.id}-inspection.json`);
  run(process.execPath, [path.join(dist, 'python-cli.js'), '--project', root, '--scorecard-output', evidencePath, '--run-id', runId, '--audit-id', auditId,
    ...sample.include.flatMap(item => ['--include', item]), ...sample.productionPaths.flatMap(item => ['--production-path', item])]);
  run(process.execPath, [path.join(dist, 'evidence-cli.js'), '--input', evidencePath, '--inspect-output', inspectionPath,
    '--expected-ecosystem', 'python', '--expected-root', root, '--expected-entry-point', root, '--expected-run-id', runId, '--expected-audit-id', auditId]);
  const inspection = JSON.parse(fs.readFileSync(inspectionPath)), evidence = inspection.evidence, cq = evidence.dimensions.codeQuality;
  const previous = original.samples.find(item => item.id === sample.id);
  assert.equal(inspection.usable, true); assert.equal(inspection.compatibility.comparisonAvailable, false);
  assert.deepEqual(cq.duplication.groups, previous.groups);
  assert.deepEqual(cq.duplication.summary, previous.summary);
  assert.equal(cq.experimentalPreview.population.score, complexity.samples.find(item => item.id === sample.id).experimentalPreview);
  assert.ok(Object.values(evidence.dimensions).every(item => item.status === 'skipped' && !('score' in item)));
  assert.equal(run('git', ['rev-parse', 'HEAD'], root), sample.revision);
  assert.equal(run('git', ['status', '--porcelain'], root), '');
  summary.samples.push({ id: sample.id, revision: sample.revision, runId, adapterVersion: evidence.tool.version,
    ruleset: evidence.analysis.ruleset, duplication: cq.duplication.summary, complexityPreview: cq.experimentalPreview.population.score,
    allGroupsMatchOfflineEvidence: true, allDimensionsUnscored: true });
  console.log(sample.id, JSON.stringify(summary.samples.at(-1)));
}
fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
