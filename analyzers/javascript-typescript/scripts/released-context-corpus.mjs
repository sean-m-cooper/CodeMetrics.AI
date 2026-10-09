// Exercise the released skill/package path, without executing the target application's scripts.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const [skill, repositories, output] = process.argv.slice(2).map(p=>path.resolve(p));
assert.ok(skill && repositories && output, 'Usage: released-context-corpus.mjs <skill> <repositories> <output>');
const corpusPath = new URL('../../../shared/calibration/corpus/javascript-typescript-context.json', import.meta.url);
const corpus=JSON.parse(fs.readFileSync(corpusPath,'utf8'));
const sha256=data=>createHash('sha256').update(data).digest('hex');
const slash=p=>p.replaceAll('\\','/');
function execute(command,args,cwd) {
  const result=spawnSync(command,args,{cwd,encoding:'utf8',windowsHide:true,timeout:300_000,maxBuffer:32*1024*1024});
  assert.equal(result.status,0,result.stdout+result.stderr+String(result.error??''));
  return result.stdout.trim();
}
const compatibility=JSON.parse(fs.readFileSync(path.join(skill,'compatibility.json'),'utf8'));
assert.equal(compatibility.analyzers['javascript-typescript'].preferredVersion,'0.4.0');
assert.equal(compatibility.evidenceToolVersion,'0.4.0');
fs.mkdirSync(output,{recursive:true});
const summary={generatedAtUtc:new Date().toISOString(),corpusSha256:sha256(fs.readFileSync(corpusPath)),
  skillRevision:execute('git',['rev-parse','HEAD'],skill),skillSha256:sha256(fs.readFileSync(path.join(skill,'SKILL.md'))),
  nodeVersion:process.version,packageVersion:'0.4.0',scope:corpus.description,samples:[]};
for(const sample of corpus.samples) {
  const checkout=path.join(repositories,sample.repository),root=path.join(checkout,sample.package),out=path.join(output,sample.id);
  assert.equal(execute('git',['rev-parse','HEAD'],checkout),sample.revision,`Changed sample: ${sample.id}`);
  assert.equal(execute('git',['status','--porcelain','--untracked-files=no'],checkout),'',`Tracked source changed: ${sample.id}`);
  fs.mkdirSync(out,{recursive:true});
  const config={...(sample.config?{extends:slash(path.join(root,sample.config))}:{}),
    compilerOptions:{allowJs:true,noEmit:true,...(sample.target?{target:sample.target}:{})},
    include:sample.include.map(pattern=>slash(path.join(root,pattern))),
    exclude:['**/node_modules/**','**/__tests__/**','**/tests/**','**/*.test.*','**/*.spec.*']};
  const configPath=path.join(out,'source-scope.json');fs.writeFileSync(configPath,JSON.stringify(config,null,2)+'\n');
  const result=JSON.parse(execute(process.execPath,[path.join(skill,'scripts/run-scorecard.mjs'),'--repo',checkout,
    '--entry-point',path.join(root,'package.json'),'--tsconfig',configPath,'--cache',path.join(output,'tool-cache')],checkout));
  fs.writeFileSync(path.join(out,'runner-result.json'),JSON.stringify(result,null,2)+'\n');
  assert.equal(result.results.length,1);const run=result.results[0];
  assert.equal(run.status,'complete');assert.equal(run.fresh,true);assert.equal(run.version,'0.4.0');
  assert.equal(run.source,'compatibility-manifest');assert.equal(run.analyzerExitCode,0);assert.equal(run.validationExitCode,0);
  const inspection=JSON.parse(fs.readFileSync(run.artifacts.inspection,'utf8')),e=inspection.evidence;
  assert.equal(inspection.usable,true);assert.equal(e.analysis.status,'complete');assert.equal(e.tool.version,'0.4.0');
  assert.equal(e.analysis.runId,run.runId);assert.equal(e.analysis.auditId,result.auditId);
  assert.equal(e.dimensions.codeQuality.componentDetails.decomposition.version,2);
  assert.equal(e.dimensions.errorHandling.handlerEvidence.version,2);
  fs.copyFileSync(run.artifacts.evidence,path.join(out,'evidence.json'));
  fs.copyFileSync(run.artifacts.inspection,path.join(out,'inspection.json'));
  fs.copyFileSync(run.artifacts.metrics,path.join(out,'metrics.csv'));
  assert.equal(execute('git',['status','--porcelain','--untracked-files=no'],checkout),'');
  summary.samples.push({...sample,runId:run.runId,auditId:run.auditId,artifacts:run.artifacts,
    configurationFingerprint:e.analysis.configurationFingerprint,configurationSha256:sha256(fs.readFileSync(configPath)),
    evidenceSha256:sha256(fs.readFileSync(run.artifacts.evidence)),csvSha256:sha256(fs.readFileSync(run.artifacts.metrics)),
    population:e.population,filters:e.filters,ruleset:e.analysis.ruleset,
    scores:Object.fromEntries(Object.entries(e.dimensions).map(([key,d])=>[key,d.score??null]))});
  fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2)+'\n');
  console.log(`${sample.id}: ${e.population.members} functions; ${JSON.stringify(summary.samples.at(-1).scores)}`);
}
