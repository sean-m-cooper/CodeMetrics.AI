import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { compare, gate, sarif, validateEvidence } from "../dist/evidence-tools.js";

const roots: string[] = [];
function inspect(source: string, extension = "ts", extra: Record<string, string> = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "codemetrics-async-"));
  roots.push(root);
  for (const [name, contents] of Object.entries({ "package.json": '{"name":"async-fixture"}', [`code.${extension}`]: source, ...extra }))
    fs.writeFileSync(path.join(root, name), contents);
  return analyze({ project: path.join(root, "package.json") }, "0.3.0");
}
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });

describe("bounded promise usage", () => {
  it.each(["js", "ts"])("finds standard async Promise executors in .%s without React", extension => {
    const result = inspect("export const work = new Promise(async resolve => { resolve(1); });", extension);
    const dimension = result.evidence.dimensions.performanceAsync;
    expect(dimension.status).toBe("scored");
    expect(dimension.score).toBe(6);
    expect(dimension.findings).toHaveLength(1);
    expect(dimension.findings[0]).toMatchObject({ category: "asyncPromiseExecutor", member: "<module>", severity: "warning", confidence: "high" });
    expect(dimension.scoringDecision!.findingEffects[0].effect).toBe("policyInput");
    expect(() => validateEvidence(result.evidence)).not.toThrow();
  });

  it("finds globalThis.Promise and keeps nested owners distinct", () => {
    const dimension = inspect(`export function outer() {
      const inner = () => new globalThis.Promise(async resolve => resolve(1));
      return new Promise(async resolve => resolve(inner()));
    }`).evidence.dimensions.performanceAsync;
    expect(dimension.findings.map(f => f.member).sort()).toEqual(["outer", "outer/inner"]);
    expect(new Set(dimension.findings.map(f => f.fingerprint)).size).toBe(2);
  });

  it("recognizes transparent parentheses and type assertions on inline callbacks", () => {
    const result = inspect(`export const task = new (Promise)((async resolve => resolve(1)));
      export const start = () => [1].forEach((async item => {}) as (item: number) => Promise<void>);`);
    expect(result.evidence.dimensions.performanceAsync.findings.map(f => f.category).sort())
      .toEqual(["asyncForEachCallback", "asyncPromiseExecutor"]);
  });

  it("does not mistake shadowed, imported or custom constructors for the standard Promise", () => {
    const result = inspect(`import CustomPromise from './custom';
      export function local(Promise: any) { return new Promise(async () => {}); }
      export const custom = new CustomPromise(async () => {});
      export const lookalike = new ({Promise: class { constructor(callback: any) {} }}).Promise(async () => {});`, "ts",
      { "custom.ts": "export default class Promise { constructor(callback: any) {} }" });
    expect(result.evidence.dimensions.performanceAsync.findings).toEqual([]);
  });

  it("keeps async Array/ReadonlyArray forEach as review leads and excludes custom methods", () => {
    const result = inspect(`export function launch(readonly: ReadonlyArray<number>, unknown: any) {
      [1].forEach(async item => { await Promise.resolve(item); });
      readonly.forEach(async item => { await Promise.resolve(item); });
      [1]['forEach'](async item => { await Promise.resolve(item); });
      const custom = { forEach: async (callback: () => Promise<void>) => await callback() };
      custom.forEach(async () => {});
      unknown.forEach(async () => {});
    }`);
    const dimension = result.evidence.dimensions.performanceAsync;
    expect(dimension.score).toBe(10);
    expect(dimension.findings).toHaveLength(3);
    expect(dimension.findings.every(f => f.category === "asyncForEachCallback" && f.severity === "info")).toBe(true);
    expect(dimension.scoringDecision!.findingEffects.every(f => f.effect === "excluded")).toBe(true);
    expect(sarif(result.evidence).runs[0].results.every(f => f.level === "note")).toBe(true);
    expect(() => validateEvidence(result.evidence)).not.toThrow();
  });

  it("preserves intentional sequential awaits, throttling and synchronous executors", () => {
    const dimension = inspect(`export async function ordered(items: number[]) {
      for (const item of items) { await Promise.resolve(item); }
      return new Promise(resolve => resolve(1));
    }`).evidence.dimensions.performanceAsync;
    expect(dimension.score).toBe(10);
    expect(dimension.findings).toEqual([]);
    expect(dimension.scope!.excludes).toContain("runtime-performance");
  });

  it("measures top-level await even without function metrics", () => {
    const result = inspect("export {}; await Promise.resolve(1);");
    expect(result.metrics).toEqual([]);
    expect(result.evidence.dimensions.performanceAsync.score).toBe(10);
    expect(result.evidence.dimensions.codeQuality.status).toBe("skipped");
  });

  it("keeps synchronous and type-only populations unmeasured", () => {
    expect(inspect("export const answer = () => 42;").evidence.dimensions.performanceAsync.status).toBe("skipped");
    expect(inspect("import type {ReactNode} from 'react'; export interface Props { value: ReactNode }")
      .evidence.dimensions.performanceAsync.status).toBe("skipped");
  });

  it("retains partial findings but withholds scores on parse failure", () => {
    const result = inspect("export const work = new Promise(async () => {});", "ts", { "broken.ts": "export function {" });
    const dimension = result.evidence.dimensions.performanceAsync;
    expect(result.evidence.analysis.status).toBe("incomplete");
    expect(dimension.status).toBe("failed");
    expect(dimension.score).toBeUndefined();
    expect(dimension.scoringDecision).toBeUndefined();
    expect(dimension.findings[0].category).toBe("asyncPromiseExecutor");
    expect(() => validateEvidence(result.evidence)).not.toThrow();
  });

  it("keeps fingerprints stable and review leads out of warning gates", () => {
    const text = "export const run = () => [1].forEach(async item => { await Promise.resolve(item); });";
    const before = inspect("export const run = async () => { await Promise.resolve(1); };").evidence;
    const after = inspect(text).evidence;
    const moved = inspect("\n\n" + text).evidence;
    expect(compare(moved, after).unchanged).toHaveLength(1);
    expect(gate(compare(after, before), "warning", 0)).toBe(false);
    const older = structuredClone(after); older.analysis.ruleset = "javascript-typescript-2026-09-05";
    expect(() => compare(after, older)).toThrow("Incompatible baseline");
  });
});

