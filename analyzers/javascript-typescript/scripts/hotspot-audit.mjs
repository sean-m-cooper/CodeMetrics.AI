// Audit pinned source without importing or executing corpus code.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import ts from 'typescript';
import { isFunction } from '../dist/function-nodes.js';

const [repositories, corpusDirectory, output] = process.argv.slice(2);
if (!repositories || !corpusDirectory || !output)
  throw new Error('Usage: node scripts/hotspot-audit.mjs <repos> <corpus-results> <output.json>');
const corpus = JSON.parse(fs.readFileSync(path.join(corpusDirectory, 'summary.json'), 'utf8'));
const audited = [];
for (const sample of corpus.samples) {
  const root = path.resolve(repositories, sample.repository);
  const revision = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true });
  assert.equal(revision.status, 0);
  assert.equal(revision.stdout.trim(), sample.revision);
  const evidence = JSON.parse(fs.readFileSync(path.join(corpusDirectory, sample.id, 'after.json'), 'utf8'));
  const measured = evidence.dimensions.codeQuality.scoring.observations.topOffenders[0];
  const filename = path.join(root, measured.file);
  const source = ts.createSourceFile(filename, fs.readFileSync(filename, 'utf8'), ts.ScriptTarget.Latest, true);
  let target;
  function find(node) {
    if (isFunction(node) && node.getStart(source) === measured.sourceSpanStart) target = node;
    ts.forEachChild(node, find);
  }
  find(source);
  assert.ok(target?.body, `Missing function: ${measured.member}`);
  const nested = [];
  function count(rootNode, collectNested = false) {
    const events = [];
    function visit(node) {
      if (isFunction(node)) { if (collectNested) nested.push(node); return; }
      if (ts.isExpressionWithTypeArguments(node)) { visit(node.expression); return; }
      if (ts.isTypeNode(node) || ts.isClassDeclaration(node) || ts.isClassExpression(node) || ts.isEnumDeclaration(node) ||
          ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)) return;
      let kind;
      if (ts.isIfStatement(node)) kind = 'if';
      else if (ts.isForStatement(node) || ts.isForInStatement(node) || ts.isForOfStatement(node) || ts.isWhileStatement(node) || ts.isDoStatement(node)) kind = 'loop';
      else if (ts.isCatchClause(node)) kind = 'catch';
      else if (ts.isCaseClause(node)) kind = 'case';
      else if (ts.isConditionalExpression(node)) kind = 'ternary';
      else if (ts.isBinaryExpression(node) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(node.operatorToken.kind)) kind = node.operatorToken.getText(source);
      if (kind) events.push({ kind, line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1 });
      ts.forEachChild(node, visit);
    }
    visit(rootNode);
    return events;
  }
  const events = count(target.body, true);
  assert.equal(events.length + 1, measured.ownComplexity);
  const counts = Object.fromEntries([...new Set(events.map(e => e.kind))].sort().map(kind => [kind, events.filter(e => e.kind === kind).length]));
  if (measured.complexityBreakdown) {
    const names = { ifStatements: 'if', switchCases: 'case', loops: 'loop', catchClauses: 'catch',
      ternaryExpressions: 'ternary', logicalAnd: '&&', logicalOr: '||', nullishCoalescing: '??' };
    assert.equal(measured.complexityBreakdown.baseline, 1);
    assert.equal(measured.complexityBreakdown.total, measured.ownComplexity);
    assert.equal(measured.complexityBreakdown.decisionIncrements, events.length);
    assert.deepEqual(measured.complexityBreakdown.counts,
      Object.fromEntries(Object.entries(names).map(([field, kind]) => [field, counts[kind] ?? 0])));
  }
  const before = JSON.parse(fs.readFileSync(path.join(corpusDirectory, sample.id, 'before.json'), 'utf8'));
  const oldMeasurements = new Map(before.dimensions.maintainability.scoring.observations.functionContributions.map(f => [f.id, f]));
  const changed = evidence.dimensions.maintainability.scoring.observations.functionContributions.filter(f =>
    f.ownMaintainabilityIndex !== oldMeasurements.get(f.id)?.ownMaintainabilityIndex);
  const item = { id: sample.id, commit: sample.revision, file: measured.file, member: measured.member,
    line: measured.line, endLine: source.getLineAndCharacterOfPosition(target.end - 1).line + 1,
    ownComplexity: measured.ownComplexity, ownSourceLines: measured.ownSourceLines,
    beforeOwnMi: oldMeasurements.get(measured.id).ownMaintainabilityIndex, afterOwnMi: measured.ownMaintainabilityIndex,
    baseline: 1, decisionCounts: counts, decisionEvents: events, recordedComplexityBreakdown: measured.complexityBreakdown,
    directlyNestedFunctionsExcluded: nested.length,
    directlyNestedDecisionIncrementsExcluded: nested.reduce((sum, fn) => sum + (fn.body ? count(fn.body).length : 0), 0),
    changedMiFunctionCount: changed.length,
    beforeUnroundedMiScore: before.dimensions.maintainability.scoringDecision.inputs.unroundedScore,
    afterUnroundedMiScore: evidence.dimensions.maintainability.scoringDecision.inputs.unroundedScore };
  audited.push(item);
  console.log(`${sample.id}: CC ${measured.ownComplexity} = 1 + ${JSON.stringify(counts)}; ${changed.length} MI measurements corrected`);
}
fs.writeFileSync(output, JSON.stringify({ measurement: 'owned-function-body-v2-type-erasure',
  scope: 'Direct source audit of the highest-CC function in each pinned package selection; static measurements are not defect claims.', audited }, null, 2) + '\n');
