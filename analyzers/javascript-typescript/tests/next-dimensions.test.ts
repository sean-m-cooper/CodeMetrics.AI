import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { scoreAsyncUsage } from "../src/async-scoring.js";
import type { Finding } from "../src/evidence.js";
import { validateEvidence } from "../dist/evidence-tools.js";

const roots: string[] = [];
function run(source: string, files: Record<string, string> = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "next-dimensions-")); roots.push(root);
  for (const [name, text] of Object.entries({ "package.json": '{"name":"test"}', "code.ts": source, ...files })) {
    const file = path.join(root, name); fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, text);
  }
  const result = analyze({ project: path.join(root, "package.json") }, "0.3.0");
  validateEvidence(result.evidence);
  return result.evidence;
}
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });
const owners = (n: number) => Array.from({ length: n }, (_, index) => ({ id: `f${index}`, file: "a.ts", project: "test", member: `f${index}`, line: index + 1, reasons: ["asyncFunction"] }));
const finding = (index: number): Finding => ({ category: "asyncPromiseExecutor", ruleId: "test", fingerprint: String(index), severity: "warning",
  confidence: "high", message: "test", observations: { asyncOwnerId: `f${index}` } });

describe("async owner population", () => {
  it.each([[100,0,10],[101,1,10],[100,1,9],[51,1,9],[100,2,8],[100,4,8],[100,5,6],[100,10,6],
    [100,11,4],[100,15,4],[100,16,2],[100,20,2],[100,21,0],[1,1,0]])("scores %i owners / %i affected as %i", (n,b,score) => {
    expect(scoreAsyncUsage(Array.from({ length: b }, (_, i) => finding(i)), owners(n)).score).toBe(score);
  });
  it("counts affected owners once and does not penalize review leads", () => {
    expect(scoreAsyncUsage([finding(0), {...finding(0), fingerprint: "second"}], owners(100)).score).toBe(9);
    expect(scoreAsyncUsage([{...finding(0), severity: "info", observations: {asyncOwnerId: "f0", scoreDisposition: "excludedReviewLead"}}], owners(1)).score).toBe(10);
    expect(scoreAsyncUsage([], []).status).toBe("skipped");
    expect(scoreAsyncUsage([finding(9)], owners(1)).status).toBe("failed");
    expect(scoreAsyncUsage([finding(0)], []).status).toBe("failed");
  });
  it("excludes synchronous padding and React imports without hook usage", () => {
    const result = run("import React from 'react'; export function sync(){return 1}; export function chain(){return Promise.resolve(1).then(x=>x)}");
    const data = (result.dimensions.performanceAsync.scoring as any).observations;
    expect(data.eligibleOwners).toBe(1);
    expect(data.owners[0].member).toBe("chain");
    expect(result.dimensions.performanceAsync.score).toBe(10);
  });
  it("assigns nested hazards once and exposes module-level owners", () => {
    const result = run("export function outer(){ const inner=()=>new Promise(async()=>{}); return inner; } new Promise(async()=>{}); await Promise.resolve(1);");
    const data = (result.dimensions.performanceAsync.scoring as any).observations;
    expect(data.affectedOwners).toBe(2);
    expect(data.eligibleOwners).toBe(4);
    expect(data.owners.some((owner: any) => owner.member === "outer")).toBe(false);
    expect(new Set(result.dimensions.performanceAsync.findings.map(f => f.observations.asyncOwnerId)).size).toBe(2);
  });
  it("keeps multiple React violations within one affected owner", () => {
    const result = run("import {useEffect} from 'react'; export function App(){ if(flag) useEffect(async()=>{}, []); }");
    const data = (result.dimensions.performanceAsync.scoring as any).observations;
    expect(data.actionableFindings).toBe(2);
    expect(data.affectedOwners).toBe(1);
    expect(data.eligibleOwners).toBe(2);
  });
  it("excludes bodyless overloads and recognizes top-level for-await", () => {
    const result = run("async function f(x:number):Promise<number>; async function f(x:any){return x;} for await(const x of source){} export {};");
    const data = (result.dimensions.performanceAsync.scoring as any).observations;
    expect(data.eligibleOwners).toBe(2);
    expect(data.owners.find((owner: any) => owner.member === "<module>").reasons).toEqual(["forAwait"]);
  });
});

