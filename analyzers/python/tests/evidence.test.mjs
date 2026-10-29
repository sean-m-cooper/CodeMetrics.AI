import { createHash } from 'node:crypto';
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
const { fingerprint } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-error-context.js')));
const { complexityPopulation } = await import(pathToFileURL(path.join(packageRoot, 'dist/shared-source-facts/population.mjs')));
const { pythonTarget } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-analyzer.js')));

test('Windows short-name entry points use the same native identity as Python collection receipts', { skip: process.platform !== 'win32' }, t => {
  const alias = path.join(process.env.SystemDrive ?? 'C:', 'PROGRA~1');
  if (!fs.existsSync(alias)) return t.skip('This Windows host has no Program Files short-name alias.');
  const native = fs.realpathSync.native(alias);
  assert.equal(pythonTarget(alias).root, native);
  assert.equal(pythonTarget(alias).entryPoint, native);
  assert.equal(pythonTarget(alias).target, native);
});

function assertSupportedPythonScores(evidence) {
  assert.equal(evidence.dimensions.codeQuality.status, 'scored');
  assert.equal(typeof evidence.dimensions.codeQuality.score, 'number');
  assert.ok(Object.entries(evidence.dimensions).every(([key, d]) => key === 'codeQuality' || evidence.tool.version === '0.15.0' && key === 'performanceAsync' || d.status === 'skipped' && !('score' in d)));
  if (evidence.tool.version === '0.15.0') {
    const d = evidence.dimensions.performanceAsync, expected = d.asyncScoring.summary.score;
    assert.equal(d.status, expected === null ? 'skipped' : 'scored');
    assert.equal(d.score, expected ?? undefined);
  }
  assert.equal(evidence.dimensions.codeQuality.experimentalPreview, undefined);
}

function fixture(t) {
  const directory = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'python evidence ')));
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


function incompleteScoringReports(evidence, base = evidence.dimensions.performanceAsync.review) {
  const helpers = structuredClone(evidence.dimensions.performanceAsync.helperReview);
  helpers.asyncRowsSha256 = fingerprint(base.functions);
  Object.assign(helpers, {status: 'incomplete', functions: [], summary: null, diagnostics: [{code: 'async-helper-analysis', message: 'Dependent measurement failed.'}]});
  const score = structuredClone(evidence.dimensions.performanceAsync.asyncScoring);
  score.helperRowsSha256 = fingerprint([]);
  Object.assign(score, {status: 'incomplete', functions: [], summary: null, diagnostics: [{code: 'async-scoring-analysis', message: 'Dependent measurement failed.'}]});
  return [helpers, score];
}

function incompleteAsyncAdapters(evidence, base = evidence.dimensions.performanceAsync.review) {
  const report = structuredClone(evidence.dimensions.performanceAsync.adapterReview);
  Object.assign(report, {status:'incomplete', functions:[], summary:null,
    diagnostics:[{code:'async-adapter-analysis', message:'Adapter source changed.'}]});
  if (!base.functions.length) report.asyncRowsSha256 = createHash('sha256').update('[]').digest('hex');
  return report;
}

const adapterSource = `from functools import partial as p
from asgiref.sync import sync_to_async as wrap
import asyncio
def local(value):
    return value + 1
@wrap(thread_sensitive=False)
def decorated(value):
    return value + 1
async def work(items, func):
    callback = p(local, 1, *items, limit=2)
    await asyncio.to_thread(callback)
    callback()
    await asyncio.to_thread(p(local, 2))
    await wrap(p(local, 3))()
    wrapped = wrap(callback)
    await wrapped()
    await decorated(1)
    await asyncio.to_thread(decorated, 2)
    deferred = (callback() for x in items)
    func = p(func, *items)
    await asyncio.to_thread(func)
`;

test('callback adapters survive packaging with separate constructions and references', t => {
  const f = fixture(t); f.write('work.py', adapterSource);
  const {evidence, output} = f.run(), d = evidence.dimensions.performanceAsync, r = d.adapterReview;
  f.inspect(output); validateEvidence(evidence); assertSupportedPythonScores(evidence);
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  assert.equal(r.policy, 'python/async-callback-adapters-review-v1');
  const direct = r.summary.contexts['async-body'];
  assert.equal(direct.partialConstructions, 4);
  assert.equal(direct.partialCalleeReferences, 1);
  assert.equal(direct.partialCallbackReferences, 6);
  assert.equal(direct.decoratedCalleeReferences, 1); assert.equal(direct.decoratedCallbackReferences, 1);
  assert.equal(r.summary.contexts['deferred-generator-body'].partialCalleeReferences, 0);
  assert.equal(d.status, 'scored'); assert.equal(d.score, d.asyncScoring.summary.score);
  assert.equal(evidence.dimensions.errorHandling.status, 'skipped');
  assert.ok(fs.existsSync(path.join(packageRoot, 'dist/python/ASYNC_ADAPTERS.md')));
});

test('callback adapter validator rejects changed provenance, forged links and counts', t => {
  const f = fixture(t); f.write('work.py', adapterSource);
  const original = f.run().evidence;
  const calls = r => r.functions.flatMap(owner => owner.calls);
  const partial = r => calls(r).find(c => c.partialConstruction).partialConstruction;
  const stored = r => calls(r).find(c => c.partialCallee.status === 'stored-partial-syntax').partialCallee;
  const decorator = r => calls(r).find(c => c.decoratedCallee.status === 'decorated-wrapper-syntax').decoratedCallee;
  const mutations = [
    r => { r.runId = '11111111-1111-4111-8111-111111111111'; },
    r => { r.asyncRowsSha256 = '0'.repeat(64); }, r => { r.configuration.policy = 'other'; },
    r => { r.producer.version = '99'; }, r => { r.sourceFactsPolicy = 'other'; },
    r => { r.functions.pop(); }, r => { r.functions[0].sourceSha256 = '0'.repeat(64); },
    r => { calls(r)[0].executionContext = 'deferred-generator-body'; },
    r => { partial(r).construction.endLine++; }, r => { partial(r).importTarget = 'other.partial'; },
    r => { partial(r).callback.reference.definition.ownerId = '0'.repeat(64); },
    r => { partial(r).boundPositionalExpressions[0].shape = 'name'; },
    r => { partial(r).boundPositionalExpressions[0].literalValue = 'secret'; },
    r => { partial(r).keywordExpressions[0].unpacking = true; },
    r => { stored(r).constructionCallId = '0'.repeat(64); }, r => { stored(r).assignment.endLine = 999; },
    r => { stored(r).name = 'not_callback'; },
    r => { stored(r).partial.keywordExpressions = []; },
    r => { decorator(r).definition.functionKind = 'AsyncFunctionDef'; },
    r => { decorator(r).definition.ownerId = '0'.repeat(64); },
    r => { decorator(r).decorator.options[0].name = 'executor'; },
    r => { decorator(r).decorator.options[0].value = 'false'; },
    r => { decorator(r).decorator.line = 999; },
    r => { r.summary.contexts['async-body'].partialConstructions++; },
    r => { r.summary.populations.production.contexts['async-body'].decoratedCalleeReferences++; },
    r => { r.status = 'incomplete'; r.diagnostics = [{code:'async-adapter-analysis',message:'failed'}]; },
    r => { calls(r)[0].score = 10; },
  ];
  for (const mutate of mutations) {
    const copy = structuredClone(original); mutate(copy.dimensions.performanceAsync.adapterReview);
    assert.throws(() => validateEvidence(copy), String(mutate));
  }
  for (const value of [null, {}, undefined]) {
    const copy = structuredClone(original); copy.dimensions.performanceAsync.adapterReview = value;
    assert.throws(() => validateEvidence(copy));
  }
});

test('adapter-only failure removes all scores and findings', async t => {
  const f = fixture(t); f.write('work.py', adapterSource);
  const current = f.run().evidence, cq = current.dimensions.codeQuality, eh = current.dimensions.errorHandling;
  const {pythonEvidence} = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview,
    cq.decomposition, eh.review, eh.contextReview, eh.transferReview, eh.operationReview,
    current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, incompleteAsyncAdapters(current), ...incompleteScoringReports(current));
  validateEvidence(failed); assert.equal(failed.analysis.status, 'incomplete');
  assert.ok(Object.values(failed.dimensions).every(d => d.score === undefined && !d.findings.length));
  const output = path.join(f.directory, 'adapter-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('saved 0.13 reports retain their contract without fabricated adapter evidence', t => {
  const old = JSON.parse(fs.readFileSync(new URL('fixtures/python-v013.json', import.meta.url)));
  validateEvidence(old); assertSupportedPythonScores(old); assert.equal(old.tool.version, '0.13.0');
  assert.equal(old.dimensions.performanceAsync.adapterReview, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(old));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  const copy = structuredClone(old); copy.dimensions.performanceAsync.adapterReview = null;
  assert.throws(() => validateEvidence(copy));
  f.write('work.py', adapterSource); f.inspect(f.run().output, ['--baseline', output], 2);
});

