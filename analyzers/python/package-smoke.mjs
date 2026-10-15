import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = fileURLToPath(new URL('../javascript-typescript/', import.meta.url));
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'python package '));
const npm = process.env.npm_execpath ?? path.join(path.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
function run(args, cwd, env = {}) {
  const result = spawnSync(process.execPath, args, { cwd, env: { ...process.env, ...env }, encoding: 'utf8', windowsHide: true, timeout: 300_000 });
  assert.ifError(result.error); assert.equal(result.status, 0, result.stdout + result.stderr); return result.stdout;
}
try {
  const packed = run([npm, 'pack', '--json', '--pack-destination', temporary], root);
  const archive = JSON.parse(packed.slice(packed.indexOf('[')))[0];
  assert.ok(archive.files.every(file => !file.path.includes('__pycache__')));
  const install = path.join(temporary, 'isolated install'); fs.mkdirSync(install);
  run([npm, 'install', '--ignore-scripts', '--omit=dev', '--no-audit', '--no-fund', path.join(temporary, archive.filename)], install);
  const packageRoot = path.join(install, 'node_modules/codemetrics-ai');
  for (const file of ['source_facts.py', 'source_selection.py', 'duplication.py', 'analysis.py', 'decomposition.py', 'DECOMPOSITION.md', 'METHOD_COMPLEXITY.md',
    'error_handling.py', 'ERROR_HANDLING.md', 'error_handling_context.py', 'ERROR_HANDLING_CONTEXT.md', 'error_handling_transfers.py', 'ERROR_HANDLING_TRANSFERS.md', 'coverage_review.py',
    'coverage_provenance.py', 'coverage_collect.py', 'coverage_worker.py', 'COVERAGE.md'])
    assert.deepEqual(fs.readFileSync(path.join(packageRoot, 'dist/python', file)), fs.readFileSync(new URL(file, import.meta.url)));
  assert.equal(JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'))).bin['codemetrics-python'], './dist/python-cli.js');
  assert.equal(JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'))).bin['codemetrics-python-collect'], './dist/python-collect-cli.js');
  console.log(run(['--test', fileURLToPath(new URL('tests/evidence.test.mjs', import.meta.url))], install, { CODEMETRICS_PACKAGE_ROOT: packageRoot }));
  console.log('Installed Python package smoke passed.');
} finally { fs.rmSync(temporary, { recursive: true, force: true }); }
