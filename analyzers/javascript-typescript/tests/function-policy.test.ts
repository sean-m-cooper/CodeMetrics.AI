import ts from "typescript";
import { describe, expect, it } from "vitest";
import { analyzeFile } from "../src/metrics.js";
import type { FunctionMeasurement } from "../src/function-measurements.js";
import { complexityAnchors, maintainabilityAnchors, scoreFunctions } from "../src/function-scoring.js";
import { add, decimal, fraction, interpolate, numeric, roundScore } from "../src/score-arithmetic.js";
import { validateEvidence } from "../dist/evidence-tools.js";
import { analyze } from "../src/analyzer.js";
import path from "node:path";

const measured = (cc: number, mi = 75, id = "a"): FunctionMeasurement => ({ id, file: "code.ts", project: "sample", member: id, line: 1,
  sourceSpanStart: 0, ownComplexity: cc, ownMaintainabilityIndex: mi, ownSourceLines: 1, ownHalsteadVolume: 1 });
function inspect(text: string, kind = ts.ScriptKind.TSX) {
  const source = ts.createSourceFile("/sample/code.tsx", text, ts.ScriptTarget.Latest, true, kind);
  const host = ts.createCompilerHost({});
  host.getSourceFile = file => file === source.fileName ? source : undefined;
  const program = ts.createProgram([source.fileName], { noLib: true }, host);
  return analyzeFile(source, program.getTypeChecker(), "sample", "/sample");
}
function contributions(result: ReturnType<typeof scoreFunctions>) {
  return (result.scoring as { observations: { functionContributions: (FunctionMeasurement & {
    group: string; weightNumerator: number; weightDenominator: number; individualScore: number; weightedScoreLoss: number;
  })[] } }).observations.functionContributions;
}

describe("function population score arithmetic", () => {
  it.each(complexityAnchors)("CC %s maps to %s", (cc, score) => {
    expect(scoreFunctions([measured(cc)], "codeQuality").score).toBe(score);
  });
  it.each(maintainabilityAnchors)("MI %s maps to %s", (mi, score) => {
    expect(scoreFunctions([measured(1, mi)], "maintainability").score).toBe(score);
  });
  it("interpolates, clamps and rounds only the final aggregate half up", () => {
    expect(numeric(interpolate(4, complexityAnchors))).toBe(9);
    expect(numeric(interpolate(61.5, maintainabilityAnchors))).toBe(5);
    expect(numeric(interpolate(100, complexityAnchors))).toBe(0);
    expect(numeric(interpolate(-1, maintainabilityAnchors))).toBe(0);
    expect(roundScore(decimal(8.65))).toBe(8.7);
    expect(roundScore(decimal(2.65))).toBe(2.7);
    expect(roundScore(fraction(173n, 20n))).toBe(8.7);
    expect(roundScore(decimal(8.649999999))).toBe(8.6);
    expect(numeric(decimal(1e-7))).toBe(1e-7);
    expect(numeric(decimal(1e21))).toBe(1e21);
    expect(() => decimal(NaN)).toThrow();
    // Individual scores 9.6 and 7.8 produce exactly 8.88, then 8.9.
    expect(scoreFunctions([measured(1, 74), measured(1, 69.5, "b")], "maintainability").score).toBe(8.9);
  });
  it.each([2, 10, 1000])("a CC40 function cannot be hidden among %s simple functions", count => {
    const functions = Array.from({ length: count }, (_, i) => measured(1, 75, String(i)));
    expect(scoreFunctions([...functions, measured(40, 75, "worst")], "codeQuality").score).toBe(6);
  });
  it("removes exactly one tied worst function and is independent of input order", () => {
    const functions = [measured(20, 75, "a"), measured(20, 75, "b"), measured(1, 75, "c")];
    const result = scoreFunctions(functions, "codeQuality");
    expect(result.score).toBe(5.8);
    expect(contributions(result).filter(item => item.group === "weakest")).toHaveLength(1);
    expect(scoreFunctions([...functions].reverse(), "codeQuality")).toEqual(result);
  });
  it.each([1, 2, 5, 6, 10, 11, 99])("MI partitions %s functions once with weights summing to one", count => {
    const result = scoreFunctions(Array.from({ length: count }, (_, i) => measured(1, 40 + i % 61, String(i))), "maintainability");
    const items = contributions(result);
    expect(items.filter(item => item.group === "weakest")).toHaveLength(Math.ceil(count / 5));
    expect(new Set(items.map(item => item.id)).size).toBe(count);
    const totalWeight = items.reduce((sum, item) => add(sum, fraction(BigInt(item.weightNumerator), BigInt(item.weightDenominator))), decimal(0));
    expect(totalWeight).toEqual(fraction(1n));
    expect(result.score).toBeCloseTo(10 - items.reduce((sum, item) => sum + item.weightedScoreLoss, 0), 0);
  });
  it("does not award a score to a function-free scope", () => {
    for (const dimension of ["codeQuality", "maintainability"] as const) {
      const result = scoreFunctions([], dimension);
      expect(result.status).toBe("skipped");
      expect(result.score).toBeUndefined();
      expect(result.scoringDecision).toBeUndefined();
    }
  });
  it("worsening an existing function cannot improve either aggregate", () => {
    const base = Array.from({ length: 20 }, (_, i) => measured(i + 1, 40 + i * 2, String(i)));
    for (let index = 0; index < base.length; index++) {
      for (const dimension of ["codeQuality", "maintainability"] as const) {
        const worse = base.map((item, i) => i === index ? { ...item, ownComplexity: item.ownComplexity + 20, ownMaintainabilityIndex: item.ownMaintainabilityIndex - 20 } : item);
        expect(scoreFunctions(worse, dimension).score!).toBeLessThanOrEqual(scoreFunctions(base, dimension).score!);
      }
    }
  });
});

