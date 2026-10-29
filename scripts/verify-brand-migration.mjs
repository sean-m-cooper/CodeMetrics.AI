// Local artifact parity check, deliberately separate from normal baseline gates.
// Leaves its evidence and receipt under TestResults for review.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { parseArgs } from 'node:util';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const { values } = parseArgs({ options: Object.fromEntries(
  ['legacy-npm', 'current-npm', 'legacy-nuget', 'current-nuget', 'output'].map(key => [key, { type: 'string' }])) });
const artifacts = Object.fromEntries(['legacy-npm', 'current-npm', 'legacy-nuget', 'current-nuget'].map(key => {
  assert.ok(values[key], `--${key} is required`);
  const file = path.resolve(values[key]);
  return [key, { file, sha256: createHash('sha256').update(fs.readFileSync(file)).digest('hex') }];
}));
const root = path.resolve(values.output ?? `TestResults/brand-migration/verify-${randomUUID()}`);
assert.ok(!fs.existsSync(root), 'Use a fresh output directory for each verification.');
fs.mkdirSync(root, { recursive: true });
function run(command, args, cwd = root, expected = 0) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', windowsHide: true, timeout: 300_000,
    env: { ...process.env, NUGET_PACKAGES: path.join(root, 'nuget-cache'), NUGET_HTTP_CACHE_PATH: path.join(root, 'nuget-http-cache') } });
  assert.ifError(result.error);
  assert.equal(result.status, expected, `${command}: ${result.stdout}\n${result.stderr}`);
  return result.stdout;
}
const npmCandidates = [process.env.npm_execpath];
for (const directory of [path.dirname(process.execPath), ...(process.env.PATH ?? '').split(path.delimiter)]) {
  npmCandidates.push(path.join(directory, 'node_modules/npm/bin/npm-cli.js'));
  const executable = path.join(directory, 'npm');
  if (fs.existsSync(executable)) npmCandidates.push(fs.realpathSync(executable));
}
const npm = npmCandidates.find(file => file?.endsWith('.js') && fs.existsSync(file));
assert.ok(npm, 'Cannot locate npm-cli.js.');
const install = path.join(root, 'npm'); fs.mkdirSync(install);
run(process.execPath, [npm, 'install', '--ignore-scripts', '--no-audit', '--no-fund', '--omit=dev',
  artifacts['legacy-npm'].file, artifacts['current-npm'].file], install);
