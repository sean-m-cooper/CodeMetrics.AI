import ts from "typescript";
import { unwrapHandler } from "./handler-resolution.js";
import type { FailureContracts } from "./failure-contracts.js";

export type DispositionKind = "propagated" | "failureResult" | "reported" | "fallback" | "documented" | "unexplainedSwallowing" | "unknown";
export interface HandlerDisposition { classification: DispositionKind; reason: string; comments?: string[]; }
const result = (classification: DispositionKind, reason: string): HandlerDisposition => ({ classification, reason });

// These are declarations of intent, not a judgment about whether the rationale is good.
const intentional = /\b(?:ignor(?:e|ed|ing)|swallow(?:ed|ing)?|discard(?:ed|ing)?|optional|best[- ]effort|does nothing|no[- ]op|intention(?:al|ally))\b/i;
function leadingComments(node: ts.Node): string[] {
  const source = node.getSourceFile();
  return (ts.getLeadingCommentRanges(source.text, node.getFullStart()) ?? [])
    .map(range => source.text.slice(range.pos, range.end)).filter(text => intentional.test(text));
}
function declaredIntent(body: ts.ConciseBody, checker: ts.TypeChecker): string[] {
  const owner = body.parent;
  if (ts.isCatchClause(owner)) return [...leadingComments(owner), ...leadingComments(owner.parent)];
  if (ts.isFunctionDeclaration(owner)) {
    const declarations = owner.name ? checker.getSymbolAtLocation(owner.name)?.declarations ?? [owner] : [owner];
    return declarations.flatMap(leadingComments);
  }
  if ((ts.isArrowFunction(owner) || ts.isFunctionExpression(owner)) && ts.isVariableDeclaration(owner.parent)) {
    const list = owner.parent.parent;
    // A comment on a multi-binding declaration cannot identify one handler's intent.
    if (ts.isVariableDeclarationList(list) && list.declarations.length === 1) return leadingComments(list.parent);
  }
  return leadingComments(owner);
}
function explicitValue(expression: ts.Expression | undefined): boolean {
  if (!expression) return false;
  const node = unwrapHandler(expression);
  // No names, calls, spreads or property reads: their value/side effects need more flow analysis.
  if (ts.isLiteralExpression(node) || [ts.SyntaxKind.NullKeyword, ts.SyntaxKind.TrueKeyword, ts.SyntaxKind.FalseKeyword].includes(node.kind)) return true;
  if (ts.isPrefixUnaryExpression(node)) return ts.isNumericLiteral(node.operand);
  if (ts.isArrayLiteralExpression(node)) return node.elements.every(element => explicitValue(element));
  return ts.isObjectLiteralExpression(node) && node.properties.every(property => ts.isPropertyAssignment(property) &&
    !ts.isComputedPropertyName(property.name) && explicitValue(property.initializer));
}
function statementOutcome(statement: ts.Statement, checker: ts.TypeChecker, contracts: FailureContracts): HandlerDisposition | undefined {
  if (ts.isThrowStatement(statement)) return result("propagated", "Unconditional throw on the failure path.");
  if (ts.isReturnStatement(statement)) return explicitValue(statement.expression) || contracts.explicitResult(statement.expression)
    ? result("failureResult", "Returns an explicit value/container on the failure path; suitability and caller correctness are not judged.")
    : result("unknown", "Return value or caller contract requires additional analysis.");
  if (ts.isBlock(statement)) return blockOutcome(statement.statements, checker, contracts);
  if (ts.isIfStatement(statement)) {
    const yes = statementOutcome(statement.thenStatement, checker, contracts);
    const no = statement.elseStatement && statementOutcome(statement.elseStatement, checker, contracts);
    if (yes && no && ![yes.classification, no.classification].includes("unknown"))
      return result(yes.classification === no.classification ? yes.classification : "failureResult", "Both branches have an explicit failure disposition.");
    return result("unknown", "Conditional failure path is not fully resolved.");
  }
  if (ts.isExpressionStatement(statement) && ts.isCallExpression(statement.expression)) {
    if (contracts.forwardsCaughtError(statement.expression)) return result("propagated", "Passes the caught error to a caller-supplied callback; consumer correctness is outside scope.");
    if (contracts.reports(statement.expression)) return result("reported", "Standard console output or a resolved source-backed logging wrapper; runtime filtering is outside scope.");
  }
  if (ts.isExpressionStatement(statement) || ts.isVariableStatement(statement) || ts.isEmptyStatement(statement) ||
    ts.isFunctionDeclaration(statement) || ts.isTypeAliasDeclaration(statement) || ts.isInterfaceDeclaration(statement)) return;
  return result("unknown", "Control flow is outside the bounded handler classifier.");
}
function blockOutcome(statements: readonly ts.Statement[], checker: ts.TypeChecker, contracts: FailureContracts): HandlerDisposition | undefined {
  for (const statement of statements) {
    const outcome = statementOutcome(statement, checker, contracts);
    if (outcome) return outcome;
  }
}
function containsReturn(node: ts.Node): boolean {
  if (ts.isReturnStatement(node)) return true;
  if (ts.isFunctionLike(node)) return false;
  return ts.forEachChild(node, containsReturn) ?? false;
}
function guardedFallback(statement: ts.TryStatement, next: ts.Statement | undefined, checker: ts.TypeChecker, contracts: FailureContracts): boolean {
  if (!next || !ts.isIfStatement(next) || statement.tryBlock.statements.length !== 1) return false;
  const assignment = statement.tryBlock.statements[0];
  if (!ts.isExpressionStatement(assignment) || !ts.isBinaryExpression(assignment.expression) ||
    assignment.expression.operatorToken.kind !== ts.SyntaxKind.EqualsToken || !ts.isIdentifier(assignment.expression.left)) return false;
  const symbol = checker.getSymbolAtLocation(assignment.expression.left);
  const declarations = symbol?.declarations ?? [];
  if (declarations.length !== 1 || !ts.isVariableDeclaration(declarations[0]) || declarations[0].initializer) return false;
  const refersToBinding = (node: ts.Node): boolean => (ts.isIdentifier(node) && checker.getSymbolAtLocation(node) === symbol) ||
    (ts.forEachChild(node, refersToBinding) ?? false);
  if (refersToBinding(assignment.expression.right)) return false;
  // Require the uninitialized binding immediately before the try: no earlier assigned state.
  const parent = statement.parent;
  if (!ts.isBlock(parent) && !ts.isSourceFile(parent)) return false;
  const previous = parent.statements[parent.statements.indexOf(statement) - 1];
  if (!previous || !ts.isVariableStatement(previous) || previous.declarationList.declarations.length !== 1 ||
    !(previous.declarationList.flags & ts.NodeFlags.Let) || previous.declarationList.declarations[0] !== declarations[0]) return false;
  const condition = unwrapHandler(next.expression);
  if (!ts.isBinaryExpression(condition) || condition.operatorToken.kind !== ts.SyntaxKind.EqualsEqualsEqualsToken) return false;
  const left = unwrapHandler(condition.left), right = unwrapHandler(condition.right);
  if (!ts.isIdentifier(left) || checker.getSymbolAtLocation(left) !== symbol || !ts.isIdentifier(right) || right.text !== "undefined") return false;
  const undefinedDeclarations = checker.getSymbolAtLocation(right)?.declarations ?? [];
  if (undefinedDeclarations.some(declaration => !declaration.getSourceFile().hasNoDefaultLib)) return false;
  const outcome = statementOutcome(next.thenStatement, checker, contracts);
  return !!outcome && ["failureResult", "propagated", "reported"].includes(outcome.classification);
}
export function classifyDisposition(body: ts.ConciseBody, checker: ts.TypeChecker, empty: boolean, comments: string[], use: ts.Node, contracts: FailureContracts): HandlerDisposition {
  const owner = body.parent;
  // Finally may replace a throw/return, even when the catch itself is explicit.
  if (ts.isCatchClause(owner)) {
    for (let ancestor: ts.Node | undefined = owner.parent; ancestor && !ts.isFunctionLike(ancestor); ancestor = ancestor.parent)
      if (ts.isTryStatement(ancestor) && ancestor.finallyBlock)
        return result("unknown", "Finally may change the failure disposition.");
  }
  const intent = [...(empty ? comments : comments.filter(comment => intentional.test(comment))), ...declaredIntent(body, checker)];
  if (intent.length) return { ...result("documented", "Attached developer intent is honored without judging the business decision."), comments: intent };
  if (!empty) {
    const outcome = ts.isBlock(body) ? blockOutcome(body.statements, checker, contracts) : explicitValue(body) || contracts.explicitResult(body) ? result("failureResult", "Explicit value/container from rejection handler.") : undefined;
    return outcome ?? result("unknown", "Code presence alone does not establish a failure disposition.");
  }
  if (!ts.isCatchClause(owner)) {
    if (!ts.isExpressionStatement(use.parent)) return result("unknown", "The rejection handler's result is used by an enclosing expression or caller; its failure contract needs context.");
    return result("unexplainedSwallowing", "Empty rejection handler resolves without a value or declared intent; its result is discarded.");
  }
  const statement = owner.parent, parent = statement.parent;
  if (!ts.isBlock(parent) && !ts.isSourceFile(parent)) return result("unknown", "Enclosing control flow requires context.");
  const following = parent.statements.slice(parent.statements.indexOf(statement) + 1);
  if (guardedFallback(statement, following[0], checker, contracts)) return result("fallback", "Failed assignment is followed by an explicit undefined guard and failure outcome.");
  if (following.length) {
    const outcome = blockOutcome(following, checker, contracts);
    if (outcome && outcome.classification !== "unknown") return result("fallback", outcome.reason);
    return result("unknown", "Continuation after the empty catch needs a failure-contract review.");
  }
  if (containsReturn(statement.tryBlock) && contracts.guardedUndefinedResult(body)) return result("fallback", "Every selected-source call tests the optional result before using it; no escaping function reference was found.");
  if (containsReturn(statement.tryBlock)) return result("unknown", "Success returns a value; the implicit/undefined failure result may be a caller contract.");
  if (!ts.isSourceFile(parent) && !ts.isFunctionLike(parent.parent)) return result("unknown", "Outer continuation or control flow requires context.");
  return result("unexplainedSwallowing", "Terminal empty catch discards failure without an explicit result or attached intent.");
}