describe("decomposition evidence", () => {
  it("owns nested bodies once and preserves data/declaration/export context", () => {
    const result = run(`export function outer(){
      function inner(){
        return {a:1,b:2};
      }
      return inner();
    }
    interface Data {x:number}
    enum Kind {A,B}
    export const table=[1,2,3];`, {"index.ts":"export * from './code';"});
    const component = (result.dimensions.codeQuality.componentDetails as any).decomposition;
    expect(component.score).toBeNull(); expect(component.status).toBe("unscored");
    const module = component.modules.find((item: any) => item.file === "code.ts");
    expect(module.implementedFunctions).toBe(2);
    expect(module.sumOwnedFunctionStatements).toBe(2);
    expect(module.functions.find((item: any) => item.member === "outer").dataLiteralEntries).toBe(0);
    expect(module.functions.find((item: any) => item.member === "outer/inner").dataLiteralEntries).toBe(2);
    expect(module.initializer.dataLiteralEntries).toBe(3);
    expect(module.typeDeclarations).toBe(1); expect(module.enums).toBe(1);
    expect(component.modules.find((item: any) => item.file === "index.ts").sumOwnedFunctionLines).toBe(0);
    expect(component.population.sumOwnedFunctionLines).toBe(module.functions.reduce((sum: number, item: any) => sum + item.ownedSourceLines, 0));
  });
  it("does not fabricate functions or a score for data-only and empty modules", () => {
    const result = run("export const table={a:1,b:2}; interface Shape {x:number}");
    const component = (result.dimensions.codeQuality.componentDetails as any).decomposition;
    expect(result.dimensions.codeQuality.status).toBe("skipped");
    expect(component.population.functions).toBe(0);
    expect(component.population.p90FunctionLines).toBeNull();
    expect(component.largestFunctionContainers).toEqual([]);
  });
});

describe("error-handling evidence", () => {
  it("measures handlers in context and trusts comments without scoring rationale", () => {
    const result = run(`try { work() } catch {}
      try {work()} catch { /* Optional cleanup can fail. */ }
      try {work()} catch(e) { log(e) }
      Promise.reject(1).catch(()=>undefined);
      Promise.reject(1).then(undefined,()=>{ // Cancellation is intentional.
      });`);
    const dimension = result.dimensions.errorHandling, data = dimension.handlerEvidence as any;
    expect(dimension.status).toBe("skipped"); expect(dimension.score).toBeUndefined();
    expect(data.totalHandlers).toBe(5);
    expect(data.unexplainedEmptyHandlers).toBe(2); expect(data.documentedEmptyHandlers).toBe(2);
    expect(data.handlersContainingCode).toBe(1); expect(data.unexplainedEmptyPercent).toBe(40);
    expect(dimension.findings).toHaveLength(2);
    expect(dimension.findings.every(f => f.severity === "info")).toBe(true);
  });
  it("does not borrow nearby comments or confuse code with a no-op", () => {
    const result = run(`// Unrelated comment
      try {work()} catch {;}
      try {work()} catch { return false }
      try {work()} catch { return void log() }
      Promise.reject(1).catch(()=> /* Ignored by design */ undefined);
      Promise.reject(1).catch((undefined)=>undefined);`);
    const data = result.dimensions.errorHandling.handlerEvidence as any;
    expect(data.unexplainedEmptyHandlers).toBe(1);
    expect(data.documentedEmptyHandlers).toBe(1);
    expect(data.handlersContainingCode).toBe(3);
  });
  it("excludes custom catch APIs, discloses referenced callbacks and keeps nested handlers distinct", () => {
    const result = run(`const custom={catch(cb:()=>void){cb()}}; custom.catch(()=>{});
      const callback=()=>{}; Promise.reject(1).catch(callback);
      try {work()} catch { try {cleanup()} catch {} }`);
    const data = result.dimensions.errorHandling.handlerEvidence as any;
    expect(data.totalHandlers).toBe(2);
    expect(data.uninspectedRejectionCallbacks).toBe(1);
    expect(data.unexplainedEmptyHandlers).toBe(1);
    expect(data.handlersContainingCode).toBe(1);
  });
  it("has no percentage or perfect score without observed handlers", () => {
    const dimension = run("export const value=1;").dimensions.errorHandling;
    expect((dimension.handlerEvidence as any).unexplainedEmptyPercent).toBeNull();
    expect(dimension.score).toBeUndefined();
  });
  it("withholds scored conclusions on parse failure while retaining diagnostics", () => {
    const result = run("export async function f(){try {await work()} catch {}}", {"broken.ts":"export function {"});
    expect(result.analysis.status).toBe("incomplete");
    expect(result.dimensions.performanceAsync.status).toBe("failed");
    expect(result.dimensions.errorHandling.status).toBe("failed");
    expect((result.dimensions.errorHandling.handlerEvidence as any).totalHandlers).toBe(1);
    expect((result.dimensions.codeQuality.componentDetails as any).decomposition.status).toBe("failed");
    expect(result.dimensions.codeQuality.score).toBeUndefined();
  });
});