test('execution failures cannot carry detached callback adapter evidence', t => {
  const f = fixture(t); f.write('work.py', adapterSource);
  const current = f.run().evidence;
  const failed = f.run(['--python', path.join(f.directory, 'missing-python.exe')], 2).evidence;
  validateEvidence(failed); assert.equal(failed.dimensions.codeQuality.sourceFacts, null);
  failed.dimensions.performanceAsync.adapterReview = current.dimensions.performanceAsync.adapterReview;
  assert.throws(() => validateEvidence(failed), /requires? source facts/);
});

function incompleteAsyncContext(evidence, base = evidence.dimensions.performanceAsync.review) {
  const report = structuredClone(evidence.dimensions.performanceAsync.contextReview);
  Object.assign(report, {status: 'incomplete', functions: [], summary: null,
    diagnostics: [{code:'async-context-analysis', message:'Context source changed.'}]});
  if (!base.functions.length) report.asyncRowsSha256 = createHash('sha256').update('[]').digest('hex');
  return report;
}

function incompleteOperations(evidence, transfers = evidence.dimensions.errorHandling.transferReview) {
  const report = structuredClone(evidence.dimensions.errorHandling.operationReview);
  Object.assign(report, { transferReview: transfers, status: 'incomplete', handlers: [], summary: null,
    diagnostics: transfers.diagnostics.length ? transfers.diagnostics : [{code:'exception-operation-analysis', message:'Call/return source changed.'}] });
  if (!transfers.handlers.length) report.transferRowsSha256 = createHash('sha256').update('[]').digest('hex');
  return report;
}

test('async context packaging preserves links, inventory and unscored boundaries', t => {
  const f = fixture(t);
  f.write('work.py', `from asgiref.sync import sync_to_async as wrap
import asyncio
def local(value):
    return value + 1
async def work(items, client):
    wrapped = wrap(local)
    await wrapped(1)
    await wrapped(2)
    await asyncio.to_thread(local)
    await asyncio.to_thread(local(3))
    await wrap(local)(4)
    client.wait()
    deferred = (wrapped(x) for x in items)
    return local(5)
`);
  f.write('tests/test_async.py', 'async def test_work():\n    missing()\n');
  const {evidence, output} = f.run(['--include-tests']);
  assertSupportedPythonScores(evidence);
  const d = evidence.dimensions.performanceAsync, r = d.contextReview;
  assert.equal(d.status, 'scored'); assert.equal(d.score, d.asyncScoring.summary.score);
  assert.equal(evidence.dimensions.errorHandling.status, 'skipped');
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  assert.deepEqual(r.selection, d.review.selection);
  const direct = r.summary.populations.production.contexts['async-body'];
  assert.equal(direct.storedWrapperInvocationLinks, 2);
  assert.equal(direct.localCalleeLinks, 2);
  assert.equal(direct.localCallbackReferenceLinks, 6);
  assert.equal(r.summary.populations.test.contexts['async-body'].callExpressions, 1);
  assert.equal(r.summary.contexts['deferred-generator-body'].storedWrapperInvocationLinks, 0);
  const linked = r.functions.flatMap(row => row.calls).filter(call => call.storedWrapper?.status === 'stored-wrapper-syntax');
  assert.equal(new Set(linked.map(call => call.storedWrapper.definition.constructionCallId)).size, 1);
  assert.equal(direct.unresolvedInventory.reduce((sum, group) => sum + group.callExpressions, 0), direct.unresolvedImportCallExpressions);
  f.inspect(output);
});

test('async context rejects stale provenance, forged links and corrupted inventory', t => {
  const f = fixture(t);
  f.write('work.py', 'from asgiref.sync import sync_to_async as wrap\ndef local(x):\n    return x + 1\nasync def work():\n    wrapped = wrap(local)\n    await wrapped(1)\n    local(2)\n');
  const original = f.run().evidence;
  const owner = r => r.functions.find(row => row.status === 'observed');
  const stored = r => owner(r).calls.find(call => call.storedWrapper?.status === 'stored-wrapper-syntax');
  const mutations = [
    r => { r.runId = '00000000-0000-4000-8000-000000000000'; },
    r => { r.auditId = '00000000-0000-4000-8000-000000000000'; },
    r => { r.asyncRowsSha256 = '0'.repeat(64); },
    r => { r.configuration.maxExamplesPerGroup = 10; },
    r => { r.functions.reverse(); },
    r => { owner(r).calls.reverse(); },
    r => { owner(r).sourceSha256 = '0'.repeat(64); },
    r => { owner(r).calls[0].callId = '0'.repeat(64); },
    r => { owner(r).calls[0].shape = 'attribute-path'; },
    r => { stored(r).unresolvedReason = null; },
    r => { stored(r).executionContext = 'thread'; },
    r => { stored(r).storedWrapper.definition.constructionCallId = '0'.repeat(64); },
    r => { stored(r).storedWrapper.definition.assignment.endLine = 500; },
    r => { stored(r).storedWrapper.definition.name = 'another'; },
    r => { stored(r).storedWrapper.definition.importTarget = 'asyncio.to_thread'; },
    r => { stored(r).callback.reference.definition.ownerId = owner(r).id; },
    r => { stored(r).callback.reference.definition.functionKind = 'AsyncFunctionDef'; },
    r => { stored(r).callback.reference.definition.column++; },
    r => { stored(r).callback.evaluation = 'safe-offload'; },
    r => { r.summary.contexts['async-body'].storedWrapperInvocationLinks++; },
    r => { r.summary.contexts['async-body'].unresolvedInventory[0].examples[0].callId = '0'.repeat(64); },
    r => { r.summary.contexts['async-body'].unresolvedInventory[0].callExpressions++; },
    r => { r.functions = []; },
    r => { r.status = 'incomplete'; },
    r => { r.score = 10; }
  ];
  for (const mutate of mutations) {
    const copy = structuredClone(original); mutate(copy.dimensions.performanceAsync.contextReview);
    assert.throws(() => validateEvidence(copy), String(mutate));
  }
  for (const replacement of [null, undefined]) {
    const copy = structuredClone(original); copy.dimensions.performanceAsync.contextReview = replacement;
    assert.throws(() => validateEvidence(copy));
  }
});

