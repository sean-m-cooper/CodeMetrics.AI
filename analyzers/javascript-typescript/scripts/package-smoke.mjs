import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';

const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'codemetrics-package-'));
function run(args, cwd, expected = 0) {
  const result = spawnSync(process.execPath, args, { cwd, encoding: 'utf8', timeout: 120_000, windowsHide: true });
  assert.equal(result.status, expected, result.stdout + result.stderr);
  return result.stdout;
}
try {
  const packageRoot = path.resolve(import.meta.dirname, '..');
  const npm = process.env.npm_execpath;
  assert.ok(npm, 'Run through npm run test:package');
  const output = run([npm, 'pack', '--json', '--pack-destination', temporary], packageRoot);
  const packed = JSON.parse(output.slice(output.indexOf('[')))[0];
  const install = path.join(temporary, 'install'); fs.mkdirSync(install);
  run([npm, 'install', '--ignore-scripts', '--omit=dev', '--no-audit', '--no-fund', path.join(temporary, packed.filename)], install);
  const fixture = path.join(temporary, 'fixture'); fs.mkdirSync(fixture);
  fs.writeFileSync(path.join(fixture, 'package.json'), '{"name":"installed-smoke"}');
  fs.writeFileSync(path.join(fixture, 'App.tsx'), "import {useEffect} from 'react'; export function App(){useEffect(async()=>{},[]); return <div/>;}");
  const cli = path.join(install, 'node_modules/codemetrics-ai/dist/cli.js');
  const evidenceCli = path.join(install, 'node_modules/codemetrics-ai/dist/evidence-cli.js');
  assert.ok(fs.readFileSync(cli, 'utf8').startsWith('#!/usr/bin/env node'));
  run([cli, '--scorecard-output', 'before.json', '--sarif', 'before.sarif'], fixture);
  const evidence = JSON.parse(fs.readFileSync(path.join(fixture, 'before.json'), 'utf8'));
  assert.equal(evidence.schemaVersion, 3); assert.ok(evidence.population.members >= 1);
  assert.equal(evidence.dimensions.performanceAsync.findings[0].category, 'asyncEffectCallback');
  run([cli, '--scorecard-output', 'after.json', '--baseline', 'before.json', '--fail-on-new', 'warning', '--comparison-output', 'comparison.json'], fixture);
  run([evidenceCli, '--input', 'after.json', '--baseline', 'before.json', '--max-score-drop', '0', '--sarif', 'after.sarif'], fixture);
  assert.equal(JSON.parse(fs.readFileSync(path.join(fixture, 'comparison.json'), 'utf8')).new.length, 0);
  run([cli, '--project', 'missing.json'], fixture, 2);
  console.log('Installed package analysis, schema validation, baseline gates, SARIF and invalid-input smoke checks passed.');
} finally { fs.rmSync(temporary, { recursive: true, force: true }); }
