import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const run = (command, args, cwd) => {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', windowsHide: true, timeout: 120_000 });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  return result.stdout;
};
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const portable = read(path.join(root, 'plugin.json'));
const claude = read(path.join(root, '.claude-plugin/plugin.json'));
const { $schema: portableSchema, ...portableIdentity } = portable;
const { $schema: claudeSchema, ...claudeIdentity } = claude;
assert.deepEqual(portableIdentity, claudeIdentity, 'Host manifests must describe the same plugin.');
assert.equal(portable.name, 'codetestament');
assert.equal(portable.license, 'CC-BY-4.0');
const marketplace = read(path.join(root, '.claude-plugin/marketplace.json'));
assert.equal(marketplace.plugins.length, 1);
assert.equal(marketplace.plugins[0].name, portable.name);
assert.equal(marketplace.plugins[0].source, './');

// Git's tracked inventory excludes caches, outputs and unrelated analyzer sources.
const files = run('git', ['ls-files', '-z', '--', 'plugin.json', '.claude-plugin', 'skills/code-scorecard'], root)
  .split('\0').filter(Boolean);
for (const required of ['plugin.json', '.claude-plugin/plugin.json',
  'skills/code-scorecard/SKILL.md', 'skills/code-scorecard/compatibility.json',
  'skills/code-scorecard/LICENSE', 'skills/code-scorecard/NOTICE.md']) {
  assert.ok(files.includes(required), `Stage the plugin file before verification: ${required}`);
}
const destination = fs.mkdtempSync(path.join(os.tmpdir(), 'codetestament-plugin-'));
try {
  for (const file of files) {
    const target = path.join(destination, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(root, file), target);
  }
  console.log(run(process.execPath, ['--test',
    'skills/code-scorecard/scripts/tests/runtime.test.mjs',
    'skills/code-scorecard/scripts/tests/package-selection.test.mjs',
    'skills/code-scorecard/scripts/tests/rule-catalog.test.mjs',
    'skills/code-scorecard/evals/run.test.mjs'], destination));
  console.log(`Verified isolated plugin with ${files.length} tracked files.`);
} finally {
  // Only the unique directory created by this invocation is removed.
  fs.rmSync(destination, { recursive: true, force: true });
}