test('async-context-only failure withholds every score and finding', async t => {
  const f = fixture(t); f.write('work.py', 'async def work():\n    return 1\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality, eh = current.dimensions.errorHandling;
  const {pythonEvidence} = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview,
    cq.decomposition, eh.review, eh.contextReview, eh.transferReview, eh.operationReview, current.dimensions.performanceAsync.review, incompleteAsyncContext(current), incompleteAsyncAdapters(current), ...incompleteScoringReports(current));
  validateEvidence(failed); assert.equal(failed.analysis.status, 'incomplete');
  assert.ok(Object.values(failed.dimensions).every(d => d.score === undefined && !d.findings.length));
  const output = path.join(f.directory, 'context-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('saved 0.12 reports keep their original contract and comparison support', t => {
  const old = JSON.parse(fs.readFileSync(new URL('fixtures/python-v012.json', import.meta.url)));
  validateEvidence(old); assertSupportedPythonScores(old); assert.equal(old.tool.version, '0.12.0');
  assert.equal(old.dimensions.performanceAsync.contextReview, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(old));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  const altered = structuredClone(old); altered.dimensions.performanceAsync.contextReview = null;
  assert.throws(() => validateEvidence(altered));
  f.write('work.py', 'async def work():\n    return 1\n');
  f.inspect(f.run().output, ['--baseline', output], 2);
});

function incompleteTransfers(evidence, context = evidence.dimensions.errorHandling.contextReview) {
  const report = structuredClone(evidence.dimensions.errorHandling.transferReview);
  Object.assign(report, { contextReview: context, status: 'incomplete', handlers: [], summary: null,
    diagnostics: context.diagnostics.length ? context.diagnostics : [{code:'exception-transfer-analysis', message:'Transfer source changed.'}] });
  if (!context.contexts.length) report.contextRowsSha256 = createHash('sha256').update('[]').digest('hex');
  return report;
}

test('async packaging preserves ownership, callbacks, deferred bodies and unscored dimensions', t => {
  const f = fixture(t);
  f.write('work.py', `import time as clock
import asyncio
from asgiref.sync import sync_to_async as wrap
async def work(items):
    # Deliberate watchdog probe, not an automatically judged defect.
    clock.sleep(0)
    await asyncio.to_thread(clock.sleep, 1)
    await asyncio.to_thread(clock.sleep(2))
    await wrap(clock.sleep)(1)
    deferred = (clock.sleep(x) for x in items)
    def nested():
        clock.sleep(3)
    callback = lambda: clock.sleep(4)
    values = [clock.sleep(5) for clock in items]
    return callback
async def shadow(clock):
    clock.sleep(6)
`);
  f.write('tests/test_work.py', 'import time\nasync def test_work():\n    time.sleep(1)\n');
  const { evidence, output } = f.run(['--include-tests']);
  assertSupportedPythonScores(evidence);
  const d = evidence.dimensions.performanceAsync, r = d.review;
  assert.equal(d.status, 'scored'); assert.equal(d.score, d.asyncScoring.summary.score);
  assert.equal(evidence.dimensions.errorHandling.status, 'skipped'); assert.equal(evidence.dimensions.errorHandling.score, undefined);
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  assert.deepEqual(r.selection, evidence.dimensions.codeQuality.sourceFacts.selection);
  assert.equal(r.summary.asyncFunctions, 3); assert.equal(r.summary.excludedSynchronousFunctions, 2);
  assert.equal(r.summary.deferredGeneratorCallExpressions, 1);
  assert.equal(r.summary.populations.production.asyncBodyCallCategories['blocking-call-candidate'], 2);
  assert.equal(r.summary.populations.test.asyncBodyCallCategories['blocking-call-candidate'], 1);
  assert.equal(r.summary.asyncBodyCallCategories['offload-call'], 2);
  assert.equal(r.summary.asyncBodyCallCategories['offload-wrapper-construction'], 1);
  assert.equal(r.summary.asyncBodyCallCategories['offload-wrapped-invocation'], 1);
  const calls = r.functions.flatMap(row => row.measurement?.calls ?? []);
  assert.equal(new Set(calls.map(call => call.id)).size, calls.length);
  assert.ok(calls.some(call => call.callback?.evaluation === 'eager-call-expression'));
  assert.ok(calls.some(call => call.binding.reason === 'comprehension-local-binding'));
  f.inspect(output);
});

test('async evidence rejects stale, malformed, incoherent and artificially scored observations', t => {
  const f = fixture(t); f.write('work.py', 'import time\nimport asyncio\nasync def work():\n    time.sleep(1)\n    await asyncio.to_thread(time.sleep, 2)\n');
  const original = f.run().evidence;
  for (const mutate of [
    e => { delete e.dimensions.performanceAsync.review; },
    e => { e.dimensions.performanceAsync.review = null; },
    e => { e.dimensions.performanceAsync.score = 10; e.dimensions.performanceAsync.status = 'scored'; },
    e => { e.dimensions.errorHandling.score = 10; e.dimensions.errorHandling.status = 'scored'; },
    e => { e.dimensions.performanceAsync.findings.push(e.dimensions.codeQuality.findings[0] ?? {}); },
    e => { e.dimensions.performanceAsync.review.runId = '00000000-0000-4000-8000-000000000000'; },
    e => { e.dimensions.performanceAsync.review.auditId = '00000000-0000-4000-8000-000000000000'; },
    e => { e.dimensions.performanceAsync.review.subject.root += '/other'; },
    e => { e.dimensions.performanceAsync.review.selection.files[0].contentSha256 = '0'.repeat(64); },
    e => { e.dimensions.performanceAsync.review.configuration.blockingCatalogue = []; },
    e => { e.dimensions.performanceAsync.review.functions[0].sourceSha256 = '0'.repeat(64); },
    e => { e.dimensions.performanceAsync.review.functions[0].functionKind = 'FunctionDef'; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].id = '0'.repeat(64); },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].endLine = 999; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].category = 'offload-call'; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].binding.status = 'unresolved'; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].executionContext = 'thread'; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[0].directlyAwaited = true; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls[1].callback.evaluation = 'safe'; },
    e => { e.dimensions.performanceAsync.review.functions[0].measurement.calls.push(e.dimensions.performanceAsync.review.functions[0].measurement.calls[0]); },
    e => { e.dimensions.performanceAsync.review.functions = []; },
    e => { e.dimensions.performanceAsync.review.summary.asyncFunctions++; },
    e => { e.dimensions.performanceAsync.review.summary.functionsWithBlockingCandidates = 0; },
    e => { e.dimensions.performanceAsync.review.status = 'incomplete'; },
    e => { e.dimensions.performanceAsync.review.score = 10; },
    e => { e.analysis.ruleset = 'python-unknown'; }
  ]) {
    const copy = structuredClone(original); mutate(copy); assert.throws(() => validateEvidence(copy), String(mutate));
  }
});

test('async callbacks retain Unicode, dynamic attributes and unsupported argument layouts', t => {
  const f = fixture(t); f.write('work.py', `import asyncio as tâche
from time import sleep as dormir
async def café(values):
    await tâche.to_thread(dormir, 1)
    await tâche.to_thread(factory().callback, 1)
    await tâche.to_thread(*values)
    await tâche.to_thread(lambda: dormir(2))
    yield await tâche.sleep(0)
`);
  const {evidence, output} = f.run();
  const row = evidence.dimensions.performanceAsync.review.functions.find(row => row.member === 'café');
  assert.equal(row.measurement.kind, 'async-generator');
  assert.ok(row.measurement.calls.some(call => call.callback?.binding?.syntax === 'dormir'));
  assert.ok(row.measurement.calls.some(call => call.callback?.kind === 'unresolved-argument-layout'));
  f.inspect(output);
});

test('sync-only selections do not become a perfect async score', t => {
  const f = fixture(t); f.write('work.py', 'def work():\n    return 1\n');
  const e = f.run().evidence, r = e.dimensions.performanceAsync.review;
  assertSupportedPythonScores(e); assert.equal(r.summary.availability, 'unavailable');
  assert.equal(r.summary.asyncBodyCallExpressions, null); assert.equal(r.summary.functionsWithBlockingCandidates, null);
  assert.equal(e.dimensions.performanceAsync.score, undefined);
});

