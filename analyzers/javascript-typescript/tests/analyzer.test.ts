import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { maintainability } from "../src/metrics.js";
import { compare, gate, sarif, validateEvidence } from "../dist/evidence-tools.js";

const roots: string[] = [];
function fixture(files: Record<string,string>) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "codemetrics-")); roots.push(root);
  for (const [name, text] of Object.entries({ "package.json": '{"name":"sample"}', ...files })) {
    fs.mkdirSync(path.dirname(path.join(root,name)), { recursive: true }); fs.writeFileSync(path.join(root,name),text);
  }
  return root;
}
function run(root: string) { return analyze({ project: path.join(root,"package.json") }, "0.2.0"); }
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });

describe("production source analysis", () => {
  it("counts runtime branches, isolates nested functions, and excludes type-only branches", () => {
    const result = run(fixture({"src/code.ts": `type Conditional<T> = T extends string ? 1 : 2;
      export function outer(a: boolean, b: boolean) { const inner = () => a ? 1 : 0; if (a && b) return inner(); return 0; }
      export const arrow = (a: number | null) => a ?? 0;` }));
    expect(result.metrics.map(m=>[m.member,m.complexity])).toEqual([["outer",3],["outer/inner",2],["arrow",2]]);
    expect(result.evidence.population.members).toBe(3);
    expect(result.csv.split("\n")[0]).toContain("Cyclomatic Complexity");
  });
  it.each(["js","jsx","ts","tsx"])("parses .%s and skips generated and test source", extension => {
    const root=fixture({ [`src/file.${extension}`]: "export function valid(x) { return x ? 1 : 0; }",
      "src/file.test.ts": "this is invalid", "dist/broken.js": "!!!", "src/auto.ts": "// @generated\nexport const bad = () => 0;" });
    const result=run(root); expect(result.metrics).toHaveLength(1); expect(result.evidence.analysis.status).toBe("complete");
  });
  it("handles workspace ownership and tsconfig exclusions", () => {
    const root=fixture({ "package.json": '{"name":"workspace","workspaces":["packages/*"]}',
      "packages/a/package.json": '{"name":"a"}', "packages/a/tsconfig.json": '{"include":["src/**/*.ts"]}',
      "packages/a/src/a.ts": "export const a = () => 1;", "packages/a/ignored.ts": "export const ignored=()=>0;",
      "packages/b/package.json": '{"name":"b"}', "packages/b/src/b.js": "export const b = () => 2;" });
    const result=run(root);
    expect(result.metrics.map(m=>m.project)).toEqual(["a","b"]);
    expect(new Set(result.metrics.map(m=>m.file)).size).toBe(result.metrics.length);
  });
  it("detects imported React effects and conditional hooks but ignores a shadowing local function", () => {
    const result=run(fixture({ "src/App.tsx": `import {useEffect as effect, useState} from 'react';
      export function App({active}) { if(active) useState(0); effect(async () => {}, []); return <div>{active && <span/>}</div>; }
      export function Other(effect: Function) { effect(async () => {}); return null; }` }));
    expect(result.metrics.find(m=>m.member==="App")?.kind).toBe("component");
    expect(result.metrics.find(m=>m.member==="App")?.complexity).toBe(3);
    expect(result.evidence.dimensions.performanceAsync.findings.map(f=>f.category).sort()).toEqual(["asyncEffectCallback","conditionalHook"]);
  });
  it("does not score syntax failures or empty input as clean", () => {
    for (const source of ["export function {", ""]) {
      const result=run(fixture(source ? {"src/bad.ts":source} : {}));
      expect(result.evidence.analysis.status).toBe("incomplete");
      expect(result.evidence.dimensions.codeQuality.score).toBeUndefined();
      expect(result.evidence.dimensions.security.status).toBe("skipped");
    }
  });
  it("uses repository-relative locations for a nested package", () => {
    const root=fixture({".git":"gitdir: worktree-metadata", "packages/ui/package.json":'{"name":"ui"}',
      "packages/ui/src/App.tsx":"import {useEffect} from 'react'; export function App(){useEffect(async()=>{},[]); return <div/>;}"});
    const result=analyze({project:path.join(root,"packages/ui/package.json")},"0.2.0");
    expect(result.evidence.subject.root).toBe(root);
    expect(result.evidence.dimensions.performanceAsync.findings[0].file).toBe("packages/ui/src/App.tsx");
  });
  it("matches a hand-computed maintainability formula", () => {
    // V=64, CC=4, LOC=10: (171 - 5.2*ln(64) - .23*4 - 16.2*ln(10))*100/171 = 65.002...
    expect(maintainability(64,4,10)).toBe(65);
  });
});

