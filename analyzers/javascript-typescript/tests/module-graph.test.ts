import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { cyclicComponents } from "../src/module-topology.js";
import { compare, gate, validateEvidence } from "../dist/evidence-tools.js";

const roots: string[] = [];
function fixture(files: Record<string, string>) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "module-graph-")); roots.push(root);
  for (const [file, content] of Object.entries({ "package.json": '{"name":"sample"}', ...files })) {
    const target = path.join(root, file); fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, content);
  }
  return root;
}
function run(files: Record<string, string>) {
  const result = analyze({ project: path.join(fixture(files), "package.json") }, "0.3.0");
  // Extension evidence deliberately remains readable through the generic schema.
  const graph = result.evidence.dimensions.architecture.dependencyGraph as any;
  return { ...result, graph };
}
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });

describe("module dependency evidence", () => {
  it("records a value cycle as unscored information and deduplicates fan-out", () => {
    const { evidence, graph } = run({ "a.ts": "import {b} from './b'; import * as other from './b'; export const a=()=>b;",
      "b.ts": "import {a} from './a'; export const b=()=>a;" });
    expect(graph.cycles[0].modules).toEqual(["a.ts", "b.ts"]);
    expect(graph.dependencies).toHaveLength(3);
    expect(graph.nodes.map((n: any) => [n.internalValueFanOut, n.internalValueFanIn])).toEqual([[1, 1], [1, 1]]);
    expect(graph.coverage.literalResolutionPercent).toBe(100);
    expect(evidence.dimensions.architecture.status).toBe("skipped");
    expect(evidence.dimensions.architecture.score).toBeUndefined();
    expect(evidence.dimensions.architecture.findings[0].severity).toBe("info");
    expect(() => validateEvidence(evidence)).not.toThrow();
    const baseline = structuredClone(evidence); baseline.dimensions.architecture.findings = [];
    expect(gate(compare(evidence, baseline), "warning", 0)).toBe(false);
  });
  it("separates explicit type-only imports, mixed imports, type queries and re-exports", () => {
    const { graph } = run({ "a.ts": `import type {T} from './b'; import {type T as U} from './b';
      import {type T as V, value} from './b'; export type * from './b'; export {type T as W} from './b';
      type Imported = import('./b').T; export const a=value;`, "b.ts": "import type {a} from './a'; export type T=string; export const value=1;" });
    expect(graph.dependencies.filter((e: any) => e.usage === "typeOnly")).toHaveLength(6);
    expect(graph.dependencies.filter((e: any) => e.usage === "mixed")).toHaveLength(1);
    expect(graph.cycles).toEqual([]);
    expect(graph.nodes.find((n: any) => n.id === "a.ts").internalValueFanOut).toBe(1);
  });
  it("marks re-export-only modules without penalizing them", () => {
    const { graph } = run({ "index.ts": "export * from './leaf'; export type {T} from './types';",
      "leaf.ts": "export const value=1;", "types.ts": "export type T=string;" });
    expect(graph.nodes.find((n: any) => n.id === "index.ts").reExportOnly).toBe(true);
    expect(graph.highestFanOut[0]).toMatchObject({ id: "index.ts", internalValueFanOut: 1 });
    expect(graph.cycles).toEqual([]);
  });
  it("resolves aliases with the package compiler options", () => {
    const { graph } = run({ "tsconfig.json": JSON.stringify({ compilerOptions: { baseUrl: ".", paths: { "@lib/*": ["src/*"] } }, include: ["src"] }),
      "src/a.ts": "import {b} from '@lib/b'; export const a=b;", "src/b.ts": "export const b=1;" });
    expect(graph.dependencies[0]).toMatchObject({ resolution: "internal", target: "src/b.ts", via: "typescript" });
  });
  it("separates direct named, star and namespace re-exports from imports", () => {
    const { graph } = run({ "index.ts": "export {a} from './a'; export * from './b'; export * as ns from './c'; export type {T} from './types';",
      "a.ts": "export const a=1;", "b.ts": "export const b=2;", "c.ts": "export const c=3;", "types.ts": "export type T=number;" });
    expect(graph.dependencyViews.classification).toBe("explicit-forwarding-v2");
    expect(graph.dependencyViews.implementation.highestFanOut).toEqual([]);
    expect(graph.dependencyViews.reExports.highestFanOut[0]).toMatchObject({ id: "index.ts", internalValueFanOut: 3 });
    expect(graph.dependencyViews.reExports.internalValueEdgeCount).toBe(3);
    expect(graph.highestFanOut[0].internalValueFanOut).toBe(3);
  });
  it("keeps implementation edges in mixed files and accounts for overlapping targets once overall", () => {
    const { graph } = run({ "index.ts": "import {b} from './b'; import * as other from './b'; export * from './b'; export * from './c';",
      "b.ts": "export const b=1;", "c.ts": "export const c=2;" });
    const views = graph.dependencyViews;
    expect(views.implementation.internalValueEdgeCount).toBe(1);
    expect(views.reExports.internalValueEdgeCount).toBe(2);
    expect(views.totalInternalValueEdgeCount).toBe(2);
    expect(views.sharedInternalValueEdgeCount).toBe(1);
    expect(views.implementation.highestFanIn).toEqual([{ id: "b.ts", internalValueFanOut: 0, internalValueFanIn: 1 }]);
    expect(views.reExports.highestFanIn.map((node: any) => node.id)).toEqual(["b.ts", "c.ts"]);
  });
  it("retains side-effect imports in a re-export-only module", () => {
    const { graph } = run({ "index.ts": "import './setup'; export * from './leaf';",
      "setup.ts": "export const setup=1;", "leaf.ts": "export const value=1;" });
    expect(graph.nodes.find((node: any) => node.id === "index.ts").reExportOnly).toBe(true);
    expect(graph.dependencyViews.implementation.highestFanOut[0].internalValueFanOut).toBe(1);
    expect(graph.dependencyViews.reExports.highestFanOut[0].internalValueFanOut).toBe(1);
    expect(graph.dependencyViews.sharedInternalValueEdgeCount).toBe(0);
  });
  it("preserves cycles that cross implementation and re-export references", () => {
    const { graph, evidence } = run({ "a.ts": "export * from './b';", "b.ts": "import './a'; export const b=1;" });
    expect(graph.cycles[0].modules).toEqual(["a.ts", "b.ts"]);
    expect(evidence.dimensions.architecture.findings).toHaveLength(1);
    expect(graph.dependencyViews.implementation.internalValueEdgeCount).toBe(1);
    expect(graph.dependencyViews.reExports.internalValueEdgeCount).toBe(1);
    expect(evidence.dimensions.architecture.score).toBeUndefined();
    expect(() => validateEvidence(evidence)).not.toThrow();
  });
  it("does not invent internal re-export targets when resolution or scope is incomplete", () => {
    const { graph } = run({ "tsconfig.json": '{"include":["src"]}',
      "src/a.ts": "export * from 'missing'; export * from '../tests/helper';", "tests/helper.ts": "export const value=1;" });
    expect(graph.coverage.status).toBe("gaps");
    expect(graph.dependencies.map((edge: any) => edge.resolution)).toEqual(["unresolved", "outOfScope"]);
    expect(graph.dependencyViews.reExports.internalValueEdgeCount).toBe(0);
    expect(graph.dependencyViews.reExports.highestFanOut).toEqual([]);
  });
  it("recognizes import-then-export and direct CommonJS forwarding", () => {
    const { graph } = run({ "a.ts": "import {b} from './b'; export {b};",
      "b.ts": "export const b=1;", "index.cjs": "module.exports=require('./leaf.cjs');", "leaf.cjs": "exports.value=1;" });
    expect(graph.dependencyViews.implementation.internalValueEdgeCount).toBe(0);
    expect(graph.dependencyViews.reExports.internalValueEdgeCount).toBe(2);
  });
  it("preserves local behavior when a forwarded binding is also used", () => {
    const { graph } = run({ "a.ts": "import {b as value} from './b'; export {value}; export const read=()=>value;",
      "b.ts": "export const b=1;" });
    expect(graph.dependencies[0].roles).toEqual(["implementation", "reExport"]);
    expect(graph.dependencyViews.sharedInternalValueEdgeCount).toBe(1);
    expect(graph.dependencyViews.totalInternalValueEdgeCount).toBe(1);
  });
  it("recognizes default, namespace and import-equals forwarding without name-based alias matching", () => {
    const { graph } = run({ "a.ts": "import def from './b'; import * as ns from './b'; import other = require('./b'); export {ns, other}; export default def; function f(def: string){return def;}",
      "b.ts": "export default 1;" });
    expect(graph.dependencies.map((edge: any) => edge.roles)).toEqual([["reExport"], ["reExport"], ["reExport"]]);
  });
  it("keeps shorthand object use and unforwarded bindings in the implementation view", () => {
    const { graph } = run({ "a.ts": "import {b,c} from './b'; export {b}; export const object={b};",
      "b.ts": "export const b=1, c=2;" });
    expect(graph.dependencies[0].roles).toEqual(["implementation", "reExport"]);
  });
  it("recognizes CommonJS binding/property forwarding while retaining local uses", () => {
    const { graph } = run({ "a.cjs": "const lib=require('./b.cjs'); exports.value=lib.value; exports.read=()=>lib.value; module.exports.other=require('./b.cjs').value;",
      "b.cjs": "exports.value=1;" });
    expect(graph.dependencies.map((edge: any) => edge.roles)).toEqual([["implementation", "reExport"], ["reExport"]]);
    expect(graph.dependencyViews.totalInternalValueEdgeCount).toBe(1);
  });
  it("does not infer forwarding through mutated bindings, alias chains or wrappers", () => {
    const { graph } = run({ "a.cjs": "let lib=require('./b.cjs'); lib={}; exports.lib=lib; const other=require('./b.cjs'); const alias=other; exports.alias=alias; exports.wrapped=wrap(require('./b.cjs'));",
      "b.cjs": "exports.value=1;" });
    expect(graph.dependencies.every((edge: any) => edge.roles.join() === "implementation")).toBe(true);
  });
  it("ignores shadowed CommonJS export names and dynamic export targets", () => {
    const { graph } = run({ "a.cjs": "function f(exports,module){exports.value=require('./b.cjs'); module.exports=require('./b.cjs');} exports[key]=require('./b.cjs');",
      "b.cjs": "exports.value=1;" });
    expect(graph.dependencies.every((edge: any) => edge.roles.join() === "implementation")).toBe(true);
  });
  it.each(["[lib]=[{}]", "({lib}=source)", "for(lib of source){}"])("keeps reassignment through %s out of forwarding evidence", (write) => {
    const { graph } = run({ "a.cjs": `let lib=require('./b.cjs'); ${write}; exports.lib=lib;`, "b.cjs": "exports.value=1;" });
    expect(graph.dependencies[0].roles).toEqual(["implementation"]);
  });
  it("does not turn explicit type exports into value forwarding", () => {
    const { graph } = run({ "a.ts": "import type {B} from './b'; export type {B}; import {b} from './b'; export type {b};",
      "b.ts": "export type B=number; export const b=1;" });
    expect(graph.dependencies.map((edge: any) => edge.roles)).toEqual([[], ["implementation"]]);
  });
  it("keeps initialized variable redeclarations out of forwarding evidence", () => {
    const { graph } = run({ "a.cjs": "var lib=require('./b.cjs'); var lib={}; exports.lib=lib;", "b.cjs": "exports.value=1;" });
    expect(graph.dependencies[0].roles).toEqual(["implementation"]);
  });
  it("resolves workspace export conditions without installing or guessing entry points", () => {
    const options = JSON.stringify({ compilerOptions: { module: "NodeNext", moduleResolution: "NodeNext" }, include: ["src"] });
    const { graph } = run({ "package.json": '{"name":"root","workspaces":["packages/*"]}',
      "packages/a/package.json": '{"name":"@example/a","type":"module"}', "packages/a/tsconfig.json": options,
      "packages/a/src/a.mts": "import {value} from '@example/b'; export const a=value;",
      "packages/a/src/a.cts": "const b=require('@example/b'); export const a=b;",
      "packages/b/package.json": JSON.stringify({ name: "@example/b", type: "module", exports: { ".": { import: "./src/esm.ts", require: "./src/cjs.ts" } } }),
      "packages/b/tsconfig.json": options, "packages/b/src/esm.ts": "export const value=1;", "packages/b/src/cjs.ts": "export const value=2;" });
    expect(graph.dependencies).toHaveLength(2);
    expect(graph.dependencies.every((e: any) => e.resolution === "internal" && e.via === "workspaceManifest")).toBe(true);
    expect(graph.dependencies.find((e: any) => e.from.endsWith("a.mts")).target).toBe("packages/b/src/esm.ts");
    expect(graph.dependencies.find((e: any) => e.from.endsWith("a.cts")).target).toBe("packages/b/src/cjs.ts");
  });
  it("retains missing workspace build entry points as unresolved", () => {
    const { graph } = run({ "package.json": '{"name":"root","workspaces":["packages/*"]}',
      "packages/a/package.json": '{"name":"a"}', "packages/a/a.ts": "import {b} from 'b';",
      "packages/b/package.json": '{"name":"b","main":"dist/index.js"}', "packages/b/src/index.ts": "export const b=1;" });
    expect(graph.dependencies[0].resolution).toBe("unresolved");
    expect(graph.coverage.status).toBe("gaps");
  });
  it.each(["privateSubpath", "missingBuild"])("does not replace an installed package after %s resolution fails", (scenario) => {
    const specifier = scenario === "privateSubpath" ? "library/private" : "library";
    const { graph } = run({ "package.json": '{"name":"root","type":"module","workspaces":["packages/*"]}',
      "tsconfig.json": '{"compilerOptions":{"module":"NodeNext","moduleResolution":"NodeNext"},"include":["index.ts"]}',
      "index.ts": `import '${specifier}';`,
      "node_modules/library/package.json": JSON.stringify({ name: "library", type: "module", exports: { ".": "./missing.js" } }),
      "packages/library/package.json": JSON.stringify({ name: "library", type: "module", exports: { ".": "./index.ts", "./private": "./index.ts" } }),
      "packages/library/index.ts": "export const value=1;" });
    expect(graph.dependencies[0]).toMatchObject({ specifier, resolution: "unresolved", via: "unresolved" });
    expect(graph.highestFanOut).toEqual([]);
  });
  it("prefers an installed version to a different same-named workspace", () => {
    const { graph } = run({ "package.json": '{"name":"root","workspaces":["packages/*"]}',
      "index.ts": "import 'library';",
      "node_modules/library/package.json": '{"name":"library","main":"index.js"}',
      "node_modules/library/index.js": "exports.value=2;",
      "packages/library/package.json": '{"name":"library","main":"index.ts"}',
      "packages/library/index.ts": "export const value=1;" });
    expect(graph.dependencies[0]).toMatchObject({ resolution: "external", via: "typescript", targetFile: "node_modules/library/index.js" });
  });
  it("does not choose between duplicate workspace package names", () => {
    const { graph } = run({ "package.json": '{"name":"root","workspaces":["packages/*"]}',
      "index.ts": "import 'duplicate';",
      "packages/one/package.json": '{"name":"duplicate","main":"index.ts"}', "packages/one/index.ts": "export const a=1;",
      "packages/two/package.json": '{"name":"duplicate","main":"index.ts"}', "packages/two/index.ts": "export const b=1;" });
    expect(graph.dependencies[0].resolution).toBe("unresolved");
  });
  it("records CommonJS and dynamic imports but ignores locally shadowed require/module", () => {
    const { graph } = run({ "a.js": "require('./b'); module.require('./b'); import('./b'); require(name); import(`./${name}`);" +
      "function custom(require, module) { require('not-a-module'); module.require('also-not-a-module'); }", "b.js": "exports.b=1;" });
    expect(graph.dependencies).toHaveLength(5);
    expect(graph.coverage.occurrencesByResolution.internal).toBe(3);
    expect(graph.coverage.occurrencesByResolution.dynamic).toBe(2);
    expect(graph.coverage.literalResolutionPercent).toBe(100);
    expect(graph.coverage.status).toBe("gaps");
  });
  it("supports import-equals and literal templates", () => {
    const { graph } = run({ "a.ts": "import value = require('./b'); const p=import(`./b`);", "b.ts": "export const value=1;" });
    expect(graph.dependencies.map((e: any) => [e.form, e.resolution])).toEqual([["importEquals", "internal"], ["dynamicImport", "internal"]]);
  });
  it("reports builtins, external declaration targets, missing dependencies and outside-scope files distinctly", () => {
    const { graph } = run({ "tsconfig.json": '{"include":["src"]}',
      "src/a.ts": "import 'node:fs'; import 'available'; import 'missing'; import '../test/helper';",
      "node_modules/available/package.json": '{"name":"available","types":"index.d.ts"}',
      "node_modules/available/index.d.ts": "export declare const value: number;", "test/helper.ts": "export const value=1;" });
    expect(graph.dependencies.map((e: any) => e.resolution)).toEqual(["builtin", "external", "unresolved", "outOfScope"]);
    expect(graph.dependencies[1].declarationTarget).toBe(true);
    expect(graph.coverage.literalResolutionPercent).toBe(75);
    expect(graph.highestFanOut).toEqual([]);
  });
  it("keeps cycles out of incomplete source and preserves graph diagnostics", () => {
    const { evidence, graph } = run({ "a.ts": "import './b';", "b.ts": "export function {" });
    expect(evidence.analysis.status).toBe("incomplete");
    expect(evidence.dimensions.architecture.status).toBe("failed");
    expect(graph.coverage.occurrencesByResolution.unavailableSource).toBe(1);
    expect(graph.cycles).toEqual([]);
    expect(() => validateEvidence(evidence)).not.toThrow();
  });
  it("does not call an empty dependency population 100 percent resolved", () => {
    const { graph } = run({ "a.ts": "export const a=1;" });
    expect(graph.coverage.literalResolutionPercent).toBeNull();
    expect(graph.coverage.dependencyOccurrences).toBe(0);
  });
  it("keeps cycle identity stable across checkout roots", () => {
    const files = { "a.ts": "import './b';", "b.ts": "import './a';" };
    const before = run(files), after = run(files);
    expect(after.graph).toEqual(before.graph);
    expect(compare(after.evidence, before.evidence).unchanged).toHaveLength(1);
  });
});

describe("cycle components", () => {
  it("groups overlapping cycles, preserves self-loops, and excludes acyclic modules", () => {
    const graph = new Map([['a', new Set(['b'])], ['b', new Set(['a', 'c'])], ['c', new Set(['b'])], ['d', new Set(['d'])], ['e', new Set<string>()]]);
    expect(cyclicComponents(graph)).toEqual([['a', 'b', 'c'], ['d']]);
  });
  it("handles a long chain without recursive traversal", () => {
    const graph = new Map(Array.from({ length: 20000 }, (_, i) => [String(i), new Set(i < 19999 ? [String(i + 1)] : [])]));
    expect(cyclicComponents(graph)).toEqual([]);
  });
});
