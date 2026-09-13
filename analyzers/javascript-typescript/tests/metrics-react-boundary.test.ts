import ts from "typescript";
import { describe, expect, it } from "vitest";
import { analyzeFile } from "../src/metrics.js";

function inspect(text: string) {
  const source = ts.createSourceFile("/sample/App.tsx", text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const host = ts.createCompilerHost({});
  host.getSourceFile = file => file === source.fileName ? source : undefined;
  const program = ts.createProgram([source.fileName], { noLib: true }, host);
  return analyzeFile(source, program.getTypeChecker(), "sample", "/sample");
}

describe("metrics and React finding boundaries", () => {
  it("preserves metrics, finding identity and callback ownership for mixed React imports", () => {
    const result = inspect(`import * as React from 'react';
import { useEffect as effect, useState } from 'react';
export function App(active: boolean) {
  if (active) React.useState(0);
  active && useState(1);
  effect(async () => { if (active) return; }, []);
  React.useLayoutEffect(() => {});
  const nested = () => { while (active) React.useEffect(() => {}, []); };
  return <div>{active ? 'yes' : 'no'}</div>;
}
export function Shadow(React: any, effect: any) {
  if (true) React.useState(0);
  effect(async () => {});
}`);

    // Captured before separating React rules from metric collection. The full
    // result protects source locations, fingerprints, order and every raw metric.
    expect(result).toMatchSnapshot();
    expect(result.findings.map(item => [item.finding.category, item.finding.member])).toEqual([
      ["conditionalHook", "App"],
      ["conditionalHook", "App"],
      ["asyncEffectCallback", "App"],
      ["effectWithoutDependencies", "App"],
      ["conditionalHook", "App/nested"],
    ]);
  });
});
