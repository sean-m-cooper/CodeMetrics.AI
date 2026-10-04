import ts from "typescript";
import type { Finding } from "./evidence.js";
import { isFunction } from "./function-nodes.js";

type ReactObservation = Pick<Finding, "category" | "message" | "observations" | "confidence">;

function isReactImport(declaration: ts.Declaration): boolean {
  if (ts.isImportSpecifier(declaration) && declaration.isTypeOnly) return false;
  let cursor: ts.Node | undefined = declaration;
  while (cursor && !ts.isImportDeclaration(cursor)) cursor = cursor.parent;
  return !!cursor && !cursor.importClause?.isTypeOnly && ts.isStringLiteral(cursor.moduleSpecifier) && cursor.moduleSpecifier.text === "react";
}

export function importedHookName(call: ts.CallExpression, checker: ts.TypeChecker): string | undefined {
  const symbol = checker.getSymbolAtLocation(ts.isPropertyAccessExpression(call.expression) ? call.expression.name : call.expression);
  const declarations = symbol?.declarations ?? [];
  const reactHook = declarations.some(isReactImport);
  const hookName = declarations.find(ts.isImportSpecifier);
  const calledName = hookName ? (hookName.propertyName ?? hookName.name).text : ts.isPropertyAccessExpression(call.expression) ? call.expression.name.text : "";
  const namespaceSymbol = ts.isPropertyAccessExpression(call.expression) ? checker.getSymbolAtLocation(call.expression.expression) : undefined;
  const reactNamespace = namespaceSymbol?.declarations?.some(isReactImport);
  return (reactHook || reactNamespace) && /^use[A-Z]/.test(calledName) ? calledName : undefined;
}

function isConditionalOrLoop(node: ts.Node, child: ts.Node): boolean {
  if (ts.isIfStatement(node)) return child !== node.expression;
  if (ts.isConditionalExpression(node)) return child !== node.condition;
  if (ts.isBinaryExpression(node)) return child === node.right &&
    [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(node.operatorToken.kind);
  if (ts.isForStatement(node)) return child !== node.initializer;
  if (ts.isForOfStatement(node) || ts.isForInStatement(node)) return child !== node.expression;
  return ts.isWhileStatement(node) || ts.isDoStatement(node);
}

// The metric visitor supplies only calls owned by the current function. Keeping
// traversal and identity there preserves nested-function boundaries and ordering.
export function inspectReactCall(call: ts.CallExpression, body: ts.ConciseBody, checker: ts.TypeChecker): ReactObservation[] {
  const hook = importedHookName(call, checker);
  if (!hook) return [];

  const findings: ReactObservation[] = [];
  let ancestor = call.parent;
  let child: ts.Node = call;
  while (ancestor && ancestor !== body) {
    if (isConditionalOrLoop(ancestor, child)) {
      findings.push({ category: "conditionalHook", message: `${hook} is called within a conditional or loop.`, observations: { hook }, confidence: "high" });
      break;
    }
    child = ancestor;
    ancestor = ancestor.parent;
  }

  if (["useEffect", "useLayoutEffect"].includes(hook)) {
    const callback = call.arguments[0];
    if (callback && isFunction(callback) && callback.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.AsyncKeyword))
      findings.push({ category: "asyncEffectCallback", message: "An effect callback is async and returns a Promise instead of cleanup or undefined.", observations: { hook }, confidence: "high" });
    if (call.arguments.length < 2)
      findings.push({ category: "effectWithoutDependencies", message: "Effect runs after every render; verify that this is intentional.", observations: { hook }, confidence: "low" });
  }
  return findings;
}