describe("owned function measurements", () => {
  const values = (text: string, kind = ts.ScriptKind.TSX) => inspect(text, kind).functions.map(({ ownComplexity, ownSourceLines, ownHalsteadVolume, ownMaintainabilityIndex }) =>
    ({ ownComplexity, ownSourceLines, ownHalsteadVolume, ownMaintainabilityIndex }));
  it.each([
    ["annotation", "const result = value; return result;", "const result: string = value; return result;"],
    ["definite assignment", "let result; return result;", "let result!: string; return result;"],
    ["as assertion", "return value;", "return value as string;"],
    ["chained assertions", "return value;", "return value as unknown as string;"],
    ["angle assertion", "return value;", "return <string>value;"],
    ["satisfies", "return value;", "return value satisfies string;"],
    ["non-null assertion", "return value;", "return value!;"],
    ["generic call", "return transform(value);", "return transform<string, number>(value);"],
    ["generic constructor", "return new Factory(value);", "return new Factory<string, number>(value);"],
    ["generic tag", "return tag`value`;", "return tag<string>`value`;"],
    ["instantiation", "return factory;", "return factory<string>;"],
    ["instantiated conditional", "return (value ? a : b);", "return (value ? a : b)<string>;"],
    ["instantiated call", "return factory(value ? a : b);", "return factory(value ? a : b)<string>;"],
    ["nested type arguments", "return transform(value);", "return transform<Map<string, Array<{ key: number }>>>(value);"],
  ])("does not charge erased %s syntax to MI or complexity", (_, plain, typed) => {
    expect(values(`function f(value) { ${typed} }`, ts.ScriptKind.TS)).toEqual(values(`function f(value) { ${plain} }`, ts.ScriptKind.TS));
  });
  it("excludes occupied lines containing only erased type syntax", () => {
    expect(values("function f(value) {\nreturn value as\n  Map<string,\n  number>;\n}")).toEqual(values("function f(value) {\nreturn value;\n}"));
  });
  it("still discovers implemented functions inside instantiation expressions", () => {
    const result = inspect("function outer() { return (function inner<T>(value: T) { if (value) return value; return null; })<string>; }", ts.ScriptKind.TS);
    expect(result.functions.map(item => [item.member, item.ownComplexity])).toEqual([["outer", 1], ["outer/inner", 2]]);
    expect(result.metrics.map(item => item.member)).toEqual(["outer", "outer/inner"]);
    expect(inspect("type Only = typeof factory<string>;", ts.ScriptKind.TS).functions).toHaveLength(0);
  });
  it("preserves runtime comparisons, object colons, negation and operand decisions", () => {
    const runtime = inspect("function f(value) { return { low: value < 1, high: value > 2, negated: !value, choice: value ? 1 : 2 }; }");
    expect(runtime.functions[0].ownComplexity).toBe(2);
    expect(runtime.functions[0].ownHalsteadVolume).toBeGreaterThan(100);
    const instantiated = inspect("function f(value) { return (value ? a : b)<string>; }", ts.ScriptKind.TS);
    expect(instantiated.functions[0].ownComplexity).toBe(2);
    const plain = inspect("function f(value) { return (value ? a : b); }", ts.ScriptKind.TS);
    expect(instantiated.functions[0].ownHalsteadVolume).toBe(plain.functions[0].ownHalsteadVolume);
  });
  it("ignores signatures, comments, enums and type declarations in enclosing measurements", () => {
    const baseline = values("function f(x) { return x ? 1 : 0; }");
    expect(values(`function f<T>(x: T extends number ? string : number): number {
      // Deliberately supported fallback.
      enum Local { A, B, C }
      type Choice<T> = T extends number ? 1 : 0;
      interface Shape { x: number }
      return x ? 1 : 0;
    }`)).toEqual(baseline);
  });
  it("owns nested functions and class methods separately, excluding nested class initializers", () => {
    const result = inspect(`function outer(x) {
      function inner() { if (x) return x ? 1 : 0; }
      class Nested { value = x ? 1 : 0; method() { return x ? 1 : 0; } }
      return 1;
    }`);
    expect(result.functions.map(item => item.ownComplexity)).toEqual([1, 3, 2]);
    expect(new Set(result.functions.map(item => item.id)).size).toBe(3);
    expect(result.functions[0].ownSourceLines).toBe(1);
    // Historical raw metrics remain distinct from the new owned measurement.
    expect(result.metrics[0].complexity).toBe(2);
  });
  it("counts literal occupied lines and excludes brace-only/comment-only lines", () => {
    const result = inspect("function f() {\n// comment\nreturn `one\ntwo\nthree`;\n}");
    expect(result.functions[0].ownSourceLines).toBe(3);
    expect(result.metrics[0].sourceLines).toBe(2);
  });
  it("counts callbacks on the same line separately and does not score declaration-only members", () => {
    const result = inspect("declare function f(): void; enum E { A, B } [1].map(x=>x).map(x=>x+1);");
    expect(result.functions).toHaveLength(2);
    expect(new Set(result.functions.map(item => item.id)).size).toBe(2);
    expect(inspect("interface X { f(): void; } enum E { A, B }").functions).toHaveLength(0);
  });
  it("gives implemented empty bodies MI100 without adding declaration credit", () => {
    const result = inspect("function empty() { /* deliberate */ } declare function other(): void;");
    expect(result.functions).toHaveLength(1);
    expect(result.functions[0].ownMaintainabilityIndex).toBe(100);
  });
  it("keeps getters, setters, constructors, arrows and JSX runtime expressions in scope", () => {
    const result = inspect("class C { constructor(x) {if(x) return;} get x(){return 1;} set x(v){} } const App = x => <div>{x && <span/>}</div>;");
    expect(result.functions.map(item => item.ownComplexity)).toEqual([2, 1, 1, 2]);
  });
  it("links hotspot findings to exactly one aggregate step, without separate penalties", () => {
    const result = inspect(`function f(x) { ${"if (x) x--;".repeat(11)} return x; }`);
    const findings = result.findings.map(item => item.finding);
    const score = scoreFunctions(result.functions, "codeQuality", findings);
    expect(findings[0].observations.functionId).toBe(result.functions[0].id);
    expect(score.scoringDecision!.findingEffects[0]).toMatchObject({ effect: "policyInput", stepIds: ["weakest"] });
  });
  it("emits schema-valid function decisions for the analyzer itself", () => {
    const result = analyze({ project: path.resolve("package.json") }, "0.3.0");
    expect(() => validateEvidence(result.evidence)).not.toThrow();
    for (const dimension of ["codeQuality", "maintainability"] as const) {
      expect(contributions(result.evidence.dimensions[dimension])).toHaveLength(result.metrics.length);
    }
  });
});
