import ts from "typescript";
import { hash, type Finding } from "./evidence.js";
import { isFunction } from "./function-nodes.js";
import type { FunctionMeasurement } from "./function-measurements.js";
import { isAsyncFunction, isPromiseConstruction, isStandardPromiseCall } from "./async-probe.js";
import { importedHookName } from "./react-probe.js";

export interface AsyncOwner { id: string; file: string; project: string; member: string; line: number; reasons: string[]; }
export function asyncPopulation(source: ts.SourceFile, checker: ts.TypeChecker, file: string, project: string,
  functions: FunctionMeasurement[], findings: Finding[]): AsyncOwner[] {
  const owners = new Map<string, AsyncOwner>();
  const functionsByStart = new Map(functions.map(item => [item.sourceSpanStart, item]));
  const findingsByStart = new Map<number, Finding[]>();
  for (const finding of findings) {
    const start = Number(finding.observations.sourceSpanStart);
    const group = findingsByStart.get(start) ?? []; group.push(finding); findingsByStart.set(start, group);
  }
  const moduleOwner: AsyncOwner = { id: hash(`${file}|<module-async>`), file, project, member: "<module>", line: 1, reasons: [] };
  function include(owner: AsyncOwner, reason: string) {
    const current = owners.get(owner.id) ?? { ...owner, reasons: [] };
    if (!current.reasons.includes(reason)) current.reasons.push(reason);
    owners.set(owner.id, current);
  }
  function visit(node: ts.Node, owner: AsyncOwner) {
    if (ts.isTypeNode(node) && !ts.isExpressionWithTypeArguments(node)) return;
    if (isFunction(node) && !node.body) return;
    if (isFunction(node) && node.body) {
      const measured = functionsByStart.get(node.getStart(source));
      if (measured) owner = { id: measured.id, file, project, member: measured.member, line: measured.line, reasons: [] };
    }
    if (isAsyncFunction(node)) include(owner, "asyncFunction");
    if (ts.isAwaitExpression(node)) include(owner, "await");
    if (ts.isForOfStatement(node) && node.awaitModifier) include(owner, "forAwait");
    if (isPromiseConstruction(node, checker)) include(owner, "standardPromiseConstruction");
    if (isStandardPromiseCall(node, checker)) include(owner, "standardPromiseCall");
    if (ts.isCallExpression(node) && importedHookName(node, checker)) include(owner, "reactHookCall");
    for (const finding of findingsByStart.get(node.getStart(source)) ?? []) {
      include(owner, "recognizedUsageSignal"); finding.observations.asyncOwnerId = owner.id;
    }
    ts.forEachChild(node, child => visit(child, owner));
  }
  visit(source, moduleOwner);
  return [...owners.values()].map(owner => ({ ...owner, reasons: owner.reasons.sort() })).sort((a, b) => a.id.localeCompare(b.id, "en"));
}