test('async-only failure withholds every score and finding', async t => {
  const f = fixture(t); f.write('work.py', 'async def work():\n    return 1\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality, eh = current.dimensions.errorHandling;
  const report = structuredClone(current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview);
  Object.assign(report, {status: 'incomplete', functions: [], summary: null, diagnostics: [{code:'async-usage-analysis', message:'Source changed.'}]});
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, cq.decomposition, eh.review, eh.contextReview, eh.transferReview, eh.operationReview, report, incompleteAsyncContext(current, report), incompleteAsyncAdapters(current, report), ...incompleteScoringReports(current, report));
  validateEvidence(failed); assert.equal(failed.analysis.status, 'incomplete');
  assert.ok(Object.values(failed.dimensions).every(d => d.score === undefined && !d.findings.length));
  const output = path.join(f.directory, 'async-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('saved 0.11 reports remain valid without invented async observations', t => {
  const old = JSON.parse(fs.readFileSync(new URL('fixtures/python-v011.json', import.meta.url)));
  validateEvidence(old); assertSupportedPythonScores(old); assert.equal(old.tool.version, '0.11.0');
  assert.equal(old.dimensions.performanceAsync.review, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(old));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  const altered = structuredClone(old); altered.dimensions.performanceAsync.review = null;
  assert.throws(() => validateEvidence(altered));
  f.write('work.py', 'async def work():\n    return 1\n');
  f.inspect(f.run().output, ['--baseline', output], 2);
});

test('call/return packaging validates syntax, inventory, and preserved unscored context', t => {
  const f = fixture(t);
  f.write('work.py', ['logger.exception(error)\n        raise', 'return recover(error)', 'notify(error)\n        cleanup()',
    'if ready: return recover(error)', 'error = other\n        logger.warning(error)', 'raise Wrapped() from error',
    'return None'].map((body, i) => `def f${i}():\n    try: work()\n    except Error as error:\n        ${body}\n`).join('\n'));
  f.write('tests/test_work.py', 'try: work()\nexcept* Error: recover()\n');
  const { evidence, output } = f.run(['--include-tests']);
  const d = evidence.dimensions.errorHandling, r = d.operationReview;
  assertSupportedPythonScores(evidence); assert.equal(d.score, undefined); assert.deepEqual(d.findings, []);
  assert.deepEqual(r.transferReview, d.transferReview);
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  assert.equal(r.summary.handlers, 8); assert.equal(r.summary.callExpressions, 8); assert.equal(r.summary.returnStatements, 3);
  assert.equal(r.summary.recognizedShapes, 5); assert.equal(r.summary.newlyRecognizedShapes, 3);
  assert.equal(r.summary.inputUnresolvedInventory.handlers, 6);
  assert.equal(r.summary.inputUnresolvedInventory.multiStatement, 3);
  assert.equal(r.summary.populations.test.unresolved, 1);
  f.inspect(output);
  const called = r => r.handlers.find(row => row.classification.pattern === 'direct-calls-then-bare-raise');
  const returned = r => r.handlers.find(row => row.classification.pattern === 'direct-symbol-or-call-return');
  for (const mutate of [
    r => { r.runId = '33333333-3333-4333-8333-333333333333'; },
    r => { r.transferRowsSha256 = '0'.repeat(64); },
    r => { r.transferReview.handlers[0].line++; },
    r => { r.configuration.loggingNames.pop(); },
    r => { r.handlers.pop(); }, r => { r.handlers[1] = r.handlers[0]; },
    r => { called(r).sourceSha256 = '0'.repeat(64); }, r => { called(r).suite.statementCount++; },
    r => { called(r).calls[0].id = '0'.repeat(64); }, r => { called(r).calls[0].endColumn++; },
    r => { called(r).calls[0].loggingNameSyntax = null; }, r => { called(r).calls[0].immediatelyAwaited = true; },
    r => { called(r).calls[0].directStatement = false; }, r => { called(r).calls[0].starredArguments = 100; },
    r => { called(r).calls[0].containsNamedExpression = true; }, r => { called(r).calls[0].ancestorsTruncated = true; },
    r => { called(r).calls.pop(); }, r => { called(r).calls[0].callee.matchesHandlerNameSyntax = true; },
    r => { returned(r).returns[0].direct = false; }, r => { returned(r).returns[0].awaitedExpression = returned(r).returns[0].expression; },
    r => { called(r).classification.pattern = 'proven-logging'; }, r => { called(r).originalClassification.pattern = 'safe'; },
    r => { r.summary.callExpressions++; }, r => { r.summary.inputUnresolvedInventory.multiStatement++; },
    r => { r.summary.inputUnresolvedInventory.suiteShapes[0].handlers++; },
    r => { r.summary.populations.production.recognizedShapes++; }, r => { r.score = 10; }, r => { r.status = 'incomplete'; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.errorHandling.operationReview);
    assert.throws(() => validateEvidence(copy), String(mutate));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.errorHandling.operationReview; assert.throws(() => validateEvidence(missing));
});

test('call/return extents preserve nested calls, Unicode, await, ownership and bounded previews', t => {
  const f = fixture(t);
  f.write('work.py', 'async def café():\n    try: work()\n    except Error as é:\n        await logger.error(é)\n        return await dépôt.récupérer(é)\n' +
    'def nested():\n    try: work()\n    except Error:\n        result = (later(x) for x in values)\n        def hidden(): return hidden_call()\n        return factory()()()\n' +
    'try: work()\nexcept Error:\n' + '    notify()\n'.repeat(40) + '    raise\n' +
    'try: work()\nexcept Error:\n' + '    '.repeat(1) + 'notify(\n        inner()\n    )\n' +
    'def parenthesized():\n    try: work()\n    except Error:\n        (\n            logger.error("🐍")\n        )\n');
  const { evidence, output } = f.run(); const r = evidence.dimensions.errorHandling.operationReview;
  assert.equal(r.summary.immediatelyAwaitedCalls, 2);
  const asyncRow = r.handlers.find(row => row.owner.member === 'café');
  assert.equal(asyncRow.returns[0].awaitedExpression.symbol, 'dépôt.récupérer'); assert.equal(asyncRow.classification.status, 'unresolved');
  const nested = r.handlers.find(row => row.owner.member === 'nested');
  const repeated = nested.calls.filter(call => call.callee.symbol === 'factory' || call.callee.kind === 'call');
  assert.equal(repeated.length, 3); assert.equal(new Set(repeated.map(call => call.id)).size, 3);
  assert.ok(nested.calls.some(call => call.ancestors.some(a => a.kind === 'GeneratorExp')));
  assert.ok(r.handlers.every(row => row.calls.every(call => call.callee.symbol !== 'hidden_call')));
  assert.ok(r.handlers.some(row => row.suite.truncated && row.classification.pattern === 'direct-calls-then-bare-raise'));
  f.inspect(output);
});

test('operation-only failure with complete prior measurements withholds scores', async t => {
  const f = fixture(t); f.write('work.py', 'def work():\n    try: call()\n    except Missing: return recover()\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality, d = current.dimensions.errorHandling;
  const report = incompleteOperations(current);
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, cq.decomposition, d.review, d.contextReview, d.transferReview, report, current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed); assert.equal(failed.analysis.status, 'incomplete');
  assert.ok(Object.values(failed.dimensions).every(item => item.score === undefined && !item.findings.length));
  const output = path.join(f.directory, 'operation-failed.json'); fs.writeFileSync(output, JSON.stringify(failed)); f.inspect(output, [], 2);
  const malformed = structuredClone(failed); malformed.dimensions.errorHandling.operationReview.handlers = d.operationReview.handlers;
  assert.throws(() => validateEvidence(malformed));
});

test('saved 0.10 reports retain their own contract without invented call/return evidence', t => {
  const old = JSON.parse(fs.readFileSync(new URL('fixtures/python-v010.json', import.meta.url)));
  validateEvidence(old); assertSupportedPythonScores(old); assert.equal(old.tool.version, '0.10.0');
  assert.equal(old.dimensions.errorHandling.operationReview, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(old));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  for (const mutate of [e => { e.dimensions.errorHandling.operationReview = null; }, e => { e.tool.version = '0.11.0'; }]) {
    const copy = structuredClone(old); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
  f.write('work.py', 'def work(): return None\n'); f.inspect(f.run().output, ['--baseline', output], 2);
});

test('raise/chaining evidence preserves syntax, prior context and unscored disposition', t => {
  const f = fixture(t);
  f.write('work.py', ['raise', 'raise error', 'raise pkg.NewError(error)', 'raise NewError() from error',
    'raise NewError() from None', 'raise NewError() from other', 'if ready: raise NewError() from error',
    'log(error)\n        raise'].map((body, index) => `def f${index}():\n    try: call()\n    except Missing as error:\n        ${body}\n`).join('\n'));
  f.write('tests/test_work.py', 'try: exercise()\nexcept* Missing as error: raise NewError() from error\n');
  const { evidence, output } = f.run(['--include-tests']);
  const d = evidence.dimensions.errorHandling, r = d.transferReview;
  assertSupportedPythonScores(evidence); assert.equal(d.score, undefined); assert.deepEqual(d.findings, []);
  assert.deepEqual(r.contextReview, d.contextReview);
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  assert.equal(r.summary.handlers, 9); assert.equal(r.summary.raiseStatements, 9);
  assert.equal(r.summary.recognizedShapes, 5); assert.equal(r.summary.newlyRecognizedShapes, 4);
  assert.equal(r.summary.populations.test.unresolved, 1);
  assert.equal(r.summary.handlerNameRaiseSyntax, 1); assert.equal(r.summary.handlerNameCauseSyntax, 3);
  f.inspect(output);
  const raised = report => report.handlers.find(row => row.classification.pattern === 'direct-raise-from-handler-name');
  for (const mutate of [
    r => { r.runId = '33333333-3333-4333-8333-333333333333'; },
    r => { r.contextRowsSha256 = '0'.repeat(64); },
    r => { r.contextReview.contexts[0].line++; },
    r => { r.configuration.maxRaises++; },
    r => { r.handlers.pop(); },
    r => { r.handlers[1] = r.handlers[0]; },
    r => { raised(r).sourceSha256 = '0'.repeat(64); },
    r => { raised(r).handlerNameSyntax = 'other'; },
    r => { raised(r).raises[0].id = '0'.repeat(64); },
    r => { raised(r).raises[0].line++; },
    r => { raised(r).raises[0].endLine = 999; },
    r => { raised(r).raises[0].exception.kind = 'name'; },
    r => { raised(r).raises[0].cause.matchesHandlerNameSyntax = false; },
    r => { raised(r).raises[0].direct = false; },
    r => { raised(r).raises[0].containersTruncated = true; },
    r => { raised(r).raises[0].containsNamedExpression = true; },
    r => { raised(r).raises.push(raised(r).raises[0]); },
    r => { raised(r).originalClassification.pattern = 'safe-translation'; },
    r => { raised(r).classification.pattern = 'safe-translation'; },
    r => { r.summary.raiseStatements++; },
    r => { r.summary.handlerNameCauseSyntax++; },
    r => { r.summary.populations.production.unresolved++; },
    r => { r.score = 10; },
    r => { r.status = 'incomplete'; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.errorHandling.transferReview);
    assert.throws(() => validateEvidence(copy), String(mutate));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.errorHandling.transferReview;
  assert.throws(() => validateEvidence(missing));
  const scored = structuredClone(evidence); scored.dimensions.errorHandling.score = 10; scored.dimensions.errorHandling.status = 'scored';
  assert.throws(() => validateEvidence(scored));
});

test('packaged transfer bounds and ownership preserve Unicode, nested and same-line raises', t => {
  const f = fixture(t), longName = 'é'.repeat(201);
  f.write('work.py', 'def café():\n    try: call()\n    except Missing as é:\n        raise NewError("🐍") from é\n' +
    `try: call()\nexcept Missing as ${longName}: raise ${longName}\n` +
    'try: call()\nexcept Missing:\n    if ready:\n        raise One(); raise Two()\n' +
    '    try: call()\n    except Other: raise Three()\n' +
    '    def nested():\n        raise Hidden()\n' +
    'try: call()\nexcept Missing:\n' + '    call()\n'.repeat(35) + '    raise Last()\n');
  const { evidence, output } = f.run();
  const r = evidence.dimensions.errorHandling.transferReview;
  assert.equal(r.summary.raiseStatements, 6);
  assert.equal(r.summary.handlerNameRaiseSyntax, 1);
  const unicode = r.handlers.find(row => row.handlerNameSyntax === 'é');
  assert.equal(unicode.classification.pattern, 'direct-raise-from-handler-name');
  const long = r.handlers.find(row => row.handlerNameSyntax === longName);
  assert.equal(long.raises[0].exception.symbol, null); assert.equal(long.raises[0].exception.matchesHandlerNameSyntax, true);
  assert.equal(long.classification.status, 'unresolved');
  assert.ok(r.handlers.some(row => row.raises.length === 2 && row.raises.every(item => !item.direct)));
  assert.ok(r.handlers.some(row => row.classification.unresolvedReasons.includes('nested-handler')));
  assert.ok(r.handlers.every(row => row.raises.every(item => item.exception.symbol !== 'Hidden')));
  f.inspect(output);
});

test('transfer-only failure with complete previous evidence withholds every score and finding', async t => {
  const f = fixture(t); f.write('work.py', 'def work():\n    try: call()\n    except Missing: raise NewError()\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality, d = current.dimensions.errorHandling;
  const report = incompleteTransfers(current);
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, cq.decomposition, d.review, d.contextReview, report, incompleteOperations(current, report), current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed); assert.equal(failed.analysis.status, 'incomplete');
  assert.ok(Object.values(failed.dimensions).every(item => item.score === undefined && !item.findings.length));
  const output = path.join(f.directory, 'transfer-failed.json'); fs.writeFileSync(output, JSON.stringify(failed)); f.inspect(output, [], 2);
  const malformed = structuredClone(failed); malformed.dimensions.errorHandling.transferReview.handlers = d.transferReview.handlers;
  assert.throws(() => validateEvidence(malformed));
});

test('saved 0.9 context reports retain their own contract and self-comparison', t => {
  const old = JSON.parse(fs.readFileSync(new URL('fixtures/python-v09.json', import.meta.url)));
  validateEvidence(old); assertSupportedPythonScores(old);
  assert.equal(old.tool.version, '0.9.0'); assert.equal(old.dimensions.errorHandling.transferReview, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(old));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  for (const mutate of [e => { e.dimensions.errorHandling.transferReview = null; }, e => { e.tool.version = '0.10.0'; }]) {
    const copy = structuredClone(old); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
  f.write('work.py', 'def work(): return None\n'); f.inspect(f.run().output, ['--baseline', output], 2);
});

test('context packaging preserves bounded syntax, Unicode documentation and conservative classifications', t => {
  const f = fixture(t);
  f.write('work.py', `try: import optional
except ImportError: pass
try: from another import feature
except (ImportError, ModuleNotFoundError): feature = None
def fallback():
    """Café handling 🐍."""
    try: call()
    except Missing: return None
    except Other: raise
def after():
    try: call()
    except Missing: pass
    return False
try: call()
except Missing:
    # Café fallback 🐍 is intentional.
    pass
with resource():
    try: call()
    except Missing: raise
try: call()
except* Missing: raise
def finalized():
    try: call()
    except Missing: return None
    finally: cleanup()
`);
  f.write('tests/test_work.py', 'try: exercise()\nexcept Missing: pass\n');
  const { evidence, output } = f.run(['--include-tests']);
  const d = evidence.dimensions.errorHandling, r = d.contextReview;
  assertSupportedPythonScores(evidence);
  assert.deepEqual(r.handlerReview, d.review);
  assert.equal(r.summary.handlers, 10);
  assert.equal(r.summary.recognizedShapes, 6); assert.equal(r.summary.unresolved, 4);
  assert.equal(r.summary.populations.test.handlers, 1);
  assert.equal(Object.keys(r.summary.byPattern).length, 6);
  assert.ok(r.contexts.some(row => row.surroundingDocumentation.items.some(item => item.text.includes('🐍'))));
  assert.ok(r.contexts.every(row => row.disposition === 'review-only'));
  assert.equal(r.runId, evidence.analysis.runId); assert.equal(r.auditId, evidence.analysis.auditId);
  f.inspect(output);
  for (const mutate of [
    r => { r.runId = '33333333-3333-4333-8333-333333333333'; },
    r => { r.handlerRowsSha256 = '0'.repeat(64); },
    r => { r.handlerReview.handlers[0].line++; },
    r => { r.contexts.pop(); },
    r => { r.contexts[1] = r.contexts[0]; },
    r => { r.contexts[0].sourceSha256 = '0'.repeat(64); },
    r => { r.contexts[0].owner.kind = 'function'; },
    r => { r.contexts[0].population = 'generated'; },
    r => { r.contexts[0].tryStatement.handlerLines = []; },
    r => { r.contexts[0].tryStatement.body.statementCount++; },
    r => { r.contexts[0].handlerBody.statements[0].endLine = 999; },
    r => { r.contexts[0].followingInSameSuite.truncated = !r.contexts[0].followingInSameSuite.truncated; },
    r => { r.contexts[0].classification.pattern = 'safe-optional-import'; },
    r => { r.contexts[0].classification.evidenceLines = [999]; },
    r => { r.contexts.find(row => row.classification.status === 'unresolved').classification.unresolvedReasons = []; },
    r => { r.contexts.find(row => row.classification.pattern === 'direct-literal-fallback-return').enclosingConstructs.push({kind:'With', line:1}); },
    r => { r.contexts.find(row => row.surroundingDocumentation.items.length).surroundingDocumentation.items[0].text = 'x'.repeat(2001); },
    r => { r.summary.recognizedShapes++; },
    r => { r.summary.populations.production.handlers++; },
    r => { r.configuration.maxFollowingStatements++; },
    r => { r.status = 'incomplete'; },
    r => { r.score = 10; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.errorHandling.contextReview);
    assert.throws(() => validateEvidence(copy), String(mutate));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.errorHandling.contextReview;
  assert.throws(() => validateEvidence(missing));
});

test('context truncation and import binding veto survive packaging', t => {
  const f = fixture(t);
  f.write('work.py', 'def shadow(ImportError): return 1\ntry: import optional\nexcept ImportError: pass\n' +
    'def large():\n    """' + '🐍'.repeat(2005) + '"""\n    try:\n' + '        call()\n'.repeat(40) +
    '    except Missing:\n' + '        call()\n'.repeat(40) + '    call()\n'.repeat(5));
  const { evidence, output } = f.run(), r = evidence.dimensions.errorHandling.contextReview;
  assert.deepEqual(r.contexts[0].classification.unresolvedReasons, ['import-exception-binding-unresolved']);
  const row = r.contexts[1];
  assert.equal(row.tryStatement.body.statementCount, 40); assert.equal(row.tryStatement.body.statements.length, 32);
  assert.equal(row.handlerBody.truncated, true); assert.equal(row.followingInSameSuite.statements.length, 3);
  assert.equal(row.followingInSameSuite.truncated, true); assert.equal(row.surroundingDocumentation.truncated, true);
  assert.equal([...row.surroundingDocumentation.items[0].text].length, 2000);
  f.inspect(output);
});

test('context-only failure with complete base measurements withholds scores and findings', async t => {
  const f = fixture(t); f.write('work.py', 'def work():\n    try: call()\n    except Missing: return None\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality;
  const context = structuredClone(current.dimensions.errorHandling.contextReview);
  Object.assign(context, { status: 'incomplete', contexts: [], summary: null,
    diagnostics: [{code:'exception-context-analysis', message:'Source changed during context analysis.'}] });
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, cq.decomposition, current.dimensions.errorHandling.review, context, incompleteTransfers(current, context), incompleteOperations(current, incompleteTransfers(current, context)), current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete'); assert.equal(failed.dimensions.errorHandling.status, 'failed');
  assert.ok(Object.values(failed.dimensions).every(d => d.score === undefined && !d.findings.length));
  const output = path.join(f.directory, 'context-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
});

test('saved 0.8 handler evidence remains valid without fabricated context', t => {
  // Generated by the archived, installed 0.8.0 adapter before the context package build.
  const historical = JSON.parse(fs.readFileSync(new URL('fixtures/python-v08.json', import.meta.url)));
  validateEvidence(historical); assertSupportedPythonScores(historical);
  assert.equal(historical.tool.version, '0.8.0'); assert.equal(historical.dimensions.errorHandling.contextReview, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json'); fs.writeFileSync(output, JSON.stringify(historical));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  for (const mutate of [e => { e.dimensions.errorHandling.contextReview = null; }, e => { e.tool.version = '0.9.0'; }]) {
    const copy = structuredClone(historical); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
  f.write('work.py', 'def work(): return None\n');
  f.inspect(f.run().output, ['--baseline', output], 2);
});

test('handler evidence preserves context, populations and unscored disposition', t => {
  const f = fixture(t);
  f.write('work.py', 'try: import optional\nexcept ImportError: pass\nclass Client:\n    try: load()\n    except Missing:\n        # Default configuration applies.\n        pass\n    def work(self):\n        try: run()\n        except Missing: return None\n        except Other: raise\n        except Last: logger.exception("failed")\n');
  f.write('tests/test_work.py', 'try: exercise()\nexcept* Expected: raise\n');
  const { evidence, output } = f.run(['--include-tests']);
  const dimension = evidence.dimensions.errorHandling, report = dimension.review;
  assertSupportedPythonScores(evidence);
  assert.equal(dimension.status, 'skipped'); assert.deepEqual(dimension.findings, []);
  assert.equal(report.runId, evidence.analysis.runId); assert.equal(report.auditId, evidence.analysis.auditId);
  assert.equal(report.summary.handlers, 6);
  assert.equal(report.summary.populations.production.handlers, 5); assert.equal(report.summary.populations.test.handlers, 1);
  assert.equal(report.summary.byClassification['documented-no-op'], 1);
  assert.equal(report.summary.byClassification['undocumented-no-op'], 1);
  assert.equal(report.summary.byOwnerKind['class-body'], 1);
  assert.ok(report.handlers.some(row => row.exceptionGroup));
  assert.ok(report.handlers.every(row => row.disposition === 'review-only'));
  f.inspect(output);
  for (const mutate of [
    r => { r.runId = '33333333-3333-4333-8333-333333333333'; },
    r => { r.auditId = '33333333-3333-4333-8333-333333333333'; },
    r => { r.handlers[0].sourceSha256 = '0'.repeat(64); },
    r => { r.handlers[0].population = 'generated'; },
    r => { r.handlers.push(r.handlers[0]); },
    r => { r.handlers[0].owner.functionId = '0'.repeat(64); },
    r => { r.handlers.find(row => row.owner.kind === 'function').owner.member = 'forged'; },
    r => { r.handlers[0].classification = 'defective'; },
    r => { r.handlers[0].exceptionTypeSyntax = null; },
    r => { r.handlers[0].bodyObservations.raises[0].line = 999; },
    r => { r.handlers[0].enclosingFinallyLines = [999]; },
    r => { r.handlers.find(row => row.classification === 'undocumented-no-op').bodyObservations.calls = 1; },
    r => { r.handlers.find(row => row.classification === 'documented-no-op').documentation = []; },
    r => { r.handlers.find(row => row.classification === 'documented-no-op').documentation[0].text = 'noqa'; },
    r => { r.summary.handlers++; },
    r => { r.summary.byClassification['terminal-raise']++; },
    r => { r.summary.populations.production.handlers++; },
    r => { r.configuration.maxHandlers++; },
    r => { r.score = 10; },
  ]) {
    const copy = structuredClone(evidence); mutate(copy.dimensions.errorHandling.review);
    assert.throws(() => validateEvidence(copy));
  }
  const missing = structuredClone(evidence); delete missing.dimensions.errorHandling.review;
  assert.throws(() => validateEvidence(missing));
});

test('handler failure with otherwise complete evidence withholds the score and all findings', async t => {
  const f = fixture(t); f.write('work.py', 'def work():\n    try: call()\n    except: pass\n');
  const current = f.run().evidence, cq = current.dimensions.codeQuality;
  const review = structuredClone(current.dimensions.errorHandling.review, current.dimensions.errorHandling.contextReview, current.dimensions.errorHandling.transferReview, current.dimensions.errorHandling.operationReview);
  review.status = 'incomplete'; review.handlers = []; review.summary = null;
  review.diagnostics = [{ code: 'error-handling-analysis', message: 'Source changed during handler analysis.' }];
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const context = structuredClone(current.dimensions.errorHandling.contextReview);
  Object.assign(context, { handlerReview: review, handlerRowsSha256: createHash('sha256').update('[]').digest('hex'), status: 'incomplete', contexts: [], summary: null, diagnostics: review.diagnostics });
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, cq.decomposition, review, context, incompleteTransfers(current, context), incompleteOperations(current, incompleteTransfers(current, context)), current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.errorHandling.status, 'failed');
  assert.equal(failed.dimensions.codeQuality.score, undefined);
  assert.ok(Object.values(failed.dimensions).every(d => !d.findings.length));
  const output = path.join(f.directory, 'handlers-failed.json'); fs.writeFileSync(output, JSON.stringify(failed));
  f.inspect(output, [], 2);
  const copy = structuredClone(failed); copy.dimensions.errorHandling.review.handlers = current.dimensions.errorHandling.review.handlers;
  assert.throws(() => validateEvidence(copy));
});

test('saved 0.7 scoring evidence retains its original contract and self-comparison', t => {
  const historical = JSON.parse(fs.readFileSync(new URL('fixtures/python-v07.json', import.meta.url)));
  validateEvidence(historical);
  assert.equal(historical.tool.version, '0.7.0'); assertSupportedPythonScores(historical);
  assert.equal(historical.dimensions.errorHandling.review, undefined);
  const f = fixture(t), output = path.join(f.directory, 'historical.json');
  fs.writeFileSync(output, JSON.stringify(historical));
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  const copy = structuredClone(historical); copy.dimensions.errorHandling.review = null;
  assert.throws(() => validateEvidence(copy));
});

test('packaged CLI emits one validated Method Complexity score with current IDs', t => {
  const f = fixture(t);
  f.write('pyproject.toml', '[build-system]\nbuild-backend="do_not_execute"\n');
  f.write('work.py', 'def work(x):\n' + '    if x: x -= 1\n'.repeat(10) + '    return x\n');
  const ids = ['--run-id', '11111111-1111-4111-8111-111111111111', '--audit-id', '22222222-2222-4222-8222-222222222222'];
  const { output, evidence } = f.run(ids, 0, path.join(f.root, 'pyproject.toml'));
  assert.equal(evidence.analysis.status, 'complete'); assert.equal(evidence.population.members, 1);
  assert.equal(evidence.analysis.calibration, 'baseline');
  assertSupportedPythonScores(evidence);
  assert.deepEqual(evidence.dimensions.codeQuality.methodComplexity.population, complexityPopulation(evidence.dimensions.codeQuality.sourceFacts));
  assert.equal(evidence.dimensions.codeQuality.findings[0].severity, 'info');
  const inspection = path.join(f.directory, 'inspection.json');
  f.inspect(output, ['--inspect-output', inspection, '--expected-ecosystem', 'python', '--expected-root', f.root,
    '--expected-entry-point', path.join(f.root, 'pyproject.toml'), '--expected-run-id', ids[1], '--expected-audit-id', ids[3]]);
  const inspected = JSON.parse(fs.readFileSync(inspection));
  assert.equal(inspected.usable, true); assert.equal(inspected.compatibility.comparisonAvailable, true);
  f.inspect(output, ['--expected-run-id', '33333333-3333-4333-8333-333333333333'], 2);
  f.inspect(output, ['--baseline', output, '--max-score-drop', '0']);
  const sarif = path.join(f.directory, 'review.sarif'); f.inspect(output, ['--sarif', sarif]);
  assert.equal(JSON.parse(fs.readFileSync(sarif)).runs[0].results[0].level, 'note');
});

test('selected parse failures withhold previews and findings and remain diagnosable', t => {
  const f = fixture(t); f.write('good.py', 'def f(): return 1\n'); f.write('bad.py', 'def broken(:');
  const { output, evidence } = f.run([], 2);
  assert.equal(evidence.analysis.status, 'incomplete'); assert.equal(evidence.dimensions.codeQuality.status, 'failed');
  assert.equal(evidence.dimensions.codeQuality.sourceFacts.functions.length, 1);
  assert.equal(evidence.dimensions.codeQuality.methodComplexity.population, null);
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
  assert.equal(evidence.tool.version, '0.15.0');
  assert.equal(facts.producer.version, '0.4.0-experimental');
  assert.equal(facts.measurementPolicy, 'python/owned-function-complexity-v2');
  assert.equal(evidence.dimensions.codeQuality.scope.id, evidence.dimensions.codeQuality.methodComplexity.scoringPolicy);
  assert.equal(evidence.analysis.ruleset, 'python-2026-10-28-async-scoring-v1');
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
  assert.equal(evidence.population.members, 0); assert.equal(evidence.dimensions.codeQuality.methodComplexity.status, 'unavailable');
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

test('validator rejects altered scores, forged populations and altered contributions', t => {
  const f = fixture(t); f.write('work.py', 'def f(x):\n    if x: return 1\n    return 2\n');
  const { evidence } = f.run();
  for (const mutate of [
    value => { value.dimensions.codeQuality.status = 'scored'; value.dimensions.codeQuality.score = 1; },
    value => { value.population.members++; },
    value => { value.dimensions.testing.status = 'scored'; value.dimensions.testing.score = 10; },
    value => { value.dimensions.codeQuality.methodComplexity.population.score = 2; },
    value => { value.dimensions.codeQuality.sourceFacts.functions[0].population = 'test'; },
    value => { value.analysis.configurationFingerprint = 'forged'; },
    value => { value.dimensions.codeQuality.sourceFacts.measurementPolicy = 'unknown-policy'; },
    value => { delete value.analysis.runId; },
    value => { value.tool.version = '0.4.0-experimental'; value.analysis.ruleset = 'unknown';
      delete value.dimensions.codeQuality.coverageReview; delete value.dimensions.codeQuality.decomposition; },
    value => { value.tool.version = '0.4.0-experimental'; value.analysis.ruleset = 'unknown';
      delete value.dimensions.codeQuality.coverageReview; delete value.dimensions.codeQuality.decomposition;
      delete value.dimensions.codeQuality.methodComplexity; }
  ]) {
    const copy = structuredClone(evidence); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
});

test('invalid selection produces diagnostics and no score', t => {
  const f = fixture(t); f.write('work.py', 'def f(): return 1\n');
  const { evidence } = f.run(['--include', '../outside/**'], 2);
  assert.equal(evidence.analysis.diagnostics[0].kind, 'invalid-selection');
  assert.equal(evidence.dimensions.codeQuality.methodComplexity.population, null);
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
  assertSupportedPythonScores(evidence);
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
  assert.deepEqual(failed.findings, []); assert.equal(failed.methodComplexity.population, null);
});

test('duplication failure with complete complexity still withholds the score and all findings', async t => {
  const f = fixture(t); f.write('one.py', 'def one(): return 1\n');
  const current = f.run().evidence;
  const report = structuredClone(current.dimensions.codeQuality.duplication);
  report.status = 'incomplete'; report.summary = null; report.excludedFunctions = [];
  report.diagnostics = [{ code: 'duplication-analysis', message: 'Selected source changed after population measurement.' }];
  const { pythonEvidence } = await import(pathToFileURL(path.join(packageRoot, 'dist/python-evidence.js')));
  const failed = pythonEvidence(current.dimensions.codeQuality.sourceFacts, current.subject, current.analysis, undefined, report, current.dimensions.codeQuality.coverageReview, current.dimensions.codeQuality.decomposition, current.dimensions.errorHandling.review, current.dimensions.errorHandling.contextReview, current.dimensions.errorHandling.transferReview, current.dimensions.errorHandling.operationReview, current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.codeQuality.methodComplexity.population, null);
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
  assertSupportedPythonScores(evidence);
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
    assert.equal(failed.dimensions.codeQuality.methodComplexity.population, null);
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


test('decomposition preserves unscored size, nesting, population and unavailable lambda evidence', t => {
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
  const lambda = report.functions.find(row => row.functionKind === 'Lambda');
  assert.equal(lambda.measurement, null);
  assert.ok(cq.methodComplexity.population.contributions.some(row => row.id === lambda.id));
  assert.deepEqual(cq.findings, []); // Size and nesting add no thresholds or findings.
  assertSupportedPythonScores(evidence);
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
  const failed = pythonEvidence(cq.sourceFacts, current.subject, current.analysis, undefined, cq.duplication, cq.coverageReview, report, current.dimensions.errorHandling.review, current.dimensions.errorHandling.contextReview, current.dimensions.errorHandling.transferReview, current.dimensions.errorHandling.operationReview, current.dimensions.performanceAsync.review, current.dimensions.performanceAsync.contextReview, current.dimensions.performanceAsync.adapterReview, ...incompleteScoringReports(current));
  validateEvidence(failed);
  assert.equal(failed.analysis.status, 'incomplete');
  assert.equal(failed.dimensions.codeQuality.methodComplexity.population, null);
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
    e => { e.tool.version = '0.7.0'; },
    e => { e.analysis.ruleset = 'python-2026-10-13-method-complexity-v1'; },
    e => { e.dimensions.codeQuality.coverageReview.runId = '11111111-1111-4111-8111-111111111111'; }
  ]) {
    const copy = structuredClone(historical); mutate(copy); assert.throws(() => validateEvidence(copy));
  }
});


test('production-only scoring cannot be padded by included test, support or generated functions', t => {
  const f = fixture(t);
  f.write('work.py', 'def hard(x):\n' + '    if x: x -= 1\n'.repeat(20) + '    return x\n');
  f.write('tests/test_work.py', 'def test_easy(): return 1\n');
  f.write('setup.py', 'def support(): return 1\n');
  f.write('generated.py', '# Generated by a tool\ndef generated(): return 1\n');
  const before = f.run().evidence;
  const { evidence, output } = f.run(['--include-tests', '--include-support', '--include-generated']);
  assert.equal(evidence.population.members, 4);
  assert.equal(evidence.dimensions.codeQuality.score, 3.8);
  assert.equal(evidence.dimensions.codeQuality.score, before.dimensions.codeQuality.score);
  const details = evidence.dimensions.codeQuality.methodComplexity;
  assert.equal(details.selectedFunctions, 4); assert.equal(details.eligibleFunctions, 1);
  assert.deepEqual(details.excludedPopulations, { test: 1, support: 1, generated: 1 });
  assert.equal(details.population.contributions.length, 1);
  f.inspect(output);
  const onlyTests = f.run(['--include-tests', '--include', 'tests/**']).evidence;
  assert.equal(onlyTests.dimensions.codeQuality.status, 'skipped');
  assert.equal(onlyTests.dimensions.codeQuality.score, undefined);
  assert.equal(onlyTests.dimensions.codeQuality.methodComplexity.population, null);
  assert.equal(onlyTests.dimensions.codeQuality.methodComplexity.eligibleFunctions, 0);
  const promoted = f.run(['--include', 'tests/**', '--production-path', 'tests/**']).evidence;
  assert.equal(promoted.dimensions.codeQuality.score, 10);
});

test('supported scoring uses accepted anchors, weighting and half-up rounding without review deductions', t => {
  const f = fixture(t);
  const source = cc => 'def work(x):\n' + '    if x: x -= 1\n'.repeat(cc - 1) + '    return x\n';
  for (const [cc, score] of [[1, 10], [3, 10], [5, 8], [10, 6], [20, 4], [40, 0], [60, 0]]) {
    f.write('work.py', source(cc));
    const cq = f.run().evidence.dimensions.codeQuality;
    assert.equal(cq.score, score); assert.equal(cq.scoringDecision.finalScore, score);
    assert.equal(cq.methodComplexity.population.contributions[0].weightNumerator, 1);
    assert.equal(cq.methodComplexity.population.contributions[0].weightDenominator, 1);
  }
  // One CC=20 body at 40%, sixteen remaining bodies with a score mean of 8.25.
  f.write('work.py', source(20));
  for (let i = 0; i < 16; i++) f.write(`rest${i}.py`, source(i < 2 ? 3 : 5));
  const cq = f.run().evidence.dimensions.codeQuality;
  assert.equal(cq.methodComplexity.population.unroundedScore, 6.55);
  assert.equal(cq.score, 6.6);
  assert.ok(cq.findings.some(f => f.category === 'exactBodyDuplicationReview'));
  assert.ok(cq.scoringDecision.findingEffects.filter(f => f.category === 'exactBodyDuplicationReview').every(f => f.effect !== 'policyInput'));
  assert.ok(cq.scoringDecision.findingEffects.filter(f => f.category === 'methodComplexityReview').every(f => f.effect === 'policyInput'));
});

test('supported comparisons gate only recorded dimension changes and reject historical previews', t => {
  const f = fixture(t); f.write('work.py', 'def work(x): return x\n');
  const before = f.run();
  f.write('work.py', 'def work(x):\n' + '    if x: x -= 1\n'.repeat(19) + '    return x\n');
  const after = f.run();
  f.inspect(after.output, ['--baseline', before.output]);
  f.inspect(after.output, ['--baseline', before.output, '--max-score-drop', '1'], 1);
  f.inspect(after.output, ['--baseline', before.output, '--max-score-drop', '6']);
  const empty = f.run(['--include', 'missing/**']);
  f.inspect(empty.output, ['--baseline', empty.output], 2);
  for (const version of ['05', '06']) {
    const old = fileURLToPath(new URL(`fixtures/python-v${version}.json`, import.meta.url));
    const e = JSON.parse(fs.readFileSync(old)); validateEvidence(e);
    assert.equal(e.dimensions.codeQuality.score, undefined);
    assert.ok(e.dimensions.codeQuality.experimentalPreview);
    f.inspect(old, ['--baseline', old, '--max-score-drop', '0'], 2);
    f.inspect(after.output, ['--baseline', old, '--max-score-drop', '0'], 2);
    const forged = structuredClone(e); forged.dimensions.codeQuality.status = 'scored'; forged.dimensions.codeQuality.score = 10;
    assert.throws(() => validateEvidence(forged));
  }
});


test('async scoring packages owner contributions, exclusions and exact affected rates', t => {
  const f = fixture(t);
  f.write('work.py', `import time
import asyncio
def blocking():
    time.sleep(1)
async def hazard():
    blocking()
    blocking()
async def documented():
    # Synchronous operation required by the device protocol
    time.sleep(1)
async def offloaded():
    await asyncio.to_thread(blocking)
async def unresolved(client):
    await client.fetch()
`);
  f.write('tests/test_work.py', 'import time\nasync def test_sleep(): time.sleep(1)\n');
  const {evidence, output} = f.run(['--include-tests']); f.inspect(output);
  const d = evidence.dimensions.performanceAsync;
  assert.equal(d.status, 'scored'); assert.equal(d.score, 0);
  assert.equal(d.asyncScoring.summary.eligibleOwners, 4);
  assert.equal(d.asyncScoring.summary.affectedOwners, 1);
  assert.equal(d.asyncScoring.summary.distinctScoredSourceCalls, 1);
  assert.deepEqual(d.asyncScoring.summary.excludedOwnersByPopulation, {test: 1});
  assert.equal(d.asyncScoring.functions.find(row => row.member === 'documented').observations[0].classification, 'documented-choice');
  assert.equal(d.asyncScoring.functions.find(row => row.member === 'offloaded').affected, false);
  assert.equal(d.asyncScoring.summary.ownersWithUnresolvedPaths, 1);
  assert.equal(d.findings.filter(row => row.severity === 'warning').length, 1);
  assert.equal(evidence.dimensions.errorHandling.status, 'skipped');
  assert.equal(evidence.dimensions.errorHandling.score, undefined);
  for (const name of ['async_scoring.py', 'async_helper_paths.py', 'review_cli.py', 'ASYNC_SCORING.md'])
    assert.ok(fs.existsSync(path.join(packageRoot, 'dist/python', name)));
});

test('async scoring canonical ladder uses exact endpoints and no-async is unavailable', async () => {
  const {asyncOwnerScore} = await import(pathToFileURL(path.join(packageRoot, 'dist/python-async-scoring.js')));
  for (const [b,n,expected] of [[0,0,null],[0,1,10],[1,101,10],[1,100,9],[1,51,9],[1,50,8],[1,21,8],[1,20,6],[1,10,6],[11,100,4],[15,100,4],[16,100,2],[20,100,2],[21,100,0],[1,1,0]])
    assert.equal(asyncOwnerScore(b,n),expected);
  for (const [b,n] of [[-1,1],[1,0],[2,1],[0.5,1]]) assert.throws(() => asyncOwnerScore(b,n));
});

test('async scoring rejects altered contributions, provenance, paths and summaries', t => {
  const f=fixture(t); f.write('work.py', 'import time\nimport asyncio\ndef helper(): time.sleep(1)\nasync def run():\n    time.sleep(1)\n    helper()\n    await asyncio.to_thread(time.sleep, 1)\n');
  const original=f.run().evidence;
  for (const mutate of [
    d => { delete d.helperReview; }, d => { delete d.asyncScoring; }, d => { d.asyncScoring=null; },
    d => { d.helperReview.runId='00000000-0000-4000-8000-000000000000'; },
    d => { d.asyncScoring.auditId='00000000-0000-4000-8000-000000000000'; },
    d => { d.helperReview.asyncRowsSha256='0'.repeat(64); },
    d => { d.asyncScoring.helperRowsSha256='0'.repeat(64); },
    d => { d.asyncScoring.configuration.ladder[0].score=9; },
    d => { d.helperReview.functions[0].traces[0].path[0].ownerId='unknown'; },
    d => { d.helperReview.functions[0].traces[0].executionContext='thread-helper-callback'; },
    d => { d.asyncScoring.functions[0].eligible=false; },
    d => { d.asyncScoring.functions[0].affected=false; },
    d => { d.asyncScoring.functions[0].observations.pop(); },
    d => { d.asyncScoring.functions[0].observations[0].classification='documented-choice'; },
    d => { d.asyncScoring.functions[0].observations[0].documentation=[{file:'elsewhere.py',callId:'fake',line:1,text:'Blocking is intentional'}]; },
    d => { d.asyncScoring.functions[0].unresolvedPaths.push({}); },
    d => { d.asyncScoring.summary.score=10; }, d => { d.asyncScoring.summary.affectedOwners=0; },
    d => { d.asyncScoring.summary.eligibleOwners++; }, d => { d.asyncScoring.functions.push(d.asyncScoring.functions[0]); },
    d => { d.asyncScoring.summary.extra=1; }, d => { d.score=10; },
    d => { d.helperReview.extra=true; }
  ]) {
    const copy=structuredClone(original); mutate(copy.dimensions.performanceAsync);
    assert.throws(()=>validateEvidence(copy),String(mutate));
  }
});

test('helper and scoring failures withhold all dimension scores and findings', async t => {
  const f=fixture(t); f.write('work.py','import time\nasync def work(): time.sleep(1)\n');
  const current=f.run().evidence, cq=current.dimensions.codeQuality, eh=current.dimensions.errorHandling, pa=current.dimensions.performanceAsync;
  const {pythonEvidence}=await import(pathToFileURL(path.join(packageRoot,'dist/python-evidence.js')));
  for (const helperFailed of [false,true]) {
    const reports=incompleteScoringReports(current);
    if (!helperFailed) { reports[0]=pa.helperReview; reports[1].helperRowsSha256=fingerprint(pa.helperReview.functions); }
    const failed=pythonEvidence(cq.sourceFacts,current.subject,current.analysis,undefined,cq.duplication,cq.coverageReview,cq.decomposition,
      eh.review,eh.contextReview,eh.transferReview,eh.operationReview,pa.review,pa.contextReview,pa.adapterReview,...reports);
    validateEvidence(failed); assert.equal(failed.analysis.status,'incomplete');
    assert.ok(Object.values(failed.dimensions).every(d=>d.score===undefined&&!d.findings.length));
  }
});

test('historical 0.14 reports keep unscored async and cannot acquire new scoring fields', t => {
  const old=JSON.parse(fs.readFileSync(new URL('fixtures/python-v014.json',import.meta.url)));
  validateEvidence(old); assert.equal(old.dimensions.performanceAsync.status,'skipped');
  const f=fixture(t), historical=path.join(f.directory,'historical.json'); fs.writeFileSync(historical,JSON.stringify(old));
  f.inspect(historical,['--baseline',historical,'--max-score-drop','0']);
  for (const key of ['helperReview','asyncScoring']) {
    const changed=structuredClone(old); changed.dimensions.performanceAsync[key]=null;
    assert.throws(()=>validateEvidence(changed));
  }
  f.write('work.py','async def work(): pass\n'); f.inspect(f.run().output,['--baseline',historical],2);
});

test('detached helper/scoring payloads cannot survive execution failure without source facts', t => {
  const f=fixture(t); f.write('work.py','async def work(): pass\n');
  const current=f.run().evidence;
  const failure=f.run(['--python',path.join(f.directory,'missing-python.exe')],2).evidence;
  for (const key of ['helperReview','asyncScoring']) {
    const changed=structuredClone(failure); changed.dimensions.performanceAsync[key]=current.dimensions.performanceAsync[key];
    assert.throws(()=>validateEvidence(changed),/requires? source facts/);
  }
});


test('compatible async score drops gate while complexity remains unchanged', t => {
  const f=fixture(t);
  const source = count => 'import time\n' + Array.from({length:25},(_,i)=>`async def work_${i}():\n    ${i<count ? 'time.sleep(1)' : 'pass'}\n`).join('');
  f.write('work.py',source(1)); const before=f.run();
  f.write('work.py',source(2)); const after=f.run();
  assert.equal(before.evidence.dimensions.performanceAsync.score,8);
  assert.equal(after.evidence.dimensions.performanceAsync.score,6);
  assert.equal(before.evidence.dimensions.codeQuality.score,after.evidence.dimensions.codeQuality.score);
  f.inspect(after.output,['--baseline',before.output,'--max-score-drop','1'],1);
  f.inspect(after.output,['--baseline',before.output,'--max-score-drop','2']);
});
