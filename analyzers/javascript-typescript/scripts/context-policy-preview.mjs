// Calibration only: no production import, score change, finding or gate.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { fraction, roundScore } from '../dist/score-arithmetic.js';
import { preview } from './decomposition-preview.mjs';
import { validateEvidence } from '../dist/evidence-tools.js';

export function handlerCandidate(total, unexplained, gaps = 0) {
  assert.ok([total, unexplained, gaps].every(n=>Number.isSafeInteger(n) && n>=0));
  assert.ok(unexplained<=total);
  if (!total) return { score:null, reason:'No inspected handler bodies.' };
  // Gaps are use sites; neither invent distinct bodies nor credit them as handled.
  if (gaps) return { score:null, reason:'Unresolved callback uses prevent a complete candidate population.' };
  const population=fraction(10n*BigInt(total-unexplained),BigInt(total));
  return { score: Math.min(unexplained?9:10,roundScore(population)),
    reason:'Experimental: 10 - 10*unexplained/total; cap 9 when unexplained > 0; half-up once.' };
}

export function contextPreview(evidence) {
  validateEvidence(evidence);
  assert.equal(evidence.analysis.status,'complete');
  const decomposition=preview(evidence);
  const quality=evidence.dimensions.codeQuality;
  const measurements=new Map(quality.scoring.observations.functionContributions.map(r=>[r.id,r]));
  const rows=quality.componentDetails.decomposition.modules.flatMap(m=>m.functions.map(row=>({
    ...row,file:m.file,ownComplexity:measurements.get(row.id).ownComplexity,
    ownMaintainabilityIndex:measurements.get(row.id).ownMaintainabilityIndex })));
  const large=rows.filter(r=>r.ownedStatements>40);
  const additional=large.filter(r=>r.ownComplexity<=10 && r.ownMaintainabilityIndex>=65);
  const h=evidence.dimensions.errorHandling.handlerEvidence;
  assert.equal(h.version,2);assert.equal(h.countingUnit,'distinctHandlerBody');
  assert.equal(new Set(h.handlers.map(r=>r.id)).size,h.handlers.length);
  assert.equal(h.totalHandlers,h.handlers.length);
  const count=kind=>h.handlers.filter(r=>r.classification===kind).length;
  assert.equal(h.unexplainedEmptyHandlers,count('unexplainedEmpty'));
  assert.equal(h.documentedEmptyHandlers,count('documentedEmpty'));
  assert.equal(h.handlersContainingCode,count('containsCode'));
  assert.equal(h.totalHandlers,h.unexplainedEmptyHandlers+h.documentedEmptyHandlers+h.handlersContainingCode);
  assert.equal(h.handlerUseSites,h.handlers.reduce((sum,r)=>sum+r.uses.length,0));
  assert.equal(h.uninspectedRejectionCallbacks,h.uninspectedCallbacks.length);
  return {status:'experimental-unscored',runId:evidence.analysis.runId,auditId:evidence.analysis.auditId,
    decomposition:{...decomposition,reviewThresholds:{ownedStatementsGreaterThan:40,existingCcGreaterThan:10,existingMiLessThan:65},
      largeFunctions:large.length,alsoHighCc:large.filter(r=>r.ownComplexity>10).length,
      alsoLowMi:large.filter(r=>r.ownMaintainabilityIndex<65).length,additionalReviewCandidates:additional},
    handlers:{totalBodies:h.totalHandlers,unexplainedBodies:h.unexplainedEmptyHandlers,documentedBodies:h.documentedEmptyHandlers,
      bodiesContainingCode:h.handlersContainingCode,useSites:h.handlerUseSites,referencedUses:h.referencedCallbackUseSites,
      unresolvedUses:h.uninspectedRejectionCallbacks,candidate:handlerCandidate(h.totalHandlers,h.unexplainedEmptyHandlers,h.uninspectedRejectionCallbacks),
      unexplained:h.handlers.filter(r=>r.classification==='unexplainedEmpty'),unresolved:h.uninspectedCallbacks},
    productionScores:Object.fromEntries(Object.entries(evidence.dimensions).map(([id,d])=>[id,d.score??null]))};
}

export function verifyReview(samples, labels, readSource) {
  const remaining=new Map(samples.flatMap(s=>s.handlers.unexplained.map(h=>[`${s.id}:${h.id}`,{sample:s,handler:h}])));
  const counts={fallbackOutsideBody:0,declaredIntentOutsideBody:0,requiresReview:0};
  for(const label of labels) {
    const key=`${label.sample}:${label.handlerId}`,matched=remaining.get(key);
    assert.ok(matched,`Missing or duplicate review target: ${key}`);remaining.delete(key);
    assert.equal(label.revision,matched.sample.revision);
    assert.ok(label.runId && label.auditId,'Retain the original review provenance');
    assert.equal(label.file,matched.handler.file);assert.equal(label.line,matched.handler.line);assert.equal(label.useSites,matched.handler.uses.length);
    assert.ok(Object.hasOwn(counts,label.reviewLabel));assert.ok(label.rationale.trim());
    const bytes=readSource(label);
    assert.equal(createHash('sha256').update(bytes).digest('hex'),label.sourceSha256,'Reviewed source changed');
    const lines=bytes.toString('utf8').split(/\r?\n/),[start,end]=label.sourceRange;
    assert.ok(Number.isInteger(start)&&Number.isInteger(end)&&start>0&&end>=start&&end<=lines.length);
    assert.equal(createHash('sha256').update(lines.slice(start-1,end).join('\n')).digest('hex'),label.sourceRangeSha256);
    counts[label.reviewLabel]++;
  }
  assert.equal(remaining.size,0,'Every unexplained body must receive a review, not just selected examples');
  return {reviewedBodies:labels.length,...counts,
    provenance:'Original review run IDs retained. Reuse across fresh runs requires identical revision, handler identity, location, and source hashes.',
    interpretation:'Source-context counterexamples and unresolved review questions; unknowns are not labeled defects. No runtime correctness claim.'};
}

function main() {
  const [input,output,repositories]=process.argv.slice(2);assert.ok(input && output,'Usage: context-policy-preview.mjs <released-corpus> <output-json> [repositories]');
  const summary=JSON.parse(fs.readFileSync(path.join(input,'summary.json'),'utf8'));
  const samples=summary.samples.map(sample=>{
    const bytes=fs.readFileSync(path.join(input,sample.id,'evidence.json'));
    assert.equal(createHash('sha256').update(bytes).digest('hex'),sample.evidenceSha256);
    const evidence=JSON.parse(bytes);assert.equal(evidence.analysis.runId,sample.runId);assert.equal(evidence.analysis.auditId,sample.auditId);
    return {id:sample.id,revision:sample.revision,...contextPreview(evidence)};
  });
  const reviewPath=new URL('../../../shared/calibration/corpus/javascript-typescript-context-review.json',import.meta.url);
  const sourceReview=repositories?verifyReview(samples,JSON.parse(fs.readFileSync(reviewPath,'utf8')).handlers,label=>{
    const sample=summary.samples.find(s=>s.id===label.sample);
    const root=path.resolve(repositories,sample.repository),file=path.resolve(root,label.file);
    assert.ok(file.startsWith(root+path.sep));return fs.readFileSync(file);
  }):null;
  fs.writeFileSync(output,JSON.stringify({status:'experimental-unscored',generatedAtUtc:new Date().toISOString(),
    decisions:'Candidates require source-context review. No scores in this document are applied to evidence or gates.',sourceReview,samples},null,2)+'\n');
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) main();
