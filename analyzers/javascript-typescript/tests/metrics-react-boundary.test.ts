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

    // Keep the historical result snapshot unchanged when adding separate scored
    // measurements: locations, fingerprints, order and every raw metric are protected.
    const { functions, ...legacyResult } = result;
    expect(functions).toHaveLength(result.metrics.length);
    expect(result.findings.map(item => item.finding.observations?.sourceSpanStart)).toEqual([140,171,186,237,310]);
    // The new diagnostic offset supplies async ownership; it is not a raw metric.
    const snapshot = structuredClone(legacyResult);
    for (const item of snapshot.findings) delete item.finding.observations?.sourceSpanStart;
    expect(snapshot).toMatchSnapshot();
    expect(result.findings.map(item => [item.finding.category, item.finding.member])).toEqual([
      ["conditionalHook", "App"],
      ["conditionalHook", "App"],
      ["asyncEffectCallback", "App"],
      ["effectWithoutDependencies", "App"],
      ["conditionalHook", "App/nested"],
    ]);
  });
});