describe("evidence workflows", () => {
  it("validates generated v3 evidence and rejects invalid status/score combinations", () => {
    const evidence=run(fixture({"src/a.ts":"export const a=()=>1;"})).evidence;
    // Source tests load the same schema as the installed dist files.
    expect(()=>validateEvidence(evidence)).not.toThrow();
    evidence.dimensions.security.score=10;
    expect(()=>validateEvidence(evidence)).toThrow("Invalid evidence");
  });
  it("keeps fingerprints across checkout roots and line movement; reports additions and removals", () => {
    const source="import { useEffect } from 'react'; export function App(){ useEffect(async()=>{},[]); return null; }";
    const before=run(fixture({"src/a.ts":source})).evidence;
    const after=run(fixture({"src/a.ts":"\n\n"+source})).evidence;
    expect(compare(after,before).unchanged).toHaveLength(1);
    expect(gate(compare(after,before),"warning",0)).toBe(false);
    const clean=run(fixture({"src/a.ts":"import {useEffect} from 'react'; export function App(){return null;}"})).evidence;
    expect(compare(after,clean).new).toHaveLength(1);
    expect(compare(clean,after).resolved).toHaveLength(1);
    expect(gate(compare(after,clean),"warning")).toBe(true);
    expect(sarif(after).runs[0].results[0].partialFingerprints["codemetrics/v1"]).toBe(after.dimensions.performanceAsync.findings[0].fingerprint);
  });
  it("rejects incompatible versions and disables gates for exploratory comparisons", () => {
    const before=run(fixture({"src/a.ts":"export const a=()=>1;"})).evidence;
    const after=structuredClone(before); after.tool.version="99.0.0";
    expect(()=>compare(after,before)).toThrow("Incompatible baseline");
    const result=compare(after,before,true);
    expect(result.dimensions.codeQuality.delta).toBeNull();
    expect(()=>gate(result,"warning")).toThrow("incompatible");
  });
  it("gates score drops and severity increases, while allowing severity reductions", () => {
    const before=run(fixture({"src/a.ts":"import {useEffect} from 'react'; export function App(){useEffect(async()=>{},[]);}"})).evidence;
    const after=structuredClone(before);
    after.dimensions.performanceAsync.findings[0].severity="error";
    expect(gate(compare(after,before),"error")).toBe(true);
    expect(gate(compare(before,after),"warning")).toBe(false);
    after.dimensions.codeQuality.score=before.dimensions.codeQuality.score!-1;
    expect(gate(compare(after,before),undefined,0)).toBe(true);
    after.analysis.status="incomplete";
    expect(()=>compare(after,before)).toThrow("incomplete");
  });
  it("rejects changed tsconfig selection while ignoring the checkout directory", () => {
    const files={"tsconfig.json":'{"include":["src/*.ts"]}',"src/a.ts":"export const a=()=>1;"};
    const before=run(fixture(files)).evidence;
    expect(compare(run(fixture(files)).evidence,before).compatible).toBe(true);
    const after=run(fixture({...files,"tsconfig.json":'{"include":["src/**/*.ts"]}'})).evidence;
    expect(()=>compare(after,before)).toThrow("configurationFingerprint");
  });
  it("executes the compiled CLI with actual custom output paths and invalid arguments", () => {
    const root=fixture({"src/a.js":"export const a=()=>1;"});
    const cli=path.resolve("dist/cli.js");
    const result=spawnSync(process.execPath,[cli,"--output","out/m.csv","--scorecard-output","out/e.json"],{cwd:root,encoding:"utf8"});
    expect(result.status,result.stderr).toBe(0); expect(fs.existsSync(path.join(root,"out/m.csv"))).toBe(true);
    expect(JSON.parse(fs.readFileSync(path.join(root,"out/e.json"),"utf8")).population.members).toBe(1);
    expect(spawnSync(process.execPath,[cli,"--unknown"],{cwd:root}).status).toBe(2);
  });
});
