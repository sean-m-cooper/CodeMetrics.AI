import ts from "typescript";
import type { Finding } from "./evidence.js";
import { isFunction } from "./function-nodes.js";

type AsyncObservation = Pick<Finding, "category" | "message" | "observations" | "confidence" | "severity">;

function unwrap(node: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isTypeAssertionExpression(node) ||
      ts.isNonNullExpression(node) || ts.isSatisfiesExpression(node)) node = node.expression;
  return node;
}

export function isAsyncFunction(node: ts.Node): boolean {
  return isFunction(node) && !!node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.AsyncKeyword);
}

function standardLibraryDeclaration(node: ts.Node): boolean {
  const source = node.getSourceFile();
  return source.hasNoDefaultLib && /(?:^|[/\\])lib\.[^/\\]+\.d\.ts$/.test(source.fileName);
}

export function isPromiseConstruction(node: ts.Node, checker: ts.TypeChecker): node is ts.NewExpression {
  if (!ts.isNewExpression(node)) return false;
  const target = unwrap(node.expression);
  const expression = ts.isPropertyAccessExpression(target) ? target.name : target;
  const declarations = checker.getSymbolAtLocation(expression)?.declarations ?? [];
  return declarations.some(declaration => ts.isVariableDeclaration(declaration) &&
    ts.isIdentifier(declaration.name) && declaration.name.text === "Promise" && standardLibraryDeclaration(declaration));
}

// Limit recognition to the standard Array contract, not arbitrary APIs named forEach.
function isArrayForEach(call: ts.CallExpression, checker: ts.TypeChecker): boolean {
  const declaration = checker.getResolvedSignature(call)?.declaration;
  return !!declaration && ts.isMethodSignature(declaration) &&
    ts.isIdentifier(declaration.name) && declaration.name.text === "forEach" &&
    ts.isInterfaceDeclaration(declaration.parent) &&
    ["Array", "ReadonlyArray"].includes(declaration.parent.name.text) && standardLibraryDeclaration(declaration);
}

export function inspectAsyncNode(node: ts.Node, checker: ts.TypeChecker): AsyncObservation[] {
  if (isPromiseConstruction(node, checker) && node.arguments?.[0] && isAsyncFunction(unwrap(node.arguments[0]))) {
    return [{ category: "asyncPromiseExecutor", severity: "warning", confidence: "high",
      message: "The Promise constructor ignores its async executor's returned Promise; a rejection there does not automatically reject the constructed Promise.",
      observations: { classification: "actionableSignal", classificationReason: "standardPromiseIgnoresExecutorReturn", scoreDisposition: "scored" } }];
  }
  if (ts.isCallExpression(node) && node.arguments[0] && isAsyncFunction(unwrap(node.arguments[0])) && isArrayForEach(node, checker)) {
    return [{ category: "asyncForEachCallback", severity: "info", confidence: "high",
      message: "Array.forEach ignores callback Promises. Review completion and rejection handling; detached work may be intentional, and concurrency is not automatically a safe replacement.",
      observations: { classification: "reviewLead", classificationReason: "arrayForEachIgnoresCallbackReturn", scoreDisposition: "excludedReviewLead" } }];
  }
  return [];
}
