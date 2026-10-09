import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { createHash } from "node:crypto";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { contextPreview, handlerCandidate, verifyReview } from "../scripts/context-policy-preview.mjs";

const roots: string[]=[];
function evidence(source: string) {
  const root=fs.mkdtempSync(path.join(os.tmpdir(),"context-policy-"));roots.push(root);
  fs.writeFileSync(path.join(root,"package.json"),'{"name":"context-policy"}');
  fs.writeFileSync(path.join(root,"source.ts"),source);
  return analyze({project:path.join(root,"package.json")},"0.4.0").evidence;
}
afterEach(()=>{for(const root of roots.splice(0)) fs.rmSync(root,{recursive:true,force:true});});

describe("offline handler population candidate", () => {
  it("distinguishes small populations from sparse issues and rounds half up", () => {
    expect(handlerCandidate(2,2).score).toBe(0);
    expect(handlerCandidate(100,2).score).toBe(9);
    expect(handlerCandidate(100,20).score).toBe(8);
    expect(handlerCandidate(100,50).score).toBe(5);
    expect(handlerCandidate(16,5).score).toBe(6.9);
    expect(handlerCandidate(100,0).score).toBe(10);
  });
  it("refuses to credit absent or uninspectable populations", () => {
    expect(handlerCandidate(0,0).score).toBeNull();
    expect(handlerCandidate(100,0,1).score).toBeNull();
    expect(handlerCandidate(100,2,10).score).toBeNull();
    for (const args of [[1,2],[-1,0],[2,0.5],[2,1,-1]]) expect(()=>handlerCandidate(...args)).toThrow();
  });
  it("uses distinct bodies, credits declared intent and never changes production evidence",()=>{
    const e=evidence('function ignore(){ /* Optional best effort. */ } Promise.reject(1).catch(ignore); Promise.reject(2).catch(ignore); try { work() } catch {}');
    const original=JSON.stringify(e),preview=contextPreview(e);
    expect(preview.handlers.totalBodies).toBe(2);
    expect(preview.handlers.useSites).toBe(3);
    expect(preview.handlers.documentedBodies).toBe(1);
    expect(preview.handlers.candidate.score).toBe(5);
    expect(e.dimensions.errorHandling.score).toBe(5);
    expect(JSON.stringify(e)).toBe(original);
    const broken=structuredClone(e);(broken.dimensions.errorHandling.handlerEvidence as any).handlers.push((broken.dimensions.errorHandling.handlerEvidence as any).handlers[0]);
    expect(()=>contextPreview(broken)).toThrow();
  });
  it("does not mistake a referenced callback gap or partial run for a complete scoring population",()=>{
    const e=evidence('function ignore() {} Promise.reject(1).catch(ignore); Promise.reject(2).catch(missing);');
    expect(contextPreview(e).handlers.candidate.score).toBeNull();
    expect(()=>contextPreview(evidence('export function {'))).toThrow();
  });
  it("demonstrates why body-only scoring fails a real fallback contract",()=>{
    function parseWithFailureResult(value: string) {
      let parsed;
      try { parsed=JSON.parse(value); } catch {}
      if (parsed===undefined) return {ok:false};
      return {ok:true,value:parsed};
    }
    expect(parseWithFailureResult('{')).toEqual({ok:false});
    expect(parseWithFailureResult('1')).toEqual({ok:true,value:1});
    const e=evidence(`export ${parseWithFailureResult.toString()}`);
    // Rejected experiment would produce zero, although failure has an explicit result.
    expect(contextPreview(e).handlers.candidate.score).toBe(0);
    expect(e.dimensions.errorHandling.status).toBe('skipped');
    expect(e.dimensions.errorHandling.score).toBeUndefined();
  });
  it("requires complete, current source review rather than cherry-picked labels",()=>{
    const source=Buffer.from('try { work(); } catch {}\n');
    const digest=(bytes: any)=>createHash('sha256').update(bytes).digest('hex');
    const samples=[{id:'sample',revision:'rev',runId:'run',auditId:'audit',handlers:{unexplained:[{id:'h',file:'a.ts',line:1,uses:[{}]}]}}];
    const label={sample:'sample',revision:'rev',runId:'run',auditId:'audit',handlerId:'h',file:'a.ts',line:1,useSites:1,
      reviewLabel:'requiresReview',rationale:'Failure contract has not been established.',sourceRange:[1,1],sourceSha256:digest(source),sourceRangeSha256:digest(source.toString().trimEnd())};
    expect(verifyReview(samples,[label],()=>source).requiresReview).toBe(1);
    expect(()=>verifyReview(samples,[],()=>source)).toThrow();
    expect(()=>verifyReview(samples,[label,label],()=>source)).toThrow();
    expect(verifyReview(samples,[{...label,runId:'original-review-run'}],()=>source).reviewedBodies).toBe(1);
    expect(()=>verifyReview(samples,[{...label,revision:'other-source'}],()=>source)).toThrow();
    expect(()=>verifyReview(samples,[label],()=>Buffer.from('different source'))).toThrow();
  });
});