describe("React evaluation context", () => {
  it.each([
    "if (useState(0)) return null;",
    "return useState(0) ? 1 : 2;",
    "return useState(0) && 1;",
    "for (const item of useState([])) { console.log(item); }",
    "for (let value = useState(0); false;) {}"
  ])("does not penalize unconditionally evaluated hooks: %s", body => {
    expect(inspect(`import {useState} from 'react'; export function App() { ${body} }`)
      .evidence.dimensions.performanceAsync.findings).toEqual([]);
  });

  it("still finds branches, repeated loop tests and conditional outer contexts", () => {
    const result = inspect(`import {useState} from 'react'; export function App(active: boolean) {
      if (active) { if (useState(0)) return null; }
      const value = active ? useState(1) : useState(2);
      active && useState(3);
      while (useState(4)) {}
      for (let n = 0; useState(5); n++) {}
      for (const [item = useState(6)] of [[undefined]]) {}
    }`);
    expect(result.evidence.dimensions.performanceAsync.findings).toHaveLength(7);
  });

  it("reports dependency-free effects as information and permits React use in conditions", () => {
    const result = inspect(`import {use, useEffect} from 'react'; export function App(active: boolean) {
      if (active) use(Promise.resolve(1));
      useEffect(() => { console.log('every render'); });
    }`);
    const dimension = result.evidence.dimensions.performanceAsync;
    expect(dimension.score).toBe(10);
    expect(dimension.findings).toHaveLength(1);
    expect(dimension.findings[0]).toMatchObject({ category: "effectWithoutDependencies", severity: "info" });
    expect(dimension.scoringDecision!.findingEffects[0].effect).toBe("excluded");
  });
});