const distributions = [
  { name: 'codemetrics-ai', version: '0.6.0-preview.2', js: 'codemetrics-ai', python: 'CodeMetrics.AI.Python' },
  { name: 'codetestament', version: '0.6.0-preview.3', js: 'CodeTestament.JavaScript', python: 'CodeTestament.Python' }
].map(item => ({ ...item, root: path.join(install, 'node_modules', item.name) }));
for (const item of distributions) {
  const manifest = JSON.parse(fs.readFileSync(path.join(item.root, 'package.json')));
  assert.equal(manifest.name, item.name); assert.equal(manifest.version, item.version);
  for (const command of Object.keys(manifest.bin))
    assert.ok(fs.existsSync(path.join(install, 'node_modules/.bin', command + (process.platform === 'win32' ? '.cmd' : ''))));
}
const currentRoot = distributions[1].root;
const { validateEvidence, compare, gate } = await import(pathToFileURL(path.join(currentRoot, 'dist/evidence-tools.js')));
const { inspectEvidence } = await import(pathToFileURL(path.join(currentRoot, 'dist/evidence-reader.js')));
const results = [];
function normalize(evidence) {
  const copy = structuredClone(evidence);
  delete copy.tool.name; delete copy.tool.version;
  for (const dimension of Object.values(copy.dimensions)) {
    if (dimension.ruleCatalog) {
      assert.equal(dimension.ruleCatalog.toolVersion, evidence.tool.version);
      assert.equal(dimension.ruleCatalog.command, evidence.tool.name === 'CodeMetrics.AI'
        ? 'code-metrics rules --format json' : 'codetestament-dotnet rules --format json');
      delete dimension.ruleCatalog.toolVersion; delete dimension.ruleCatalog.command;
    }
  }
  // Only invocation metadata varies below the envelope. Producer IDs and all
  // hashes, findings, policies, populations, scores and diagnostics remain compared.
  const ids = new Map([[evidence.analysis.runId, '<run-id>'], [evidence.analysis.auditId, '<audit-id>']]);
  return JSON.parse(JSON.stringify(copy, (key, value) => key === 'generatedAtUtc' ? '<timestamp>' : ids.get(value) ?? value));
}
function checkPair(ecosystem, files, names, versions) {
  const evidence = files.map((file, index) => {
    const item = inspectEvidence(file, { name: names[index], version: versions[index], ecosystem });
    assert.equal(item.usable, true); validateEvidence(item.evidence); return item.evidence;
  });
  assert.notEqual(evidence[0].analysis.runId, evidence[1].analysis.runId);
  assert.notEqual(evidence[0].analysis.auditId, evidence[1].analysis.auditId);
  assert.deepEqual(normalize(evidence[1]), normalize(evidence[0]), `${ecosystem} substantive evidence changed`);
  assert.throws(() => compare(evidence[1], evidence[0]), /Incompatible baseline/);
  assert.throws(() => gate(compare(evidence[1], evidence[0], true), 'warning', 0), /incompatible/i);
  results.push({ ecosystem, files, tools: evidence.map(e => e.tool), parity: 'passed', crossBrandGate: 'rejected' });
  console.log(`${ecosystem}: paired evidence matches; cross-brand gates rejected.`);
}
for (const ecosystem of ['javascript-typescript', 'python']) {
  const fixture = path.join(root, ecosystem); fs.mkdirSync(fixture);
  if (ecosystem === 'python') fs.writeFileSync(path.join(fixture, 'work.py'), 'import time\nasync def work(value):\n    if value:\n        time.sleep(1)\n    return value\n');
  else {
    fs.writeFileSync(path.join(fixture, 'package.json'), '{"name":"brand-parity"}');
    fs.writeFileSync(path.join(fixture, 'work.ts'), 'export async function work(x: number) { if (x) return x; return 0; }\nexport const result = new Promise(async resolve => resolve(1));\n');
  }
  const files = distributions.map((item, index) => {
    const output = path.join(root, `${ecosystem}-${index}.json`);
    run(process.execPath, [path.join(item.root, ecosystem === 'python' ? 'dist/python-cli.js' : 'dist/cli.js'), '--project', ecosystem === 'python' ? fixture : path.join(fixture, 'package.json'),
      '--scorecard-output', output, ...(ecosystem === 'python' ? [] : ['--output', path.join(root, `${ecosystem}-${index}.csv`)])]);
    return output;
  });
  checkPair(ecosystem, files, distributions.map(d => ecosystem === 'python' ? d.python : d.js),
    distributions.map(d => ecosystem === 'python' ? '0.15.0' : d.version));
}
const dotnet = path.join(root, 'dotnet-tools');
const config = path.join(root, 'NuGet.Config');
fs.writeFileSync(config, '<configuration><packageSources><clear /></packageSources></configuration>');
const packages = [['legacy-nuget', 'CodeMetrics.AI', '2.3.1', 'code-metrics'], ['current-nuget', 'CodeTestament.Scorecard', '2.4.0', 'codetestament-dotnet']];
for (const [key, id, version] of packages) {
  const source = path.join(root, 'nuget-sources', key); fs.mkdirSync(source, { recursive: true });
  fs.copyFileSync(artifacts[key].file, path.join(source, path.basename(artifacts[key].file)));
  run('dotnet', ['tool', 'install', id, '--version', version, '--tool-path', dotnet, '--configfile', config, '--add-source', source, '--no-cache']);
}
const fixture = path.join(root, 'dotnet'); fs.mkdirSync(fixture);
const project = path.join(fixture, 'Fixture.csproj');
fs.writeFileSync(project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
fs.writeFileSync(path.join(fixture, 'Worker.cs'), 'public class Worker { public int Run(int x) { if (x > 0) return x; return 0; } }');
run('dotnet', ['restore', project, '--configfile', config]);
const files = packages.map(([, name, version, command], index) => {
  const executable = path.join(dotnet, command + (process.platform === 'win32' ? '.exe' : ''));
  assert.ok(run(executable, ['--version']).trim().startsWith(version + '+'));
  const catalog = JSON.parse(run(executable, ['rules', '--format', 'json']));
  assert.equal(catalog.tool.name, name); assert.equal(catalog.tool.version, version);
  fs.writeFileSync(path.join(root, `catalog-${index}.json`), JSON.stringify(catalog, null, 2));
  const output = path.join(root, `dotnet-${index}.json`);
  run(executable, ['--solution', project, '--skip-dependency-probe', '--output', path.join(root, `dotnet-${index}.csv`), '--scorecard-output', output]);
  return output;
});
checkPair('dotnet', files, packages.map(p => p[1]), packages.map(p => p[2]));
const catalogs = [0, 1].map(i => JSON.parse(fs.readFileSync(path.join(root, `catalog-${i}.json`))));
delete catalogs[0].tool; delete catalogs[1].tool;
assert.deepEqual(catalogs[1], catalogs[0]);
const receipt = { status: 'passed', sourceRevision: run('git', ['rev-parse', 'HEAD'], path.resolve('.')).trim(),
  sourceMayIncludeLocalChanges: true, artifacts, results, limitations: ['Local Windows/Linux host only; no public registry installation or skill default migration.', 'Fixture parity; external dependency probes intentionally disabled.'] };
fs.writeFileSync(path.join(root, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
console.log(`Migration evidence: ${root}`);
