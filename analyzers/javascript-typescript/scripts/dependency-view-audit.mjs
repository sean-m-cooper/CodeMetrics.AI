// Verify that the dependency split is additive on paired, pinned corpus runs.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { compare, gate, validateEvidence } from '../dist/evidence-tools.js';

const [directory, reportFile] = process.argv.slice(2);
assert(directory && reportFile, 'Usage: dependency-view-audit.mjs <paired-corpus-directory> <report-json>');
const summary = JSON.parse(fs.readFileSync(path.join(directory, 'summary.json'), 'utf8'));
assert.equal(summary.samples.length, 4);
const sha = data => createHash('sha256').update(data).digest('hex');
const compactView = ({ modules, ...view }) => view;
const samples = summary.samples.map(sample => {
  const root = path.join(directory, sample.id);
  const before = JSON.parse(fs.readFileSync(path.join(root, 'before.json'), 'utf8'));
  const currentBytes = fs.readFileSync(path.join(root, 'after.json'));
  const after = JSON.parse(currentBytes);
  validateEvidence(before); validateEvidence(after);
  assert.equal(before.analysis.status, 'complete'); assert.equal(after.analysis.status, 'complete');
  assert.notEqual(before.analysis.runId, after.analysis.runId);
  assert.equal(before.analysis.configurationFingerprint, after.analysis.configurationFingerprint);
  assert.deepEqual(before.population, after.population); assert.deepEqual(before.filters, after.filters);
  assert(fs.readFileSync(path.join(root, 'before.csv')).equals(fs.readFileSync(path.join(root, 'after.csv'))));
  const views = after.dimensions.architecture.dependencyGraph.dependencyViews;
  assert([1, 2].includes(views.version));
  assert.equal(views.classification, views.version === 1 ? 'direct-reexport-syntax-v1' : 'explicit-forwarding-v2');
  assert.equal(views.totalInternalValueEdgeCount,
    views.implementation.internalValueEdgeCount + views.reExports.internalValueEdgeCount - views.sharedInternalValueEdgeCount);
  const stripped = structuredClone(after.dimensions);
  const previous = structuredClone(before.dimensions);
  delete stripped.architecture.dependencyGraph.dependencyViews;
  delete previous.architecture.dependencyGraph.dependencyViews;
  for (const dimensions of [stripped, previous]) for (const edge of dimensions.architecture.dependencyGraph.dependencies) delete edge.roles;
  assert.deepEqual(stripped, previous, sample.id);
  const comparison = compare(after, before);
  assert.equal(comparison.compatible, true);
  for (const key of ['new', 'resolved', 'changed', 'severityIncreases']) assert.equal(comparison[key].length, 0);
  assert.equal(gate(comparison, 'warning', 0), false);
  const { details, moduleGraph, ...provenance } = sample;
  const oldEdges = before.dimensions.architecture.dependencyGraph.dependencies;
  const reclassifiedReferences = after.dimensions.architecture.dependencyGraph.dependencies.flatMap((edge, index) => {
    const old = oldEdges[index];
    const beforeRoles = old.roles ?? (old.usage === 'typeOnly' ? [] : [old.form === 'reExport' ? 'reExport' : 'implementation']);
    const afterRoles = edge.roles ?? (edge.usage === 'typeOnly' ? [] : [edge.form === 'reExport' ? 'reExport' : 'implementation']);
    return JSON.stringify(beforeRoles) === JSON.stringify(afterRoles) ? [] : [{ from: edge.from, line: edge.line,
      specifier: edge.specifier, form: edge.form, resolution: edge.resolution, beforeRoles, afterRoles }];
  });
  return { ...provenance, evidenceSha256: sha(currentBytes),
    reclassifiedReferences,
    existingEvidenceUnchangedApartFromDependencyViewsAndRoles: true, comparisonCompatible: true, warningAndScoreDropGatePassed: true,
    moduleGraph: { ...moduleGraph, dependencyViews: { ...views,
      implementation: compactView(views.implementation), reExports: compactView(views.reExports) } } };
});
fs.mkdirSync(path.dirname(path.resolve(reportFile)), { recursive: true });
fs.writeFileSync(reportFile, JSON.stringify({ ...summary, samples }, null, 2) + '\n');
console.log('Four paired samples: only dependency views and role metadata differ; all other evidence, measurements and comparison gates preserved.');
