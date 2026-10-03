import ts from "typescript";
import { describe, expect, it } from "vitest";
import { analyzeFile } from "../src/metrics.js";
import { scoreFunctions } from "../src/function-scoring.js";

function inspect(text: string) {
  const source = ts.createSourceFile("/sample/code.ts", text, ts.ScriptTarget.Latest, true);
  const host = ts.createCompilerHost({});
  host.getSourceFile = file => file === source.fileName ? source : undefined;
  const program = ts.createProgram([source.fileName], { noLib: true }, host);
  return analyzeFile(source, program.getTypeChecker(), "sample", "/sample");
}

describe("owned complexity explanations", () => {
  it("accounts for every supported decision once, including shared case labels and all five loop forms", () => {
    const result = inspect(`function f(a, b, value, items) {
      if (a && b) {} else if (a || b) {}
      switch (value) { case 1: case 2: break; default: break; }
      for (let i = 0; i < 2; i++) {}
      for (const key in items) {}
      for (const item of items) {}
      while (a) { break; }
      do {} while (b);
      try {} catch (error) {} finally {}
      return a ? (b ?? value) : false;
    }`);
    expect(result.functions[0].complexityBreakdown).toEqual({ version: 1, baseline: 1,
      counts: { ifStatements: 2, switchCases: 2, loops: 5, catchClauses: 1,
        ternaryExpressions: 1, logicalAnd: 1, logicalOr: 1, nullishCoalescing: 1 }, decisionIncrements: 14, total: 15 });
    expect(result.functions[0].ownComplexity).toBe(15);
    expect(result.metrics[0].complexity).toBe(15);
  });
  it("reports zero increments for implemented bodies without decisions", () => {
    for (const item of inspect("function empty() {} function simple(){ return { value: 1 }; }").functions) {
      expect(item.complexityBreakdown!.decisionIncrements).toBe(0);
      expect(item.complexityBreakdown!.total).toBe(1);
    }
  });
  it("keeps nested function and class decisions out of the parent", () => {
    const result = inspect("function outer() { function inner(){ if (x) return 1; } class C { m(){while(x){break;}} } return 1; }");
    expect(result.functions.map(item => item.complexityBreakdown!.decisionIncrements)).toEqual([0, 1, 1]);
    expect(result.functions[1].complexityBreakdown!.counts.ifStatements).toBe(1);
    expect(result.functions[2].complexityBreakdown!.counts.loops).toBe(1);
  });
  it("retains runtime decisions under type erasure and excludes conditional types", () => {
    const result = inspect("function f(a) { type Choice<T> = T extends string ? 1 : 2; return (a ? f : g)<Choice<string>>; }");
    expect(result.functions[0].complexityBreakdown!.counts.ternaryExpressions).toBe(1);
    expect(result.functions[0].complexityBreakdown!.total).toBe(2);
  });
  it("does not reinterpret identical counts as a nesting or defect classification", () => {
    const flat = inspect("function f(a,b) { if(a) work(); if(b) work(); }").functions[0];
    const nested = inspect("function f(a,b) { if(a) { if(b) work(); } }").functions[0];
    expect(flat.complexityBreakdown).toEqual(nested.complexityBreakdown);
  });
  it("preserves one breakdown across findings, contribution rows and sampled hotspots without changing the score", () => {
    const result = inspect(`function f(x) { ${"if(x) work();".repeat(11)} } function g(){return 1;}`);
    const findings = result.findings.map(item => item.finding);
    const scored = scoreFunctions(result.functions, "codeQuality", findings);
    expect(findings[0].observations.complexityBreakdown).toEqual(result.functions[0].complexityBreakdown);
    const observations = (scored.scoring as { observations: { functionContributions: unknown[]; topOffenders: unknown[] } }).observations;
    expect(observations.functionContributions[0]).toMatchObject({ complexityBreakdown: result.functions[0].complexityBreakdown });
    expect(observations.topOffenders[0]).toMatchObject({ complexityBreakdown: result.functions[0].complexityBreakdown });
    const historical = result.functions.map(({ complexityBreakdown, ...item }) => item);
    expect(scoreFunctions(historical, "codeQuality", findings).scoringDecision).toEqual(scored.scoringDecision);
    expect(scoreFunctions(historical, "maintainability").score).toBe(scoreFunctions(result.functions, "maintainability").score);
  });
});
