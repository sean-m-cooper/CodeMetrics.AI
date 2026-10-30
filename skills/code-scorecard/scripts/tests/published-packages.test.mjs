import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { compatibility } from '../run-scorecard.mjs';

// Explicit release verification; this downloads real registry packages.
const destination = process.env.SCORECARD_PUBLISHED_OUTPUT;
test('published defaults install into empty caches and validate all three ecosystems', {
  skip: !destination, timeout: 300_000,
}, () => {
  assert.deepEqual(compatibility.defaults, {
    npm: 'codetestament@0.6.0-preview.3', dotnet: 'CodeTestament.Scorecard@2.4.0',
  });
  const output = path.resolve(destination);
  assert.ok(!fs.existsSync(output), 'Release verification requires a new output directory.');
  const root = path.join(output, 'repo'), cache = path.join(output, 'cache');
  const write = (relative, value) => {
    const file = path.join(root, relative); fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, value);
  };
  write('app/App.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
  write('app/Work.cs', 'public class Work { public int Run(int value) => value > 0 ? value : 0; }');
  write('ui/package.json', '{"name":"registry-fixture","private":true}');
  write('ui/work.ts', 'export function choose(value: number) { return value > 0 ? value : 0; }');
  write('python/pyproject.toml', '[project]\nname="registry-fixture"\nversion="0.0.0"\n');
  write('python/work.py', 'import time\nasync def work():\n    time.sleep(1)\n');
  const env = { ...process.env, NUGET_PACKAGES: path.join(output, 'restore-packages'),
    NUGET_HTTP_CACHE_PATH: path.join(output, 'restore-http'), npm_config_cache: path.join(output, 'npm-download-cache') };
  // Restrict this rehearsal's NuGet resolution to the public registry.
  fs.writeFileSync(path.join(output, 'NuGet.config'), '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>');
  const invoke = (command, args) => {
    const result = spawnSync(command, args, { cwd: root, env, encoding: 'utf8', windowsHide: true, timeout: 240_000 });
    assert.ifError(result.error);
    return result;
  };
  const restored = invoke('dotnet', ['restore', 'app/App.csproj']);
  assert.equal(restored.status, 0, restored.stdout + restored.stderr);
  const runner = fileURLToPath(new URL('../run-scorecard.mjs', import.meta.url));
  const child = invoke(process.execPath, [runner, '--repo', root, '--cache', cache, '--skip-dependency-probe']);
  fs.writeFileSync(path.join(output, 'helper.stdout.json'), child.stdout ?? '');
  fs.writeFileSync(path.join(output, 'helper.stderr.log'), child.stderr ?? '');
  assert.equal(child.status, 0, child.stderr + child.stdout);
  const result = JSON.parse(child.stdout);
  assert.equal(result.results.length, 3);
  assert.equal(new Set(result.results.map(run => run.runId)).size, 3);
  const expected = { dotnet: ['CodeTestament.Scorecard', '2.4.0'],
    'javascript-typescript': ['CodeTestament.JavaScript', '0.6.0-preview.3'], python: ['CodeTestament.Python', '0.15.0'] };
  for (const run of result.results) {
    assert.equal(run.status, 'complete'); assert.equal(run.fresh, true);
    assert.equal(run.source, 'compatibility-manifest');
    assert.equal(run.analyzerExitCode, 0); assert.equal(run.validationExitCode, 0);
    const inspection = JSON.parse(fs.readFileSync(run.artifacts.inspection, 'utf8'));
    const evidence = inspection.evidence;
    assert.equal(inspection.usable, true);
    assert.deepEqual([evidence.tool.name, evidence.tool.version], expected[run.ecosystem]);
    assert.equal(evidence.analysis.runId, run.runId); assert.equal(evidence.analysis.auditId, result.auditId);
    assert.equal(run.tooling.analyzer.archiveSha256, null);
    assert.equal(path.basename(run.tooling.analyzer.cache), 'registry');
    if (run.ecosystem === 'dotnet') {
      assert.equal(run.ruleCatalog.status, 'available');
      const catalog = JSON.parse(fs.readFileSync(run.artifacts.ruleCatalog, 'utf8'));
      assert.equal(catalog.tool.name, 'CodeTestament.Scorecard'); assert.equal(catalog.tool.version, '2.4.0');
      assert.ok(catalog.rules.some(rule => rule.code === 'CMAI5001'));
    } else {
      const installed = JSON.parse(fs.readFileSync(path.join(run.tooling.analyzer.cache, 'node_modules/codetestament/package.json'), 'utf8'));
      assert.equal(installed.name, 'codetestament'); assert.equal(installed.version, '0.6.0-preview.3');
      const lock = JSON.parse(fs.readFileSync(path.join(run.tooling.analyzer.cache, 'package-lock.json'), 'utf8'));
      const provenance = lock.packages['node_modules/codetestament'];
      assert.match(provenance.resolved, /^https:\/\/registry\.npmjs\.org\/codetestament\/-\//);
      assert.match(provenance.integrity, /^sha512-/);
      if (run.ecosystem === 'python') {
        assert.equal(evidence.dimensions.errorHandling.status, 'skipped');
        assert.equal(evidence.dimensions.errorHandling.score, undefined);
        assert.ok(Object.values(evidence.dimensions).filter(value => value.status === 'scored').length <= 2);
      }
    }
  }
  fs.writeFileSync(path.join(output, 'verified.json'), JSON.stringify({
    defaults: compatibility.defaults, source: compatibility.source,
    auditId: result.auditId, runs: result.results, status: 'passed',
  }, null, 2) + '\n');
});
