// Read-only selected-source calibration. No target installation or application execution.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {analyze} from '../dist/analyzer.js';
import {validateEvidence} from '../dist/evidence-tools.js';
import {dependencyMetadata} from '../dist/dependency-metadata.js';
import {dependencyEvidence} from '../dist/dependency-evidence.js';

const [repositories,baseline,scopes,output,...flags]=process.argv.slice(2);
assert.ok(repositories && baseline && scopes && output,'Usage: dependency-corpus.mjs <repos> <0.5 corpus> <source-scope corpus> <output> [--metadata]');
assert.ok(flags.every(flag=>flag==='--metadata'));
const network=flags.includes('--metadata');
const corpus=JSON.parse(fs.readFileSync(new URL('../../../shared/calibration/corpus/javascript-typescript-context.json',import.meta.url),'utf8'));
const sha=text=>createHash('sha256').update(text).digest('hex');
const sharedSourceFactsSha256=sha(['arithmetic','contract','population'].flatMap(name=>['mjs','d.mts'].map(extension=>{
  const file=`${name}.${extension}`;
  return `${file}\0${fs.readFileSync(new URL(`../../../shared/source-facts/${file}`,import.meta.url),'utf8')}`;
})).join('\0'));
const report={generatedAtUtc:new Date().toISOString(),toolVersion:'0.5.0-dev',scope:'Selected-source npm/pnpm dependency evidence; other scores remain unchanged. Feed observations are time-dependent, not a quality score.',
  metadataRequested:network,metadataLimit:network?25:0,sharedSourceFactsSha256,analyzerSourceSha256:sha(fs.readdirSync(new URL('../src/',import.meta.url)).filter(name=>name.endsWith('.ts')).sort().map(name=>`${name}\0${fs.readFileSync(new URL(`../src/${name}`,import.meta.url),'utf8')}`).join('\0')),samples:[]};
function git(root,args){const run=spawnSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true});assert.equal(run.status,0,run.stderr);return run.stdout.trim();}
fs.mkdirSync(output,{recursive:true});
for(const sample of corpus.samples) {
  const root=path.resolve(repositories,sample.repository);
  assert.equal(git(root,['rev-parse','HEAD']),sample.revision);assert.equal(git(root,['status','--porcelain','--untracked-files=no']),'');
  const prior=JSON.parse(fs.readFileSync(path.resolve(baseline,sample.id,'evidence.json'),'utf8'));
  const result=analyze({project:path.join(root,sample.package,'package.json'),tsconfig:path.resolve(scopes,sample.id,'source-scope.json'),dependencyMetadata:network},report.toolVersion);
  for(const dimension of Object.keys(prior.dimensions).filter(name=>name!=='dependencyManagement')) assert.deepEqual(result.evidence.dimensions[dimension],prior.dimensions[dimension]);
  assert.equal(result.csv,fs.readFileSync(path.resolve(scopes,sample.id,'metrics.csv'),'utf8'));
  const inventory=result.evidence.dimensions.dependencyManagement.dependencyInventory;
  if(network) result.evidence.dimensions.dependencyManagement=dependencyEvidence(inventory,await dependencyMetadata(inventory,undefined,{maxPackages:25,deadlineMs:30000}));
  validateEvidence(result.evidence);assert.equal(result.evidence.analysis.status,'complete');
  const dimension=result.evidence.dimensions.dependencyManagement,metadata=dimension.dependencyMetadata;
  const destination=path.resolve(output,sample.id);fs.mkdirSync(destination,{recursive:true});
  const serialized=JSON.stringify(result.evidence,null,2)+'\n';fs.writeFileSync(path.join(destination,'evidence.json'),serialized);
  const summary={id:sample.id,revision:sample.revision,runId:result.evidence.analysis.runId,auditId:result.evidence.analysis.auditId,
    evidenceSha256:sha(serialized),ruleset:result.evidence.analysis.ruleset,configurationFingerprint:result.evidence.analysis.configurationFingerprint,
    inventoryStatus:inventory.status,manifests:inventory.manifests,lockfiles:inventory.lockfiles,configurationInputs:inventory.configurationInputs??[],gaps:inventory.gaps,notices:inventory.notices,
    sourceClasses:Object.fromEntries(['publicRegistry','workspace','otherSource'].map(source=>[source,inventory.packages.filter(p=>p.source===source).length])),
    packageVersions:inventory.packages.length,installations:inventory.packages.reduce((sum,p)=>sum+p.locations.length,0),
    directPackageVersions:inventory.packages.filter(p=>p.uses.some(use=>use.direct)).length,
    productionReachable:inventory.packages.filter(p=>p.uses.some(use=>use.kind==='production')).length,
    developmentReachable:inventory.packages.filter(p=>p.uses.some(use=>use.kind==='development')).length,
    metadataStatus:metadata.status,availablePackages:metadata.availablePackages,unavailablePackages:metadata.unavailablePackages,
    metadataCheckedAtUtc:metadata.checkedAtUtc,advisoryResponseSha256:metadata.advisoryResponseSha256,
    assessedObservations:metadata.observations.filter(row=>row.metadata.status==='available'||row.vulnerabilities.status==='available'),
    deprecatedPackageVersions:metadata.observations.filter(row=>row.deprecated).length,
    updatesWithinDeclaredRanges:metadata.observations.filter(row=>row.upgrades?.some(u=>u.newerWithinRange)).length,
    vulnerablePackageVersions:metadata.observations.filter(row=>row.vulnerabilities.advisories?.length).length,
    score:dimension.score??null,unchanged:'Raw CSV and every other dimension verified equal to released 0.5.0 source results.'};
  report.samples.push(summary);console.log(sample.id,JSON.stringify({inventory:summary.inventoryStatus,packages:summary.packageVersions,metadata:summary.metadataStatus,available:summary.availablePackages,gaps:summary.gaps.length}));
}
fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(report,null,2)+'\n');
