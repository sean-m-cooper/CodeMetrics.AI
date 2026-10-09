// Source-only classifier calibration. No target dependencies or application scripts run.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { analyze } from '../dist/analyzer.js';
import { validateEvidence } from '../dist/evidence-tools.js';
import { contextPreview, verifyReview } from './context-policy-preview.mjs';

const [repositories, baseline, output] = process.argv.slice(2);
assert.ok(repositories && baseline && output, 'Usage: handler-disposition-corpus.mjs <repositories> <released-context-corpus> <output>');
const corpus=JSON.parse(fs.readFileSync(new URL('../../../shared/calibration/corpus/javascript-typescript-context.json',import.meta.url),'utf8'));
const labels=JSON.parse(fs.readFileSync(new URL('../../../shared/calibration/corpus/javascript-typescript-context-review.json',import.meta.url),'utf8')).handlers;
const version=JSON.parse(fs.readFileSync(new URL('../package.json',import.meta.url),'utf8')).version;
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
function git(root,args){
  const run=spawnSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true});
  assert.equal(run.status,0,run.stderr);return run.stdout.trim();
}
fs.mkdirSync(output,{recursive:true});
const summary={generatedAtUtc:new Date().toISOString(),toolVersion:version,
  analyzerSourceSha256:sha(fs.readdirSync(new URL('../src/',import.meta.url)).filter(name=>name.endsWith('.ts')).sort()
    .map(name=>`${name}\0${fs.readFileSync(new URL(`../src/${name}`,import.meta.url),'utf8')}`).join('\0')),
  scope:'Source-only classifier calibration; not a registry package run or runtime correctness assessment.',samples:[]};
const previews=[];
for(const sample of corpus.samples){
  const root=path.resolve(repositories,sample.repository), destination=path.resolve(output,sample.id);
  assert.equal(git(root,['rev-parse','HEAD']),sample.revision);
  assert.equal(git(root,['status','--porcelain','--untracked-files=no']),'');
  const previous=JSON.parse(fs.readFileSync(path.resolve(baseline,sample.id,'evidence.json'),'utf8'));
  const result=analyze({project:path.join(root,sample.package,'package.json'),tsconfig:path.resolve(baseline,sample.id,'source-scope.json')},version);
  const e=result.evidence;validateEvidence(e);assert.equal(e.analysis.status,'complete');
  assert.equal(result.csv,fs.readFileSync(path.resolve(baseline,sample.id,'metrics.csv'),'utf8'));
  for(const key of Object.keys(e.dimensions).filter(key=>key!=='errorHandling')) assert.deepEqual(e.dimensions[key],previous.dimensions[key]);
  fs.mkdirSync(destination,{recursive:true});fs.writeFileSync(path.join(destination,'evidence.json'),JSON.stringify(e,null,2)+'\n');
  const d=e.dimensions.errorHandling, handlers=d.handlerEvidence.handlers;
  const reviewed=labels.filter(label=>label.sample===sample.id).map(label=>{
    const current=handlers.find(h=>h.id===label.handlerId);assert.ok(current);
    return {handlerId:current.id,file:current.file,line:current.line,reviewLabel:label.reviewLabel,...current.disposition};
  });
  // Context counterexamples must never become deductions. Unknown is allowed; it withholds the score.
  for(const row of reviewed.filter(row=>row.reviewLabel!=='requiresReview')) assert.notEqual(row.classification,'unexplainedSwallowing');
  previews.push({id:sample.id,revision:sample.revision,...contextPreview(e)});
  summary.samples.push({id:sample.id,revision:sample.revision,runId:e.analysis.runId,auditId:e.analysis.auditId,
    configurationFingerprint:e.analysis.configurationFingerprint,evidenceSha256:sha(fs.readFileSync(path.join(destination,'evidence.json'))),
    ruleset:e.analysis.ruleset,status:d.status,score:d.score??null,...d.dispositionEvidence,reviewed,
    unchanged:'Raw CSV and every other dimension are byte/equality checked against the released corpus.'});
  console.log(sample.id,JSON.stringify({score:d.score??null,...d.dispositionEvidence}));
}
summary.sourceReview=verifyReview(previews,labels,label=>{
  const sample=corpus.samples.find(sample=>sample.id===label.sample);
  return fs.readFileSync(path.resolve(repositories,sample.repository,label.file));
});
fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2)+'\n');
